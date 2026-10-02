using System.CommandLine;
using System.Text.Json;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Engine;
using WinOpt.Engine.Detection;
using WinOpt.Engine.Diagnostics;
using WinOpt.Engine.Gaming;
using WinOpt.Engine.Logging;
using WinOpt.Engine.Providers;
using WinOpt.Engine.Security;
using WinOpt.Engine.Recommendation;
using WinOpt.Engine.Tweaks;
using WinOpt.Providers.Registry;
using WinOpt.Providers.Service;
using WinOpt.Providers.PowerShell;
using WinOpt.Providers.NetSh;
using WinOpt.Providers.ScheduledTask;

namespace WinOpt.Cli;

public static class Program
{
    private static string TweaksDir {
        get {
            // Check portable location first (tweaks/ next to the exe)
            var portable = Path.Combine(AppContext.BaseDirectory, "tweaks");
            if (Directory.Exists(portable)) return portable;
            // Check Tauri bundled resources (resources/tweaks/)
            var bundled = Path.Combine(AppContext.BaseDirectory, "resources", "tweaks");
            if (Directory.Exists(bundled)) return bundled;
            // Dev layout: walk up from the build output until the repo's tweaks/
            // directory turns up. A fixed ".." depth only matched one specific
            // bin layout — for any other it resolved to a directory that does
            // not exist, and every command then silently operated on an empty
            // catalogue instead of failing.
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "tweaks");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir)!;
            }
            return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tweaks");
        }
    }

    public static async Task<int> Main(string[] args)
    {
        // JSON and table output must leave this process as UTF-8 whatever the
        // console codepage is. Without this the descriptions and emoji in the
        // tweak catalogue are re-encoded into the OEM codepage on the way out
        // and arrive at the UI (or a pipe) as mojibake.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached and the runtime refused the switch — keep
            // the default encoding rather than failing the command.
        }

        var rootCommand = new RootCommand("WinOpt — Windows PC Optimizer CLI");

        // -- system --
        var systemCmd = new Command("system", "Show system information");
        var systemSummaryOpt = new Option<bool>("--summary", "Show one-line summary");
        var systemJsonOpt = new Option<bool>("--json", "Output as JSON");
        systemCmd.AddOption(systemSummaryOpt);
        systemCmd.AddOption(systemJsonOpt);
        systemCmd.SetHandler(async (summary, json) => await RunSystem(summary, json), systemSummaryOpt, systemJsonOpt);
        rootCommand.AddCommand(systemCmd);

        // -- scan --
        var scanCmd = new Command("scan", "Detect current state of all tweaks");
        var scanTweakArg = new Argument<string?>("tweak-id", () => null, "Specific tweak to scan");
        var scanProfileOpt = new Option<string?>("--profile", "Limit the scan to tweaks in this profile");
        var scanJsonOpt = new Option<bool>("--json", "Output as JSON");
        scanCmd.AddArgument(scanTweakArg);
        scanCmd.AddOption(scanProfileOpt);
        scanCmd.AddOption(scanJsonOpt);
        scanCmd.SetHandler(async (tweakId, profile, json) => await RunScan(tweakId, profile, json),
            scanTweakArg, scanProfileOpt, scanJsonOpt);
        rootCommand.AddCommand(scanCmd);

        // -- list --
        var listCmd = new Command("list", "List all tweaks");
        var listCategoryOpt = new Option<string?>("--category", "Filter by category");
        var listRiskOpt = new Option<string?>("--risk", "Filter by risk level");
        var listProfileOpt = new Option<string?>("--profile", "Filter by profile");
        var listIncludeOpt = new Option<string?>("--include", "Comma-separated tweak IDs to add on top of the selection (profile opt-ins)");
        var listJsonOpt = new Option<bool>("--json", "Output as JSON");
        listCmd.AddOption(listCategoryOpt);
        listCmd.AddOption(listRiskOpt);
        listCmd.AddOption(listProfileOpt);
        listCmd.AddOption(listIncludeOpt);
        listCmd.AddOption(listJsonOpt);
        listCmd.SetHandler(async (cat, risk, profile, include, json) => await RunList(cat, risk, profile, json, include),
            listCategoryOpt, listRiskOpt, listProfileOpt, listIncludeOpt, listJsonOpt);
        rootCommand.AddCommand(listCmd);

        // -- profile-selector --
        var psCmd = new Command("profile-selector", "What a profile applies, what it reaches, and why");
        var psProfileArg = new Argument<string>("profile-id", "Profile to explain");
        var psJsonOpt = new Option<bool>("--json", "Output as JSON");
        psCmd.AddArgument(psProfileArg);
        psCmd.AddOption(psJsonOpt);
        psCmd.SetHandler(async (profileId, json) => await RunProfileSelector(profileId, json),
            psProfileArg, psJsonOpt);
        rootCommand.AddCommand(psCmd);

        // -- apply --
        var applyCmd = new Command("apply", "Apply tweaks");
        var applyTweakArg = new Argument<string?>("tweak-id", () => null, "Tweak ID, comma-separated IDs, or 'all'");
        var applyProfileOpt = new Option<string?>("--profile", "Apply a profile");
        var applyDryRunOpt = new Option<bool>("--dry-run", "Preview changes without applying");
        var applyCategoryOpt = new Option<string?>("--category", "Apply by category");
        var applyIncludeOpt = new Option<string?>("--include", "Comma-separated tweak IDs to add on top of the selection (profile opt-ins)");
        var applyJsonOpt = new Option<bool>("--json", "Output as JSON");
        applyCmd.AddArgument(applyTweakArg);
        applyCmd.AddOption(applyProfileOpt);
        applyCmd.AddOption(applyDryRunOpt);
        applyCmd.AddOption(applyCategoryOpt);
        applyCmd.AddOption(applyIncludeOpt);
        applyCmd.AddOption(applyJsonOpt);
        applyCmd.SetHandler(async (tweakId, profile, dryRun, category, include, json) =>
            await RunApply(tweakId, profile, dryRun, category, json, include),
            applyTweakArg, applyProfileOpt, applyDryRunOpt, applyCategoryOpt, applyIncludeOpt, applyJsonOpt);
        rootCommand.AddCommand(applyCmd);

        // -- rollback --
        var rollbackCmd = new Command("rollback", "Rollback changes");
        var rollbackTweakArg = new Argument<string?>("tweak-id", () => null, "Tweak to rollback");
        var rollbackSnapshotOpt = new Option<string?>("--snapshot", "Snapshot ID to rollback (default: newest snapshot containing the tweak)");
        var rollbackAllOpt = new Option<bool>("--all", "Rollback all changes");
        var rollbackJsonOpt = new Option<bool>("--json", "Output as JSON");
        rollbackCmd.AddArgument(rollbackTweakArg);
        rollbackCmd.AddOption(rollbackSnapshotOpt);
        rollbackCmd.AddOption(rollbackAllOpt);
        rollbackCmd.AddOption(rollbackJsonOpt);
        rollbackCmd.SetHandler(async (tweakId, snapshot, all, json) => await RunRollback(tweakId, snapshot, all, json),
            rollbackTweakArg, rollbackSnapshotOpt, rollbackAllOpt, rollbackJsonOpt);
        rootCommand.AddCommand(rollbackCmd);

        // -- profile --
        var profileCmd = new Command("profile", "List optimization profiles");
        var profileJsonOpt = new Option<bool>("--json", "Output as JSON");
        profileCmd.AddOption(profileJsonOpt);
        profileCmd.SetHandler((json) => RunProfiles(json), profileJsonOpt);
        rootCommand.AddCommand(profileCmd);

        // -- recommend --
        var recommendCmd = new Command("recommend", "Get optimization recommendations");
        var recProfileOpt = new Option<string?>("--profile", "Target profile");
        var recTopOpt = new Option<int>("--top", () => 20, "Top N recommendations");
        var recJsonOpt = new Option<bool>("--json", "Output as JSON");
        recommendCmd.AddOption(recProfileOpt);
        recommendCmd.AddOption(recTopOpt);
        recommendCmd.AddOption(recJsonOpt);
        recommendCmd.SetHandler(async (profile, top, json) => await RunRecommend(profile, top, json),
            recProfileOpt, recTopOpt, recJsonOpt);
        rootCommand.AddCommand(recommendCmd);

        // -- doctor --
        var doctorCmd = new Command("doctor", "Run diagnostics");
        var doctorModeArg = new Argument<string?>("mode", () => null, "Diagnostics mode: health, network, startup, bench");
        var doctorJsonOpt = new Option<bool>("--json", "Output as JSON");
        doctorCmd.AddArgument(doctorModeArg);
        doctorCmd.AddOption(doctorJsonOpt);
        doctorCmd.SetHandler(async (mode, json) => await RunDoctor(mode, json), doctorModeArg, doctorJsonOpt);
        rootCommand.AddCommand(doctorCmd);

        // -- snapshots --
        var snapshotCmd = new Command("snapshots", "List snapshots");
        var snapshotJsonOpt = new Option<bool>("--json", "Output as JSON");
        snapshotCmd.AddOption(snapshotJsonOpt);
        snapshotCmd.SetHandler((json) => RunSnapshots(json), snapshotJsonOpt);
        rootCommand.AddCommand(snapshotCmd);

        // -- plan --
        // Takes exactly the same selection arguments as `apply`, so it answers
        // "what would apply do to this selection" rather than a different
        // question with similar wording.
        var planCmd = new Command("plan", "Show what an apply would do: order, blockers, conflicts");
        var planTweakArg = new Argument<string?>("tweak-id", () => null, "Tweak ID, comma-separated IDs, or 'all'");
        var planProfileOpt = new Option<string?>("--profile", "Plan a profile");
        var planCategoryOpt = new Option<string?>("--category", "Plan a category");
        var planIncludeOpt = new Option<string?>("--include", "Comma-separated tweak IDs to add on top of the selection (profile opt-ins)");
        var planJsonOpt = new Option<bool>("--json", "Output as JSON");
        planCmd.AddArgument(planTweakArg);
        planCmd.AddOption(planProfileOpt);
        planCmd.AddOption(planCategoryOpt);
        planCmd.AddOption(planIncludeOpt);
        planCmd.AddOption(planJsonOpt);
        planCmd.SetHandler(async (tweakId, profile, category, include, json) =>
            await RunPlan(tweakId, profile, category, include, json),
            planTweakArg, planProfileOpt, planCategoryOpt, planIncludeOpt, planJsonOpt);
        rootCommand.AddCommand(planCmd);

        // -- journal --
        var journalCmd = new Command("journal", "Read the change journal — what this machine actually changed");
        var journalLimitOpt = new Option<int>("--limit", () => 50, "Maximum entries to return");
        var journalTweakOpt = new Option<string?>("--tweak", "Filter by tweak ID");
        var journalOpOpt = new Option<string?>("--operation", "Filter by operation: apply, rollback");
        var journalResultOpt = new Option<string?>("--result", "Filter by result: success, failure, skipped, blocked");
        var journalJsonOpt = new Option<bool>("--json", "Output as JSON");
        journalCmd.AddOption(journalLimitOpt);
        journalCmd.AddOption(journalTweakOpt);
        journalCmd.AddOption(journalOpOpt);
        journalCmd.AddOption(journalResultOpt);
        journalCmd.AddOption(journalJsonOpt);
        journalCmd.SetHandler(async (limit, tweak, op, res, json) =>
                await RunJournal(limit, tweak, op, res, json),
            journalLimitOpt, journalTweakOpt, journalOpOpt, journalResultOpt, journalJsonOpt);
        rootCommand.AddCommand(journalCmd);

        // -- game-mode --
        var gmCmd = new Command("game-mode", "Gaming Center: find launchers and games, run a temporary optimisation session");
        var gmActionArg = new Argument<string>("action",
            "detect | folders | folder-add | folder-remove | start | status | stop | preset-list | preset-save | preset-apply | preset-delete");
        var gmPlanOpt = new Option<string>("--plan", () => "high-performance",
            "Power plan for the session: a scheme name (High performance) or a GUID");
        var gmNoNotificationsOpt = new Option<bool>("--no-notifications", "Leave toast notifications alone");
        var gmNoBackgroundOpt = new Option<bool>("--no-background-apps", "Leave background app execution alone");
        var gmServicesOpt = new Option<string?>("--services", "Comma-separated services to stop for the session");
        var gmProcessOpt = new Option<string?>("--for-process", "Process whose priority is raised while it runs");
        var gmGameOpt = new Option<string?>("--game", "Game path this session is for");
        var gmControlsOpt = new Option<string?>("--controls",
            "Comma-separated control kinds to run: power-plan, notifications, background-apps, service, priority");
        var gmPathOpt = new Option<string?>("--path", "Folder to add or remove");
        var gmNameOpt = new Option<string?>("--name", "Preset name");
        var gmJsonOpt = new Option<bool>("--json", "Output as JSON");
        gmCmd.AddArgument(gmActionArg);
        foreach (var option in new Option[]
                 {
                     gmPlanOpt, gmNoNotificationsOpt, gmNoBackgroundOpt, gmServicesOpt,
                     gmProcessOpt, gmGameOpt, gmControlsOpt, gmPathOpt, gmNameOpt, gmJsonOpt,
                 })
            gmCmd.AddOption(option);

        gmCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunGameMode(new GameModeRequest
            {
                Action = parse.GetValueForArgument(gmActionArg),
                Plan = parse.GetValueForOption(gmPlanOpt) ?? "high-performance",
                NoNotifications = parse.GetValueForOption(gmNoNotificationsOpt),
                NoBackgroundApps = parse.GetValueForOption(gmNoBackgroundOpt),
                Services = parse.GetValueForOption(gmServicesOpt),
                ForProcess = parse.GetValueForOption(gmProcessOpt),
                Game = parse.GetValueForOption(gmGameOpt),
                Controls = parse.GetValueForOption(gmControlsOpt),
                Path = parse.GetValueForOption(gmPathOpt),
                Name = parse.GetValueForOption(gmNameOpt),
                Json = parse.GetValueForOption(gmJsonOpt),
            });
        });
        rootCommand.AddCommand(gmCmd);

        // -- defender --
        var defCmd = new Command("defender", "Windows Defender exclusions — list, exclude a folder, put it back");
        var defActionArg = new Argument<string>("action", "list | add | remove | export");
        var defPathOpt = new Option<string?>("--path", "Folder or file to exclude or unexclude");
        var defConfirmOpt = new Option<bool>("--confirm", "Required for add: you have read the path that will be excluded");
        var defOutputOpt = new Option<string?>("--output", "Export destination (default: a file under %LOCALAPPDATA%\\WinOpt)");
        var defJsonOpt = new Option<bool>("--json", "Output as JSON");
        defCmd.AddArgument(defActionArg);
        foreach (var option in new Option[] { defPathOpt, defConfirmOpt, defOutputOpt, defJsonOpt })
            defCmd.AddOption(option);

        defCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunDefender(new DefenderRequest
            {
                Action = parse.GetValueForArgument(defActionArg),
                Path = parse.GetValueForOption(defPathOpt),
                Confirm = parse.GetValueForOption(defConfirmOpt),
                Output = parse.GetValueForOption(defOutputOpt),
                Json = parse.GetValueForOption(defJsonOpt),
            });
        });
        rootCommand.AddCommand(defCmd);

        var exitCode = await rootCommand.InvokeAsync(args);
        // Handlers signal failures via Environment.ExitCode (InvokeAsync itself
        // returns 0 for void/Task handlers), so propagate it here.
        return exitCode != 0 ? exitCode : Environment.ExitCode;
    }

    // === Command Handlers ===

    private static async Task RunSystem(bool summary, bool json)
    {
        var detector = new SystemDetector();
        var info = await detector.DetectAsync();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(info, JsonOpts));
            return;
        }

        if (summary)
        {
            Console.WriteLine($"{info.OsEdition} Build {info.BuildNumber} | {info.CpuName} ({info.CpuCores} cores) | {info.RamTotalGb}GB RAM | {info.GpuName} | {info.FormFactor} | Tier: {info.OverallTier}");
            return;
        }

        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║           WinOpt System Info            ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine($"  OS:         {info.OsCaption}");
        Console.WriteLine($"  Build:      {info.BuildNumber}");
        Console.WriteLine($"  Arch:       {info.OsArchitecture}");
        Console.WriteLine($"  Edition:    {info.OsEdition}");
        Console.WriteLine($"  CPU:        {info.CpuName}");
        Console.WriteLine($"  CPU Tier:   {info.CpuTier} ({info.CpuCores}C/{info.CpuLogicalProcessors}T @ {info.CpuMaxClockMhz}MHz)");
        Console.WriteLine($"  RAM:        {info.RamTotalGb}GB ({info.RamTier})");
        Console.WriteLine($"  Storage:    {info.PrimaryStorageModel} ({info.PrimaryStorageType}, {info.PrimaryStorageSizeGb}GB)");
        Console.WriteLine($"  GPU:        {info.GpuName} ({info.GpuVendor}, {info.GpuRamMb}MB)");
        Console.WriteLine($"  Form Factor: {info.FormFactor}");
        Console.WriteLine($"  Network:    {info.PrimaryNicName} ({info.PrimaryNicSpeed})");
        Console.WriteLine($"  Power:      {info.ActivePowerPlan}");
        Console.WriteLine($"  Overall:    {info.OverallTier}");
    }

    private static async Task RunScan(string? tweakId, string? profile, bool json)
    {
        var engine = CreateEngine();
        await engine.Database.LoadAsync();
        RegisterProviders(engine);

        var systemInfo = await new SystemDetector().DetectAsync();

        if (tweakId != null)
        {
            var result = await engine.ScanSingleAsync(tweakId);
            if (json)
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            else
                PrintDetection(result);
            if (!result.DetectionSucceeded || result.State == TweakState.DetectionFailed) Environment.ExitCode = 1;
            return;
        }

        IReadOnlyList<TweakDefinition>? targets = null;
        if (!string.IsNullOrEmpty(profile))
        {
            var match = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profile);
            if (match == null)
            {
                Console.Error.WriteLine($"  Unknown profile '{profile}'. Available: " +
                    string.Join(", ", BuiltInProfiles.All.Select(p => p.Id)));
                Environment.ExitCode = 1;
                return;
            }

            // Same selection apply uses, so a profile scan reports exactly the
            // tweaks that profile would apply on this machine.
            targets = engine.Database.FilterCompatible(engine.Database.GetForProfile(match), systemInfo);
        }
        else
        {
            // Full scans skip machine-incompatible tweaks (settings the active
            // scheme does not expose) instead of reporting them as failures.
            targets = engine.Database.FilterCompatible(
                engine.Database.Tweaks.Values.ToList(), systemInfo);
        }

        var results = await engine.ScanAsync(targets);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(results, JsonOpts));
        }
        else
        {
            foreach (var r in results)
                PrintDetection(r);
            Console.WriteLine($"\n  {results.Count(r => r.State == TweakState.Applied)} applied, " +
                $"{results.Count(r => r.State == TweakState.NotApplied)} not applied, " +
                $"{results.Count(r => !r.DetectionSucceeded)} failed");
        }
    }

    private static async Task RunList(string? category, string? risk, string? profile, bool json, string? include = null)
    {
        var engine = CreateEngine();
        await engine.Database.LoadAsync();

        // Same machine filter scan and apply use, so every tweak listed here is
        // one this machine could actually run.
        var systemInfo = await new SystemDetector().DetectAsync();
        var compatible = engine.Database
            .FilterCompatible(engine.Database.Tweaks.Values.ToList(), systemInfo)
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tweaks = engine.Database.Tweaks.Values
            .AsEnumerable()
            .Where(t => compatible.Contains(t.Id));

        if (!string.IsNullOrEmpty(category))
            tweaks = tweaks.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(risk) && Enum.TryParse<RiskLevel>(risk, true, out var riskLevel))
            tweaks = tweaks.Where(t => t.Risk == riskLevel);
        if (!string.IsNullOrEmpty(profile))
        {
            // Use the same selection apply would make, so a listed tweak is
            // one the profile would actually run on this machine.
            var match = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profile);
            if (match == null)
            {
                Console.Error.WriteLine($"  Unknown profile '{profile}'. Available: " +
                    string.Join(", ", BuiltInProfiles.All.Select(p => p.Id)));
                Environment.ExitCode = 1;
                return;
            }

            var selected = engine.Database.GetForProfile(match)
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            tweaks = tweaks.Where(t => selected.Contains(t.Id));
        }

        // Opt-ins the user ticked above the profile. `tweaks` at this point is
        // whatever the filters selected; an opt-in only ever widens it, never
        // narrows it, so it is appended rather than intersected. Filtered on
        // `compatible` the same way everything else here is, so `list
        // --profile --include` describes the run rather than a set the apply
        // would then shrink.
        var start = tweaks.ToList();
        var listed = new HashSet<string>(start.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var id in (include ?? string.Empty).Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var optIn = engine.Database.Get(id);
            if (optIn == null || !compatible.Contains(optIn.Id) || !listed.Add(optIn.Id))
                continue;
            start.Add(optIn);
        }
        tweaks = start;

        var list = tweaks.ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(list, JsonOpts));
        }
        else
        {
            Console.WriteLine($"  {"ID",-35} {"Name",-30} {"Risk",-12} {"Evidence",7}");
            Console.WriteLine($"  {new string('─', 35)} {new string('─', 30)} {new string('─', 12)} {new string('─', 7)}");
            foreach (var t in list)
            {
                var riskColor = t.Risk switch
                {
                    RiskLevel.Safe => ConsoleColor.Green,
                    RiskLevel.Recommended => ConsoleColor.Cyan,
                    RiskLevel.Optional => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };
                Console.Write($"  {t.Id,-35} {t.Name,-30} ");
                var old = Console.ForegroundColor;
                Console.ForegroundColor = riskColor;
                Console.Write($"{t.Risk,-12}");
                Console.ForegroundColor = old;
                Console.WriteLine($" {t.Evidence}/5");
            }
            Console.WriteLine($"\n  {list.Count} tweaks");
        }
    }

    /// <summary>
    /// Explain a profile: the tweaks it applies unasked, the ones it reaches
    /// but will not apply without being asked, and the ones it never touches —
    /// each with the bar it failed.
    ///
    /// <para>
    /// The point is to make silence legible. A profile that drops a tweak
    /// without saying why is indistinguishable from one that applied it.
    /// </para>
    /// </summary>
    private static async Task RunProfileSelector(string profileId, bool json)
    {
        var profile = BuiltInProfiles.All.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
        {
            Console.Error.WriteLine($"  Unknown profile '{profileId}'. Available: " +
                string.Join(", ", BuiltInProfiles.All.Select(p => p.Id)));
            Environment.ExitCode = 1;
            return;
        }

        var engine = CreateEngine();
        await engine.Database.LoadAsync();
        var systemInfo = await new SystemDetector().DetectAsync();

        var selection = new ProfileSelector(engine.Database).Select(profile, systemInfo);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(selection, JsonOpts));
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  {profile.Icon} {profile.Name} — what this profile does on this machine");
        Console.WriteLine($"  Policy: risk up to {profile.MaxRisk}, evidence {profile.MinEvidence}/5 or better");
        Console.WriteLine();

        // Things that are true of the profile on this machine rather than of
        // any one tweak. Printed before the lists, because a warning you read
        // after clicking Apply is a warning that arrived too late.
        foreach (var notice in selection.Notices)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("  ! ");
            Console.ForegroundColor = old;
            Console.WriteLine(notice);
        }
        if (selection.Notices.Count > 0) Console.WriteLine();

        void Section(string heading, List<ProfileTweakVerdict> items, ConsoleColor color, bool withReasons)
        {
            Console.WriteLine($"  {heading} ({items.Count})");
            if (items.Count == 0)
            {
                Console.WriteLine("    —");
                Console.WriteLine();
                return;
            }

            foreach (var v in items)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"    {v.Tweak.Id,-42} ");
                Console.ForegroundColor = old;
                Console.Write($"{v.Tweak.Risk,-12} e{v.Tweak.Evidence}/5");
                if (withReasons && !string.IsNullOrEmpty(v.Detail))
                    Console.WriteLine($"   {v.Detail}");
                else
                    Console.WriteLine();
            }
            Console.WriteLine();
        }

        Section("Applied by default", selection.DefaultSet, ConsoleColor.Green, withReasons: false);
        Section("Available if you want them", selection.OptIn, ConsoleColor.Yellow, withReasons: true);

        // The excluded bucket is dominated by one reason — "not in this
        // profile's categories" is true of most of the catalogue for most
        // profiles. One line per tweak turned a useful explanation into a
        // wall of near-identical rows that buried the handful of exclusions
        // that were actually deliberate. Grouped by reason instead: the count
        // is the headline, the ids are the footnote, and every exclusion
        // still appears exactly once.
        Console.WriteLine($"  Not part of this profile ({selection.Excluded.Count})");
        if (selection.Excluded.Count == 0)
        {
            Console.WriteLine("    —");
        }
        else
        {
            foreach (var group in selection.Excluded.GroupBy(v => v.Reason))
            {
                var ids = group.Select(v => v.Tweak.Id).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
                var explain = group.First().Detail;
                var old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"    {group.Key} ({group.Count()}) ");
                Console.ForegroundColor = old;
                Console.WriteLine(string.IsNullOrEmpty(explain) ? string.Empty : explain);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                foreach (var line in Wrap(string.Join(", ", ids), 66))
                    Console.WriteLine($"        {line}");
                Console.ForegroundColor = old;
            }
        }
        Console.WriteLine();

        Console.WriteLine($"  Applying this profile applies the {selection.DefaultSet.Count} in the first list only.");
        Console.WriteLine();
    }

    /// <summary>
    /// Break a comma-joined id list into indented lines. Console output is
    /// read on a terminal, so a 300-character row of ids wraps badly or not at
    /// all depending on the window width; this keeps the column readable
    /// without pulling in a layout library for four callers' worth of use.
    /// </summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>
    /// The tweaks a selection arguments resolve to. Shared by `apply` and
    /// `plan` so a plan and the apply it previews can never drift apart.
    /// </summary>
    private sealed record Selection(IReadOnlyList<TweakDefinition> Tweaks, string? Error = null)
    {
        public static Selection Fail(string error) => new(Array.Empty<TweakDefinition>(), error);
    }

    private static Selection SelectTweaks(OptimizeEngine engine, SystemInfo systemInfo,
        string? tweakId, string? profile, string? category, string? include = null)
    {
        // An empty catalogue otherwise reads as "0 of 0 would run" and exit 0 —
        // a silent no-op that looks like a successful run.
        if (engine.Database.Count == 0)
            return Selection.Fail($"No tweak definitions found under '{TweaksDir}'.");

        var selection = Resolve(engine, systemInfo, tweakId, profile, category);
        if (selection.Error != null) return selection;

        return AddIncludes(engine, systemInfo, selection.Tweaks, include);
    }

    /// <summary>
    /// The selection a selector resolves to, before any opt-ins are added.
    /// Split out so <see cref="SelectTweaks"/> stays a two-step "base, then
    /// extras" and the opt-in path can never alter which tweaks the profile
    /// itself chose.
    /// </summary>
    private static Selection Resolve(OptimizeEngine engine, SystemInfo systemInfo,
        string? tweakId, string? profile, string? category)
    {
        if (!string.IsNullOrEmpty(profile))
        {
            var p = BuiltInProfiles.All.FirstOrDefault(x =>
                string.Equals(x.Id, profile, StringComparison.OrdinalIgnoreCase));
            if (p == null) return Selection.Fail($"Unknown profile '{profile}'. Run 'profile' to list them.");

            var selected = engine.Database.FilterCompatible(
                engine.Database.GetForProfile(p), systemInfo);
            return selected.Count > 0
                ? new Selection(selected)
                : Selection.Fail($"Profile '{p.Id}' selects no tweaks compatible with this machine.");
        }

        if (!string.IsNullOrEmpty(category))
        {
            var known = engine.Database.GetCategories();
            if (!known.Contains(category, StringComparer.OrdinalIgnoreCase))
                return Selection.Fail($"Unknown category '{category}'. Available: {string.Join(", ", known)}");

            var inCategory = engine.Database.FilterCompatible(
                engine.Database.GetByCategory(category), systemInfo);
            return inCategory.Count > 0
                ? new Selection(inCategory)
                : Selection.Fail($"No tweaks in category '{category}' are compatible with this machine.");
        }

        if (string.Equals(tweakId, "all", StringComparison.OrdinalIgnoreCase))
            // Same selection a full scan reports, so "apply all" applies exactly
            // the tweaks that are known to work on this machine.
            return new Selection(engine.Database.FilterCompatible(
                engine.Database.Tweaks.Values.ToList(), systemInfo));

        if (!string.IsNullOrEmpty(tweakId))
        {
            // An explicit list is handed to the planner as one batch, so the
            // order and any conflict between two of its members are seen before
            // anything runs. Nothing here is filtered: a pick the machine cannot
            // take is reported, not silently dropped.
            var ids = tweakId.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var picked = new List<TweakDefinition>(ids.Length);
            foreach (var id in ids)
            {
                var tweak = engine.Database.Get(id);
                if (tweak == null)
                    return Selection.Fail($"Unknown tweak '{id}'.");

                if (!picked.Any(p => string.Equals(p.Id, tweak.Id, StringComparison.OrdinalIgnoreCase)))
                    picked.Add(tweak);
            }

            return new Selection(picked);
        }

        return Selection.Fail("Specify a tweak ID, a comma-separated list, or 'all'; --profile; or --category.");
    }

    /// <summary>
    /// Add tweaks the user explicitly ticked, on top of a selection that did
    /// not name them.
    ///
    /// <para>
    /// This is how the profile page's opt-in list becomes an apply. Every
    /// extra still passes through the same security guard and hardware gate as
    /// the base selection — being listed under "available if you want them"
    /// means <em>offered</em>, not <em>exempt</em>. Opting into something this
    /// machine cannot take, or that the guard refuses outright, is reported
    /// back rather than quietly dropped, because a checkbox that does nothing
    /// is the same defect as a profile that hides what it does.
    /// </para>
    /// </summary>
    private static Selection AddIncludes(
        OptimizeEngine engine, SystemInfo systemInfo, IReadOnlyList<TweakDefinition> baseSelection, string? include)
    {
        var ids = (include ?? string.Empty).Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Length == 0) return new Selection(baseSelection);

        var result = baseSelection.ToList();
        var problems = new List<string>();
        var known = baseSelection.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            if (known.Contains(id)) continue;

            var tweak = engine.Database.Get(id);
            if (tweak == null) { problems.Add($"unknown tweak '{id}'"); continue; }
            if (TweakDatabase.IsSecurityBlocked(tweak))
            {
                problems.Add($"'{id}' is blocked by the security guard and will not be run");
                continue;
            }
            if (!TweakDatabase.IsHardwareCompatible(tweak, systemInfo))
            {
                problems.Add($"'{id}' is not compatible with this machine");
                continue;
            }

            result.Add(tweak);
            known.Add(id);
        }

        return problems.Count == 0
            ? new Selection(result)
            : Selection.Fail($"Opted in but not applied — {string.Join("; ", problems)}.");
    }

    private static async Task RunApply(
        string? tweakId, string? profile, bool dryRun, string? category, bool json, string? include)
    {
        var engine = CreateEngine();
        await engine.Database.LoadAsync();
        RegisterProviders(engine);

        var systemInfo = await new SystemDetector().DetectAsync();

        if (dryRun)
            Console.Error.WriteLine("  [DRY RUN] No changes will be made.\n");

        var selection = SelectTweaks(engine, systemInfo, tweakId, profile, category, include);
        if (selection.Error != null)
        {
            Console.Error.WriteLine($"  {selection.Error}");
            Environment.ExitCode = 1;
            return;
        }

        // One plain id takes the single-tweak path: it gets the up-front
        // power-setting check and a focused result. A comma list is a batch —
        // it needs the planner to order it, hold back anything that contradicts
        // another member, and cover the whole run with one snapshot. An opt-in
        // is never a single: it is the base selection plus extras, which is
        // by definition a batch.
        var isSingle = !string.IsNullOrEmpty(tweakId)
            && string.IsNullOrEmpty(include)
            && !tweakId.Contains(',')
            && !string.Equals(tweakId, "all", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(profile) && string.IsNullOrEmpty(category)
            && selection.Tweaks.Count == 1;

        SessionResult result;

        if (isSingle)
        {
            var tweak = selection.Tweaks[0];

            // Refuse up front rather than running a command the active power
            // plan cannot accept — powercfg would reject it anyway.
            if (!string.IsNullOrEmpty(tweak.RequiresPowerSetting) &&
                systemInfo.PowerSettings != null &&
                !systemInfo.PowerSettings.Contains(tweak.RequiresPowerSetting))
            {
                Console.Error.WriteLine(
                    $"  '{tweak.Id}' needs the '{tweak.RequiresPowerSetting}' power setting, " +
                    "which this machine's active power plan does not expose.");
                Environment.ExitCode = 1;
                return;
            }

            var tweakResult = await engine.ApplyAsync(tweak.Id, dryRun);
            var needsElevation = tweakResult.Status == TweakResultStatus.RequiresElevation;
            var finished = tweakResult.Status is TweakResultStatus.Success
                or TweakResultStatus.AlreadyApplied
                or TweakResultStatus.Skipped
                or TweakResultStatus.RequiresElevation;
            result = new SessionResult
            {
                TweaksAttempted = 1,
                TweaksSucceeded = tweakResult.Status is TweakResultStatus.Success or TweakResultStatus.AlreadyApplied ? 1 : 0,
                TweaksSkipped = tweakResult.Status == TweakResultStatus.Skipped ? 1 : 0,
                TweaksNeedElevation = needsElevation ? 1 : 0,
                TweaksBlocked = tweakResult.Status == TweakResultStatus.Blocked ? 1 : 0,
                TweaksFailed = finished ? 0 : 1,
                Results = { tweakResult }
            };
        }
        else
        {
            var description = !string.IsNullOrEmpty(profile) ? $"Profile: {profile}"
                : !string.IsNullOrEmpty(category) ? $"Category: {category}"
                : tweakId?.Contains(',') == true ? "Selected tweaks"
                : "All tweaks";
            result = await engine.ApplyBatchAsync(selection.Tweaks, dryRun, description: description);
        }

        // Anything that was asked for and not done is a non-zero exit, but the
        // reason stays in the JSON so callers can tell "broken" from "needs
        // admin" from "held back by the planner" without parsing prose.
        if (result.TweaksFailed > 0 || result.TweaksNeedElevation > 0 || result.TweaksBlocked > 0)
            Environment.ExitCode = 1;

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
        }
        else
        {
            var tally = $"{result.TweaksSucceeded} succeeded, {result.TweaksFailed} failed, {result.TweaksSkipped} skipped";
            if (result.TweaksBlocked > 0)
                tally += $", {result.TweaksBlocked} held back";
            if (result.TweaksNeedElevation > 0)
                tally += $", {result.TweaksNeedElevation} need administrator rights";
            Console.WriteLine($"\n  Results: {tally}");
            foreach (var r in result.Results)
            {
                var color = r.Status switch
                {
                    TweakResultStatus.Success => ConsoleColor.Green,
                    TweakResultStatus.AlreadyApplied => ConsoleColor.DarkGray,
                    TweakResultStatus.Failed => ConsoleColor.Red,
                    TweakResultStatus.RequiresElevation => ConsoleColor.DarkCyan,
                    TweakResultStatus.SecurityBlocked => ConsoleColor.DarkRed,
                    TweakResultStatus.Blocked => ConsoleColor.DarkYellow,
                    _ => ConsoleColor.Yellow
                };
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"  {r.Status,-20}");
                Console.ForegroundColor = old;
                Console.WriteLine($"{r.TweakId} — {r.Message}");
            }
            if (result.TweaksBlocked > 0)
            {
                Console.WriteLine("\n  Held back tweaks ran nothing. Run 'plan' with the same arguments to see why,");
                Console.WriteLine("  then narrow the selection or drop the conflicting tweak.");
            }
            if (result.TweaksNeedElevation > 0)
                Console.WriteLine("\n  Run this from an Administrator prompt to apply the remaining tweaks.");
            if (!string.IsNullOrEmpty(result.SnapshotId))
                Console.WriteLine($"\n  Snapshot: {result.SnapshotId}");
        }
    }

    /// <summary>
    /// Preview a batch: the order it would run in, and why anything would be
    /// held back, without touching the system.
    /// </summary>
    private static async Task RunPlan(string? tweakId, string? profile, string? category, string? include, bool json)
    {
        var engine = CreateEngine();
        await engine.Database.LoadAsync();
        RegisterProviders(engine);

        var systemInfo = await new SystemDetector().DetectAsync();
        var selection = SelectTweaks(engine, systemInfo, tweakId, profile, category, include);
        if (selection.Error != null)
        {
            Console.Error.WriteLine($"  {selection.Error}");
            Environment.ExitCode = 1;
            return;
        }

        var plan = engine.PlanBatch(selection.Tweaks);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(plan, JsonOpts));
        }
        else
        {
            Console.WriteLine($"\n  {plan.ApplicableCount} of {plan.RequestedCount} requested tweaks would run.\n");

            var order = 1;
            foreach (var id in plan.OrderedTweakIds)
                Console.WriteLine($"    {order++,3}. {id}");

            var blocked = plan.Entries.Where(e => e.Action != PlanAction.Apply).ToList();
            if (blocked.Count > 0)
            {
                Console.WriteLine($"\n  Held back ({blocked.Count}):");
                foreach (var entry in blocked)
                {
                    var old = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"    {entry.Action,-22}");
                    Console.ForegroundColor = old;
                    Console.WriteLine($"{entry.TweakId}");
                    Console.WriteLine($"      {entry.Reason}");
                }
            }

            if (!plan.HasIssues)
                Console.WriteLine("\n  No dependencies, conflicts, or blockers.");
        }

        // A plan that runs nothing is worth noticing; a plan with issues exits
        // non-zero so a script stops before running an apply it did not expect.
        if (plan.HasIssues)
            Environment.ExitCode = 1;
    }

    /// <summary>
    /// Read the append-only change journal.
    /// </summary>
    private static Task RunJournal(int limit, string? tweakId, string? operation, string? result, bool json)
    {
        var logger = new WinOptLogger();
        var entries = logger.ReadJournal(limit, tweakId, operation, result);

        if (json)
        {
            Console.WriteLine(logger.ReadJournalJson(limit, tweakId, operation, result));
            return Task.CompletedTask;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("\n  The journal is empty — nothing has been changed on this machine yet.");
            return Task.CompletedTask;
        }

        Console.WriteLine($"\n  {entries.Count} journal entries (newest first)\n");
        foreach (var e in entries)
        {
            var color = e.Result switch
            {
                "success" => ConsoleColor.Green,
                "failure" => ConsoleColor.Red,
                "blocked" => ConsoleColor.DarkYellow,
                _ => ConsoleColor.DarkGray
            };
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"  {e.Result,-10}");
            Console.ForegroundColor = old;
            Console.Write($" {e.Operation,-9} {e.Timestamp:yyyy-MM-dd HH:mm:ss}  {e.TweakId}");
            if (e.OldValue != null || e.NewValue != null)
                Console.Write($"  {e.OldValue ?? "?"} → {e.NewValue ?? "?"}");
            Console.WriteLine();
            if (e.ErrorDetails != null)
                Console.WriteLine($"      {e.ErrorDetails}");
        }

        Console.WriteLine($"\n  Full journal: {logger.JournalDirectory}");
        return Task.CompletedTask;
    }

    private static async Task RunRollback(string? tweakId, string? snapshot, bool all, bool json)
    {
        var engine = CreateEngine();
        RegisterProviders(engine);

        if (all)
        {
            var snapshots = engine.Snapshots.List();
            var target = !string.IsNullOrEmpty(snapshot)
                ? snapshots.FirstOrDefault(s => s.Id == snapshot)
                : snapshots.FirstOrDefault();

            if (target == null)
            {
                var notFound = new
                {
                    success = false,
                    message = string.IsNullOrEmpty(snapshot)
                        ? "No snapshots found."
                        : $"Snapshot '{snapshot}' not found."
                };
                if (json) Console.WriteLine(JsonSerializer.Serialize(notFound, JsonOpts));
                else Console.Error.WriteLine($"  {notFound.message}");
                Environment.ExitCode = 1;
                return;
            }

            var count = await engine.RollbackAllAsync(target.Id);
            var allResult = new
            {
                success = true,
                snapshotId = target.Id,
                entriesAttempted = target.EntryCount,
                entriesRolledBack = count
            };

            if (json)
                Console.WriteLine(JsonSerializer.Serialize(allResult, JsonOpts));
            else
                Console.WriteLine($"  Rolled back {count} of {target.EntryCount} entries from snapshot {target.Id}");

            if (count < target.EntryCount) Environment.ExitCode = 1;
            return;
        }

        if (string.IsNullOrEmpty(tweakId))
        {
            var usage = new { success = false, message = "Specify a tweak ID, or --all (optionally with --snapshot <id>)." };
            if (json) Console.WriteLine(JsonSerializer.Serialize(usage, JsonOpts));
            else Console.Error.WriteLine($"  {usage.message}");
            Environment.ExitCode = 1;
            return;
        }

        // Fall back to the newest snapshot that actually holds an entry for this
        // tweak, otherwise a plain "rollback <id>" always failed.
        var snapshotId = snapshot;
        if (string.IsNullOrEmpty(snapshotId))
        {
            var match = engine.Snapshots.List()
                .Select(s => engine.Snapshots.Load(s.Id))
                .Where(s => s != null && s.Entries.Any(e => e.TweakId == tweakId))
                .OrderByDescending(s => s!.Timestamp)
                .FirstOrDefault();
            snapshotId = match?.Id;
        }

        if (string.IsNullOrEmpty(snapshotId))
        {
            var notFound = new { success = false, tweakId, message = $"No snapshot entry found for tweak '{tweakId}'." };
            if (json) Console.WriteLine(JsonSerializer.Serialize(notFound, JsonOpts));
            else Console.Error.WriteLine($"  {notFound.message}");
            Environment.ExitCode = 1;
            return;
        }

        var result = await engine.RollbackAsync(tweakId, snapshotId);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = result.Success,
                tweakId = result.TweakId,
                snapshotId,
                message = result.Message
            }, JsonOpts));
        }
        else
        {
            Console.WriteLine(result.Success
                ? $"  Rolled back {tweakId} from snapshot {snapshotId}."
                : $"  Rollback failed: {result.Message}");
        }

        if (!result.Success) Environment.ExitCode = 1;
    }

    private static void RunProfiles(bool json)
    {
        if (json)
        {
            // The full profile, not a summary: the Profiles page renders the
            // category chips and the risk/evidence thresholds the user is
            // agreeing to, and a second hand-written copy of these on the
            // frontend drifted out of date silently. `tags` and `riskLevel`
            // stay for the onboarding step, which reads them as labels.
            var list = BuiltInProfiles.All.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                icon = p.Icon,
                riskLevel = p.MaxRisk.ToString().ToLower(),
                tags = p.IncludeCategories.ToArray(),
                includeCategories = p.IncludeCategories.ToArray(),
                excludeCategories = p.ExcludeCategories.ToArray(),
                includeTweaks = p.IncludeTweaks.ToArray(),
                excludeTweaks = p.ExcludeTweaks.ToArray(),
                maxRisk = p.MaxRisk.ToString(),
                minEvidence = p.MinEvidence,
                allowAutoOptimize = p.AllowAutoOptimize,
                minTier = p.MinTier?.ToString(),
                maxTier = p.MaxTier?.ToString(),
                formFactor = p.FormFactor,
                gpuVendor = p.GpuVendor,
            }).ToList();
            Console.WriteLine(JsonSerializer.Serialize(list, JsonOpts));
            return;
        }

        Console.WriteLine("  Available Optimization Profiles:\n");
        foreach (var p in BuiltInProfiles.All)
        {
            Console.WriteLine($"  {p.Icon} {p.Id,-15} {p.Name}");
            Console.WriteLine($"    {p.Description}");
            if (p.MinTier.HasValue || p.MaxTier.HasValue)
                Console.WriteLine($"    Tier: {p.MinTier?.ToString() ?? "Any"} — {p.MaxTier?.ToString() ?? "Any"}");
            Console.WriteLine();
        }
    }

    private static async Task RunRecommend(string? profile, int top, bool json)
    {
        var engine = CreateEngine();
        await engine.Database.LoadAsync();
        var detector = new SystemDetector();
        var systemInfo = await detector.DetectAsync();

        var recEngine = new RecommendationEngine(engine.Database, engine.Logger);
        var recs = recEngine.TopN(systemInfo, top, profile);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(recs, JsonOpts));
        }
        else
        {
            Console.WriteLine($"  Recommendations for {systemInfo.OverallTier} tier system:\n");
            foreach (var rec in recs)
            {
                var color = rec.Priority switch
                {
                    RecommendationPriority.High => ConsoleColor.Green,
                    RecommendationPriority.Medium => ConsoleColor.Yellow,
                    RecommendationPriority.Low => ConsoleColor.DarkGray,
                    _ => ConsoleColor.Gray
                };
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"  [{rec.Score,4:F0}] {rec.Priority,-8}");
                Console.ForegroundColor = old;
                Console.WriteLine($" {rec.Tweak.Id,-35} {rec.Tweak.Name}");
                Console.WriteLine($"         {rec.Reason}");
            }
        }
    }

    private static async Task RunDoctor(string? mode, bool json)
    {
        var detector = new SystemDetector();
        var logger = new WinOptLogger();
        var diag = new DiagnosticsEngine(detector, logger);

        switch (mode?.ToLowerInvariant())
        {
            case "network" or "net":
                var net = await diag.NetworkDiagnosticsAsync();
                if (json) { Console.WriteLine(JsonSerializer.Serialize(net, JsonOpts)); return; }
                Console.WriteLine($"  Network Diagnostics — Target: {net.Target}\n");
                Console.WriteLine($"  Adapters: {net.AdapterInfo.Count}");
                foreach (var a in net.AdapterInfo)
                    Console.WriteLine($"    {a.Name} ({a.Type}) — {a.Status}, {a.Speed / 1_000_000}Mbps");
                Console.WriteLine($"\n  Ping: avg={net.PingResults.AverageLatencyMs:F1}ms, min={net.PingResults.MinLatencyMs}ms, max={net.PingResults.MaxLatencyMs}ms, loss={net.PingResults.PacketLossPercent:F1}%");
                Console.WriteLine($"  DNS ({net.DnsResolution.Domain}): {net.DnsResolution.ResolutionMs}ms, resolved={net.DnsResolution.Resolved}");
                break;

            case "startup":
                var startup = await diag.StartupDiagnosticsAsync();
                if (json) { Console.WriteLine(JsonSerializer.Serialize(startup, JsonOpts)); return; }
                Console.WriteLine($"  Startup Items: {startup.TotalCount}\n");
                foreach (var item in startup.Items)
                    Console.WriteLine($"    {item.Name,-30} [{item.Source}] {item.Command}");
                Console.WriteLine($"\n  {startup.Recommendation}");
                break;

            case "bench" or "benchmark":
                var bench = await diag.BenchmarkAsync();
                if (json) { Console.WriteLine(JsonSerializer.Serialize(bench, JsonOpts)); return; }
                Console.WriteLine("  Benchmark Results:\n");
                Console.WriteLine($"  Network:  avg={bench.Network.AvgLatencyMs:F1}ms, min={bench.Network.MinLatencyMs}ms, max={bench.Network.MaxLatencyMs}ms, loss={bench.Network.PacketLossPercent:F1}%");
                Console.WriteLine($"  DNS:      {bench.DnsResolutionMs}ms");
                Console.WriteLine($"  Memory:   {bench.Memory.UsedGb:F1}GB / {bench.Memory.TotalGb}GB");
                Console.WriteLine($"  Duration: {bench.Duration.TotalSeconds:F1}s");
                break;

            default: // health
                var health = await diag.HealthCheckAsync();
                if (json) { Console.WriteLine(JsonSerializer.Serialize(health, JsonOpts)); return; }
                var color = health.OverallStatus switch
                {
                    HealthStatus.Ok => ConsoleColor.Green,
                    HealthStatus.Warning => ConsoleColor.Yellow,
                    HealthStatus.Critical => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };
                Console.Write("  Overall: ");
                var oc = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(health.OverallStatus);
                Console.ForegroundColor = oc;
                Console.WriteLine();
                foreach (var check in health.Checks)
                {
                    var cc = check.Status switch
                    {
                        HealthStatus.Ok => ConsoleColor.Green,
                        HealthStatus.Warning => ConsoleColor.Yellow,
                        HealthStatus.Critical => ConsoleColor.Red,
                        _ => ConsoleColor.White
                    };
                    Console.Write("  ");
                    Console.ForegroundColor = cc;
                    Console.Write($"[{check.Status,-8}]");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($" {check.Name,-25} {check.Details}");
                }
                break;
        }
    }

    private static void RunSnapshots(bool json)
    {
        var logger = new WinOptLogger();
        var mgr = new Engine.Snapshots.SnapshotManager(logger);
        var snapshots = mgr.List();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(snapshots, JsonOpts));
            return;
        }

        if (snapshots.Count == 0)
        {
            Console.WriteLine("  No snapshots found.");
            return;
        }

        Console.WriteLine($"  {"ID",-38} {"Date",-22} {"Entries",7} {"Description"}");
        Console.WriteLine($"  {new string('─', 38)} {new string('─', 22)} {new string('─', 7)} {new string('─', 30)}");
        foreach (var s in snapshots)
        {
            Console.WriteLine($"  {s.Id,-38} {s.Timestamp:yyyy-MM-dd HH:mm:ss} {s.EntryCount,7} {s.Description}");
        }
    }

    // === Helpers ===

    // === Gaming Center ===

    private sealed class GameModeRequest
    {
        public string Action { get; init; } = string.Empty;
        public string Plan { get; init; } = "high-performance";
        public bool NoNotifications { get; init; }
        public bool NoBackgroundApps { get; init; }
        public string? Services { get; init; }
        public string? ForProcess { get; init; }
        public string? Game { get; init; }
        public string? Controls { get; init; }
        public string? Path { get; init; }
        public string? Name { get; init; }
        public bool Json { get; init; }
    }

    private static List<string> SplitList(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static async Task RunGameMode(GameModeRequest request)
    {
        var logger = new WinOptLogger();
        var manager = new GameModeManager(logger);
        var detector = new LauncherDetector();

        switch (request.Action.ToLowerInvariant())
        {
            case "detect":
            {
                var detection = detector.Detect();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(detection, JsonOpts));
                    return;
                }

                Console.WriteLine("\n  Launchers");
                foreach (var launcher in detection.Launchers)
                {
                    if (!launcher.Detected)
                    {
                        Console.WriteLine($"    · {launcher.Name,-24} not found");
                        continue;
                    }
                    Console.WriteLine($"    ✓ {launcher.Name,-24} {launcher.Evidence}");
                    if (launcher.InstallPath != null) Console.WriteLine($"      {launcher.InstallPath}");
                    foreach (var library in launcher.Libraries)
                        Console.WriteLine($"      library: {library}");
                }

                var manifests = detection.Games.Count(g => g.Source == "manifest");
                var candidates = detection.Games.Count - manifests;
                Console.WriteLine($"\n  Games — {manifests} from launchers, {candidates} candidates from folders");
                foreach (var game in detection.Games)
                {
                    var tag = game.Source == "manifest" ? "manifest" : "candidate";
                    Console.WriteLine($"    [{tag,-8}] {game.Name,-40} {game.Launcher}");
                    if (game.InstallPath != null) Console.WriteLine($"      {game.InstallPath}");
                }

                if (detection.Folders.Count > 0)
                {
                    Console.WriteLine("\n  Folders you added");
                    foreach (var folder in detection.Folders) Console.WriteLine($"    {folder}");
                }

                foreach (var warning in detection.Warnings)
                    Console.WriteLine($"\n  ! {warning}");
                return;
            }

            case "folders":
            {
                var folders = detector.GetFolders();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(folders, JsonOpts));
                    return;
                }
                Console.WriteLine(folders.Count == 0
                    ? "\n  No folders added. Use 'game-mode folder-add --path <folder>'."
                    : "\n  " + string.Join("\n  ", folders));
                return;
            }

            case "folder-add":
            {
                if (string.IsNullOrWhiteSpace(request.Path)) { Fail("folder-add requires --path"); return; }
                var added = detector.AddFolder(request.Path);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { path = request.Path, added }, JsonOpts));
                    return;
                }
                if (!added) { Fail($"'{request.Path}' is not a readable directory."); return; }
                Console.WriteLine($"\n  Added {request.Path}");
                return;
            }

            case "folder-remove":
            {
                if (string.IsNullOrWhiteSpace(request.Path)) { Fail("folder-remove requires --path"); return; }
                var removed = detector.RemoveFolder(request.Path);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { path = request.Path, removed }, JsonOpts));
                    return;
                }
                if (!removed) { Fail("That folder was not in the list."); return; }
                Console.WriteLine($"\n  Removed {request.Path}");
                return;
            }

            case "start":
            {
                var result = await manager.StartAsync(new GameModeOptions
                {
                    Plan = request.Plan,
                    Notifications = !request.NoNotifications,
                    BackgroundApps = !request.NoBackgroundApps,
                    Services = SplitList(request.Services),
                    ForProcess = request.ForProcess,
                    GamePath = request.Game,
                    Only = request.Controls == null ? null : SplitList(request.Controls),
                });
                PrintGameModeResult("Game mode started", result, request.Json);
                if (!result.Success) Environment.ExitCode = 1;
                return;
            }

            case "status":
            {
                var status = manager.Status();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(status, JsonOpts));
                    return;
                }
                if (!status.Active)
                {
                    Console.WriteLine("\n  No game-mode session is active.");
                    return;
                }
                Console.WriteLine($"\n  Game mode active since {status.StartedAt:yyyy-MM-dd HH:mm:ss} UTC" +
                                  (status.Session?.GamePath != null ? $"  — {status.Session.GamePath}" : ""));
                if (status.OwnerRunning != null)
                    Console.WriteLine($"  {status.Session?.OwnerProcess}: {(status.OwnerRunning.Value ? "still running" : "no longer running")}");
                PrintControls(status.Controls);
                return;
            }

            case "stop":
            {
                var result = await manager.StopAsync();
                PrintGameModeResult("Game mode stopped", result, request.Json);
                if (!result.Success) Environment.ExitCode = 1;
                return;
            }

            case "preset-list":
            {
                var presets = manager.Presets();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(presets, JsonOpts));
                    return;
                }
                if (presets.Count == 0)
                {
                    Console.WriteLine("\n  No presets saved.");
                    return;
                }
                foreach (var preset in presets)
                {
                    Console.WriteLine($"\n  {preset.Name}");
                    Console.WriteLine($"    plan: {preset.Plan ?? "default"}  notifications: {(preset.Notifications ? "suppress" : "leave")}  " +
                                      $"background apps: {(preset.BackgroundApps ? "deny" : "leave")}");
                    if (preset.Services.Count > 0) Console.WriteLine($"    stop services: {string.Join(", ", preset.Services)}");
                    if (preset.PriorityProcess != null) Console.WriteLine($"    raise priority: {preset.PriorityProcess}");
                    if (preset.GamePath != null) Console.WriteLine($"    game: {preset.GamePath}");
                }
                return;
            }

            case "preset-save":
            {
                var name = request.Name;
                if (string.IsNullOrWhiteSpace(name)) { Fail("preset-save requires --name"); return; }
                var saved = manager.SavePreset(new GameModePreset
                {
                    Name = name,
                    GamePath = request.Game,
                    Plan = request.Plan,
                    Services = SplitList(request.Services),
                    Notifications = !request.NoNotifications,
                    BackgroundApps = !request.NoBackgroundApps,
                    PriorityProcess = request.ForProcess,
                });
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { name, saved }, JsonOpts));
                    return;
                }
                if (!saved) { Fail($"Could not save preset '{name}'."); return; }
                Console.WriteLine($"\n  Saved preset '{name}'. It stores what the controls are, never a previous value.");
                return;
            }

            case "preset-apply":
            {
                if (string.IsNullOrWhiteSpace(request.Name)) { Fail("preset-apply requires --name"); return; }
                var preset = manager.FindPreset(request.Name);
                if (preset == null)
                {
                    if (request.Json)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(
                            new GameModeResult { Success = false, Message = $"No preset named '{request.Name}'." }, JsonOpts));
                        Environment.ExitCode = 1;
                        return;
                    }
                    Fail($"No preset named '{request.Name}'.");
                    return;
                }
                var result = await manager.StartAsync(GameModeManager.ToOptions(preset));
                PrintGameModeResult($"Game mode started from preset '{preset.Name}'", result, request.Json);
                if (!result.Success) Environment.ExitCode = 1;
                return;
            }

            case "preset-delete":
            {
                if (string.IsNullOrWhiteSpace(request.Name)) { Fail("preset-delete requires --name"); return; }
                var deleted = manager.DeletePreset(request.Name);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { name = request.Name, deleted }, JsonOpts));
                    return;
                }
                if (!deleted) { Fail($"No preset named '{request.Name}'."); return; }
                Console.WriteLine($"\n  Deleted preset '{request.Name}'.");
                return;
            }

            default:
                Fail($"Unknown action '{request.Action}'. " +
                     "Use detect, folders, folder-add, folder-remove, start, status, stop, " +
                     "preset-list, preset-save, preset-apply or preset-delete.");
                return;
        }
    }

    private static void PrintGameModeResult(string heading, GameModeResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            return;
        }

        Console.WriteLine($"\n  {heading} — {result.Message}");
        PrintControls(result.Controls);

        if (result.Status != null && result.Status.Active && result.Status.StartedAt != null)
            Console.WriteLine($"\n  Session is live. 'winopt game-mode stop' puts every value back.");
    }

    private static void PrintControls(IReadOnlyList<GameModeControl> controls)
    {
        foreach (var control in controls)
        {
            var (mark, color) = control.Applied
                ? ("✓", ConsoleColor.Green)
                : ("✗", ConsoleColor.Red);
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"    {mark} ");
            Console.ForegroundColor = old;
            Console.WriteLine($"{control.Kind,-16} {control.Description}");
            if (control.Before != null)
                Console.WriteLine($"        was: {control.Before}" + (control.Restorable ? "" : "  (nothing to restore)"));
            if (control.Error != null)
                Console.WriteLine($"        {control.Error}");
        }
    }

    // === Defender exclusions ===

    private sealed class DefenderRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Path { get; init; }
        public bool Confirm { get; init; }
        public string? Output { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunDefender(DefenderRequest request)
    {
        var logger = new WinOptLogger();
        var exclusions = new DefenderExclusions(logger);

        switch (request.Action.ToLowerInvariant())
        {
            case "list":
            {
                var state = await exclusions.ListAsync();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(state, JsonOpts));
                    return;
                }
                if (!state.Readable)
                {
                    Console.WriteLine($"\n  {state.Message ?? "Exclusions cannot be read."}");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"\n  {state.Paths.Count} excluded paths");
                foreach (var path in state.Paths) Console.WriteLine($"    {path}");
                if (state.Processes.Count > 0)
                {
                    Console.WriteLine($"\n  {state.Processes.Count} excluded processes");
                    foreach (var process in state.Processes) Console.WriteLine($"    {process}");
                }
                return;
            }

            case "add":
            {
                if (string.IsNullOrWhiteSpace(request.Path)) { Fail("add requires --path"); return; }
                var change = await exclusions.AddAsync(request.Path, request.Confirm);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(change, JsonOpts));
                    if (!change.Success) Environment.ExitCode = 1;
                    return;
                }
                if (!change.Success)
                {
                    Fail(change.Message ?? "The exclusion was not added.");
                    return;
                }
                var (allowed, reason) = DefenderExclusions.ValidatePath(change.Path);
                Console.WriteLine($"\n  {(change.Unchanged ? "Already excluded" : "Excluded")}: {change.Path}");
                if (allowed) Console.WriteLine("  Defender will not scan the contents of this path.");
                else Console.WriteLine($"  {reason}");
                return;
            }

            case "remove":
            {
                if (string.IsNullOrWhiteSpace(request.Path)) { Fail("remove requires --path"); return; }
                var change = await exclusions.RemoveAsync(request.Path);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(change, JsonOpts));
                    if (!change.Success) Environment.ExitCode = 1;
                    return;
                }
                if (!change.Success)
                {
                    Fail(change.Message ?? "The exclusion was not removed.");
                    return;
                }
                Console.WriteLine($"\n  {(change.Unchanged ? "Was not excluded" : "Removed")}: {change.Path}");
                return;
            }

            case "export":
            {
                try
                {
                    var file = await exclusions.ExportAsync(request.Output);
                    if (request.Json)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(new { file }, JsonOpts));
                        return;
                    }
                    Console.WriteLine($"\n  Wrote the exclusion list to {file}");
                    Console.WriteLine("  Re-apply with: Add-MpPreference -ExclusionPath (Get-Content <file>)");
                }
                catch (Exception ex)
                {
                    Fail(ex.Message);
                    if (request.Json) Console.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
                }
                return;
            }

            default:
                Fail($"Unknown action '{request.Action}'. Use list, add, remove or export.");
                return;
        }
    }

    private static void Fail(string message)
    {
        Environment.ExitCode = 1;
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"\n  {message}");
        Console.ForegroundColor = old;
    }

    private static OptimizeEngine CreateEngine()
    {
        var engine = new OptimizeEngine(tweaksDir: TweaksDir);
        return engine;
    }

    /// <summary>
    /// Every provider here owns at least one <see cref="TweakMethod"/>. The
    /// registry dispatches on method, so a provider claiming none can never be
    /// reached — those exist only as dead code, and are not kept.
    /// </summary>
    private static void RegisterProviders(OptimizeEngine engine)
    {
        engine.Providers.Register(new RegistryProvider());
        engine.Providers.Register(new ServiceProvider());
        engine.Providers.Register(new PowerShellProvider());
        engine.Providers.Register(new NetShProvider());
        engine.Providers.Register(new ScheduledTaskProvider());
    }

    private static void PrintDetection(DetectionResult r)
    {
        var stateIcon = r.State switch
        {
            TweakState.Applied => "✓",
            TweakState.NotApplied => "○",
            TweakState.PartiallyApplied => "~",
            TweakState.Incompatible => "⊘",
            TweakState.ConflictsDetected => "⚠",
            _ => "?"
        };
        var color = r.State switch
        {
            TweakState.Applied => ConsoleColor.Green,
            TweakState.NotApplied => ConsoleColor.Yellow,
            TweakState.PartiallyApplied => ConsoleColor.Cyan,
            TweakState.Incompatible => ConsoleColor.DarkGray,
            _ => ConsoleColor.Red
        };
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"  {stateIcon} ");
        Console.ForegroundColor = old;
        Console.Write($"{r.TweakId,-35}");
        Console.ForegroundColor = color;
        Console.Write($" {r.State,-20}");
        Console.ForegroundColor = old;
        Console.WriteLine(r.CurrentValue ?? "");
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
