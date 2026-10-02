using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinOpt.Core.Models;
using WinOpt.Engine.Logging;

namespace WinOpt.Engine.Gaming;

/// <summary>What a game-mode start was asked to do.</summary>
public sealed class GameModeOptions
{
    /// <summary>Plan name or GUID. Resolved against this machine's own schemes.</summary>
    public string Plan { get; init; } = "high-performance";

    public bool Notifications { get; init; } = true;
    public bool BackgroundApps { get; init; } = true;

    /// <summary>Services to stop for the session. Empty means none — services are never chosen by default.</summary>
    public IReadOnlyList<string> Services { get; init; } = Array.Empty<string>();

    /// <summary>Process whose priority is raised. The session's only non-restorable control.</summary>
    public string? ForProcess { get; init; }

    public string? GamePath { get; init; }

    /// <summary>
    /// Restricts the run to named control kinds. Null means everything the
    /// booleans and lists above asked for.
    /// </summary>
    public IReadOnlyList<string>? Only { get; init; }
}

/// <summary>
/// Starts and stops a temporary optimisation session.
///
/// The contract is capture-first: every control reads what is in place before
/// it writes anything, the session file holding those reads is on disk before
/// the first write, and stop puts them back. A control that could not be given
/// a previous value never enters the session — it is reported as a failure
/// instead of recorded as a change that can never be undone.
/// </summary>
public sealed class GameModeManager
{
    private const string PushNotificationsKey =
        @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string AppPrivacyKey =
        @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";

    private readonly WinOptLogger? _logger;

    public GameModeManager(WinOptLogger? logger = null) => _logger = logger;

    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // --- Status ---

    public GameModeStatus Status()
    {
        var session = GamingState.Read<GameModeSession>(GamingState.SessionFile);
        if (session == null)
            return new GameModeStatus { Active = false };

        bool? ownerRunning = null;
        if (session.OwnerProcess != null)
        {
            var instances = Process.GetProcessesByName(session.OwnerProcess);
            ownerRunning = instances.Length > 0;
            foreach (var p in instances) p.Dispose();
        }

        return new GameModeStatus
        {
            Active = true,
            Session = session,
            OwnerRunning = ownerRunning,
            StartedAt = session.StartedAt,
            Controls = session.Controls,
        };
    }

    // --- Start ---

