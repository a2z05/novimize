using System.Text.RegularExpressions;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Installer;

/// <summary>
/// The winget integration layer.
///
/// Every call here is a real winget process — nothing is faked, and nothing
/// reports success on a command that failed. Two rules shape the flags:
///
/// * <c>--source winget</c> on reads. Without it winget also queries msstore,
///   which on this machine is unreachable and turns a 2-second list into a
///   40-second one while printing an error line into stdout ahead of the table.
/// * <c>--disable-interactivity</c> everywhere. A CLI that can block waiting
///   for a prompt nobody can see is a CLI that hangs; when something genuinely
///   needs a decision, winget says so in its output and that is returned.
///
/// Install, uninstall and upgrade are allowed a long timeout: a real installer
/// can run for minutes, and killing it half-way would leave the machine in a
/// state neither we nor the user can describe.
/// </summary>
public static class WinGet
{
    public const string Exe = "winget";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Winget IDs are dotted tokens. Anything else never reaches a
    /// command line — an ID that could carry a quote or a redirect has no
    /// business being passed to a shell, elevated or not.</summary>
    private static readonly Regex SafeId = new("^[A-Za-z0-9][A-Za-z0-9._+#-]{0,199}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id) && SafeId.IsMatch(id);

    // === reads ===

    public static async Task<WinGetInfo> ProbeAsync()
    {
        var versionResult = await ProcessRunner.RunAsync(Exe, new[] { "--version" },
            TimeSpan.FromSeconds(20));
        if (!versionResult.Success)
        {
            return new WinGetInfo
            {
                Available = false,
                Error = "winget is not available on this machine. " +
                        "It ships with the App Installer package from the Microsoft Store; " +
                        $"the command exited with {versionResult.ExitCode}.",
            };
        }

        var info = new WinGetInfo
        {
            Available = true,
            Version = versionResult.Output.Trim(),
        };

        // `source list` has no --accept-source-agreements: passing it makes
        // winget print "Argument name was not recognized" and a usage page
        // instead of the table, which reads as "this machine has no sources".
        var sources = await ProcessRunner.RunAsync(Exe,
            new[] { "source", "list", "--disable-interactivity" },
            TimeSpan.FromSeconds(30));
        info.Sources.AddRange(ParseSources(sources.Output));

        return info;
    }

    public static async Task<List<InstalledPackage>> ListInstalledAsync(bool deep = false)
    {
        var args = new List<string> { "list", "--disable-interactivity", "--accept-source-agreements" };
        if (!deep)
        {
            // The fast pass reads winget's own index and skips the local
            // Add/Remove Programs scan, which is what makes the difference
            // between two seconds and forty. The deep pass reads everything,
            // including copies that were installed by hand and are only
            // visible through ARP.
            args.AddRange(new[] { "--source", "winget" });
        }

        var result = await ProcessRunner.RunAsync(Exe, args, deep ? TimeSpan.FromMinutes(3) : ReadTimeout);
        return WingetTable.Parse(result.Output);
    }

    /// <summary>
    /// The authoritative answer for one package.
    ///
    /// Filtering by ID makes winget resolve the manifest and cross-reference it
    /// against Add/Remove Programs, so a copy installed by hand still comes back
    /// under its real ID. The unfiltered list does not do that: it reports
    /// Firefox as "ARP\Machine\X64\Mozilla Firefox", which no catalogue entry
    /// can match. The price is time — this is a full ARP scan, around ten
    /// seconds — so it is used where the answer changes what the user can do,
    /// not to fill a list.
    /// </summary>
    public static async Task<List<InstalledPackage>> ListOneAsync(string id)
    {
        if (!IsSafeId(id)) return new List<InstalledPackage>();

        var result = await ProcessRunner.RunAsync(Exe, new[]
        {
            "list", "--id", id, "--exact", "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements",
        }, TimeSpan.FromSeconds(120));

        // A miss exits non-zero with a sentence and no table, so an empty parse
        // is the answer rather than an error.
        return WingetTable.Parse(result.Output);
    }

    public static async Task<List<InstalledPackage>> SearchAsync(string query)
    {
        var result = await ProcessRunner.RunAsync(Exe, new[]
        {
            "search", query,
            "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements",
        }, ReadTimeout);
        return WingetTable.Parse(result.Output);
    }

    public static async Task<PackageDetail> ShowAsync(string id)
    {
        if (!IsSafeId(id))
            return new PackageDetail { Id = id, Error = $"'{id}' is not a package ID this application will run winget against." };

        var result = await ProcessRunner.RunAsync(Exe, new[]
        {
            "show", "--id", id, "--exact",
            "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements",
        }, ReadTimeout);

        var detail = ParseShow(result.Output);
        if (detail is null)
        {
            return new PackageDetail
            {
                Id = id,
                Error = "winget found no package with that ID. " + FirstLine(result.Output),
            };
        }

        return detail with { Id = id, Error = result.Success ? null : FirstLine(result.Output) };
    }

    /// <summary>List upgrades winget knows about, restricted to its own source
    /// so the answer is fast and does not include the broken msstore.</summary>
    public static async Task<List<InstalledPackage>> ListUpgradesAsync()
    {
        var result = await ProcessRunner.RunAsync(Exe, new[]
        {
            "upgrade", "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements",
        }, ReadTimeout);
        return WingetTable.Parse(result.Output);
    }

    // === writes ===

    public static Task<AppChange> InstallAsync(string id, InstallScope scope, bool elevated) =>
        elevated
            ? RunElevatedAsync("install", id, scope)
            : RunAsync(BuildInstallArgs(id, scope), "install", id, scope);

    public static Task<AppChange> UninstallAsync(string id, bool elevated) =>
        elevated
            ? RunElevatedAsync("uninstall", id, InstallScope.Any)
            : RunAsync(new[] { "uninstall", "--id", id, "--exact", "--disable-interactivity" },
                "uninstall", id, InstallScope.Any);

    public static Task<AppChange> UpgradeAsync(string id, bool elevated) =>
        elevated
            ? RunElevatedAsync("upgrade", id, InstallScope.Any)
            : RunAsync(new[]
            {
                "upgrade", "--id", id, "--exact", "--source", "winget",
                "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements",
            }, "upgrade", id, InstallScope.Any);

    public static async Task<AppChange> UpgradeAllAsync()
    {
        var result = await ProcessRunner.RunAsync(Exe, new[]
        {
            "upgrade", "--all", "--source", "winget",
            "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements",
        }, WriteTimeout);

        var log = Combined(result);
        return new AppChange
        {
            Action = "upgrade-all",
            Id = "*",
            Success = result.ExitCode == 0,
            Unchanged = result.ExitCode != 0 && SaysNothingToDo(log),
            Message = result.ExitCode == 0
                ? "Everything winget knew how to upgrade has been upgraded."
                : FirstLine(log),
            ExitCode = Unsigned(result.ExitCode),
            Log = log,
        };
    }

    private static string[] BuildInstallArgs(string id, InstallScope scope)
    {
        var args = new List<string>
        {
            "install", "--id", id, "--exact", "--source", "winget",
            "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements",
        };
        var scopeName = scope switch
        {
            InstallScope.User => "user",
            InstallScope.Machine => "machine",
            _ => null,
        };
        // "Any" omits the flag on purpose: forcing a scope the package does not
        // support is how you get a failed install that reports nothing useful.
        if (scopeName is not null)
        {
            args.Add("--scope");
            args.Add(scopeName);
        }
        return args.ToArray();
    }

    private static async Task<AppChange> RunAsync(string[] args, string action, string id, InstallScope scope)
    {
        var result = await ProcessRunner.RunAsync(Exe, args, WriteTimeout);
        var log = Combined(result);
        var nothing = !result.Success && SaysNothingToDo(log);
        var needsElevation = !result.Success && NeedsElevation(log);

        return new AppChange
        {
            Action = action,
            Id = id,
            Success = result.ExitCode == 0,
            Unchanged = nothing,
            Message = Describe(action, result.ExitCode == 0, nothing, needsElevation, log),
            ExitCode = Unsigned(result.ExitCode),
            Log = log,
            Scope = ScopeName(scope),
            NeedsElevation = needsElevation,
        };
    }

    /// <summary>
    /// The same winget call, launched through UAC.
    ///
    /// Output cannot be piped out of an elevated process, so the command line
    /// is written to a file by cmd instead and read back afterwards. Anything
    /// else and the only answer to "what went wrong" would be an exit code.
    /// </summary>
    private static async Task<AppChange> RunElevatedAsync(string action, string id, InstallScope scope)
    {
        var args = action switch
        {
            "install" => BuildInstallArgs(id, scope),
            "uninstall" => new[] { "uninstall", "--id", id, "--exact", "--disable-interactivity" },
            "upgrade" => new[]
            {
                "upgrade", "--id", id, "--exact", "--source", "winget",
                "--disable-interactivity", "--accept-package-agreements", "--accept-source-agreements",
            },
            _ => throw new ArgumentException($"Unknown elevated action '{action}'."),
        };

        var logPath = Path.Combine(Path.GetTempPath(), $"novimize-winget-{Guid.NewGuid():N}.log");
        // Only the log path is quoted. Every other token is either one of this
        // file's own literals or an ID that has already been through
        // IsSafeId, so there is nothing in it that cmd could read as syntax.
        var line = string.Join(' ', args) + $" > {Quote(logPath)} 2>&1";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + line,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        var log = string.Empty;
        int exitCode;
        try
        {
            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start the elevated command.");
            using var cancellation = new CancellationTokenSource(WriteTimeout);
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new AppChange
                {
                    Action = action,
                    Id = id,
                    Success = false,
                    Message = $"winget did not finish within {WriteTimeout.TotalMinutes:0} minutes and was stopped.",
                    Scope = ScopeName(scope),
                    NeedsElevation = true,
                };
            }
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — the user declined the UAC prompt. That is an
            // answer, not a fault, and it should read like one.
            return new AppChange
            {
                Action = action,
                Id = id,
                Success = false,
                Unchanged = true,
                Message = "Administrator permission was declined, so nothing was changed.",
                Scope = ScopeName(scope),
                NeedsElevation = true,
            };
        }
        catch (Exception ex)
        {
            return new AppChange
            {
                Action = action,
                Id = id,
                Success = false,
                Message = $"Could not start an elevated winget: {ex.Message}",
                Scope = ScopeName(scope),
                NeedsElevation = true,
            };
        }

        if (File.Exists(logPath))
        {
            try
            {
                log = await File.ReadAllTextAsync(logPath);
                File.Delete(logPath);
            }
            catch { /* a leftover temp file is not worth failing over */ }
        }

        var unchanged = exitCode != 0 && SaysNothingToDo(log);
        return new AppChange
        {
            Action = action,
            Id = id,
            Success = exitCode == 0,
            Unchanged = unchanged,
            Message = exitCode == 0
                ? $"{action} finished as administrator."
                : Describe(action, false, unchanged, NeedsElevation(log), log),
            ExitCode = Unsigned(exitCode),
            Log = log.Length > 0
                ? log
                : "winget ran elevated but produced no readable log. Treat the result as unknown and check the app list.",
            Scope = ScopeName(scope),
        };
    }

