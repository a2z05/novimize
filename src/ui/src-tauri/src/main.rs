#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::process::Command;
use tauri::command;

/// Resolve the path to the winopt-cli sidecar executable.
fn cli_path() -> String {
    #[cfg(debug_assertions)]
    {
        // The build `dotnet build` just produced comes first. The RID-specific
        // folders are what a `dotnet publish -r win-x64` leaves behind, and
        // they keep answering for days after Program.cs has changed — a UI
        // running against one silently talks to an older CLI that does not
        // know the commands these pages call.
        let manifest_dir = env!("CARGO_MANIFEST_DIR");
        let base = std::path::Path::new(manifest_dir).join("../../../src/cli/bin/Debug/net8.0");
        for relative in [
            "WinOpt.Cli.exe",
            "win-x64/WinOpt.Cli.exe",
            "win-x64/publish/WinOpt.Cli.exe",
        ] {
            let candidate = base.join(relative);
            if candidate.exists() {
                return candidate.to_string_lossy().to_string();
            }
        }
        base.join("WinOpt.Cli.exe").to_string_lossy().to_string()
    }
    #[cfg(not(debug_assertions))]
    {
        "winopt-cli.exe".to_string()
    }
}

/// Create a Command that hides the console window on Windows.
fn silent_command(path: &str) -> Command {
    let mut cmd = Command::new(path);
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd
}

/// Execute the CLI with --json flag for commands that support it.
/// Strips log lines that appear before the JSON.
///
/// A non-zero exit is not treated as an error on its own: `apply` deliberately
/// exits 1 when some tweaks fail, and its stdout still carries the per-tweak
/// results the UI needs to render. Folding that into an error message threw the
/// payload away and showed only "CLI exited with code 1". Failure is decided by
/// whether a JSON body came back at all.
fn run_cli_json(args: &[String]) -> Result<String, String> {
    let mut full_args = args.to_vec();
    full_args.push("--json".into());
    let path = cli_path();
    let output = silent_command(&path)
        .args(&full_args)
        .output()
        .map_err(|e| format!("Failed to run CLI at {}: {}", path, e))?;

    let stdout = String::from_utf8_lossy(&output.stdout);
    if let Some(json) = extract_json(&stdout) {
        return Ok(json.to_string());
    }

    let stderr = String::from_utf8_lossy(&output.stderr);
    Err(format!(
        "CLI exited with code {:?}: {}",
        output.status.code(),
        if stderr.trim().is_empty() { stdout.trim().to_string() } else { stderr.trim().to_string() }
    ))
}

/// Locate the JSON document inside CLI stdout, skipping any leading log lines.
/// Logger lines begin with `[2026-01-01 …]`, so a bracket only starts the
/// document when it opens at the beginning of a line.
fn extract_json(raw: &str) -> Option<&str> {
    let trimmed = raw.trim_start();
    let mut first_brace: Option<usize> = None;
    for (i, c) in trimmed.char_indices() {
        if c != '{' && c != '[' {
            continue;
        }
        if i == 0 || trimmed[..i].ends_with('\n') {
            return Some(&trimmed[i..]);
        }
        if first_brace.is_none() {
            first_brace = Some(i);
        }
    }
    first_brace.map(|i| &trimmed[i..])
}

fn push_opt(args: &mut Vec<String>, flag: &str, val: &Option<String>) {
    if let Some(v) = val {
        args.push(flag.to_string());
        args.push(v.clone());
    }
}

// ============ Tauri Commands ============

#[command]
async fn get_system_info() -> Result<String, String> {
    run_cli_json(&["system".into()])
}

#[command]
async fn scan_tweaks(profile: Option<String>) -> Result<String, String> {
    let mut args = vec!["scan".to_string()];
    push_opt(&mut args, "--profile", &profile);
    run_cli_json(&args)
}