    public async Task<GameModeResult> StartAsync(GameModeOptions options)
    {
        if (GamingState.Read<GameModeSession>(GamingState.SessionFile) != null)
        {
            return new GameModeResult
            {
                Success = false,
                Message = "A game-mode session is already active. Stop it before starting another — " +
                          "starting over would discard the previous values this session needs to restore.",
                Status = Status(),
            };
        }

        var attempted = new List<GameModeControl>();
        var elevated = IsElevated();

        if (Wanted(options, "power-plan"))
        {
            var current = await ReadActivePlanAsync();
            var target = await ResolvePlanAsync(options.Plan);

            if (current == null)
                attempted.Add(Refused("power-plan", options.Plan, "Could not read the active power plan."));
            else if (target == null)
                attempted.Add(Refused("power-plan", options.Plan,
                    $"No power plan on this machine matches '{options.Plan}'. Run 'powercfg /list' to see the ones that exist."));
            else
                attempted.Add(new GameModeControl
                {
                    Kind = "power-plan",
                    Target = target.Guid,
                    // "Switched" and "already there" are different things, and
                    // the user is about to be told what this session did.
                    Description = string.Equals(current, target.Guid, StringComparison.OrdinalIgnoreCase)
                        ? $"Power plan → {target.Name} (already active)"
                        : $"Power plan → {target.Name}",
                    Before = current,
                    BeforeExisted = true,
                    After = target.Guid,
                    Restorable = true,
                });
        }

        if (Wanted(options, "notifications"))
        {
            if (!elevated)
            {
                attempted.Add(Refused("notifications", PushNotificationsKey,
                    "Requires administrator — suppressing toast notifications writes HKLM."));
            }
            else
            {
                foreach (var value in new[] { "NoToastApplicationNotification", "NoCloudApplicationNotification" })
                    attempted.Add(CaptureRegistryControl("notifications", PushNotificationsKey, value,
                        writeData: "1", description: "Toast notifications suppressed"));
            }
        }

        if (Wanted(options, "background-apps"))
        {
            if (!elevated)
            {
                attempted.Add(Refused("background-apps", AppPrivacyKey,
                    "Requires administrator — the background-apps policy writes HKLM."));
            }
            else
            {
                attempted.Add(CaptureRegistryControl("background-apps", AppPrivacyKey,
                    "LetAppsRunInBackground", writeData: "2",
                    description: "Background app execution set to Force deny"));
            }
        }

        if (Wanted(options, "service"))
        {
            foreach (var service in options.Services)
                attempted.Add(await CaptureServiceAsync(service));
        }

        if (Wanted(options, "priority") && !string.IsNullOrWhiteSpace(options.ForProcess))
            attempted.Add(CapturePriority(options.ForProcess!));

        // Every control that reached the session had its previous value read
        // and was about to be written. Everything else is reported as a
        // failure and left out, because a control with no `before` is a change
        // stop could never undo.
        var planned = attempted.Where(c => c.Error == null).ToList();

        if (planned.Count == 0)
        {
            return new GameModeResult
            {
                Success = false,
                Message = "Nothing was changed — no control could be applied.",
                Controls = attempted,
                Status = Status(),
            };
        }

        var session = new GameModeSession
        {
            GamePath = options.GamePath,
            OwnerProcess = options.ForProcess,
            Controls = planned,
        };

        // On disk before the first write: a crash between here and the end of
        // the loop still leaves every `before` value available to stop.
        GamingState.Write(GamingState.SessionFile, session);

        foreach (var control in planned)
        {
            var (ok, error) = await ApplyAsync(control);
            control.Applied = ok;
            control.Error = error;
            Audit(control, "start", session.Id, success: ok, error: error);
        }

        GamingState.Write(GamingState.SessionFile, session);

        var applied = planned.Count(c => c.Applied);
        return new GameModeResult
        {
            Success = applied > 0,
            Message = applied == attempted.Count
                ? $"{applied} of {attempted.Count} controls applied."
                : $"{applied} of {attempted.Count} controls applied — every one that did not is listed with the reason.",
            Controls = attempted,
            Status = Status(),
        };
    }

    // --- Stop ---