    // === helpers ===

    private static string Describe(string action, bool success, bool unchanged, bool needsElevation, string log)
    {
        if (success)
            return action switch
            {
                "install" => "Installed.",
                "uninstall" => "Uninstalled.",
                "upgrade" => "Upgraded.",
                _ => $"{action} finished.",
            };
        if (unchanged)
            return "Nothing to do — winget reported the package is already in that state.";
        if (needsElevation)
            return "winget refused because this needs administrator rights.";
        return FirstLine(log);
    }

    /// <summary>Winget exits non-zero with a plain sentence when there is
    /// nothing to change. Reporting that as a failure would be a lie.</summary>
    private static bool SaysNothingToDo(string log) =>
        Contains(log, "no newer package versions")
        || Contains(log, "already installed")
        || Contains(log, "no applicable update")
        || Contains(log, "no installed package found");

    private static bool NeedsElevation(string log) =>
        Contains(log, "levation");   // elevation / elevated / Elevation required

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string Combined(CommandResult result)
    {
        var outText = result.StdOut?.Trim() ?? string.Empty;
        var errText = result.StdErr?.Trim() ?? string.Empty;
        if (outText.Length == 0) return errText;
        if (errText.Length == 0) return outText;
        return Contains(errText, outText) ? errText : outText + Environment.NewLine + errText;
    }