#[command]
async fn list_tweaks(
    category: Option<String>,
    risk: Option<String>,
    profile: Option<String>,
    include: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["list".to_string()];
    push_opt(&mut args, "--category", &category);
    push_opt(&mut args, "--risk", &risk);
    push_opt(&mut args, "--profile", &profile);
    // `list --profile` answers with the profile's own selection and knows
    // nothing about what the user ticked above it. With --include it answers
    // with what would actually run, which is the list the confirm modal and
    // the apply both need.
    push_opt(&mut args, "--include", &include);
    run_cli_json(&args)
}

#[command]
async fn apply_tweak(tweak_id: String, dry_run: Option<bool>) -> Result<String, String> {
    let ids: Vec<&str> = tweak_id.split(',').map(str::trim).filter(|s| !s.is_empty()).collect();
    if ids.is_empty() {
        return Err("No tweak IDs given.".into());
    }

    // One CLI call for the whole list, so the batch planner sees every member
    // at once: it decides the order, holds back anything that contradicts
    // another member, and covers the run with a single snapshot. Spawning one
    // process per tweak meant N snapshots, no cross-tweak conflict detection,
    // and a tally re-implemented here that could drift from the CLI's own.
    let mut args = vec!["apply".to_string(), ids.join(",")];
    if dry_run == Some(true) {
        args.push("--dry-run".into());
    }
    run_cli_json(&args)
}

#[command]
async fn plan_tweak(
    tweak_id: Option<String>,
    profile: Option<String>,
    category: Option<String>,
    include: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["plan".to_string()];
    if let Some(id) = tweak_id.filter(|s| !s.is_empty()) {
        args.push(id);
    }
    push_opt(&mut args, "--profile", &profile);
    push_opt(&mut args, "--category", &category);
    push_opt(&mut args, "--include", &include);
    run_cli_json(&args)
}

#[command]
async fn get_journal(
    limit: Option<u32>,
    tweak_id: Option<String>,
    operation: Option<String>,
    result: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["journal".to_string()];
    args.push("--limit".into());
    args.push(limit.unwrap_or(50).to_string());
    push_opt(&mut args, "--tweak", &tweak_id);
    push_opt(&mut args, "--operation", &operation);
    push_opt(&mut args, "--result", &result);
    run_cli_json(&args)
}

#[command]
async fn apply_profile(
    profile_id: String,
    dry_run: Option<bool>,
    include: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["apply".into(), "--profile".into(), profile_id];
    if dry_run == Some(true) {
        args.push("--dry-run".into());
    }
    push_opt(&mut args, "--include", &include);
    run_cli_json(&args)
}

#[command]
async fn rollback_tweak(tweak_id: String, snapshot_id: Option<String>) -> Result<String, String> {
    let mut args = vec!["rollback".into(), tweak_id];
    push_opt(&mut args, "--snapshot", &snapshot_id);
    run_cli_json(&args)
}

#[command]
async fn rollback_all(snapshot_id: String) -> Result<String, String> {
    run_cli_json(&["rollback".into(), "--snapshot".into(), snapshot_id, "--all".into()])
}

#[command]
async fn get_recommendations(profile: Option<String>, top: Option<u32>) -> Result<String, String> {
    let mut args = vec!["recommend".to_string()];
    push_opt(&mut args, "--profile", &profile);
    if let Some(n) = top {
        args.push("--top".to_string());
        args.push(n.to_string());
    }
    run_cli_json(&args)
}

#[command]
async fn list_profiles() -> Result<String, String> {
    run_cli_json(&["profile".into()])
}

/// What a profile applies here, what it merely reaches, and why everything
/// else is out — the split the profile page shows before you agree to it.
#[command]
async fn profile_selector(profile: String) -> Result<String, String> {
    run_cli_json(&["profile-selector".into(), profile, "--json".into()])
}

#[command]
async fn run_diagnostics(mode: Option<String>) -> Result<String, String> {
    let mut args = vec!["doctor".to_string()];
    if let Some(m) = mode {
        args.push(m);
    }
    run_cli_json(&args)
}

#[command]
async fn get_snapshots() -> Result<String, String> {
    run_cli_json(&["snapshots".into()])
}

// ============ Gaming Center ============

/// Launchers, their libraries, and the games those libraries publish.
#[command]
async fn gaming_detect() -> Result<String, String> {
    run_cli_json(&["game-mode".into(), "detect".into()])
}

