using System.Text.Json;
using System.Text.RegularExpressions;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Power;

/// <summary>
/// What the power configuration is, and how to move between plans.
///
/// Every value here comes from <c>powercfg</c> rather than from the registry
/// layout underneath it: the registry holds whatever the scheme overrides and
/// falls back silently to a default for everything else, so a missing key
/// reads as "not configured" when it means "inherits". powercfg answers for
/// the setting whether or not the scheme stores it.
///
/// Nothing in this class adds a second definition of any setting the tweak
/// catalogue already manages — it reads them. Plan switching is the one thing
/// it writes, and it switches to a plan Windows actually lists rather than to
/// a GUID compiled in here, because a machine can hold half a dozen schemes
/// all called "Ultimate Performance".
/// </summary>
public sealed class PowerCenter
{
    /// <summary>Well-known scheme GUIDs. Everything else is whatever the machine has.</summary>
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    /// <summary>
    /// What the "Novimize" plan sets. Every one of these is also a tweak in
    /// its own right — the difference is that those change the plan you are
    /// on, while this creates a scheme you can switch away from and come back
    /// to, which is the thing a plan is for.
    /// </summary>
    private static readonly (string Subgroup, string Setting, int Ac, int Dc)[] NovimizeValues =
    {
        ("54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, 5),
        ("54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", 100, 100),
        ("54533251-82be-4824-96c1-47b60b740d00", "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1, 0),
        ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100, 0),
        ("54533251-82be-4824-96c1-47b60b740d00", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0, 100),
        ("54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 2, 0),
    };

    private readonly string _statePath;

    public PowerCenter(string? statePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "power");
        _statePath = statePath ?? Path.Combine(dir, "plan.json");
    }

    private sealed record SavedPlan
    {
        public string? Guid { get; init; }
        public string? Name { get; init; }
        public DateTimeOffset At { get; init; }
    }

    // --- reading -----------------------------------------------------------

    public async Task<PowerStatus> StatusAsync(CancellationToken cancel = default)
    {
        var activeRaw = await RunAsync(new[] { "/getactivescheme" }, cancel);
        var activeGuid = ParseGuid(activeRaw ?? string.Empty) ?? string.Empty;

        var listRaw = await RunAsync(new[] { "/list" }, cancel);
        var plans = ParsePlanList(listRaw ?? string.Empty);

        var settings = new List<PowerSetting>();
        foreach (var spec in SettingSpecs)
            settings.Add(await ReadSettingAsync(spec, cancel));

        var battery = await ReadBatteryAsync(cancel);
        var saved = Load();
        var formFactor = DetectFormFactor();

        var activeName = plans.FirstOrDefault(p => p.Active)?.Name
                         ?? plans.FirstOrDefault(p => p.Guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase))?.Name
                         ?? (string.IsNullOrEmpty(activeGuid) ? "unknown" : activeGuid);

        return new PowerStatus
        {
            Plans = plans,
            ActivePlan = activeName,
            ActivePlanGuid = activeGuid,
            Settings = settings,
            Battery = battery,
            FormFactor = formFactor,
            IsLaptop = formFactor is "Laptop" or "Tablet",
            PreviousPlanGuid = saved?.Guid,
            PreviousPlanName = saved?.Name,
            UltimateAvailable = plans.Any(p =>
                p.Guid.Equals(Ultimate, StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)),
            Error = activeGuid.Length == 0 ? "powercfg did not report an active scheme." : null,
        };
    }