    private static string FirstLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("---", StringComparison.Ordinal)) continue;
            return line;
        }
        return "winget produced no output.";
    }

    /// <summary>Winget's exit codes are HRESULTs. Read as an int they arrive
    /// negative — 0x8A150014 as -1978335212 — which nobody can look up, so they
    /// are widened to the unsigned value winget itself reports.</summary>
    private static long Unsigned(int exitCode) => unchecked((uint)exitCode);

    private static string ScopeName(InstallScope scope) => scope switch
    {
        InstallScope.User => "user",
        InstallScope.Machine => "machine",
        _ => "any",
    };

    /// <summary>Quote a token for cmd.exe. Only ever applied to IDs that have
    /// already passed <see cref="IsSafeId"/>, and to strings this file built.</summary>
    private static string Quote(string token) =>
        "\"" + token.Replace("\"", "\\\"").Replace("%", "%%") + "\"";

    private static List<WinGetSource> ParseSources(string output)
    {
        var sources = new List<WinGetSource>();
        var lines = WingetTable.Split(output);
        var headerIndex = lines.ToList().FindIndex(l => l.StartsWith("Name", StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length) return sources;
        if (!WingetTable.IsSeparator(lines[headerIndex + 1])) return sources;

        var header = lines[headerIndex];
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var values = WingetTable.ReadRow(header, lines[i]);
            if (!values.TryGetValue("Name", out var name)) continue;
            sources.Add(new WinGetSource
            {
                Name = name,
                Argument = values.GetValueOrDefault("Argument", string.Empty),
                Explicit = values.GetValueOrDefault("Explicit", string.Empty)
                    .Equals("true", StringComparison.OrdinalIgnoreCase),
            });
        }
        return sources;
    }

    /// <summary>Parse <c>winget show</c>'s key: value blocks, including the
    /// indented Installer: section.</summary>
    private static PackageDetail? ParseShow(string output)
    {
        var lines = WingetTable.Split(output);
        var found = lines.FirstOrDefault(l => l.StartsWith("Found ", StringComparison.Ordinal));
        if (found is null) return null;

        string? Field(string name) =>
            lines.FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim();

        // The installer block is indented one level under "Installer:"; a plain
        // scan of the whole output would still find them because the labels are
        // unique, but the indented form is matched by its label either way.
        string? Installer(string name) =>
            lines.FirstOrDefault(l => l.TrimStart().StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?
                .Trim()[(name.Length + 1)..].Trim();

        var name = found["Found ".Length..].Trim();
        var bracket = name.LastIndexOf('[');
        if (bracket >= 0 && name.EndsWith(']'))
        {
            // "Found 7-Zip [7zip.7zip]" — the title, not the id (the id is
            // echoed back by the caller, who already knows it).
            name = name[..bracket].Trim();
        }

        return new PackageDetail
        {
            Name = name,
            Version = Field("Version"),
            Publisher = Field("Publisher"),
            PublisherUrl = Field("Publisher Url"),
            Homepage = Field("Homepage"),
            License = Field("License"),
            Description = Field("Description"),
            InstallerType = Installer("Installer Type"),
            InstallerUrl = Installer("Installer Url"),
            InstallerSha256 = Installer("Installer SHA256"),
            ReleaseDate = Installer("Release Date") ?? Field("Release Date"),
            Source = "winget",
        };
    }
}