/// `action` is `add` or `remove`; the CLI spells those `folder-add` and
/// `folder-remove`, and the translation lives here rather than in the UI so
/// there is one place that can drift.
#[command]
async fn gaming_folder(action: String, path: String) -> Result<String, String> {
    let verb = match action.as_str() {
        "add" => "folder-add",
        "remove" => "folder-remove",
        other => return Err(format!("Unknown folder action '{}'.", other)),
    };
    run_cli_json(&["game-mode".into(), verb.into(), "--path".into(), path])
}

/// Start a temporary session. The `no_*` switches mean "leave this alone", the
/// same words the CLI uses — a translation layer that renames them is a layer
/// where "suppress notifications" and "do not touch notifications" swap.
#[command]
async fn gaming_start(
    plan: Option<String>,
    no_notifications: Option<bool>,
    no_background_apps: Option<bool>,
    services: Option<String>,
    for_process: Option<String>,
    game: Option<String>,
    controls: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["game-mode".into(), "start".into()];
    push_opt(&mut args, "--plan", &plan);
    if no_notifications == Some(true) {
        args.push("--no-notifications".into());
    }
    if no_background_apps == Some(true) {
        args.push("--no-background-apps".into());
    }
    push_opt(&mut args, "--services", &services);
    push_opt(&mut args, "--for-process", &for_process);
    push_opt(&mut args, "--game", &game);
    push_opt(&mut args, "--controls", &controls);
    run_cli_json(&args)
}

#[command]
async fn gaming_status() -> Result<String, String> {
    run_cli_json(&["game-mode".into(), "status".into()])
}

#[command]
async fn gaming_stop() -> Result<String, String> {
    run_cli_json(&["game-mode".into(), "stop".into()])
}

/// Presets in all four shapes: `list`, `save`, `apply`, `delete`.
#[command]
async fn gaming_preset(
    action: String,
    name: Option<String>,
    plan: Option<String>,
    services: Option<String>,
    for_process: Option<String>,
    game: Option<String>,
    no_notifications: Option<bool>,
    no_background_apps: Option<bool>,
) -> Result<String, String> {
    let verb = match action.as_str() {
        "list" => "preset-list",
        "save" => "preset-save",
        "apply" => "preset-apply",
        "delete" => "preset-delete",
        other => return Err(format!("Unknown preset action '{}'.", other)),
    };

    let mut args = vec!["game-mode".into(), verb.into()];
    push_opt(&mut args, "--name", &name);
    push_opt(&mut args, "--plan", &plan);
    if no_notifications == Some(true) {
        args.push("--no-notifications".into());
    }
    if no_background_apps == Some(true) {
        args.push("--no-background-apps".into());
    }
    push_opt(&mut args, "--services", &services);
    push_opt(&mut args, "--for-process", &for_process);
    push_opt(&mut args, "--game", &game);
    run_cli_json(&args)
}

/// Returns `readable: false` rather than an error when the list needs elevation:
/// "you may not see these yet" is not the same as "this failed".
#[command]
async fn defender_list() -> Result<String, String> {
    run_cli_json(&["defender".into(), "list".into()])
}

/// `add`, `remove` or `export`. `add` without `confirm` is refused by the CLI,
/// so the UI cannot exclude anything without having shown the path first.
#[command]
async fn defender_change(
    action: String,
    path: Option<String>,
    confirm: Option<bool>,
    output: Option<String>,
) -> Result<String, String> {
    let verb = match action.as_str() {
        "add" | "remove" | "export" => action.clone(),
        other => return Err(format!("Unknown defender action '{}'.", other)),
    };

    let mut args = vec!["defender".into(), verb];
    push_opt(&mut args, "--path", &path);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    push_opt(&mut args, "--output", &output);
    run_cli_json(&args)
}

// ============ App Installer ============

/// Whether winget exists here, which version, and what it is pointed at.
#[command]
async fn apps_probe() -> Result<String, String> {
    run_cli_json(&["apps".into(), "probe".into()])
}