    public async Task<GameModeResult> StopAsync()
    {
        var session = GamingState.Read<GameModeSession>(GamingState.SessionFile);
        if (session == null)
        {
            return new GameModeResult
            {
                Success = true,
                Message = "No active game-mode session — nothing to restore.",
                Status = Status(),
            };
        }

        var results = new List<GameModeControl>();
        var stillInForce = new List<GameModeControl>();

        // Reverse order: the last change is undone first, so no control has to
        // restore a value another one is about to overwrite.
        foreach (var control in session.Controls.AsEnumerable().Reverse())
        {
            if (!control.Restorable)
            {
                results.Add(new GameModeControl
                {
                    Kind = control.Kind,
                    Target = control.Target,
                    Description = control.Description + " — ends with the process, nothing to restore",
                    Before = control.Before,
                    BeforeExisted = control.BeforeExisted,
                    After = control.After,
                    Applied = true,
                    Restorable = false,
                });
                continue;
            }

            // A control that failed to apply is still restored: its `before`
            // value is the machine's real state either way, and writing it
            // back is a no-op when nothing was written over it.
            var (ok, error) = await RestoreAsync(control);
            results.Add(new GameModeControl
            {
                Kind = control.Kind,
                Target = control.Target,
                Description = await DescribeRestoreAsync(control),
                Before = control.After,
                BeforeExisted = control.After != null,
                After = control.Before,
                Applied = ok,
                Error = error,
                Restorable = true,
            });

            Audit(control, "stop", session.Id, success: ok, error: error);

            if (!ok) stillInForce.Add(control);
        }

        results.Reverse();

        if (stillInForce.Count == 0)
        {
            File.Delete(GamingState.SessionFile);
        }
        else
        {
            // Keep only what could not be put back, so a second stop retries
            // exactly those instead of re-undoing what is already restored.
            GamingState.Write(GamingState.SessionFile, new GameModeSession
            {
                Id = session.Id,
                StartedAt = session.StartedAt,
                GamePath = session.GamePath,
                OwnerProcess = session.OwnerProcess,
                Controls = stillInForce,
            });
        }

        // Only the restorable half can be restored, and counting a process
        // priority among them would claim work that was never done.
        var undoable = results.Where(r => r.Restorable).ToList();
        var failed = undoable.Count(r => !r.Applied);
        var undone = undoable.Count - failed;
        var ending = results.Count - undoable.Count;

        string message;
        if (failed == 0)
        {
            var suffix = ending == 0
                ? string.Empty
                : $"; {ending} had nothing to restore and ends on its own.";
            message = $"Restored {undoable.Count} control{(undoable.Count == 1 ? "" : "s")}{suffix}.";
        }
        else
        {
            message = $"{undone} of {undoable.Count} controls restored; " +
                      $"{failed} could not be and are still in force.";
        }

        return new GameModeResult
        {
            Success = failed == 0,
            Message = message,
            Controls = results,
            Status = Status(),
        };
    }

    /// <summary>
    /// The line shown for one control being put back.
    ///
    /// "Restore" plus the original description reads backwards for a power
    /// plan: the session switched to Balanced, so that line comes out as
    /// "Restore Power plan → Balanced" while the machine is being returned to
    /// High performance. A plan is therefore named by where it is going, and
    /// everything else by what it is undoing.
    /// </summary>
    private static async Task<string> DescribeRestoreAsync(GameModeControl control)
    {
        if (!string.Equals(control.Kind, "power-plan", StringComparison.Ordinal))
            return $"Undo: {control.Description}";

        if (string.IsNullOrWhiteSpace(control.Before))
            return "Power plan → the plan that was active before";

        var plans = await ListPlansAsync();
        var name = plans.FirstOrDefault(p => p.Guid == control.Before)?.Name;
        return $"Power plan → {name ?? control.Before}";
    }

    // --- Presets ---

    public IReadOnlyList<GameModePreset> Presets()
        => GamingState.Read<List<GameModePreset>>(GamingState.PresetsFile) ?? new List<GameModePreset>();