    private static readonly (string Id, string Name, string Subgroup, string Setting, string Unit, string? Note, string? Tweak)[] SettingSpecs =
    {
        ("sleep", "Sleep after",
            "238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da",
            "seconds", "Sleeping costs a moment to wake; never sleeping costs power overnight.", null),
        ("hibernate", "Hibernate after",
            "238c9fa8-0aad-41ed-83f4-97be242c8f20", "9d7815a6-7ee4-497e-8888-515a05f02364",
            "seconds", "Hibernation writes RAM to disk, so it survives a flat battery.", null),
        ("display", "Turn off display after",
            "7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e",
            "seconds", "The monitor is usually the largest thing drawing power while you are away.", null),
        ("usb", "USB selective suspend",
            "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226",
            "enum", "Turning it off stops USB devices sleeping — a mouse or DAC that goes quiet is the usual cost.", null),
        ("pcie", "PCI Express link state power management",
            "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5",
            "enum", "Aggressive link power saving can add latency to NVMe and network devices.", null),
        ("procMin", "Minimum processor state",
            "54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c",
            "percent", "A higher floor means less time ramping up, and more idle power.", "cpu-power.minState.performance"),
        ("procMax", "Maximum processor state",
            "54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec",
            "percent", "Below 100% caps the clock — the usual way to cap heat on a laptop.", null),
        ("cooling", "System cooling policy",
            "54533251-82be-4824-96c1-47b60b740d00", "94d3a615-a899-4ac5-ae2b-e4d8f634367f",
            "enum", "Active ramps the fans first; passive throttles the CPU first.", "cpu-power.cooling.active"),
        ("boost", "Processor performance boost mode",
            "54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7",
            "enum", "Boost is where most of the heat under load comes from.", "cpu-power.turbo.aggressive"),
        ("parking", "Core parking minimum cores",
            "54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583",
            "percent", "Parking cores saves power and costs a beat when they wake.", "cpu-power.cores.parkingMax"),
        ("epp", "Energy performance preference",
            "54533251-82be-4824-96c1-47b60b740d00", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863",
            "percent", "0 leans performance, 100 leans battery. Intel Speed Shift acts on this.", "cpu-power.epp.performance"),
    };

    private async Task<PowerSetting> ReadSettingAsync(
        (string Id, string Name, string Subgroup, string Setting, string Unit, string? Note, string? Tweak) spec,
        CancellationToken cancel)
    {
        var output = await RunAsync(new[] { "/qh", "SCHEME_CURRENT", spec.Subgroup, spec.Setting }, cancel);
        if (string.IsNullOrWhiteSpace(output) || !output.Contains(spec.Setting, StringComparison.OrdinalIgnoreCase))
        {
            return new PowerSetting
            {
                Id = spec.Id,
                Name = spec.Name,
                Unit = spec.Unit,
                Note = spec.Note,
                ExistingTweak = spec.Tweak,
                Unavailable = true,
                Value = "not exposed by this scheme",
            };
        }

        var parsed = ParseSetting(output, spec.Setting);
        return new PowerSetting
        {
            Id = spec.Id,
            Name = spec.Name,
            Unit = spec.Unit,
            Note = spec.Note,
            ExistingTweak = spec.Tweak,
            Value = Format(parsed.Ac, parsed, spec.Unit),
            BatteryValue = parsed.Dc is null ? null : Format(parsed.Dc, parsed, spec.Unit),
            Unavailable = parsed.Ac is null,
        };
    }

    /// <summary>
    /// Read one setting out of a powercfg query.
    ///
    /// The rule that makes this survive a localized Windows: after the line
    /// carrying the setting's GUID, the values that belong to the setting sit
    /// at that line's indentation — everything describing the range sits deeper
    /// — and every one of them is hex. The labels around them are translated;
    /// the indentation and the hex are not.
    /// </summary>
    public static ParsedSetting ParseSetting(string output, string settingGuid)
    {
        var lines = output.Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains(settingGuid, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return new ParsedSetting(null, null, null, new Dictionary<int, string>(), null, null);

        var indent = lines[start].Length - lines[start].TrimStart().Length;
        int? ac = null, dc = null;
        string? units = null;
        long? min = null, max = null;
        var possible = new Dictionary<int, string>();
        int? pending = null;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var lineIndent = line.Length - line.TrimStart().Length;
            var trimmed = line.Trim();

            // The block ends where the next peer of the setting begins.
            if (lineIndent < indent && !trimmed.StartsWith("Current", StringComparison.OrdinalIgnoreCase)) break;
            if (lineIndent == indent && trimmed.StartsWith("Power Setting GUID", StringComparison.OrdinalIgnoreCase)) break;

            var hex = Regex.Matches(line, @"0x([0-9a-fA-F]{8})");
            if (lineIndent == indent && hex.Count > 0 && trimmed.StartsWith("Current", StringComparison.OrdinalIgnoreCase))
            {
                var value = Convert.ToInt32(hex[0].Groups[1].Value, 16);
                if (ac is null) ac = value;
                else if (dc is null) dc = value;
                continue;
            }

            if (trimmed.StartsWith("Possible Settings units", StringComparison.OrdinalIgnoreCase))
            {
                var colon = trimmed.IndexOf(':');
                units = colon >= 0 ? trimmed[(colon + 1)..].Trim() : null;
            }
            else if (trimmed.StartsWith("Minimum Possible Setting", StringComparison.OrdinalIgnoreCase))
            {
                min = HexAt(trimmed);
            }
            else if (trimmed.StartsWith("Maximum Possible Setting", StringComparison.OrdinalIgnoreCase))
            {
                max = HexAt(trimmed);
            }
            else if (trimmed.StartsWith("Possible Setting Index", StringComparison.OrdinalIgnoreCase))
            {
                var colon = trimmed.IndexOf(':');
                pending = colon >= 0 && int.TryParse(trimmed[(colon + 1)..].Trim(), out var n) ? n : null;
            }
            else if (trimmed.StartsWith("Possible Setting Friendly Name", StringComparison.OrdinalIgnoreCase) && pending is not null)
            {
                var colon = trimmed.IndexOf(':');
                if (colon >= 0)
                {
                    var label = trimmed[(colon + 1)..].Trim();
                    if (!possible.ContainsKey(pending.Value)) possible[pending.Value] = label;
                }
                pending = null;
            }
        }

        return new ParsedSetting(ac, dc, units, possible, min, max);

        static long? HexAt(string line)
        {
            var m = Regex.Match(line, @"0x([0-9a-fA-F]+)");
            return m.Success ? Convert.ToInt64(m.Groups[1].Value, 16) : null;
        }
    }