/// The curated catalogue itself: categories, entries, and the packages that
/// were deliberately left out. Adding an app is a JSON edit — this command is
/// how the UI learns about it, with no code change on either side.
#[command]
async fn apps_catalog() -> Result<String, String> {
    run_cli_json(&["apps".into(), "catalog".into()])
}

/// Installed state for every catalogue app. `deep` also scans Add/Remove
/// Programs, which takes about forty seconds; the default pass is a couple of
/// seconds and covers everything winget itself tracks.
#[command]
async fn apps_status(deep: Option<bool>) -> Result<String, String> {
    let mut args = vec!["apps".into(), "status".into()];
    if deep == Some(true) {
        args.push("--deep".into());
    }
    run_cli_json(&args)
}

/// The authoritative answer for one package. Filtering by ID makes winget
/// resolve the manifest against Add/Remove Programs, so a copy installed by
/// hand still comes back under its real ID — which the bulk pass cannot do.
#[command]
async fn apps_installed(app_id: String) -> Result<String, String> {
    run_cli_json(&["apps".into(), "installed".into(), "--id".into(), app_id])
}

/// Everything winget publishes about one package: publisher, licence, homepage,
/// installer type, installer URL and its SHA256.
#[command]
async fn apps_show(app_id: String) -> Result<String, String> {
    run_cli_json(&["apps".into(), "show".into(), "--id".into(), app_id])
}

#[command]
async fn apps_search(query: String) -> Result<String, String> {
    run_cli_json(&["apps".into(), "search".into(), "--query".into(), query])
}

/// `action` is install, uninstall, upgrade or upgrade-all. `scope` is only
/// meaningful for install. `elevated` retries through UAC after winget has
/// already said it needs administrator rights — the UI never sends it first.
#[command]
async fn apps_change(
    action: String,
    app_id: Option<String>,
    scope: Option<String>,
    elevated: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "install" | "uninstall" | "upgrade" | "upgrade-all" => {}
        other => return Err(format!("Unknown apps action '{}'.", other)),
    }

    let mut args = vec!["apps".into(), action];
    push_opt(&mut args, "--id", &app_id);
    push_opt(&mut args, "--scope", &scope);
    if elevated == Some(true) {
        args.push("--elevated".into());
    }
    run_cli_json(&args)
}

/// Open a catalogue tool by resolving its name against the Start menu and
/// starting the entry that matched. The CLI reports which entry it settled on,
/// because the name on the card and the name in the Start menu differ often
/// enough that "opened something" is not the same claim as "opened this".
#[command]
async fn apps_launch(app_id: String) -> Result<String, String> {
    run_cli_json(&["apps".into(), "launch".into(), "--id".into(), app_id])
}

// ============ Blocker ============

/// Everything the Blocker page reads in one pass: the hosts file's managed and
/// unmanaged counts, whether it is writable, the firewall rule count, the
/// catalogue of fetchable lists, what has already been applied, and every rule
/// with its provenance. One call rather than five, because the numbers are
/// meant to be read against each other.
#[command]
async fn blocker_status() -> Result<String, String> {
    run_cli_json(&["blocker".into(), "status".into()])
}

/// Download a list and report what it contains. Nothing is written: this is
/// the step that makes the size, the category and the diff visible before the
/// user agrees to anything, and it is deliberately a separate command from
/// `blocker_apply` so no code path can fold the two together.
#[command]
async fn blocker_fetch(source: String) -> Result<String, String> {
    run_cli_json(&["blocker".into(), "fetch".into(), "--source".into(), source])
}