    public bool SavePreset(GameModePreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name)) return false;

        var presets = Presets().ToList();
        presets.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        presets.Add(preset);
        GamingState.Write(GamingState.PresetsFile, presets);
        return true;
    }

    public bool DeletePreset(string name)
    {
        var presets = Presets().ToList();
        var kept = presets
            .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (kept.Count == presets.Count) return false;

        GamingState.Write(GamingState.PresetsFile, kept);
        return true;
    }

    public GameModePreset? FindPreset(string name)
        => Presets().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static GameModeOptions ToOptions(GameModePreset preset) => new()
    {
        Plan = preset.Plan ?? "high-performance",
        Notifications = preset.Notifications,
        BackgroundApps = preset.BackgroundApps,
        Services = preset.Services,
        ForProcess = preset.PriorityProcess,
        GamePath = preset.GamePath,
    };

    // --- Control construction ---

    /// <summary>
    /// Whether a control belongs in this session.
    ///
    /// Two filters, both must pass: the options have to have the control turned
    /// on (the <c>--no-*</c> switches exist so a user can keep part of the
    /// machine alone), and an explicit <c>--controls</c> list, when present,
    /// narrows the run further. A control that fails either is not attempted,
    /// not refused, and not reported — nothing was done to it, so there is
    /// nothing to say about it.
    /// </summary>
    private static bool Wanted(GameModeOptions options, string kind)
    {
        var enabled = kind switch
        {
            "notifications" => options.Notifications,
            "background-apps" => options.BackgroundApps,
            "service" => options.Services.Count > 0,
            "priority" => !string.IsNullOrWhiteSpace(options.ForProcess),
            _ => true,
        };
        if (!enabled) return false;

        return options.Only == null || options.Only.Contains(kind, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A control refused before anything was written. Never enters a session.</summary>
    private static GameModeControl Refused(string kind, string target, string error) => new()
    {
        Kind = kind,
        Target = target,
        Description = error,
        Applied = false,
        Error = error,
        Restorable = false,
    };

    internal static GameModeControl CaptureRegistryControl(string kind, string keyPath, string valueName,
        string writeData, string description)
    {
        string? before;
        bool existed;
        try
        {
            var (root, subKey) = ResolveRegistry(keyPath);
            using var key = root.OpenSubKey(subKey, writable: false);
            var value = key?.GetValue(valueName);
            existed = value != null;
            before = value?.ToString();
        }
        catch (Exception ex)
        {
            return Refused(kind, $"{keyPath}\\{valueName}", $"Could not read {keyPath}\\{valueName}: {ex.Message}");
        }

        return new GameModeControl
        {
            Kind = kind,
            Target = $"{keyPath}\\{valueName}",
            Description = $"{description} ({valueName} = {writeData})",
            Before = before,
            BeforeExisted = existed,
            After = writeData,
            Restorable = true,
        };
    }

    private async Task<GameModeControl> CaptureServiceAsync(string service)
    {
        var target = $"service:{service}";
        var (allowed, _, reason) = SecurityBoundaries.CheckServiceProtection(service);
        if (!allowed)
            return Refused("service", target, $"Blocked by a security boundary: {reason}");

        if (!IsElevated())
            return Refused("service", target, "Requires administrator — stopping a service talks to the service control manager.");

        try
        {
            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                $"$s = Get-Service -Name '{Escape(service)}' -ErrorAction Stop; $s.Status.ToString()",
            }, TimeSpan.FromSeconds(20));

            if (!result.Success)
                return Refused("service", target, $"Service '{service}' could not be read: {result.Output.Trim()}");

            var status = result.StdOut.Trim();
            return new GameModeControl
            {
                Kind = "service",
                Target = target,
                Description = $"Stop {service} for the session (was {status})",
                Before = status,
                BeforeExisted = true,
                After = "Stopped",
                Restorable = true,
            };
        }
        catch (Exception ex)
        {
            return Refused("service", target, ex.Message);
        }
    }

    private static GameModeControl CapturePriority(string process)
    {
        var target = $"process:{process}";
        var instances = Process.GetProcessesByName(process);
        try
        {
            if (instances.Length == 0)
                return Refused("priority", target, $"No running process named '{process}'.");

            string? before;
            try { before = instances[0].PriorityClass.ToString(); }
            catch { before = null; }

            if (before == null)
                return Refused("priority", target, $"Priority of '{process}' could not be read.");

            return new GameModeControl
            {
                Kind = "priority",
                Target = target,
                Description = $"Raise {process} to AboveNormal priority while it runs",
                Before = before,
                BeforeExisted = true,
                After = "AboveNormal",
                // Priority belongs to a running process and dies with it —
                // there is nothing left to put back once the game exits.
                Restorable = false,
            };
        }
        finally
        {
            foreach (var p in instances) p.Dispose();
        }
    }

    // --- Apply / restore ---

    internal async Task<(bool Ok, string? Error)> ApplyAsync(GameModeControl control)
    {
        try
        {
            switch (control.Kind)
            {
                case "power-plan":
                    await ProcessRunner.RunStrictAsync("powercfg.exe", new[] { "/setactive", control.Target });
                    return (true, null);

                case "notifications":
                case "background-apps":
                    return WriteRegistryControl(control.Target, control.After);

                case "service":
                {
                    var name = control.Target["service:".Length..];
                    var result = await ProcessRunner.RunAsync("powershell.exe", new[]
                    {
                        "-NoProfile", "-NonInteractive", "-Command",
                        $"Stop-Service -Name '{Escape(name)}' -Force -ErrorAction Stop; 'stopped'",
                    }, TimeSpan.FromSeconds(30));
                    if (!result.Success)
                        return (false, result.Output.Trim());
                    return (true, control.Before == "Stopped" ? "Already stopped; nothing changed." : null);
                }

                case "priority":
                {
                    var name = control.Target["process:".Length..];
                    var result = await ProcessRunner.RunAsync("powershell.exe", new[]
                    {
                        "-NoProfile", "-NonInteractive", "-Command",
                        $"$p = Get-Process -Name '{Escape(name)}' -ErrorAction SilentlyContinue; " +
                        "if (-not $p) { Write-Error 'no such process'; exit 1 }; " +
                        "$p | ForEach-Object { $_.PriorityClass = 'AboveNormal' }; 'ok'",
                    }, TimeSpan.FromSeconds(30));
                    return result.Success ? (true, null) : (false, result.Output.Trim());
                }

                default:
                    return (false, $"Unknown control kind '{control.Kind}'.");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    internal async Task<(bool Ok, string? Error)> RestoreAsync(GameModeControl control)
    {
        try
        {
            switch (control.Kind)
            {
                case "power-plan":
                    if (string.IsNullOrWhiteSpace(control.Before))
                        return (false, "No previous power plan was recorded.");
                    await ProcessRunner.RunStrictAsync("powercfg.exe", new[] { "/setactive", control.Before });
                    return (true, null);

                case "notifications":
                case "background-apps":
                    // Before == null means there was no policy here at all, and
                    // the original state is the value being absent — deleting
                    // restores it, writing an empty string does not.
                    return WriteRegistryControl(control.Target, control.Before);

                case "service":
                {
                    if (!string.Equals(control.Before, "Running", StringComparison.OrdinalIgnoreCase))
                        return (true, null);

                    var name = control.Target["service:".Length..];
                    var result = await ProcessRunner.RunAsync("powershell.exe", new[]
                    {
                        "-NoProfile", "-NonInteractive", "-Command",
                        $"Start-Service -Name '{Escape(name)}' -ErrorAction Stop; 'started'",
                    }, TimeSpan.FromSeconds(30));
                    return result.Success ? (true, null) : (false, result.Output.Trim());
                }

                default:
                    return (true, null);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Writes a DWORD, or deletes the value when <paramref name="value"/> is
    /// null. Deletion matters: "this policy was never set" and "this policy is
    /// set to nothing" are different states, and only the first one was there.
    /// </summary>
    private static (bool Ok, string? Error) WriteRegistryControl(string target, string? value)
    {
        var split = target.LastIndexOf('\\');
        if (split <= 0) return (false, $"'{target}' is not a registry value path.");

        var keyPath = target[..split];
        var valueName = target[(split + 1)..];

        try
        {
            var (root, subKey) = ResolveRegistry(keyPath);

            if (value == null)
            {
                using var read = root.OpenSubKey(subKey);
                if (read == null) return (true, null);
                if (Array.IndexOf(read.GetValueNames(), valueName) < 0) return (true, null);

                using var writable = root.OpenSubKey(subKey, writable: true)
                    ?? throw new UnauthorizedAccessException($"Cannot write {keyPath}.");
                writable.DeleteValue(valueName, throwOnMissingValue: false);
                return (true, null);
            }

            using (var key = root.CreateSubKey(subKey))
            {
                if (key == null) return (false, $"Cannot create {keyPath}.");
                if (int.TryParse(value, out var dword))
                    key.SetValue(valueName, dword, RegistryValueKind.DWord);
                else
                    key.SetValue(valueName, value, RegistryValueKind.String);
            }
            return (true, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Requires administrator.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (RegistryKey Root, string SubKey) ResolveRegistry(string keyPath)
    {
        var normalized = keyPath.Replace("/", "\\").Trim().TrimEnd('\\');
        var shortHive = normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
            ? "HKEY_LOCAL_MACHINE" + normalized[4..]
            : normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                ? "HKEY_CURRENT_USER" + normalized[4..]
                : normalized;

        var split = shortHive.IndexOf('\\');
        if (split <= 0) throw new ArgumentException($"'{keyPath}' is not a registry path.");

        var hiveName = shortHive[..split];
        var subKey = shortHive[(split + 1)..];

        var hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException($"Unsupported registry hive '{hiveName}' in '{keyPath}'."),
        };

        // The 64-bit view is opened explicitly so the answer does not depend on
        // which process architecture this code happens to be running in.
        return (RegistryKey.OpenBaseKey(hive, RegistryView.Registry64), subKey);
    }

    // --- Power plans ---

    private sealed record Plan(string Guid, string Name);

    private static async Task<string?> ReadActivePlanAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("powercfg.exe", new[] { "/getactivescheme" },
                TimeSpan.FromSeconds(15));
            var match = Regex.Match(result.StdOut, @"([0-9a-fA-F\-]{36})");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Plan?> ResolvePlanAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        if (Guid.TryParse(token, out _))
            return new Plan(token.ToLowerInvariant(), token);

        var wanted = Normalize(token);
        var schemes = await ListPlansAsync();

        foreach (var scheme in schemes)
            if (Normalize(scheme.Name) == wanted)
                return scheme;

        // Alias names (SCHEME_MIN …) are how powercfg spells them.
        foreach (var alias in await AliasesAsync())
        {
            if (Normalize(alias.Name) != wanted) continue;
            return schemes.FirstOrDefault(s => s.Guid == alias.Guid) ?? new Plan(alias.Guid, alias.Name);
        }

        return null;
    }

    private static string Normalize(string value)
        => new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static async Task<List<Plan>> ListPlansAsync()
    {
        var plans = new List<Plan>();
        try
        {
            var result = await ProcessRunner.RunAsync("powercfg.exe", new[] { "/list" }, TimeSpan.FromSeconds(15));
            foreach (var line in result.StdOut.Split('\n'))
            {
                var guid = Regex.Match(line, @"([0-9a-fA-F\-]{36})");
                if (!guid.Success) continue;
                var name = Regex.Match(line, @"\(([^)]+)\)");
                plans.Add(new Plan(guid.Groups[1].Value.ToLowerInvariant(),
                    name.Success ? name.Groups[1].Value.Trim() : guid.Groups[1].Value));
            }
        }
        catch
        {
            // An unreadable scheme list means a named plan cannot be matched —
            // reported as "no such plan", not as a crash.
        }
        return plans;
    }

    private static async Task<List<Plan>> AliasesAsync()
    {
        var aliases = new List<Plan>();
        try
        {
            var result = await ProcessRunner.RunAsync("powercfg.exe", new[] { "/aliases" }, TimeSpan.FromSeconds(15));
            foreach (var line in result.StdOut.Split('\n'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                if (!Guid.TryParse(parts[0], out _)) continue;
                aliases.Add(new Plan(parts[0].ToLowerInvariant(), parts[1]));
            }
        }
        catch { }
        return aliases;
    }

    // --- Journal ---

    private void Audit(GameModeControl control, string operation, string sessionId,
        bool success, string? error)
    {
        if (_logger == null) return;
        _logger.AuditFeature(
            featureId: $"game-mode:{control.Kind}",
            operation: operation,
            target: control.Target,
            oldValue: control.Before,
            newValue: operation == "start" ? control.After : control.Before,
            result: success ? "success" : "failure",
            error: error,
            elevationUsed: IsElevated(),
            sessionId: sessionId);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
