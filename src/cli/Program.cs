using System.CommandLine;
using System.Text.Json;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Engine;
using WinOpt.Engine.Detection;
using WinOpt.Engine.Diagnostics;
using WinOpt.Engine.Gaming;
using WinOpt.Engine.Installer;
using WinOpt.Engine.Logging;
using WinOpt.Engine.Providers;
using WinOpt.Engine.Blocker;
using WinOpt.Engine.Network;
using WinOpt.Engine.Power;
using WinOpt.Engine.Startup;
using WinOpt.Engine.Services;
using WinOpt.Engine.Tasks;
using WinOpt.Engine.Debloat;
using WinOpt.Engine.Maintenance;
using WinOpt.Engine.Update;
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

        // -- apps --
        var appsCmd = new Command("apps", "App Installer: the curated catalogue, what is installed, and install/uninstall/upgrade through winget");
        var appsActionArg = new Argument<string>("action",
            "probe | catalog | status | installed | show | search | install | uninstall | upgrade | upgrade-all | launch");
        var appsIdOpt = new Option<string?>("--id", "winget package ID");
        var appsQueryOpt = new Option<string?>("--query", "Search text for `search`");
        var appsScopeOpt = new Option<string>("--scope", () => "any",
            "Install scope: any (winget decides) | user (current user) | machine (everyone, needs administrator)");
        var appsElevatedOpt = new Option<bool>("--elevated", "Retry through UAC");
        var appsDeepOpt = new Option<bool>("--deep",
            "Full installed-package scan, including apps installed outside winget (slow)");
        var appsJsonOpt = new Option<bool>("--json", "Output as JSON");
        appsCmd.AddArgument(appsActionArg);
        foreach (var option in new Option[]
                 {
                     appsIdOpt, appsQueryOpt, appsScopeOpt, appsElevatedOpt, appsDeepOpt, appsJsonOpt,
                 })
            appsCmd.AddOption(option);

        appsCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunApps(new AppsRequest
            {
                Action = parse.GetValueForArgument(appsActionArg),
                Id = parse.GetValueForOption(appsIdOpt),
                Query = parse.GetValueForOption(appsQueryOpt),
                Scope = parse.GetValueForOption(appsScopeOpt) ?? "any",
                Elevated = parse.GetValueForOption(appsElevatedOpt),
                Deep = parse.GetValueForOption(appsDeepOpt),
                Json = parse.GetValueForOption(appsJsonOpt),
            });
        });
        rootCommand.AddCommand(appsCmd);

        // -- blocker --
        var blockerCmd = new Command("blocker",
            "Blocker: hosts rules, firewall rules, blocklists — fetch, apply, list, and roll back");
        var blockerActionArg = new Argument<string>("action",
            "status | sources | fetch | apply | add | program | remove | enable | disable | " +
            "unmerge | restore | clear-firewall | export | import");
        var blockerSourceOpt = new Option<string?>("--source", "Blocklist source id (from `blocker sources`)");
        var blockerIdOpt = new Option<string?>("--id", "Rule id: a domain for hosts, a rule id for firewall");
        var blockerValueOpt = new Option<string?>("--value", "What to block: a domain, or an executable path for `program`");
        var blockerCategoryOpt = new Option<string?>("--category",
            "Ads | Trackers | Telemetry | Malware | Analytics | Software | Custom");
        var blockerPurposeOpt = new Option<string?>("--purpose", "One sentence saying what this blocks and why");
        var blockerKindOpt = new Option<string?>("--kind",
            "hosts | firewall | auto (default): auto looks in the hosts section first, then the firewall");
        var blockerSeverityOpt = new Option<string?>("--severity", "Low (default) | Medium | High");
        var blockerOutputOpt = new Option<string?>("--output", "Export destination (default: a file under %LOCALAPPDATA%\\WinOpt\\blocker)");
        var blockerInputOpt = new Option<string?>("--input", "Export file to import");
        var blockerConfirmOpt = new Option<bool>("--confirm",
            "Required for apply, unmerge, restore and clear-firewall");
        var blockerElevatedOpt = new Option<bool>("--elevated", "Retry through UAC");
        var blockerJsonOpt = new Option<bool>("--json", "Output as JSON");
        blockerCmd.AddArgument(blockerActionArg);
        foreach (var option in new Option[]
                 {
                     blockerSourceOpt, blockerIdOpt, blockerValueOpt, blockerCategoryOpt, blockerPurposeOpt,
                     blockerKindOpt, blockerSeverityOpt, blockerOutputOpt, blockerInputOpt, blockerConfirmOpt,
                     blockerElevatedOpt, blockerJsonOpt,
                 })
            blockerCmd.AddOption(option);

        blockerCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunBlocker(new BlockerRequest
            {
                Action = parse.GetValueForArgument(blockerActionArg),
                Source = parse.GetValueForOption(blockerSourceOpt),
                Id = parse.GetValueForOption(blockerIdOpt),
                Value = parse.GetValueForOption(blockerValueOpt),
                Category = parse.GetValueForOption(blockerCategoryOpt),
                Purpose = parse.GetValueForOption(blockerPurposeOpt),
                Kind = parse.GetValueForOption(blockerKindOpt),
                Severity = parse.GetValueForOption(blockerSeverityOpt),
                Output = parse.GetValueForOption(blockerOutputOpt),
                Input = parse.GetValueForOption(blockerInputOpt),
                Confirm = parse.GetValueForOption(blockerConfirmOpt),
                Elevated = parse.GetValueForOption(blockerElevatedOpt),
                Json = parse.GetValueForOption(blockerJsonOpt),
            });
        });
        rootCommand.AddCommand(blockerCmd);

        // -- dns --
        var dnsCmd = new Command("dns",
            "DNS: what the resolver is set to, change it, put it back, flush the cache, test it");
        var dnsActionArg = new Argument<string>("action", "status | set | revert | flush | test | latency");
        var dnsProviderOpt = new Option<string?>("--provider",
            "cloudflare | google | quad9 | adguard | nextdns (from `dns status`)");
        var dnsAdapterOpt = new Option<string?>("--adapter",
            "Adapter name or index. Default: every adapter that is up");
        var dnsDohOpt = new Option<bool>("--doh",
            "Also register DNS-over-HTTPS, where the provider has a confirmed endpoint");
        var dnsNameOpt = new Option<string?>("--name", "Name to resolve for `test` (default: example.com)");
        var dnsServerOpt = new Option<string?>("--server",
            "Resolve against this server instead of the system resolver");
        var dnsConfirmOpt = new Option<bool>("--confirm", "Required for set and revert");
        var dnsElevatedOpt = new Option<bool>("--elevated", "Retry through UAC");
        var dnsJsonOpt = new Option<bool>("--json", "Output as JSON");
        dnsCmd.AddArgument(dnsActionArg);
        foreach (var option in new Option[]
                 {
                     dnsProviderOpt, dnsAdapterOpt, dnsDohOpt, dnsNameOpt, dnsServerOpt,
                     dnsConfirmOpt, dnsElevatedOpt, dnsJsonOpt,
                 })
            dnsCmd.AddOption(option);

        dnsCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunDns(new DnsRequest
            {
                Action = parse.GetValueForArgument(dnsActionArg),
                Provider = parse.GetValueForOption(dnsProviderOpt),
                Adapter = parse.GetValueForOption(dnsAdapterOpt),
                Doh = parse.GetValueForOption(dnsDohOpt),
                Name = parse.GetValueForOption(dnsNameOpt),
                Server = parse.GetValueForOption(dnsServerOpt),
                Confirm = parse.GetValueForOption(dnsConfirmOpt),
                Elevated = parse.GetValueForOption(dnsElevatedOpt),
                Json = parse.GetValueForOption(dnsJsonOpt),
            });
        });
        rootCommand.AddCommand(dnsCmd);

        // -- net --
        var netCmd = new Command("net",
            "Network toolbox: adapters, routes, TCP settings, ping, traceroute, lookup, Fix Network");
        var netActionArg = new Argument<string>("action",
            "status | ping | trace | lookup | reverse | public-ip | fix | restart");
        var netHostOpt = new Option<string?>("--host", "Host or address for ping and trace");
        var netNameOpt = new Option<string?>("--name", "Name to resolve for `lookup`, adapter for `restart`");
        var netTypeOpt = new Option<string>("--type", () => "A", "Record type: A | AAAA | PTR | MX | TXT | CNAME");
        var netServerOpt = new Option<string?>("--server", "Resolve against this server instead of the system resolver");
        var netHopsOpt = new Option<int>("--hops", () => 30, "Maximum hops for `trace` (1-64)");
        var netLevelOpt = new Option<string>("--level", () => "quick",
            "quick (flush, renew, reload adapters) | full (adds TCP/IP and Winsock reset, needs a restart)");
        var netValueOpt = new Option<string?>("--value", "Address for `reverse`");
        var netConfirmOpt = new Option<bool>("--confirm", "Required for fix and restart");
        var netElevatedOpt = new Option<bool>("--elevated", "Retry through UAC");
        var netJsonOpt = new Option<bool>("--json", "Output as JSON");
        netCmd.AddArgument(netActionArg);
        foreach (var option in new Option[]
                 {
                     netHostOpt, netNameOpt, netTypeOpt, netServerOpt, netHopsOpt, netLevelOpt,
                     netValueOpt, netConfirmOpt, netElevatedOpt, netJsonOpt,
                 })
            netCmd.AddOption(option);

        netCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunNet(new NetRequest
            {
                Action = parse.GetValueForArgument(netActionArg),
                Host = parse.GetValueForOption(netHostOpt),
                Name = parse.GetValueForOption(netNameOpt),
                Type = parse.GetValueForOption(netTypeOpt) ?? "A",
                Server = parse.GetValueForOption(netServerOpt),
                Hops = parse.GetValueForOption(netHopsOpt),
                Level = parse.GetValueForOption(netLevelOpt) ?? "quick",
                Value = parse.GetValueForOption(netValueOpt),
                Confirm = parse.GetValueForOption(netConfirmOpt),
                Elevated = parse.GetValueForOption(netElevatedOpt),
                Json = parse.GetValueForOption(netJsonOpt),
            });
        });
        rootCommand.AddCommand(netCmd);

        // -- power --
        var powerCmd = new Command("power",
            "Power Center: the active plan, every setting behind it, and the way back");
        var powerActionArg = new Argument<string>("action", "status | plan | novimize | revert");
        var powerIdOpt = new Option<string?>("--id", "Plan GUID or plan name for `plan`");
        var powerConfirmOpt = new Option<bool>("--confirm", "Required for plan, novimize and revert");
        var powerJsonOpt = new Option<bool>("--json", "Output as JSON");
        powerCmd.AddArgument(powerActionArg);
        foreach (var option in new Option[] { powerIdOpt, powerConfirmOpt, powerJsonOpt })
            powerCmd.AddOption(option);

        powerCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunPower(new PowerRequest
            {
                Action = parse.GetValueForArgument(powerActionArg),
                Id = parse.GetValueForOption(powerIdOpt),
                Confirm = parse.GetValueForOption(powerConfirmOpt),
                Json = parse.GetValueForOption(powerJsonOpt),
            });
        });
        rootCommand.AddCommand(powerCmd);

        // -- startup --
        var startupCmd = new Command("startup",
            "Startup Manager: what runs at sign-in, and the flag that turns it off. Never a delete.");
        var startupActionArg = new Argument<string>("action", "status | enable | disable | open");
        var startupIdOpt = new Option<string?>("--id", "Entry id from `startup status`");
        var startupConfirmOpt = new Option<bool>("--confirm", "Required for enable and disable");
        var startupJsonOpt = new Option<bool>("--json", "Output as JSON");
        startupCmd.AddArgument(startupActionArg);
        foreach (var option in new Option[] { startupIdOpt, startupConfirmOpt, startupJsonOpt })
            startupCmd.AddOption(option);

        startupCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunStartup(new StartupRequest
            {
                Action = parse.GetValueForArgument(startupActionArg),
                Id = parse.GetValueForOption(startupIdOpt),
                Confirm = parse.GetValueForOption(startupConfirmOpt),
                Json = parse.GetValueForOption(startupJsonOpt),
            });
        });
        rootCommand.AddCommand(startupCmd);

        // -- services --
        var servicesCmd = new Command("services",
            "Services: what is running, what needs what, and what Novimize will not touch");
        var servicesActionArg = new Argument<string>("action",
            "status | start | stop | restart | manual | automatic | disabled | restore");
        var servicesNameOpt = new Option<string?>("--name", "Service name (or display name)");
        var servicesFilterOpt = new Option<string?>("--filter", "Only show services whose name or display name contains this");
        var servicesConfirmOpt = new Option<bool>("--confirm", "Required for every change");
        var servicesJsonOpt = new Option<bool>("--json", "Output as JSON");
        servicesCmd.AddArgument(servicesActionArg);
        foreach (var option in new Option[]
                 {
                     servicesNameOpt, servicesFilterOpt, servicesConfirmOpt, servicesJsonOpt,
                 })
            servicesCmd.AddOption(option);

        servicesCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunServices(new ServicesRequest
            {
                Action = parse.GetValueForArgument(servicesActionArg),
                Name = parse.GetValueForOption(servicesNameOpt),
                Filter = parse.GetValueForOption(servicesFilterOpt),
                Confirm = parse.GetValueForOption(servicesConfirmOpt),
                Json = parse.GetValueForOption(servicesJsonOpt),
            });
        });
        rootCommand.AddCommand(servicesCmd);

        // -- tasks --
        var tasksCmd = new Command("tasks",
            "Scheduled tasks: trigger, last run, next run — disabled, never deleted");
        var tasksActionArg = new Argument<string>("action", "status | enable | disable | run | restore");
        var tasksIdOpt = new Option<string?>("--id", "Task id from `tasks status`");
        var tasksFilterOpt = new Option<string?>("--filter", "Only show tasks whose name or path contains this");
        var tasksConfirmOpt = new Option<bool>("--confirm", "Required for enable, disable and restore");
        var tasksJsonOpt = new Option<bool>("--json", "Output as JSON");
        tasksCmd.AddArgument(tasksActionArg);
        foreach (var option in new Option[] { tasksIdOpt, tasksFilterOpt, tasksConfirmOpt, tasksJsonOpt })
            tasksCmd.AddOption(option);

        tasksCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunTasks(new TasksRequest
            {
                Action = parse.GetValueForArgument(tasksActionArg),
                Id = parse.GetValueForOption(tasksIdOpt),
                Filter = parse.GetValueForOption(tasksFilterOpt),
                Confirm = parse.GetValueForOption(tasksConfirmOpt),
                Json = parse.GetValueForOption(tasksJsonOpt),
            });
        });
        rootCommand.AddCommand(tasksCmd);

        // -- debloat --
        var debloatCmd = new Command("debloat",
            "Debloat Center: installed Store packages, what is safe to remove, and how to put it back");
        var debloatActionArg = new Argument<string>("action", "status | remove | restore");
        var debloatNameOpt = new Option<string?>("--name", "Package name from `debloat status`");
        var debloatAllUsersOpt = new Option<bool>("--all-users",
            "Act for every user rather than for you (remove needs administrator)");
        var debloatConfirmOpt = new Option<bool>("--confirm", "Required for remove and restore");
        var debloatJsonOpt = new Option<bool>("--json", "Output as JSON");
        debloatCmd.AddArgument(debloatActionArg);
        foreach (var option in new Option[]
                 {
                     debloatNameOpt, debloatAllUsersOpt, debloatConfirmOpt, debloatJsonOpt,
                 })
            debloatCmd.AddOption(option);

        debloatCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunDebloat(new DebloatRequest
            {
                Action = parse.GetValueForArgument(debloatActionArg),
                Name = parse.GetValueForOption(debloatNameOpt),
                AllUsers = parse.GetValueForOption(debloatAllUsersOpt),
                Confirm = parse.GetValueForOption(debloatConfirmOpt),
                Json = parse.GetValueForOption(debloatJsonOpt),
            });
        });
        rootCommand.AddCommand(debloatCmd);

        // -- maint --
        var maintCmd = new Command("maint",
            "Maintenance Center: what each action deletes, how big it is, and the commands it runs");
        var maintActionArg = new Argument<string>("action", "status | run");
        var maintIdOpt = new Option<string?>("--id",
            "Tool id: temp | recycle | thumbnails | icons | updateCache | searchIndex | " +
            "componentStore | healthCheck | restoreHealth | sfc | diskCleanup");
        var maintConfirmOpt = new Option<bool>("--confirm", "Required for run");
        var maintJsonOpt = new Option<bool>("--json", "Output as JSON");
        maintCmd.AddArgument(maintActionArg);
        foreach (var option in new Option[] { maintIdOpt, maintConfirmOpt, maintJsonOpt })
            maintCmd.AddOption(option);

        maintCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunMaintenance(new MaintenanceRequest
            {
                Action = parse.GetValueForArgument(maintActionArg),
                Id = parse.GetValueForOption(maintIdOpt),
                Confirm = parse.GetValueForOption(maintConfirmOpt),
                Json = parse.GetValueForOption(maintJsonOpt),
            });
        });
        rootCommand.AddCommand(maintCmd);

        // -- update --
        var updateCmd = new Command("update",
            "Windows Update: where it stands, what is owed, and how to finish it");
        var updateActionArg = new Argument<string>("action", "status | scan | open | restart");
        var updateConfirmOpt = new Option<bool>("--confirm", "Required for restart");
        var updateJsonOpt = new Option<bool>("--json", "Output as JSON");
        updateCmd.AddArgument(updateActionArg);
        foreach (var option in new Option[] { updateConfirmOpt, updateJsonOpt })
            updateCmd.AddOption(option);

        updateCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunWindowsUpdate(new UpdateRequest
            {
                Action = parse.GetValueForArgument(updateActionArg),
                Confirm = parse.GetValueForOption(updateConfirmOpt),
                Json = parse.GetValueForOption(updateJsonOpt),
            });
        });
        rootCommand.AddCommand(updateCmd);

        // -- health --
        var healthCmd = new Command("health",
            "Health Dashboard: one report over security, storage, memory, activation, startup, " +
            "power and updates — exportable as JSON, text or HTML");
        var healthActionArg = new Argument<string>("action", "status | report");
        var healthFormatOpt = new Option<string>("--format", () => "txt", "json | txt | html");
        var healthOutputOpt = new Option<string?>("--output", "Where to write the report (default: %LOCALAPPDATA%\\WinOpt\\health)");
        var healthJsonOpt = new Option<bool>("--json", "Output the status as JSON");
        healthCmd.AddArgument(healthActionArg);
        foreach (var option in new Option[] { healthFormatOpt, healthOutputOpt, healthJsonOpt })
            healthCmd.AddOption(option);

        healthCmd.SetHandler(async context =>
        {
            var parse = context.ParseResult;
            await RunHealth(new HealthRequest
            {
                Action = parse.GetValueForArgument(healthActionArg),
                Format = parse.GetValueForOption(healthFormatOpt) ?? "txt",
                Output = parse.GetValueForOption(healthOutputOpt),
                Json = parse.GetValueForOption(healthJsonOpt),
            });
        });
        rootCommand.AddCommand(healthCmd);

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

    // === Blocker ===

    private sealed class BlockerRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Source { get; init; }
        public string? Id { get; init; }
        public string? Value { get; init; }
        public string? Category { get; init; }
        public string? Purpose { get; init; }
        public string? Kind { get; init; }
        public string? Severity { get; init; }
        public string? Output { get; init; }
        public string? Input { get; init; }
        public bool Confirm { get; init; }
        public bool Elevated { get; init; }
        public bool Json { get; init; }
    }

    /// <summary>Actions that write, and so are the ones that can ask for UAC.</summary>
    private static readonly HashSet<string> BlockerWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply", "add", "program", "remove", "enable", "disable",
        "unmerge", "restore", "clear-firewall", "import",
    };

    /// <summary>Actions that rewrite something wholesale and need --confirm.</summary>
    private static readonly HashSet<string> BlockerNeedsConfirm = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply", "unmerge", "restore", "clear-firewall", "import",
    };

    private static async Task RunBlocker(BlockerRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "sources" or "fetch" or "export") && !BlockerWrites.Contains(action))
        {
            Fail($"Unknown action '{request.Action}'. Use status, sources, fetch, apply, add, program, " +
                 "remove, enable, disable, unmerge, restore, clear-firewall, export or import.");
            return;
        }

        if (BlockerNeedsConfirm.Contains(action) && !request.Confirm)
        {
            // A bulk write is the moment a mistake becomes expensive, so it is
            // the one place the CLI insists on being asked rather than assumed.
            Fail($"{action} writes to the hosts file or the firewall. Re-run with --confirm once you have " +
                 "read what it will do (the text before this says how many rules are involved).");
            return;
        }

        var manager = new BlockerManager();

        object result;
        switch (action)
        {
            case "status":
                result = await manager.StatusAsync();
                break;

            case "sources":
                result = manager.Catalog.Sources;
                break;

            case "fetch":
            {
                if (string.IsNullOrWhiteSpace(request.Source)) { Fail("fetch requires --source"); return; }
                result = await manager.FetchAsync(request.Source);
                break;
            }

            case "apply":
            {
                if (string.IsNullOrWhiteSpace(request.Source)) { Fail("apply requires --source"); return; }
                result = await ElevateAsync(request, () => manager.ApplyAsync(request.Source!));
                break;
            }

            case "add":
            {
                if (string.IsNullOrWhiteSpace(request.Value)) { Fail("add requires --value <domain>"); return; }
                var category = ParseCategory(request.Category);
                if (!AcceptSoftware(request, category)) return;
                var purpose = request.Purpose ?? PurposeFor(category);
                result = await ElevateAsync(request,
                    () => Task.FromResult(manager.AddDomain(request.Value!, category, purpose, ParseSeverity(request.Severity))));
                break;
            }

            case "program":
            {
                if (string.IsNullOrWhiteSpace(request.Id)) { Fail("program requires --id"); return; }
                if (string.IsNullOrWhiteSpace(request.Value)) { Fail("program requires --value <path to the executable>"); return; }
                var category = ParseCategory(request.Category);
                if (!AcceptSoftware(request, category)) return;
                var purpose = request.Purpose ?? PurposeFor(category);
                result = await ElevateAsync(request,
                    () => manager.AddProgramAsync(request.Id!, request.Value!, category, purpose, ParseSeverity(request.Severity)));
                break;
            }

            case "remove":
            {
                // Two different things called remove: one rule, or an entire
                // applied list. The list form is the only way to take a
                // blocklist off without also taking the custom rules with it,
                // so it is behind --confirm like every other wholesale write.
                if (!string.IsNullOrWhiteSpace(request.Source))
                {
                    if (!request.Confirm)
                    {
                        Fail("remove --source takes an entire list out of the hosts file and the firewall, and " +
                             "leaves everything else exactly as it is. Re-run with --confirm once you have read " +
                             "what it will do.");
                        return;
                    }
                    result = await ElevateAsync(request, () => manager.RemoveSourceAsync(request.Source!));
                    break;
                }
                goto case "disable";
            }

            case "enable":
            case "disable":
            {
                if (string.IsNullOrWhiteSpace(request.Id))
                {
                    Fail(action == "remove"
                        ? "remove requires --id <domain> for one rule, or --source <list id> for a whole list."
                        : $"{action} requires --id");
                    return;
                }
                var wanted = BlockerTarget(request);
                if (wanted is null) { Fail($"Nothing called '{request.Id}' is managed by Novimize."); return; }
                result = wanted.Value.Kind switch
                {
                    BlockKind.Hosts => await ElevateAsync(request, () => Task.FromResult(
                        action == "remove"
                            ? manager.RemoveDomain(request.Id!)
                            : manager.SetDomainEnabled(request.Id!, action == "enable"))),
                    _ => await ElevateAsync(request, () => action == "remove"
                        ? manager.RemoveFirewallAsync(request.Id!)
                        : manager.SetFirewallEnabledAsync(request.Id!, action == "enable")),
                };
                break;
            }

            case "unmerge":
                result = await ElevateAsync(request, () => Task.FromResult(manager.Unmerge()));
                break;

            case "restore":
                result = await ElevateAsync(request, () => Task.FromResult(manager.Restore()));
                break;

            case "clear-firewall":
                result = await ElevateAsync(request, () => manager.ClearFirewallAsync());
                break;

            case "export":
                result = await manager.ExportAsync(request.Output);
                break;

            default:
            {
                if (string.IsNullOrWhiteSpace(request.Input)) { Fail("import requires --input <file>"); return; }
                result = await ElevateAsync(request, () => manager.ImportAsync(request.Input!));
                break;
            }
        }

        RenderBlocker(result, request);
    }

    /// <summary>
    /// Where an id lives: the hosts section first, then the firewall, unless
    /// --kind said otherwise. Checking both means "remove tracker.test" works
    /// whether the domain is a hosts line or a firewall address rule, and the
    /// answer comes from what is actually on the machine rather than from
    /// guessing at the shape of the string.
    /// </summary>
    private static (BlockKind Kind, string Id)? BlockerTarget(BlockerRequest request)
    {
        var kind = request.Kind?.ToLowerInvariant();
        var id = request.Id!.Trim();
        var hosts = new HostsFile();

        if (hosts.Exists && !hosts.Read().Malformed)
        {
            var managed = HostsFile.ParseInner(hosts.Read().Inner);
            if (managed.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)))
                return (BlockKind.Hosts, id);
        }

        if (kind == "hosts") return null;
        if (kind == "firewall") return (BlockKind.Firewall, id);

        // Not in the hosts section: it can still be a firewall rule, and a
        // domain that is in neither is an honest "not managed".
        return (BlockKind.Firewall, id);
    }

    /// <summary>
    /// How a Software-category rule has to be described. The framing is not
    /// decoration: an endpoint rule presented as anything other than "this
    /// product talks to these hosts" is exactly what the brief is guarding
    /// against, so the wording is fixed here rather than left to the caller.
    /// </summary>
    private const string SoftwarePurpose =
        "Optional software-specific network endpoint rule. It blocks the product's own telemetry and " +
        "network endpoints only, and is not an activation or licence bypass of any kind. Expect the " +
        "product to stop updating, stop signing in, or lose features that depend on reaching its servers.";

    private static string? PurposeFor(BlockCategory category) =>
        category == BlockCategory.Software ? SoftwarePurpose : null;

    /// <summary>
    /// A Software-category rule is opt-in twice over: it needs --confirm like
    /// any write, and it gets the framing above even when the caller supplied
    /// a sentence of their own.
    /// </summary>
    private static bool AcceptSoftware(BlockerRequest request, BlockCategory category)
    {
        if (category != BlockCategory.Software || request.Confirm) return true;
        Fail("A Software-category rule is an optional network endpoint block, never an activation or licence " +
             "bypass. Re-run with --confirm once you have read what it will block and what it may break.");
        return false;
    }

    private static BlockCategory ParseCategory(string? value) =>
        Enum.TryParse<BlockCategory>(value, ignoreCase: true, out var parsed) ? parsed : BlockCategory.Custom;

    private static BlockSeverity ParseSeverity(string? value) =>
        Enum.TryParse<BlockSeverity>(value, ignoreCase: true, out var parsed) ? parsed : BlockSeverity.Low;

    /// <summary>
    /// Run the operation; if it comes back needing rights we do not have, ask
    /// once through UAC rather than telling the caller to work out the flag.
    /// The <c>--elevated</c> marker stops that becoming a loop.
    /// </summary>
    private static async Task<T> ElevateAsync<T>(BlockerRequest request, Func<Task<T>> run) where T : BlockChange
    {
        var result = await run();
        if (result is not { Success: false, NeedsElevation: true } || request.Elevated || Elevation.IsElevated())
            return result;

        var elevated = await Elevation.RunSelfElevatedAsync<T>(BlockerArgs(request));
        return elevated ?? result with
        {
            Message = result.Message + " Administrator rights are required and were not granted, so nothing was changed.",
        };
    }

    /// <summary>The same request, as arguments an elevated copy can read.</summary>
    private static string[] BlockerArgs(BlockerRequest request)
    {
        var args = new List<string> { "blocker", request.Action };
        void Add(string flag, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            args.Add(flag);
            args.Add(value);
        }

        Add("--source", request.Source);
        Add("--id", request.Id);
        Add("--value", request.Value);
        Add("--category", request.Category);
        Add("--purpose", request.Purpose);
        Add("--kind", request.Kind);
        Add("--severity", request.Severity);
        Add("--output", request.Output);
        Add("--input", request.Input);
        if (request.Confirm) args.Add("--confirm");
        args.Add("--elevated");
        args.Add("--json");
        return args.ToArray();
    }

    private static void RenderBlocker(object result, BlockerRequest request)
    {
        if (request.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            if (result is BlockChange { Success: false }) Environment.ExitCode = 1;
            return;
        }

        switch (result)
        {
            case BlockerStatus status:
                PrintBlockerStatus(status);
                return;

            case IReadOnlyList<BlockSource> sources:
                if (sources.Count == 0)
                {
                    Console.WriteLine("\n  The blocklist catalogue could not be read, so no source can be fetched.");
                    Console.WriteLine("  Check that a blocklists/sources.json sits beside the executable.");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"\n  {sources.Count} blocklists Novimize knows how to fetch");
                foreach (var source in sources)
                {
                    Console.WriteLine($"\n    {source.Name}  [{source.Id}]");
                    Console.WriteLine($"      {source.Category} · {source.Format} · severity {source.Severity}");
                    Console.WriteLine($"      {source.Url}");
                    if (source.Purpose != null) Console.WriteLine($"      {source.Purpose}");
                    if (source.Breakage != null) Console.WriteLine($"      May break: {source.Breakage}");
                }
                return;

            case BlockFetchResult fetched:
                if (fetched.Url is null)
                {
                    Console.WriteLine($"\n  '{fetched.Source}' is not a list Novimize knows about. `blocker sources` lists them.");
                    Environment.ExitCode = 1;
                    return;
                }
                if (fetched.Bytes == 0)
                {
                    Console.WriteLine($"\n  The download from {fetched.Url} failed or came back empty.");
                    Console.WriteLine("  Nothing was written. Open the page in a browser and check it is up.");
                    Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine($"\n  {fetched.Source} — {fetched.Domains} entr{(fetched.Domains == 1 ? "y" : "ies")}, {fetched.Bytes} bytes");
                if (fetched.FirstTime)
                    Console.WriteLine("  Nothing from this source is applied yet, so there is no diff to show.");
                else
                    Console.WriteLine($"  Against what this machine enforces now: {fetched.Added} added, {fetched.Removed} removed.");
                if (fetched.CachedAt != null) Console.WriteLine($"  Cached at {fetched.CachedAt}");
                Console.WriteLine("  Nothing was written. Re-run with `blocker apply --source " + fetched.Source + " --confirm`.");
                return;

            case BlockChange change:
                var (mark, color) = change.Success
                    ? ("✓", ConsoleColor.Green)
                    : ("✗", ConsoleColor.Red);
                var old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"\n  {mark} ");
                Console.ForegroundColor = old;
                Console.WriteLine(change.Message);
                if (change.Backup != null) Console.WriteLine($"    Backup: {change.Backup}");
                if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
                if (change is { Success: false, NeedsElevation: true })
                    Console.WriteLine("    Re-run with --elevated to be asked once through UAC.");
                if (!change.Success) Environment.ExitCode = 1;
                return;

            default:
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                return;
        }
    }

    private static void PrintBlockerStatus(BlockerStatus status)
    {
        Console.WriteLine($"\n  Hosts file  {status.HostsPath}");
        Console.WriteLine($"    managed rules   {status.Managed} ({status.Enabled} enabled)");
        Console.WriteLine($"    yours (not ours) {status.Unmanaged} — listed, never touched");
        if (status.HostsMalformed)
            Console.WriteLine("    markers are unbalanced — nothing can be written until they are fixed by hand");
        Console.WriteLine($"    writable        {(status.Writable ? "yes" : "no (administrator rights needed)")}");
        Console.WriteLine($"    backup          {(status.BackupExists ? status.BackupPath : "none yet — one is taken on the first write")}");

        Console.WriteLine($"\n  Firewall  {status.FirewallRules} Novimize rule(s)"
                          + (status.FirewallReadable ? "" : " — and the firewall could not be read"));

        if (status.Applied.Count > 0)
        {
            Console.WriteLine("\n  Applied lists");
            foreach (var applied in status.Applied)
                Console.WriteLine($"    {applied.Name ?? applied.Source,-32} {applied.Domains,6} rules  " +
                                  $"added {applied.AppliedAt:yyyy-MM-dd}  updated {(applied.UpdatedAt?.ToString("yyyy-MM-dd") ?? "-")}");
        }
        else
        {
            Console.WriteLine("\n  No blocklist has been applied. `blocker sources` shows what is available.");
        }

        Console.WriteLine($"\n  {status.Sources.Count} sources in the catalogue; `blocker sources` lists them.");
    }

    // === DNS ===

    private sealed class DnsRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Provider { get; init; }
        public string? Adapter { get; init; }
        public bool Doh { get; init; }
        public string? Name { get; init; }
        public string? Server { get; init; }
        public bool Confirm { get; init; }
        public bool Elevated { get; init; }
        public bool Json { get; init; }
    }

    /// <summary>Actions that change what the machine resolves with.</summary>
    private static readonly HashSet<string> DnsWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "revert",
    };

    private static async Task RunDns(DnsRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "flush" or "test" or "latency") && !DnsWrites.Contains(action))
        {
            Fail($"Unknown dns action '{request.Action}'. Use status, set, revert, flush, test or latency.");
            return;
        }

        var dns = new DnsManager();

        if (action == "set" && string.IsNullOrWhiteSpace(request.Provider))
        {
            Fail("set requires --provider. `dns status` lists the ones Novimize knows.");
            return;
        }

        // Who your queries go to is a privacy decision, so changing it is
        // refused without the dialog rather than assumed. With --json the
        // refusal carries the exact command lines, because that is the preview
        // the confirmation is supposed to show.
        if (DnsWrites.Contains(action) && !request.Confirm)
        {
            var message = $"{action} changes what this machine resolves with. " +
                          "Nothing has been written; the commands listed are what it would do.";
            if (request.Json)
            {
                var preview = action == "set"
                    ? await dns.PreviewAsync(request.Provider!, request.Adapter, request.Doh)
                    : await dns.RevertPreviewAsync(request.Adapter);
                RenderJson(preview.Success ? preview with { Success = false, Message = message } : preview);
                Environment.ExitCode = 1;
                return;
            }
            Fail(message);
            return;
        }

        switch (action)
        {
            case "status":
            {
                var status = await dns.StatusAsync();
                if (request.Json) { RenderJson(status); return; }
                PrintDnsStatus(status);
                return;
            }

            case "latency":
            {
                var measured = await dns.MeasureAsync();
                if (request.Json)
                {
                    RenderJson(new
                    {
                        measuredAt = DateTimeOffset.Now,
                        note = "Measured from this machine just now. Not a ranking; latency depends on " +
                               "where you are and what your ISP does with UDP/53.",
                        providers = measured,
                    });
                    return;
                }
                Console.WriteLine($"\n  Round trip to each resolver, measured {DateTime.Now:HH:mm:ss}");
                Console.WriteLine("  This is one machine's numbers at one moment — not a ranking.");
                foreach (var p in measured)
                {
                    var latency = p.LatencyMs is null ? "no answer" : $"{p.LatencyMs} ms";
                    var loss = p.PacketLoss is null ? "" : $"   {p.PacketLoss}% loss";
                    Console.WriteLine($"    {p.Name,-22} {p.Ipv4.FirstOrDefault(),-16} {latency,9}{loss}");
                }
                return;
            }

            case "test":
            {
                var name = string.IsNullOrWhiteSpace(request.Name) ? "example.com" : request.Name!;
                var report = await dns.ResolveAsync(name, "A", request.Server);
                if (request.Json) { RenderJson(report); return; }
                if (report.Error is not null)
                {
                    Fail($"'{name}' did not resolve: {report.Error}");
                    return;
                }
                Console.WriteLine($"\n  {name} resolved in {report.ElapsedMs} ms"
                                  + (report.Server is null ? "" : $" via {report.Server}"));
                foreach (var r in report.Records)
                    Console.WriteLine($"    {r.Type,-8} {r.Data}   ttl {r.Ttl}");
                return;
            }

            case "flush":
            {
                var change = await dns.FlushAsync();
                if (request.Json) { RenderJson(change); return; }
                PrintChange(change);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }

            case "set":
            {
                var result = await dns.SetAsync(request.Provider!, request.Adapter, request.Doh);
                var final = await ElevateNetAsync(result, request.Elevated, DnsArgs(request),
                    () => dns.SetAsync(request.Provider!, request.Adapter, request.Doh));
                if (request.Json) { RenderJson(final); return; }
                PrintChange(final);
                PrintPreview(final);
                if (!final.Success) Environment.ExitCode = 1;
                return;
            }

            default:
            {
                var result = await dns.RevertAsync(request.Adapter);
                var final = await ElevateNetAsync(result, request.Elevated, DnsArgs(request),
                    () => dns.RevertAsync(request.Adapter));
                if (request.Json) { RenderJson(final); return; }
                PrintChange(final);
                PrintPreview(final);
                if (!final.Success) Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static string[] DnsArgs(DnsRequest request)
    {
        var args = new List<string> { "dns", request.Action };
        void Add(string flag, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            args.Add(flag);
            args.Add(value);
        }

        Add("--provider", request.Provider);
        Add("--adapter", request.Adapter);
        Add("--name", request.Name);
        Add("--server", request.Server);
        if (request.Doh) args.Add("--doh");
        if (request.Confirm) args.Add("--confirm");
        args.Add("--elevated");
        args.Add("--json");
        return args.ToArray();
    }

    private static void PrintDnsStatus(DnsStatus status)
    {
        if (status.Adapters.Count == 0)
        {
            Console.WriteLine("\n  No network adapters could be read.");
            if (status.ResolverInfo is not null) Console.WriteLine($"  {status.ResolverInfo}");
            return;
        }

        Console.WriteLine($"\n  Active provider: {status.ActiveProvider}");
        foreach (var a in status.Adapters)
        {
            Console.WriteLine($"\n    {a.Name}  [{a.Status}]  if {a.Index}");
            Console.WriteLine($"      DNSv4    {(a.Ipv4.Count > 0 ? string.Join(", ", a.Ipv4) : "-")}"
                              + (a.Dhcp4 ? "  (from DHCP)" : ""));
            Console.WriteLine($"      DNSv6    {(a.Ipv6.Count > 0 ? string.Join(", ", a.Ipv6) : "-")}"
                              + (a.Dhcp6 ? "  (from DHCP)" : ""));
            if (a.Gateway is not null) Console.WriteLine($"      Gateway  {a.Gateway}");
            if (a.NetworkCategory is not null) Console.WriteLine($"      Profile  {a.NetworkCategory}");
            if (a.ChangedByNovimize)
                Console.WriteLine($"      Changed by Novimize {(a.ChangedAt?.ToString("yyyy-MM-dd HH:mm") ?? "")} — `dns revert` puts it back");
        }

        Console.WriteLine($"\n  IPv6 configured on {status.Adapters.Count(a => a.Ipv6.Count > 0)} adapter(s); "
                          + (status.Ipv6Reachable ? "a v6 default route exists." : "no v6 default route, so v6 servers cannot be reached."));

        if (status.Doh.Count > 0)
        {
            Console.WriteLine("\n  DNS-over-HTTPS known to Windows");
            foreach (var d in status.Doh.Take(8))
                Console.WriteLine($"    {d.Address,-20} {d.Template}{(d.AutoUpgrade ? "  (auto-upgrade)" : "")}");
            if (status.Doh.Count > 8) Console.WriteLine($"    … and {status.Doh.Count - 8} more");
        }
        else
        {
            Console.WriteLine("\n  Windows knows no DoH server for the addresses in use.");
        }
        Console.WriteLine($"  DoT: {DnsManager.DotNote}");

        Console.WriteLine("\n  Resolvers Novimize can configure (latency from `dns latency`):");
        foreach (var p in DnsManager.Providers)
            Console.WriteLine($"    {p.Name,-22} {string.Join(", ", p.Ipv4)}"
                              + (p.DohTemplate is null ? "   no confirmed DoH endpoint" : ""));
        if (status.ResolverInfo is not null) Console.WriteLine($"\n  {status.ResolverInfo}");
    }

    // === Network toolbox ===

    private sealed class NetRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Host { get; init; }
        public string? Name { get; init; }
        public string Type { get; init; } = "A";
        public string? Server { get; init; }
        public int Hops { get; init; } = 30;
        public string Level { get; init; } = "quick";
        public string? Value { get; init; }
        public bool Confirm { get; init; }
        public bool Elevated { get; init; }
        public bool Json { get; init; }
    }

    private static readonly HashSet<string> NetWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "public-ip", "fix", "restart",
    };

    private static async Task RunNet(NetRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "ping" or "trace" or "lookup" or "reverse") && !NetWrites.Contains(action))
        {
            Fail($"Unknown net action '{request.Action}'. Use status, ping, trace, lookup, reverse, " +
                 "public-ip, fix or restart.");
            return;
        }

        var toolbox = new NetToolbox(new DnsManager());

        // Everything an action cannot work without is checked before the
        // confirmation gate, so a missing --host is reported as a missing
        // --host rather than as a refusal to confirm.
        if (action is "ping" or "trace" && string.IsNullOrWhiteSpace(request.Host))
        {
            Fail($"{action} requires --host");
            return;
        }
        if (action == "lookup" && string.IsNullOrWhiteSpace(request.Name))
        {
            Fail("lookup requires --name");
            return;
        }
        if (action == "reverse" && string.IsNullOrWhiteSpace(request.Value))
        {
            Fail("reverse requires --value <address>");
            return;
        }
        if (action == "restart" && string.IsNullOrWhiteSpace(request.Name))
        {
            Fail("restart requires --name <adapter>");
            return;
        }

        // public-ip reaches out to a third party; fix and restart rewrite
        // configuration. None of them run because somebody typed them, and
        // with --json the refusal carries the command lines so the dialog can
        // show them rather than paraphrase them.
        if (NetWrites.Contains(action) && !request.Confirm)
        {
            var why = action switch
            {
                "public-ip" => "Nothing has been requested. Re-run with --confirm to ask.",
                "restart" => "Nothing has been run. Re-run with --confirm to restart it.",
                _ => "Nothing has been run. Re-run with --confirm to execute the commands listed.",
            };
            if (request.Json)
            {
                var preview = action switch
                {
                    "fix" => toolbox.FixPreview(request.Level),
                    "restart" => NetToolbox.RestartPreview(request.Name!),
                    _ => NetToolbox.PublicIpPreview(),
                };
                RenderJson(preview with { Success = false, Message = why });
                Environment.ExitCode = 1;
                return;
            }
            if (action == "fix") PrintPreview(toolbox.FixPreview(request.Level));
            Fail(why);
            return;
        }

        switch (action)
        {
            case "status":
            {
                var overview = await toolbox.OverviewAsync(withPublicIp: false);
                if (request.Json) { RenderJson(overview); return; }
                PrintNetStatus(overview);
                return;
            }

            case "ping":
            {
                var report = await new DnsManager().PingAsync(request.Host!);
                if (request.Json) { RenderJson(report); return; }
                if (report.Error is not null && report.Sent == 0) { Fail(report.Error); return; }
                Console.WriteLine($"\n  {report.Host}: {report.Sent} sent, {report.Lost} lost " +
                                  $"({report.LossPercent:0.#}%)");
                if (report.Average is not null)
                    Console.WriteLine($"    min {report.Min} ms  avg {report.Average} ms  max {report.Max} ms");
                return;
            }

            case "trace":
            {
                var report = await toolbox.TraceAsync(request.Host!, request.Hops);
                if (request.Json) { RenderJson(report); return; }
                if (report.Error is not null && report.Hops.Count == 0) { Fail(report.Error); return; }
                Console.WriteLine($"\n  Route to {report.Host}" +
                                  (report.TargetIp is null ? "" : $" ({report.TargetIp})"));
                foreach (var hop in report.Hops)
                {
                    var times = hop.Times.Count > 0
                        ? string.Join("  ", hop.Times.Select(t => $"{t,3} ms"))
                        : "  no reply";
                    Console.WriteLine($"    {hop.Hop,3}  {hop.Host,-40} {times}");
                }
                Console.WriteLine(report.Reached
                    ? "\n  Reached the target."
                    : "\n  The last hop is not the target — the trace stopped early or the target did not answer.");
                return;
            }

            case "lookup":
            {
                var report = await toolbox.LookupAsync(request.Name!, request.Type, request.Server);
                if (request.Json) { RenderJson(report); return; }
                if (report.Error is not null) { Fail($"'{request.Name}' did not resolve: {report.Error}"); return; }
                Console.WriteLine($"\n  {request.Name} {report.Type} in {report.ElapsedMs} ms");
                foreach (var r in report.Records)
                    Console.WriteLine($"    {r.Data}   ttl {r.Ttl}");
                return;
            }

            case "reverse":
            {
                var report = await toolbox.ReverseAsync(request.Value!);
                if (request.Json) { RenderJson(report); return; }
                if (report.Error is not null) { Fail($"{request.Value}: {report.Error}"); return; }
                foreach (var r in report.Records)
                    Console.WriteLine($"    {r.Data}");
                return;
            }

            case "public-ip":
            {
                var (ip, error) = await toolbox.PublicIpAsync();
                if (request.Json) { RenderJson(new { ip, note = error }); return; }
                if (ip is null) { Fail(error ?? "The public address could not be fetched."); return; }
                Console.WriteLine($"\n  {ip}\n  {error}");
                return;
            }

            case "fix":
            {
                var result = await toolbox.FixAsync(request.Level, confirm: true);
                var final = await ElevateNetAsync(result, request.Elevated, NetArgs(request),
                    () => toolbox.FixAsync(request.Level, confirm: true));
                if (request.Json) { RenderJson(final); return; }
                PrintChange(final);
                PrintPreview(final);
                if (final.RestartRequired)
                    Console.WriteLine("  Restart Windows to finish the reset.");
                if (!final.Success) Environment.ExitCode = 1;
                return;
            }

            default:
            {
                var result = await toolbox.RestartAdapterAsync(request.Name!, confirm: true);
                var final = await ElevateNetAsync(result, request.Elevated, NetArgs(request),
                    () => toolbox.RestartAdapterAsync(request.Name!, confirm: true));
                if (request.Json) { RenderJson(final); return; }
                PrintChange(final);
                PrintPreview(final);
                if (!final.Success) Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static string[] NetArgs(NetRequest request)
    {
        var args = new List<string> { "net", request.Action };
        void Add(string flag, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            args.Add(flag);
            args.Add(value);
        }

        Add("--host", request.Host);
        Add("--name", request.Name);
        Add("--server", request.Server);
        Add("--level", request.Level);
        Add("--value", request.Value);
        args.Add("--type");
        args.Add(request.Type);
        args.Add("--hops");
        args.Add(request.Hops.ToString());
        if (request.Confirm) args.Add("--confirm");
        args.Add("--elevated");
        args.Add("--json");
        return args.ToArray();
    }

    private static void PrintNetStatus(NetworkOverview overview)
    {
        if (overview.Adapters.Count == 0)
        {
            Console.WriteLine("\n  No network adapters could be read.");
            if (overview.Error is not null) Console.WriteLine($"  {overview.Error}");
            return;
        }

        foreach (var a in overview.Adapters)
        {
            Console.WriteLine($"\n    {a.Name}  [{a.Status}]{(a.Physical ? "" : "  (virtual)")}");
            Console.WriteLine($"      Address  {string.Join(", ", a.Ipv4)}");
            if (a.Ipv6.Count > 0) Console.WriteLine($"      v6       {string.Join(", ", a.Ipv6)}");
            if (a.Gateway is not null) Console.WriteLine($"      Gateway  {a.Gateway}");
            Console.WriteLine($"      DNS      {(string.IsNullOrEmpty(a.Dns4) ? "-" : a.Dns4)}");
            Console.WriteLine($"      MTU {a.Mtu?.ToString() ?? "-"}   metric {a.Metric?.ToString() ?? "-"}   DHCP {a.Dhcp ?? "-"}"
                              + (a.NetworkCategory is null ? "" : $"   profile {a.NetworkCategory}"));
        }

        Console.WriteLine($"\n  {overview.Routes.Count} active route(s), {overview.Profiles.Count} connection profile(s)");
        foreach (var p in overview.Profiles)
            Console.WriteLine($"    {p.InterfaceAlias,-24} {p.Category,-10} v4 {p.IPv4Connectivity}  v6 {p.IPv6Connectivity}");

        if (overview.Tcp.Values.Count > 0)
        {
            Console.WriteLine("\n  TCP/IP parameters (absent = the Windows default):");
            foreach (var pair in overview.Tcp.Values.Where(v => v.Value is not null))
                Console.WriteLine($"    {pair.Key,-28} {pair.Value}");
        }
        if (overview.Tcp.CongestionProvider is not null)
            Console.WriteLine($"\n  Congestion provider {overview.Tcp.CongestionProvider}" +
                              (overview.Tcp.InitialWindow is null ? "" : $", auto-tuning {overview.Tcp.InitialWindow}"));
    }

    /// <summary>
    /// Same contract as the Blocker's: an operation that came back wanting
    /// rights we do not have is retried once through UAC rather than handed
    /// back as a flag for the caller to interpret. <c>--elevated</c> is what
    /// stops that becoming a loop.
    /// </summary>
    private static async Task<NetworkChange> ElevateNetAsync(
        NetworkChange result, bool elevated, string[] args, Func<Task<NetworkChange>> run)
    {
        if (!result.Success || !result.NeedsElevation || elevated || Elevation.IsElevated())
            return result;

        var raised = await Elevation.RunSelfElevatedAsync<NetworkChange>(args);
        return raised ?? result with
        {
            Message = result.Message + " Administrator rights are required and were not granted, so nothing was changed.",
        };
    }

    private static void RenderJson(object result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));

    private static void PrintChange(NetworkChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
        if (change.RestartRequired) Console.WriteLine("    A restart is needed to finish this.");
    }

    private static void PrintPreview(NetworkChange change) => PrintPreview(change.Preview);

    private static void PrintPreview(IReadOnlyList<string> commands)
    {
        if (commands.Count == 0) return;
        Console.WriteLine("\n  Commands:");
        foreach (var line in commands) Console.WriteLine($"    {line}");
    }

    // === Power Center ===

    private sealed class PowerRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Id { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static readonly HashSet<string> PowerWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "plan", "novimize", "revert",
    };

    private static async Task RunPower(PowerRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not "status" && !PowerWrites.Contains(action))
        {
            Fail($"Unknown power action '{request.Action}'. Use status, plan, novimize or revert.");
            return;
        }

        if (action == "plan" && string.IsNullOrWhiteSpace(request.Id))
        {
            Fail("plan requires --id. `power status` lists the plans this machine has.");
            return;
        }

        var power = new PowerCenter();

        if (PowerWrites.Contains(action) && !request.Confirm)
        {
            // The preview comes from the same object that will run, so the
            // dialog shows the real command lines rather than a second
            // description of them.
            var preview = action switch
            {
                "plan" => await power.SetPlanAsync(request.Id!, confirm: false),
                "novimize" => await power.ApplyNovimizePlanAsync(confirm: false),
                _ => await power.RevertPlanAsync(confirm: false),
            };
            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintPowerChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        switch (action)
        {
            case "status":
            {
                var status = await power.StatusAsync();
                if (request.Json) { RenderJson(status); return; }
                PrintPowerStatus(status);
                return;
            }

            case "plan":
            {
                var change = await power.SetPlanAsync(request.Id!, request.Confirm);
                if (request.Json) { RenderJson(change); return; }
                PrintPowerChange(change);
                PrintPreview(change.Preview);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }

            case "novimize":
            {
                var change = await power.ApplyNovimizePlanAsync(request.Confirm);
                if (request.Json) { RenderJson(change); return; }
                PrintPowerChange(change);
                PrintPreview(change.Preview);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }

            default:
            {
                var change = await power.RevertPlanAsync(request.Confirm);
                if (request.Json) { RenderJson(change); return; }
                PrintPowerChange(change);
                PrintPreview(change.Preview);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static void PrintPowerStatus(PowerStatus status)
    {
        Console.WriteLine($"\n  Active plan  {status.ActivePlan}");
        Console.WriteLine($"               {status.ActivePlanGuid}");
        Console.WriteLine($"  Machine      {status.FormFactor}"
                          + (status.Battery.Present
                              ? $" · battery {status.Battery.Percent?.ToString() ?? "?"}%"
                                + (status.Battery.MinutesRemaining is int m ? $" · ~{m} min left" : "")
                                + (status.Battery.OnAc ? " · on mains" : " · on battery")
                              : " · no battery"));

        Console.WriteLine("\n  Plans this machine has");
        foreach (var plan in status.Plans)
            Console.WriteLine($"    {(plan.Active ? "*" : " ")} {plan.Name,-32} {plan.Guid}");

        Console.WriteLine("\n  Settings on the active plan");
        foreach (var setting in status.Settings)
        {
            var value = setting.Unavailable
                ? setting.Value
                : setting.BatteryValue is null
                    ? setting.Value
                    : $"{setting.Value} on mains / {setting.BatteryValue} on battery";
            Console.WriteLine($"    {setting.Name,-42} {value}");
            if (setting.ExistingTweak is not null)
                Console.WriteLine($"      also a tweak: {setting.ExistingTweak}");
        }

        if (status.PreviousPlanGuid is not null)
            Console.WriteLine($"\n  Previous plan {status.PreviousPlanName} — `power revert` puts it back.");
        if (status.IsLaptop)
            Console.WriteLine("\n  This is a laptop. Ultimate Performance is never selected automatically here.");
        if (status.Error is not null) Console.WriteLine($"\n  {status.Error}");
    }

    private static void PrintPowerChange(PowerChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
    }

    // === Startup Manager ===

    private sealed class StartupRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Id { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunStartup(StartupRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "open" or "enable" or "disable"))
        {
            Fail($"Unknown startup action '{request.Action}'. Use status, enable, disable or open.");
            return;
        }

        var manager = new StartupManager();

        if (action is "enable" or "disable" or "open" && string.IsNullOrWhiteSpace(request.Id))
        {
            Fail($"{action} requires --id. `startup status` lists the ids.");
            return;
        }

        // Disabling is a setting, not a deletion, so it does not need the
        // word "confirm" in front of a system that already reads it back —
        // but the UI still shows the row it is about to flip, and the CLI
        // refuses to write without the dialog having happened.
        if (action is "enable" or "disable" && !request.Confirm)
        {
            var status = await manager.ReadAsync();
            var item = status.Items.FirstOrDefault(i =>
                i.Id.Equals(request.Id!, StringComparison.OrdinalIgnoreCase));
            var preview = item is null
                ? new StartupChange { Action = action, Success = false, Message = $"No startup entry with id '{request.Id}'." }
                : StartupManager.Preview(item, action == "enable") with { Success = false };

            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintStartupChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        switch (action)
        {
            case "status":
            {
                var status = await manager.ReadAsync();
                if (request.Json) { RenderJson(status); return; }
                PrintStartupStatus(status);
                return;
            }

            case "open":
            {
                var change = await manager.OpenAsync(request.Id!);
                if (request.Json) { RenderJson(change); return; }
                PrintStartupChange(change);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }

            default:
            {
                var change = await manager.SetEnabledAsync(request.Id!, action == "enable", request.Confirm);
                if (request.Json) { RenderJson(change); return; }
                PrintStartupChange(change);
                PrintPreview(change.Preview);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static void PrintStartupStatus(StartupStatus status)
    {
        if (status.Items.Count == 0)
        {
            Console.WriteLine("\n  Nothing was found in the startup locations.");
            if (status.Error is not null) Console.WriteLine($"  {status.Error}");
            return;
        }

        Console.WriteLine($"\n  {status.Items.Count} startup entr{(status.Items.Count == 1 ? "y" : "ies")} — " +
                          $"{status.EnabledCount} enabled, {status.DisabledCount} disabled, " +
                          $"{status.BrokenCount} pointing at files that are gone");
        Console.WriteLine($"  User folder    {status.UserStartupFolder}");
        Console.WriteLine($"  Common folder  {status.CommonStartupFolder}");

        foreach (var group in status.Items.GroupBy(i => i.Kind))
        {
            Console.WriteLine($"\n  {group.Key}");
            foreach (var item in group.OrderBy(i => i.Name))
            {
                var state = item.Enabled ? "on " : "off";
                Console.WriteLine($"    [{state}] {item.Name,-34} {item.Impact,-8} {item.Publisher ?? "-"}");
                Console.WriteLine($"             {item.Command}");
                Console.WriteLine($"             {item.ImpactReason}");
                if (!item.Writable)
                    Console.WriteLine("             needs administrator rights to change");
            }
        }

        Console.WriteLine("\n  Nothing here is ever deleted. Disabling writes the flag Task Manager writes.");
    }

    private static void PrintStartupChange(StartupChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
    }

    // === Services ===

    private sealed class ServicesRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Filter { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static readonly HashSet<string> ServiceWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "stop", "restart", "manual", "automatic", "disabled", "restore",
    };

    private static async Task RunServices(ServicesRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not "status" && !ServiceWrites.Contains(action))
        {
            Fail($"Unknown services action '{request.Action}'. Use status, start, stop, restart, " +
                 "manual, automatic, disabled or restore.");
            return;
        }

        if (ServiceWrites.Contains(action) && string.IsNullOrWhiteSpace(request.Name))
        {
            Fail($"{action} requires --name. `services status` lists them.");
            return;
        }

        var manager = new ServiceManager();

        if (ServiceWrites.Contains(action) && !request.Confirm)
        {
            var status = await manager.ReadAsync();
            var service = status.Services.FirstOrDefault(s => s.Name.Equals(request.Name!, StringComparison.OrdinalIgnoreCase));
            var preview = service is null
                ? new ServiceChange { Action = action, Success = false, Message = $"No service called '{request.Name}'." }
                : action == "restore"
                    ? await manager.RestoreAsync(service.Name, confirm: false)
                    : await manager.PerformAsync(service.Name, action, confirm: false);

            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintServiceChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        if (action == "status")
        {
            var status = await manager.ReadAsync();
            var services = status.Services;
            if (!string.IsNullOrWhiteSpace(request.Filter))
                services = services
                    .Where(s => s.Name.Contains(request.Filter!, StringComparison.OrdinalIgnoreCase)
                                || s.DisplayName.Contains(request.Filter!, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (request.Json) { RenderJson(status with { Services = services }); return; }
            PrintServiceStatus(status with { Services = services });
            return;
        }

        var change = action == "restore"
            ? await manager.RestoreAsync(request.Name!, request.Confirm)
            : await manager.PerformAsync(request.Name!, action, request.Confirm);

        if (request.Json) { RenderJson(change); return; }
        PrintServiceChange(change);
        PrintPreview(change.Preview);
        if (!change.Success) Environment.ExitCode = 1;
    }

    private static void PrintServiceStatus(ServiceStatus status)
    {
        if (status.Services.Count == 0)
        {
            Console.WriteLine("\n  No services matched.");
            if (status.Error is not null) Console.WriteLine($"  {status.Error}");
            return;
        }

        Console.WriteLine($"\n  {status.Services.Count} services — {status.Running} running, " +
                          $"{status.Stopped} stopped, {status.ProtectedCount} Novimize will not touch");
        if (status.Error is not null) Console.WriteLine($"  {status.Error}");

        foreach (var group in status.Services.GroupBy(s => s.StartMode))
        {
            Console.WriteLine($"\n  {group.Key}");
            foreach (var service in group)
            {
                var state = service.Status == "Running" ? "on " : "off";
                Console.WriteLine($"    [{state}] {service.Name,-32} {service.DisplayName}");
                if (service.Protected)
                    Console.WriteLine($"             protected: {service.ProtectReason}");
                else if (service.DependentOn.Count > 0)
                    Console.WriteLine($"             required by: {string.Join(", ", service.DependentOn.Take(5))}");
                if (service.OriginalStartMode is not null)
                    Console.WriteLine($"             changed by Novimize from {service.OriginalStartMode}");
            }
        }

        Console.WriteLine("\n  Protected services are refused with the reason, not silently skipped.");
    }

    private static void PrintServiceChange(ServiceChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
    }

    // === Scheduled tasks ===

    private sealed class TasksRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Id { get; init; }
        public string? Filter { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static readonly HashSet<string> TaskWrites = new(StringComparer.OrdinalIgnoreCase)
    {
        "enable", "disable", "restore",
    };

    private static async Task RunTasks(TasksRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "run") && !TaskWrites.Contains(action))
        {
            Fail($"Unknown tasks action '{request.Action}'. Use status, enable, disable, run or restore.");
            return;
        }

        if (action is not "status" && string.IsNullOrWhiteSpace(request.Id))
        {
            Fail($"{action} requires --id. `tasks status` lists them.");
            return;
        }

        var manager = new TaskManager();

        if ((TaskWrites.Contains(action) || action == "run") && !request.Confirm)
        {
            var preview = await manager.PerformAsync(request.Id!, action, confirm: false);
            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintTaskChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        if (action == "status")
        {
            var status = await manager.ReadAsync();
            var tasks = status.Tasks;
            if (!string.IsNullOrWhiteSpace(request.Filter))
                tasks = tasks
                    .Where(t => t.Name.Contains(request.Filter!, StringComparison.OrdinalIgnoreCase)
                                || t.Path.Contains(request.Filter!, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (request.Json) { RenderJson(status with { Tasks = tasks }); return; }
            PrintTaskStatus(status with { Tasks = tasks });
            return;
        }

        var change = action == "restore"
            ? await manager.RestoreAsync(request.Id!, request.Confirm)
            : await manager.PerformAsync(request.Id!, action, request.Confirm);

        if (request.Json) { RenderJson(change); return; }
        PrintTaskChange(change);
        PrintPreview(change.Preview);
        if (!change.Success) Environment.ExitCode = 1;
    }

    private static void PrintTaskStatus(ScheduledTaskStatus status)
    {
        if (status.Tasks.Count == 0)
        {
            Console.WriteLine("\n  No tasks matched.");
            if (status.Error is not null) Console.WriteLine($"  {status.Error}");
            return;
        }

        Console.WriteLine($"\n  {status.Tasks.Count} tasks — {status.Enabled} enabled, " +
                          $"{status.Disabled} disabled, {status.ChangedByNovimize} changed by Novimize");
        if (status.Error is not null) Console.WriteLine($"  {status.Error}");

        foreach (var task in status.Tasks)
        {
            var state = task.Enabled ? "on " : "off";
            Console.WriteLine($"\n    [{state}] {task.Name}");
            Console.WriteLine($"             {task.Path}");
            Console.WriteLine($"             {task.Trigger}");
            Console.WriteLine($"             runs {task.Command} {task.Arguments}".TrimEnd());
            Console.WriteLine($"             last {task.LastRun?.ToString("yyyy-MM-dd HH:mm") ?? "never"}"
                              + $" · next {task.NextRun?.ToString("yyyy-MM-dd HH:mm") ?? "-"}"
                              + $" · result 0x{task.LastResult:X8}");
            if (task.SystemTask) Console.WriteLine("             a Microsoft task");
            if (task.ChangedByNovimize)
                Console.WriteLine($"             changed by Novimize; was {(task.OriginalEnabled == true ? "enabled" : "disabled")}");
            if (task.Description.Length > 0)
                Console.WriteLine($"             {task.Description}");
        }

        Console.WriteLine("\n  Nothing here is deleted. Disabling leaves the task registered.");
    }

    private static void PrintTaskChange(ScheduledTaskChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
    }

    // === Debloat Center ===

    private sealed class DebloatRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Name { get; init; }
        public bool AllUsers { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunDebloat(DebloatRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "remove" or "restore"))
        {
            Fail($"Unknown debloat action '{request.Action}'. Use status, remove or restore.");
            return;
        }

        if (action is not "status" && string.IsNullOrWhiteSpace(request.Name))
        {
            Fail($"{action} requires --name. `debloat status` lists them.");
            return;
        }

        var manager = new DebloatManager();

        if (action is not "status" && !request.Confirm)
        {
            // The preview comes from the same path the write takes, so a
            // refusal here — framework, non-removable, something waiting on
            // it, or the policy's own protection — is the refusal the write
            // would have given.
            var preview = action == "remove"
                ? await manager.RemoveAsync(request.Name!, request.AllUsers, confirm: false)
                : await manager.RestoreAsync(request.Name!, request.AllUsers, confirm: false);

            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintDebloatChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        if (action == "status")
        {
            var status = await manager.ReadAsync(request.AllUsers);
            if (request.Json) { RenderJson(status); return; }
            PrintDebloatStatus(status);
            return;
        }

        var change = action == "remove"
            ? await manager.RemoveAsync(request.Name!, request.AllUsers, request.Confirm)
            : await manager.RestoreAsync(request.Name!, request.AllUsers, request.Confirm);

        if (request.Json) { RenderJson(change); return; }
        PrintDebloatChange(change);
        PrintPreview(change.Preview);
        if (!change.Success) Environment.ExitCode = 1;
    }

    private static void PrintDebloatStatus(DebloatStatus status)
    {
        if (status.Packages.Count == 0)
        {
            Console.WriteLine("\n  No Store packages could be read.");
            if (status.Error is not null) Console.WriteLine($"  {status.Error}");
            return;
        }

        Console.WriteLine($"\n  {status.Packages.Count} packages for {status.Scope} — " +
                          $"{status.Removable} removable, {status.ProtectedCount} refused, " +
                          $"{status.Frameworks} frameworks");
        Console.WriteLine($"  Policy {status.PolicyEntries} entries at {status.PolicyPath}");
        if (status.Error is not null) Console.WriteLine($"  {status.Error}");

        foreach (var group in status.Packages.GroupBy(p => p.Verdict))
        {
            Console.WriteLine($"\n  {group.Key}");
            foreach (var package in group)
            {
                Console.WriteLine($"    {package.Name,-46} {package.Version}");
                if (package.RefusalReason is not null)
                    Console.WriteLine($"      refused: {package.RefusalReason}");
                else if (package.Reason.Length > 0)
                    Console.WriteLine($"      {package.Reason}");
                if (package.DependedOnBy.Count > 0)
                    Console.WriteLine($"      required by: {string.Join(", ", package.DependedOnBy.Take(4))}");
                Console.WriteLine($"      {(package.Provisioned ? "a provisioned copy exists, so it can be registered again" : "no provisioned copy — it would come back from the Store")}");
            }
        }

        Console.WriteLine("\n  Frameworks, Windows' own non-removable flag, and anything another package " +
                          "is waiting on are refused by the engine. The policy file is an opinion and " +
                          "cannot override them.");
    }

    private static void PrintDebloatChange(DebloatChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
        if (change.RestartRequired) Console.WriteLine("    A restart finishes this.");
    }

    // === Maintenance Center ===

    private sealed class MaintenanceRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Id { get; init; }
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunMaintenance(MaintenanceRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "run"))
        {
            Fail($"Unknown maint action '{request.Action}'. Use status or run.");
            return;
        }

        if (action == "run" && string.IsNullOrWhiteSpace(request.Id))
        {
            Fail("run requires --id. `maint status` lists them.");
            return;
        }

        var manager = new MaintenanceManager();

        if (action == "run" && !request.Confirm)
        {
            var preview = manager.Preview(request.Id!);
            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintMaintenanceChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        if (action == "status")
        {
            var status = await manager.ReadAsync();
            if (request.Json) { RenderJson(status); return; }
            PrintMaintenanceStatus(status);
            return;
        }

        var change = await manager.RunAsync(request.Id!, request.Confirm);
        if (request.Json) { RenderJson(change); return; }
        PrintMaintenanceChange(change);
        PrintPreview(change.Preview);
        if (!change.Success) Environment.ExitCode = 1;
    }

    private static void PrintMaintenanceStatus(MaintenanceStatus status)
    {
        if (status.ReclaimableBytes is long bytes)
            Console.WriteLine($"\n  {MaintenanceManager.Format(bytes)} could be freed by the clearable tools");
        Console.WriteLine($"  {status.RepairCount} of these rewrite system files; " +
                          $"{status.RestartCount} want a restart afterwards");
        if (status.Error is not null) Console.WriteLine($"  {status.Error}");

        foreach (var tool in status.Tools)
        {
            Console.WriteLine($"\n    {tool.Name}{(tool.Available ? "" : "  — see below")}");
            Console.WriteLine($"      {tool.What}");
            if (tool.Deletes is not null)
                Console.WriteLine($"      deletes  {tool.Deletes}");
            if (tool.Bytes is long size)
                Console.WriteLine($"      right now  {MaintenanceManager.Format(size)}");
            else if (tool.MeasuredNote is not null)
                Console.WriteLine($"      {tool.MeasuredNote}");
            Console.WriteLine($"      takes    {tool.Effort}");
            if (tool.Repairs) Console.WriteLine("      rewrites system files");
            if (tool.RestartRequired) Console.WriteLine("      a restart finishes this");
            if (tool.UnavailableReason is not null) Console.WriteLine($"      {tool.UnavailableReason}");
        }

        Console.WriteLine("\n  Every action shows its commands before it runs any of them.");
    }

    private static void PrintMaintenanceChange(MaintenanceChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
        if (change.RestartRequired) Console.WriteLine("    A restart finishes this.");
    }

    // === Windows Update ===

    private sealed class UpdateRequest
    {
        public string Action { get; init; } = string.Empty;
        public bool Confirm { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunWindowsUpdate(UpdateRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "scan" or "open" or "restart"))
        {
            Fail($"Unknown update action '{request.Action}'. Use status, scan, open or restart.");
            return;
        }

        var manager = new UpdateManager();

        if (action == "restart" && !request.Confirm)
        {
            // Success false on purpose: this is a refusal carrying the
            // preview, not an approval carrying a preview.
            var preview = await manager.RestartAsync(confirm: false);
            if (request.Json)
            {
                RenderJson(preview);
                Environment.ExitCode = 1;
                return;
            }
            PrintUpdateChange(preview);
            PrintPreview(preview.Preview);
            Environment.ExitCode = 1;
            return;
        }

        switch (action)
        {
            case "status":
            {
                var status = await manager.StatusAsync();
                if (request.Json) { RenderJson(status); return; }
                PrintUpdateStatus(status);
                return;
            }

            case "scan":
            {
                // A network round trip to Microsoft: worth saying so before it
                // starts rather than leaving the page looking hung.
                if (!request.Json) Console.WriteLine("\n  Asking Windows Update what is waiting…");
                var status = await manager.ScanAsync();
                if (request.Json) { RenderJson(status); return; }
                PrintUpdateStatus(status);
                if (status.Available.Count > 0)
                {
                    Console.WriteLine($"\n  {status.Available.Count} update(s) waiting");
                    foreach (var update in status.Available.Take(20))
                        Console.WriteLine($"    {update.Title}");
                }
                return;
            }

            case "open":
            {
                var change = UpdateManager.Open();
                await ProcessRunner.RunAsync("explorer.exe",
                    new[] { "ms-settings:windowsupdate" }, TimeSpan.FromSeconds(20));
                if (request.Json) { RenderJson(change); return; }
                PrintUpdateChange(change);
                return;
            }

            default:
            {
                var change = await manager.RestartAsync(request.Confirm);
                if (request.Json) { RenderJson(change); return; }
                PrintUpdateChange(change);
                PrintPreview(change.Preview);
                if (!change.Success) Environment.ExitCode = 1;
                return;
            }
        }
    }

    private static void PrintUpdateStatus(WindowsUpdateStatus status)
    {
        Console.WriteLine($"\n  State              {status.State}");
        Console.WriteLine($"  Update service     {(status.UpdateServiceRunning ? "running" : "stopped")}");
        Console.WriteLine($"  Last scan          {status.LastSearchSuccess?.ToString("yyyy-MM-dd HH:mm") ?? "never / not recorded"}");
        Console.WriteLine($"  Last installed     {status.LastInstallSuccess?.ToString("yyyy-MM-dd HH:mm") ?? "never / not recorded"}");
        Console.WriteLine($"  Last boot          {status.LastBoot?.ToString("yyyy-MM-dd HH:mm") ?? "unknown"}");

        if (status.PendingReboot)
        {
            Console.WriteLine("\n  A restart is owed:");
            foreach (var reason in status.PendingRebootReasons)
                Console.WriteLine($"    · {reason}");
        }
        else
        {
            Console.WriteLine("\n  No restart is owed.");
        }

        if (status.History.Count > 0)
        {
            Console.WriteLine("\n  Recent history");
            foreach (var entry in status.History.Take(12))
            {
                var when = entry.When?.ToString("yyyy-MM-dd") ?? "------------";
                var mark = entry.Succeeded ? "  " : "✗ ";
                Console.WriteLine($"    {when} {mark}{entry.Title}");
            }
        }

        if (status.Error is not null) Console.WriteLine($"\n  {status.Error}");
        Console.WriteLine("\n  Novimize does not change update policy. Nothing here disables updating.");
    }

    private static void PrintUpdateChange(WindowsUpdateChange change)
    {
        var mark = change.Success ? "✓" : "✗";
        Console.WriteLine($"\n  {mark} {change.Message}");
        if (change.Unchanged) Console.WriteLine("    Nothing changed.");
        if (change.NeedsElevation) Console.WriteLine("    Needs administrator rights.");
        if (change.RestartRequired) Console.WriteLine("    A restart is involved.");
    }

    // === Health Dashboard ===

    private sealed class HealthRequest
    {
        public string Action { get; init; } = string.Empty;
        public string Format { get; init; } = "txt";
        public string? Output { get; init; }
        public bool Json { get; init; }
    }

    private static async Task RunHealth(HealthRequest request)
    {
        var action = request.Action.ToLowerInvariant();
        if (action is not ("status" or "report"))
        {
            Fail($"Unknown health action '{request.Action}'. Use status or report.");
            return;
        }

        if (action == "report"
            && !HealthReportWriter.Formats.Contains(request.Format, StringComparer.OrdinalIgnoreCase))
        {
            Fail($"Unknown format '{request.Format}'. Use json, txt or html.");
            return;
        }

        var engine = new DiagnosticsEngine(new SystemDetector(), new WinOptLogger());
        var report = await engine.HealthCheckAsync();

        if (action == "status")
        {
            if (request.Json)
            {
                Console.WriteLine(HealthReportWriter.ToJson(report));
                return;
            }
            Console.Write(HealthReportWriter.ToText(report));
            return;
        }

        try
        {
            var path = await HealthReportWriter.WriteAsync(report, request.Format, request.Output);
            var result = new
            {
                action = "report",
                success = true,
                format = request.Format.ToLowerInvariant(),
                path,
                overall = report.OverallStatus.ToString(),
                checks = report.Checks.Count,
                warnings = report.Warnings,
                bytes = new FileInfo(path).Length,
            };
            if (request.Json) { RenderJson(result); return; }

            Console.WriteLine($"\n  ✓ Wrote {request.Format.ToLowerInvariant()} report to {path}");
            Console.WriteLine($"    {result.bytes} bytes, {result.checks} checks, " +
                              $"{report.Warnings.Count} warning(s)");
            if (report.Warnings.Count > 0)
            {
                Console.WriteLine("\n  Warnings");
                foreach (var warning in report.Warnings) Console.WriteLine($"    · {warning}");
            }
        }
        catch (Exception ex)
        {
            Fail($"The report could not be written: {ex.Message}");
        }
    }

    private sealed class AppsRequest
    {
        public string Action { get; init; } = string.Empty;
        public string? Id { get; init; }
        public string? Query { get; init; }
        public string Scope { get; init; } = "any";
        public bool Elevated { get; init; }
        public bool Deep { get; init; }
        public bool Json { get; init; }
    }

    private static AppCatalog LoadCatalog()
    {
        var catalog = new AppCatalog(AppCatalog.ResolveDirectory());
        catalog.Load();
        return catalog;
    }

    private static async Task RunApps(AppsRequest request)
    {
        switch (request.Action.ToLowerInvariant())
        {
            case "probe":
            {
                var info = await WinGet.ProbeAsync();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(info, JsonOpts));
                    if (!info.Available) Environment.ExitCode = 1;
                    return;
                }
                if (!info.Available) { Fail(info.Error ?? "winget is not available."); return; }
                Console.WriteLine($"\n  winget {info.Version}");
                foreach (var source in info.Sources)
                    Console.WriteLine($"    {source.Name,-12} {source.Argument}{(source.Explicit ? "  (explicit)" : "")}");
                return;
            }

            case "catalog":
            {
                var catalog = LoadCatalog();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        directory = catalog.Directory,
                        categories = catalog.Categories,
                        apps = catalog.Entries,
                        absent = catalog.Absent,
                    }, JsonOpts));
                    if (catalog.Entries.Count == 0) Environment.ExitCode = 1;
                    return;
                }
                if (catalog.Entries.Count == 0)
                {
                    Fail($"No app catalogue found at {catalog.Directory}.");
                    return;
                }
                Console.WriteLine($"\n  {catalog.Entries.Count} apps in {catalog.Categories.Count} categories ({catalog.Directory})");
                foreach (var category in catalog.Categories)
                {
                    var apps = catalog.Entries.Where(e => e.Category == category.Id).ToList();
                    Console.WriteLine($"\n  {category.Label} ({apps.Count})");
                    foreach (var app in apps) Console.WriteLine($"    {app.Id,-44} {app.Name}");
                }
                if (catalog.Absent.Count > 0)
                {
                    Console.WriteLine("\n  Deliberately not offered:");
                    foreach (var absence in catalog.Absent) Console.WriteLine($"    {absence.Name}: {absence.Reason}");
                }
                return;
            }

            case "status":
            {
                var catalog = LoadCatalog();
                if (catalog.Entries.Count == 0)
                {
                    if (request.Json)
                        Console.WriteLine(JsonSerializer.Serialize(new { available = false, error = $"No app catalogue at {catalog.Directory}.", deep = request.Deep, apps = Array.Empty<AppStatus>() }, JsonOpts));
                    else
                        Fail($"No app catalogue found at {catalog.Directory}.");
                    Environment.ExitCode = 1;
                    return;
                }

                // Probed before the list, because winget being missing would
                // otherwise read as "nothing is installed" — a wrong answer
                // rather than a smaller true one.
                var info = await WinGet.ProbeAsync();
                var installed = info.Available
                    ? await WinGet.ListInstalledAsync(request.Deep)
                    : new List<InstalledPackage>();
                // The source-restricted pass has no Source column (winget drops
                // it when only one source can answer), so "winget" is filled in
                // rather than reported as unknown.
                var statuses = MergeStatus(catalog, installed, request.Deep ? null : "winget");

                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        available = info.Available,
                        error = info.Error,
                        version = info.Version,
                        deep = request.Deep,
                        apps = statuses,
                    }, JsonOpts));
                    if (!info.Available) Environment.ExitCode = 1;
                    return;
                }

                if (!info.Available) { Fail(info.Error ?? "winget is not available."); return; }
                var installedCount = statuses.Count(s => s.Installed);
                Console.WriteLine($"\n  {installedCount} of {statuses.Count} catalogue apps installed{(!request.Deep ? " (fast pass — run with --deep to also see apps installed outside winget)" : "")}");
                foreach (var status in statuses)
                {
                    var state = !status.Installed ? "not installed"
                        : status.UpdateAvailable ? $"{status.InstalledVersion} → {status.AvailableVersion}"
                        : status.InstalledVersion ?? "installed";
                    Console.WriteLine($"    {(status.Installed ? "[x]" : "[ ]")} {status.Id,-44} {state}");
                }
                return;
            }

            case "show":
            {
                var id = RequireId(request, "show");
                if (id is null) return;

                var catalog = LoadCatalog();
                var entry = catalog.Find(id);
                var detail = await WinGet.ShowAsync(id);
                var payload = new
                {
                    id = detail.Id,
                    name = detail.Name,
                    version = detail.Version,
                    publisher = detail.Publisher,
                    publisherUrl = detail.PublisherUrl,
                    homepage = detail.Homepage,
                    license = detail.License,
                    description = detail.Description,
                    installerType = detail.InstallerType,
                    installerUrl = detail.InstallerUrl,
                    installerSha256 = detail.InstallerSha256,
                    releaseDate = detail.ReleaseDate,
                    source = detail.Source,
                    error = detail.Error,
                    inCatalogue = entry is not null,
                    category = entry?.Category,
                    catalogueName = entry?.Name,
                    catalogueHomepage = entry?.Homepage,
                    catalogueGithub = entry?.Github,
                    subcategory = entry?.Subcategory,
                    catalogueLicense = entry?.License,
                    cost = entry?.Cost,
                    windows = entry?.Windows,
                    winget = entry?.Winget ?? true,
                };
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
                    if (detail.Error is not null) Environment.ExitCode = 1;
                    return;
                }
                if (detail.Error is not null) { Fail(detail.Error); return; }
                Console.WriteLine($"\n  {detail.Name} [{id}]  {detail.Version}");
                Console.WriteLine($"  Publisher:   {detail.Publisher ?? "(not stated)"}");
                Console.WriteLine($"  Homepage:    {detail.Homepage ?? "(not stated)"}");
                Console.WriteLine($"  License:     {detail.License ?? "(not stated)"}");
                Console.WriteLine($"  Source:      {detail.Source ?? "winget"}");
                if (detail.InstallerType is not null || detail.InstallerUrl is not null)
                {
                    Console.WriteLine($"  Installer:   {detail.InstallerType ?? "?"}");
                    if (detail.InstallerUrl is not null) Console.WriteLine($"               {detail.InstallerUrl}");
                    if (detail.InstallerSha256 is not null) Console.WriteLine($"               sha256 {detail.InstallerSha256}");
                }
                if (detail.Description is not null) Console.WriteLine($"\n  {detail.Description}");
                if (entry is null) Console.WriteLine("\n  Not in the Novimize catalogue — offered because you searched for it.");
                return;
            }

            case "installed":
            {
                var id = RequireId(request, "installed");
                if (id is null) return;
                if (!WinGet.IsSafeId(id))
                {
                    if (request.Json)
                        Console.WriteLine(JsonSerializer.Serialize(new { error = $"'{id}' is not a package ID this application will run winget against." }, JsonOpts));
                    else Fail($"'{id}' is not a package ID this application will run winget against.");
                    Environment.ExitCode = 1;
                    return;
                }

                var rows = await WinGet.ListOneAsync(id);
                var here = WingetTable.FindById(rows, id);
                var payload = new
                {
                    id,
                    installed = here is not null,
                    installedVersion = here?.Version,
                    availableVersion = here?.Available,
                    source = here?.Source ?? (here is not null ? "winget" : null),
                    authoritative = true,
                };
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
                    return;
                }
                var installedLine = here is null
                    ? $"{id}: not installed."
                    : $"{id}: {here.Version}{(here.Available is null ? "" : $" → {here.Available}")}";
                Console.WriteLine();
                Console.WriteLine($"  {installedLine}");
                return;
            }

            case "search":
            {
                var query = request.Query;
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (request.Json) Console.WriteLine(JsonSerializer.Serialize(new { error = "search requires --query" }, JsonOpts));
                    else Fail("search requires --query");
                    Environment.ExitCode = 1;
                    return;
                }

                var results = await WinGet.SearchAsync(query);
                // One more call, so a hit can say "already installed" instead of
                // offering to install something that is already there.
                var installed = await WinGet.ListInstalledAsync();
                var byId = ToIdMap(installed);
                var catalog = LoadCatalog();

                var rows = results.Select(p =>
                {
                    byId.TryGetValue(p.Id, out var here);
                    var entry = catalog.Find(p.Id);
                    return new
                    {
                        id = p.Id,
                        name = p.Name,
                        version = p.Version,
                        // `--source winget` drops the Source column, so the
                        // restriction that produced the row is reported instead.
                        source = p.Source ?? "winget",
                        installed = here is not null,
                        installedVersion = here?.Version,
                        inCatalogue = entry is not null,
                        category = entry?.Category,
                        // Winget lists ARP entries whose "ID" is a registry path;
                        // those are not installable and must not be offered as such.
                        installable = WinGet.IsSafeId(p.Id),
                    };
                }).ToList();

                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { query, results = rows }, JsonOpts));
                    return;
                }
                Console.WriteLine($"\n  {rows.Count} results for \"{query}\"");
                foreach (var row in rows)
                    Console.WriteLine($"    {(row.installed ? "[x]" : "[ ]")} {row.id,-44} {row.name}  {row.version}");
                return;
            }

            case "launch":
            {
                var id = RequireId(request, "launch");
                if (id is null) return;

                var catalog = LoadCatalog();
                var entry = catalog.Find(id);
                if (entry is null)
                {
                    if (request.Json)
                        Console.WriteLine(JsonSerializer.Serialize(new LaunchResult(false, $"'{id}' is not in the catalogue, so there is no Start menu name to look for.", null, null), JsonOpts));
                    else
                        Fail($"'{id}' is not in the catalogue, so there is no Start menu name to look for.");
                    Environment.ExitCode = 1;
                    return;
                }

                // The card name and the Start menu name are two different things;
                // both are offered, and whichever the shell answers with is the
                // one that is opened and reported back.
                var result = await StartMenu.LaunchAsync(entry.StartName ?? entry.Name, entry.Name);
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { id, name = entry.Name, result.Success, result.Message, result.MatchedName, result.MatchedAppId }, JsonOpts));
                    if (!result.Success) Environment.ExitCode = 1;
                    return;
                }
                Console.WriteLine();
                Console.WriteLine($"  {result.Message}");
                return;
            }

            case "install":
            case "uninstall":
            case "upgrade":
            {
                var id = RequireId(request, request.Action);
                if (id is null) return;

                // An entry that states it has no winget package is offered only
                // through its official page. Trying anyway would run winget with
                // an ID that is really a slug, and turn "not in winget" into a
                // confusing command failure.
                var known = LoadCatalog().Find(id);
                if (known is { Winget: false })
                {
                    var message = $"'{known.Name}' has no winget package; the official page is the way to get it ({known.Homepage ?? "no page on record"}).";
                    if (request.Json)
                        Console.WriteLine(JsonSerializer.Serialize(new AppChange { Action = request.Action, Id = id, Success = false, Message = message }, JsonOpts));
                    else Fail(message);
                    Environment.ExitCode = 1;
                    return;
                }

                if (!WinGet.IsSafeId(id))
                {
                    if (request.Json)
                        Console.WriteLine(JsonSerializer.Serialize(new AppChange { Action = request.Action, Id = id, Success = false, Message = $"'{id}' is not a package ID this application will run winget against." }, JsonOpts));
                    else Fail($"'{id}' is not a package ID this application will run winget against.");
                    Environment.ExitCode = 1;
                    return;
                }

                var scope = request.Action == "install" ? ParseScope(request.Scope, request.Json) : InstallScope.Any;
                if (scope == (InstallScope)(-1)) { Environment.ExitCode = 1; return; }

                var change = request.Action switch
                {
                    "install" => await WinGet.InstallAsync(id, scope, request.Elevated),
                    "uninstall" => await WinGet.UninstallAsync(id, request.Elevated),
                    _ => await WinGet.UpgradeAsync(id, request.Elevated),
                };

                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(change, JsonOpts));
                    if (!change.Success && !change.Unchanged) Environment.ExitCode = 1;
                    return;
                }
                if (!change.Success && !change.Unchanged) { Fail(change.Message); Console.WriteLine(change.Log); return; }
                Console.WriteLine($"\n  {change.Message}");
                if (change.Log.Length > 0 && request.Action != "uninstall") Console.WriteLine(change.Log);
                return;
            }

            case "upgrade-all":
            {
                var change = await WinGet.UpgradeAllAsync();
                if (request.Json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(change, JsonOpts));
                    if (!change.Success && !change.Unchanged) Environment.ExitCode = 1;
                    return;
                }
                if (!change.Success && !change.Unchanged) { Fail(change.Message); Console.WriteLine(change.Log); return; }
                Console.WriteLine($"\n  {change.Message}");
                return;
            }

            default:
                if (request.Json)
                    Console.WriteLine(JsonSerializer.Serialize(new { error = $"Unknown action '{request.Action}'." }, JsonOpts));
                else
                    Fail($"Unknown action '{request.Action}'. Use probe, catalog, status, installed, show, search, install, uninstall, upgrade, upgrade-all or launch.");
                Environment.ExitCode = 1;
                return;
        }
    }

    private static Dictionary<string, InstalledPackage> ToIdMap(List<InstalledPackage> packages)
    {
        var map = new Dictionary<string, InstalledPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages) map[package.Id] = package;
        return map;
    }

    private static List<AppStatus> MergeStatus(AppCatalog catalog, List<InstalledPackage> installed, string? sourceFallback = null)
    {
        var byId = ToIdMap(installed);
        return catalog.Entries.Select(entry =>
        {
            byId.TryGetValue(entry.Id, out var here);
            return new AppStatus
            {
                Id = entry.Id,
                Name = entry.Name,
                Publisher = entry.Publisher,
                Description = entry.Description,
                Category = entry.Category,
                Homepage = entry.Homepage,
                Tags = entry.Tags,
                Github = entry.Github,
                Subcategory = entry.Subcategory,
                License = entry.License,
                Cost = entry.Cost,
                Windows = entry.Windows,
                Winget = entry.Winget,
                Installed = here is not null,
                InstalledVersion = here?.Version,
                AvailableVersion = here?.Available,
                Source = here?.Source ?? (here is not null ? sourceFallback : null),
            };
        }).ToList();
    }

    private static string? RequireId(AppsRequest request, string action)
    {
        if (!string.IsNullOrWhiteSpace(request.Id)) return request.Id;
        // One or the other: in --json mode a plain-text line on stdout would
        // corrupt the document the caller is about to read.
        if (request.Json)
            Console.WriteLine(JsonSerializer.Serialize(new { error = $"{action} requires --id" }, JsonOpts));
        else
            Fail($"{action} requires --id");
        Environment.ExitCode = 1;
        return null;
    }

    private static InstallScope ParseScope(string scope, bool json)
    {
        switch (scope.Trim().ToLowerInvariant())
        {
            case "any": case "": case "auto": return InstallScope.Any;
            case "user": return InstallScope.User;
            case "machine": case "everyone": return InstallScope.Machine;
            default:
                if (json) Console.WriteLine(JsonSerializer.Serialize(new { error = $"Unknown scope '{scope}'. Use any, user or machine." }, JsonOpts));
                else Fail($"Unknown scope '{scope}'. Use any, user or machine.");
                return (InstallScope)(-1);
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