/// Write a fetched list into the managed section. `confirm` is required by the
/// CLI, so the UI cannot apply a list it has not shown a dialog for.
#[command]
async fn blocker_apply(source: String, confirm: Option<bool>) -> Result<String, String> {
    let mut args = vec!["blocker".into(), "apply".into(), "--source".into(), source];
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// One domain into the managed section. The CLI frames a Software-category
/// rule itself, so passing `category: "Software"` without a confirm is refused
/// with the licensing disclaimer rather than silently applied.
#[command]
async fn blocker_add(
    value: String,
    category: Option<String>,
    purpose: Option<String>,
    severity: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    let mut args = vec!["blocker".into(), "add".into(), "--value".into(), value];
    push_opt(&mut args, "--category", &category);
    push_opt(&mut args, "--purpose", &purpose);
    push_opt(&mut args, "--severity", &severity);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// A firewall rule that drops traffic for one executable.
#[command]
async fn blocker_program(
    id: String,
    program: String,
    category: Option<String>,
    purpose: Option<String>,
    severity: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    let mut args = vec![
        "blocker".into(), "program".into(),
        "--id".into(), id,
        "--value".into(), program,
    ];
    push_opt(&mut args, "--category", &category);
    push_opt(&mut args, "--purpose", &purpose);
    push_opt(&mut args, "--severity", &severity);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// `action` is remove, enable or disable; `kind` is hosts, firewall or auto.
/// Auto is the default on the CLI and means "look in the hosts section first",
/// so the UI only sends `kind` when the row it is acting on says which one it is.
#[command]
async fn blocker_rule(action: String, id: String, kind: Option<String>) -> Result<String, String> {
    match action.as_str() {
        "remove" | "enable" | "disable" => {}
        other => return Err(format!("Unknown rule action '{}'.", other)),
    }
    let mut args = vec!["blocker".into(), action, "--id".into(), id];
    push_opt(&mut args, "--kind", &kind);
    run_cli_json(&args)
}

/// Take one applied list back off without touching the others. This is the
/// per-list rollback: `blocker_reset`'s `unmerge` would take the custom rules
/// with it, and a rollback that costs more than the mistake is not one.
#[command]
async fn blocker_remove_source(source: String, confirm: Option<bool>) -> Result<String, String> {
    let mut args = vec!["blocker".into(), "remove".into(), "--source".into(), source];
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// The three wholesale operations: `unmerge` (remove Novimize's section, keep
/// everything else), `restore` (put the file back to the first-write backup)
/// and `clear-firewall` (drop every Novimize firewall rule). All three need
/// `confirm`, so each one is behind a dialog in the UI.
#[command]
async fn blocker_reset(action: String, confirm: Option<bool>) -> Result<String, String> {
    match action.as_str() {
        "unmerge" | "restore" | "clear-firewall" => {}
        other => return Err(format!("Unknown reset action '{}'.", other)),
    }
    let mut args = vec!["blocker".into(), action];
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Write the managed rules to a JSON file the user can read, keep, or import
/// onto another machine. An export never touches the hosts file.
#[command]
async fn blocker_export(output: Option<String>) -> Result<String, String> {
    let mut args = vec!["blocker".into(), "export".into()];
    push_opt(&mut args, "--output", &output);
    run_cli_json(&args)
}

#[command]
async fn blocker_import(input: String, confirm: Option<bool>) -> Result<String, String> {
    let mut args = vec!["blocker".into(), "import".into(), "--input".into(), input];
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

// ============ Network ============

/// DNS, in all six shapes: `status`, `set`, `revert`, `flush`, `test`,
/// `latency`. Grouped rather than six commands because they share one request
/// shape and the CLI is where the action names are validated — a page that
/// invents a seventh action gets refused there instead of silently doing
/// nothing here.
///
/// `set` and `revert` are refused by the CLI without `confirm`, so the UI
/// cannot change what this machine resolves with without a dialog first.
#[command]
async fn dns_action(
    action: String,
    provider: Option<String>,
    adapter: Option<String>,
    doh: Option<bool>,
    name: Option<String>,
    server: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "set" | "revert" | "flush" | "test" | "latency" => {}
        other => return Err(format!("Unknown dns action '{}'.", other)),
    }

    let mut args = vec!["dns".into(), action];
    push_opt(&mut args, "--provider", &provider);
    push_opt(&mut args, "--adapter", &adapter);
    push_opt(&mut args, "--name", &name);
    push_opt(&mut args, "--server", &server);
    if doh == Some(true) {
        args.push("--doh".into());
    }
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// The toolbox: `status`, `ping`, `trace`, `lookup`, `reverse`, `public-ip`,
/// `fix`, `restart`. Same reasoning as `dns_action`.
///
/// `public-ip` reaches out to a third party and `fix` / `restart` rewrite
/// configuration, so all three are refused without `confirm`.
#[command]
async fn net_action(
    action: String,
    host: Option<String>,
    name: Option<String>,
    kind: Option<String>,
    server: Option<String>,
    hops: Option<u32>,
    level: Option<String>,
    value: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "ping" | "trace" | "lookup" | "reverse" | "public-ip" | "fix" | "restart" => {}
        other => return Err(format!("Unknown net action '{}'.", other)),
    }

    let mut args = vec!["net".into(), action];
    push_opt(&mut args, "--host", &host);
    push_opt(&mut args, "--name", &name);
    push_opt(&mut args, "--type", &kind);
    push_opt(&mut args, "--server", &server);
    push_opt(&mut args, "--level", &level);
    push_opt(&mut args, "--value", &value);
    if let Some(n) = hops {
        args.push("--hops".into());
        args.push(n.to_string());
    }
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Power Center: `status`, `plan`, `novimize`, `revert`. The three writes are
/// refused by the CLI without `confirm`, and the refusal carries the powercfg
/// lines the confirmation is meant to show.
#[command]
async fn power_action(
    action: String,
    id: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "plan" | "novimize" | "revert" => {}
        other => return Err(format!("Unknown power action '{}'.", other)),
    }

    let mut args = vec!["power".into(), action];
    push_opt(&mut args, "--id", &id);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Startup Manager: `status`, `enable`, `disable`, `open`. Nothing here ever
/// deletes an entry — enable and disable write the same StartupApproved flag
/// Task Manager writes, and both are refused by the CLI without `confirm`.
#[command]
async fn startup_action(
    action: String,
    id: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "enable" | "disable" | "open" => {}
        other => return Err(format!("Unknown startup action '{}'.", other)),
    }

    let mut args = vec!["startup".into(), action];
    push_opt(&mut args, "--id", &id);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Services: `status`, `start`, `stop`, `restart`, `manual`, `automatic`,
/// `disabled`, `restore`. The CLI refuses the protected list with the reason
/// attached, and every change is refused without `confirm` — with the command
/// line it would have run attached to the refusal.
#[command]
async fn services_action(
    action: String,
    name: Option<String>,
    filter: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "start" | "stop" | "restart" | "manual" | "automatic" | "disabled" | "restore" => {}
        other => return Err(format!("Unknown services action '{}'.", other)),
    }

    let mut args = vec!["services".into(), action];
    push_opt(&mut args, "--name", &name);
    push_opt(&mut args, "--filter", &filter);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Scheduled tasks: `status`, `enable`, `disable`, `run`, `restore`. Nothing
/// here deletes a task.
#[command]
async fn tasks_action(
    action: String,
    id: Option<String>,
    filter: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "enable" | "disable" | "run" | "restore" => {}
        other => return Err(format!("Unknown tasks action '{}'.", other)),
    }

    let mut args = vec!["tasks".into(), action];
    push_opt(&mut args, "--id", &id);
    push_opt(&mut args, "--filter", &filter);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Debloat Center: `status`, `remove`, `restore`. Both writes are refused by
/// the CLI without `confirm`, and the refusal carries the command lines plus
/// whatever the engine refused it for — a framework, Windows' own
/// non-removable flag, or another package waiting on it.
#[command]
async fn debloat_action(
    action: String,
    name: Option<String>,
    all_users: Option<bool>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "remove" | "restore" => {}
        other => return Err(format!("Unknown debloat action '{}'.", other)),
    }

    let mut args = vec!["debloat".into(), action];
    push_opt(&mut args, "--name", &name);
    if all_users == Some(true) {
        args.push("--all-users".into());
    }
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Maintenance Center: `status` and `run`. Every tool shows its size, what it
/// deletes and the exact commands before it runs, and `run` is refused by the
/// CLI without `confirm`.
#[command]
async fn maint_action(
    action: String,
    id: Option<String>,
    confirm: Option<bool>,
) -> Result<String, String> {
    match action.as_str() {
        "status" | "run" => {}
        other => return Err(format!("Unknown maint action '{}'.", other)),
    }

    let mut args = vec!["maint".into(), action];
    push_opt(&mut args, "--id", &id);
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Windows Update: `status`, `scan`, `open`, `restart`. Nothing here writes
/// update policy — the only write is scheduling a restart, which the CLI
/// refuses without `confirm` and shows the shutdown line for.
#[command]
async fn update_action(action: String, confirm: Option<bool>) -> Result<String, String> {
    match action.as_str() {
        "status" | "scan" | "open" | "restart" => {}
        other => return Err(format!("Unknown update action '{}'.", other)),
    }

    let mut args = vec!["update".into(), action];
    if confirm == Some(true) {
        args.push("--confirm".into());
    }
    run_cli_json(&args)
}

/// Write the health report to a file as JSON, text or HTML. The CLI renders
/// it, so the file and what the page shows come from the same code.
#[command]
async fn health_report(format: Option<String>, output: Option<String>) -> Result<String, String> {
    let mut args = vec!["health".into(), "report".into()];
    push_opt(&mut args, "--format", &format);
    push_opt(&mut args, "--output", &output);
    run_cli_json(&args)
}

/// One snapshot in full: every entry with the value it had before and after.
/// The same data the rollback reads, so the diff and the undo cannot disagree
/// about what was changed.
#[command]
async fn snapshot_show(snapshot_id: String) -> Result<String, String> {
    run_cli_json(&["snapshots".into(), "--id".into(), snapshot_id])
}

/// Write the change journal to a file, carrying the same filter that was
/// applied on screen so the file really is the slice it claims to be.
#[command]
async fn journal_export(
    limit: Option<u32>,
    tweak: Option<String>,
    operation: Option<String>,
    result: Option<String>,
    output: Option<String>,
) -> Result<String, String> {
    let mut args = vec!["journal".into(), "--limit".into()];
    args.push(limit.unwrap_or(200).to_string());
    push_opt(&mut args, "--tweak", &tweak);
    push_opt(&mut args, "--operation", &operation);
    push_opt(&mut args, "--result", &result);
    push_opt(&mut args, "--output", &output);
    run_cli_json(&args)
}

/// Open an https link in the user's browser.
///
/// The shell plugin's JS half is not a dependency here, and a plain `<a
/// target="_blank">` inside the webview can navigate the app itself away from
/// the page. This is the narrow version of "open a link": the scheme is checked
/// first, because this ends in a shell.
#[command]
async fn open_external(url: String) -> Result<(), String> {
    if !(url.starts_with("https://") || url.starts_with("http://")) {
        return Err("Only http and https links can be opened.".into());
    }
    let mut cmd = Command::new("cmd");
    cmd.args(["/c", "start", "", &url]);
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    cmd.spawn().map_err(|e| format!("Could not open {}: {}", url, e))?;
    Ok(())
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .setup(|_app| {
            #[cfg(debug_assertions)]
            {
                use tauri::Manager;
                if let Some(window) = _app.get_webview_window("main") {
                    window.open_devtools();
                }
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_system_info,
            scan_tweaks,
            list_tweaks,
            apply_tweak,
            apply_profile,
            rollback_tweak,
            rollback_all,
            get_recommendations,
            list_profiles,
            profile_selector,
            run_diagnostics,
            get_snapshots,
            plan_tweak,
            get_journal,
            gaming_detect,
            gaming_folder,
            gaming_start,
            gaming_status,
            gaming_stop,
            gaming_preset,
            defender_list,
            defender_change,
            apps_probe,
            apps_catalog,
            apps_status,
            apps_installed,
            apps_show,
            apps_search,
            apps_change,
            apps_launch,
            blocker_status,
            blocker_fetch,
            blocker_apply,
            blocker_add,
            blocker_program,
            blocker_rule,
            blocker_remove_source,
            blocker_reset,
            blocker_export,
            blocker_import,
            dns_action,
            net_action,
            power_action,
            startup_action,
            services_action,
            tasks_action,
            debloat_action,
            maint_action,
            update_action,
            health_report,
            snapshot_show,
            journal_export,
            open_external,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
