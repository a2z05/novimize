#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::process::Command;
use tauri::command;

/// Resolve the path to the winopt-cli sidecar executable.
fn cli_path() -> String {
    #[cfg(debug_assertions)]
    {
        let manifest_dir = env!("CARGO_MANIFEST_DIR");
        let cli_path = std::path::Path::new(manifest_dir)
            .join("../../../src/cli/bin/Debug/net8.0/win-x64/WinOpt.Cli.exe");
        if cli_path.exists() {
            return cli_path.to_string_lossy().to_string();
        }
        let cli_path = std::path::Path::new(manifest_dir)
            .join("../../../src/cli/bin/Debug/net8.0/win-x64/publish/WinOpt.Cli.exe");
        cli_path.to_string_lossy().to_string()
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
async fn list_tweaks(category: Option<String>, risk: Option<String>, profile: Option<String>) -> Result<String, String> {
    let mut args = vec!["list".to_string()];
    push_opt(&mut args, "--category", &category);
    push_opt(&mut args, "--risk", &risk);
    push_opt(&mut args, "--profile", &profile);
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
) -> Result<String, String> {
    let mut args = vec!["plan".to_string()];
    if let Some(id) = tweak_id.filter(|s| !s.is_empty()) {
        args.push(id);
    }
    push_opt(&mut args, "--profile", &profile);
    push_opt(&mut args, "--category", &category);
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
async fn apply_profile(profile_id: String, dry_run: Option<bool>) -> Result<String, String> {
    let mut args = vec!["apply".into(), "--profile".into(), profile_id];
    if dry_run == Some(true) {
        args.push("--dry-run".into());
    }
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
            run_diagnostics,
            get_snapshots,
            plan_tweak,
            get_journal,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