    public static string Format(int? value, ParsedSetting parsed, string unit)
    {
        if (value is null) return "—";
        if (parsed.Possible.TryGetValue(value.Value, out var label)) return label;

        switch (unit)
        {
            case "seconds":
                return value == 0
                    ? "Never"
                    : value < 60
                        ? $"{value} s"
                        : value % 60 == 0
                            ? $"{value / 60} min"
                            : $"{value / 60} min {value % 60} s";
            case "percent":
                return $"{value}%";
            default:
                return value.Value.ToString();
        }
    }

    // --- plans -------------------------------------------------------------

    /// <summary>
    /// Parse `powercfg /list`. The prefix and the plan names are translated;
    /// the GUID is not, and the active marker is a trailing asterisk wherever
    /// it is printed.
    /// </summary>
    public static List<PowerPlan> ParsePlanList(string output)
    {
        var plans = new List<PowerPlan>();
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            var guid = ParseGuid(line);
            if (guid is null) continue;

            var name = guid;
            var open = line.LastIndexOf('(');
            var close = line.LastIndexOf(')');
            if (open >= 0 && close > open) name = line[(open + 1)..close].Trim();

            plans.Add(new PowerPlan
            {
                Guid = guid,
                Name = name,
                Active = line.EndsWith('*'),
                BuiltIn = guid.Equals(Balanced, StringComparison.OrdinalIgnoreCase)
                          || guid.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase)
                          || guid.Equals(PowerSaver, StringComparison.OrdinalIgnoreCase)
                          || guid.Equals(Ultimate, StringComparison.OrdinalIgnoreCase),
            });
        }
        return plans;
    }

    public static string? ParseGuid(string text) =>
        Regex.Match(text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
            is { Success: true } m ? m.Value : null;

    /// <summary>
    /// Switch to a plan this machine actually has. Refuses an unknown GUID
    /// rather than letting powercfg be the thing that says no, because a
    /// half-typed scheme id failing inside a system tool is a worse answer
    /// than one sentence here.
    /// </summary>
    public async Task<PowerChange> SetPlanAsync(string planGuid, bool confirm, CancellationToken cancel = default)
    {
        var status = await StatusAsync(cancel);
        var target = status.Plans.FirstOrDefault(p =>
            p.Guid.Equals(planGuid, StringComparison.OrdinalIgnoreCase)
            || p.Name.Equals(planGuid, StringComparison.OrdinalIgnoreCase));

        var commands = new List<string> { $"powercfg /setactive {target?.Guid ?? planGuid}" };

        if (target is null)
            return Fail("plan", $"'{planGuid}' is not a plan this machine has.", commands);

        if (target.Active)
            return new PowerChange
            {
                Action = "plan",
                Success = true,
                Unchanged = true,
                Message = $"{target.Name} is already the active plan.",
                Preview = commands,
            };

        if (!confirm)
            return new PowerChange
            {
                Action = "plan",
                Success = false,
                Message = $"Switching to {target.Name} changes every power setting at once. Nothing has been run.",
                Preview = commands,
            };

        // Remember where it was, so "put it back" is one click and does not
        // depend on guessing which of several schemes with the same name was
        // active a moment ago.
        if (!status.ActivePlanGuid.Equals(target.Guid, StringComparison.OrdinalIgnoreCase))
            Save(new SavedPlan { Guid = status.ActivePlanGuid, Name = status.ActivePlan, At = DateTimeOffset.Now });

        var result = await RunAsync(new[] { "/setactive", target.Guid }, cancel);
        var ok = result is not null && !result.Contains("Invalid", StringComparison.OrdinalIgnoreCase);

        return new PowerChange
        {
            Action = "plan",
            Success = ok,
            Affected = ok ? 1 : 0,
            Preview = commands,
            Message = ok
                ? $"{target.Name} is now the active plan."
                : $"powercfg did not switch plans: {result?.Trim() ?? "no output"}",
            Log = result,
        };
    }

    /// <summary>Go back to whichever plan was active before the last switch.</summary>
    public async Task<PowerChange> RevertPlanAsync(bool confirm, CancellationToken cancel = default)
    {
        var saved = Load();
        if (saved?.Guid is null)
            return new PowerChange
            {
                Action = "plan-revert",
                Success = true,
                Unchanged = true,
                Message = "Novimize has not changed the power plan, so there is nothing to put back.",
            };

        return await SetPlanAsync(saved.Guid, confirm, cancel);
    }

    /// <summary>
    /// Build or refresh a scheme called "Novimize" from Balanced, then set the
    /// values above. Running it twice updates the same scheme rather than
    /// leaving a second copy behind — which is exactly what happens to
    /// Ultimate Performance on most machines, where the list fills with six
    /// entries all with the same name.
    /// </summary>
    public async Task<PowerChange> ApplyNovimizePlanAsync(bool confirm, CancellationToken cancel = default)
    {
        var status = await StatusAsync(cancel);
        var existing = status.Plans.FirstOrDefault(p =>
            p.Name.Equals("Novimize", StringComparison.OrdinalIgnoreCase));

        var commands = new List<string>();
        if (existing is null) commands.Add($"powercfg /duplicatescheme {Balanced}");
        foreach (var (subgroup, setting, ac, dc) in NovimizeValues)
        {
            commands.Add($"powercfg /setacvalueindex SCHEME_CURRENT {subgroup} {setting} {ac}");
            commands.Add($"powercfg /setdcvalueindex SCHEME_CURRENT {subgroup} {setting} {dc}");
        }
        commands.Add("powercfg /setactive <the Novimize scheme>");
        commands.Add("powercfg /setactive SCHEME_MIN  (undo: go back to Balanced)");

        if (status.IsLaptop)
            return new PowerChange
            {
                Action = "novimize-plan",
                Success = false,
                Message = "The Novimize plan is tuned for a machine on mains power. On a laptop it would " +
                          "raise the idle floor, keep the cores unparked and lean the EPP to performance — " +
                          "which is a shorter battery for a faster machine. It is not offered here.",
                Preview = commands,
            };

        if (!confirm)
            return new PowerChange
            {
                Action = "novimize-plan",
                Success = false,
                Message = "This creates a power scheme and switches to it. Nothing has been run.",
                Preview = commands,
            };

        var log = new List<string>();
        string? created = existing?.Guid;

        if (created is null)
        {
            var dup = await RunAsync(new[] { "/duplicatescheme", Balanced }, cancel);
            created = ParseGuid(dup ?? string.Empty);
            if (created is null)
                return Fail("novimize-plan", $"powercfg could not copy the Balanced scheme: {dup?.Trim() ?? "no output"}", commands);
            log.Add(dup!.Trim());
        }

        foreach (var (subgroup, setting, ac, dc) in NovimizeValues)
        {
            log.Add((await RunAsync(new[] { "/setacvalueindex", created, subgroup, setting, ac.ToString() }, cancel))?.Trim() ?? "");
            log.Add((await RunAsync(new[] { "/setdcvalueindex", created, subgroup, setting, dc.ToString() }, cancel))?.Trim() ?? "");
        }

        await RunAsync(new[] { "/changename", created, "Novimize", "Balanced, with the settings Novimize would pick." }, cancel);
        await RunAsync(new[] { "/setactive", created }, cancel);

        var active = await RunAsync(new[] { "/getactivescheme" }, cancel);
        var nowActive = ParseGuid(active ?? string.Empty);
        var ok = created.Equals(nowActive, StringComparison.OrdinalIgnoreCase);

        if (ok && !status.ActivePlanGuid.Equals(created, StringComparison.OrdinalIgnoreCase))
            Save(new SavedPlan { Guid = status.ActivePlanGuid, Name = status.ActivePlan, At = DateTimeOffset.Now });

        return new PowerChange
        {
            Action = "novimize-plan",
            Success = ok,
            Affected = ok ? NovimizeValues.Length : 0,
            Preview = commands,
            Message = ok
                ? "The Novimize plan is active. `plan revert` puts the previous plan back."
                : $"powercfg did not finish: {active?.Trim() ?? "no output"}",
            Log = string.Join("\n", log),
        };
    }

    private static PowerChange Fail(string action, string message, List<string>? preview = null) => new()
    {
        Action = action,
        Success = false,
        Message = message,
        Preview = preview ?? new(),
    };

    // --- machine facts -----------------------------------------------------

    /// <summary>
    /// Chassis first, battery second. A desktop with no battery answers
    /// "Desktop" without ever consulting the enclosure, so a machine that
    /// simply has no battery is never called a laptop.
    /// </summary>
    public static string DetectFormFactor()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT * FROM Win32_Battery");
            var batteries = searcher.Get();
            var hasBattery = batteries.Count > 0;
            batteries.Dispose();

            if (!hasBattery)
            {
                using var chassisSearcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (var obj in chassisSearcher.Get())
                {
                    var types = obj["ChassisTypes"] as ushort[];
                    if (types is null) continue;
                    if (types.Any(t => t == 30)) return "Tablet";
                    if (types.Any(t => t is 8 or 9 or 10 or 14)) return "Laptop";
                    if (types.Any(t => t is 3 or 4 or 5 or 6 or 7 or 15 or 16)) return "Desktop";
                }
                return "Desktop";
            }

            return "Laptop";
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task<BatteryState> ReadBatteryAsync(CancellationToken cancel)
    {
        var script =
            "$b = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue; " +
            "if (-not $b) { @{present=$false} | ConvertTo-Json -Compress; exit }; " +
            "$b = $b | Select-Object -First 1; " +
            "$online = $null; " +
            "try { $s = Get-CimInstance -Namespace root/wmi -ClassName BatteryStatus -ErrorAction Stop | Select-Object -First 1; $online = [bool]$s.PowerOnLine } catch {}; " +
            "$mins = $null; if ($b.EstimatedRunTime -and $b.EstimatedRunTime -lt 71582788) { $mins = [int]$b.EstimatedRunTime }; " +
            "@{ present=$true; percent=$(if ($null -ne $b.EstimatedChargeRemaining) { [int]$b.EstimatedChargeRemaining } else { $null }); " +
            "status=[string]$b.Status; minutes=$mins; onAc=$online } | ConvertTo-Json -Compress";

        try
        {
            var result = await ProcessRunner.RunAsync(
                "powershell.exe",
                new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                TimeSpan.FromSeconds(30));
            cancel.ThrowIfCancellationRequested();

            var start = result.StdOut.IndexOf('{');
            if (start < 0) return new BatteryState { OnAc = true };
            var state = JsonSerializer.Deserialize<BatteryRead>(result.StdOut[start..], new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (state is null || !state.Present) return new BatteryState { OnAc = true };

            return new BatteryState
            {
                Present = true,
                Percent = state.Percent,
                Status = state.Status,
                MinutesRemaining = state.Minutes,
                // Unknown power-source state reads as mains, which is the
                // assumption that does the least harm on a desktop.
                OnAc = state.OnAc ?? true,
            };
        }
        catch
        {
            return new BatteryState { OnAc = true };
        }
    }

    private sealed class BatteryRead
    {
        public bool Present { get; init; }
        public int? Percent { get; init; }
        public string? Status { get; init; }
        public int? Minutes { get; init; }
        public bool? OnAc { get; init; }
    }

    // --- state -------------------------------------------------------------

    private SavedPlan? Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return null;
            return JsonSerializer.Deserialize<SavedPlan>(File.ReadAllText(_statePath));
        }
        catch { return null; }
    }

    private void Save(SavedPlan state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* knowing the previous plan is a convenience, not a requirement */ }
    }

    // --- powercfg ----------------------------------------------------------

    private static async Task<string?> RunAsync(IEnumerable<string> arguments, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        try
        {
            var result = await ProcessRunner.RunAsync("powercfg", arguments, TimeSpan.FromSeconds(45));
            return result.Output;
        }
        catch { return null; }
    }
}

/// <summary>The raw shape of one setting as powercfg printed it.</summary>
public sealed record ParsedSetting(
    int? Ac,
    int? Dc,
    string? Units,
    Dictionary<int, string> Possible,
    long? Min,
    long? Max);
