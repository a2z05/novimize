use std::fs;
use std::path::Path;

fn main() {
    sync_catalogues();
    tauri_build::build()
}

/// Copy the catalogue data from the repository root into this crate's folder,
/// where `bundle.resources` expects to find it.
///
/// The bundled `tweaks/` used to be committed next to the crate, and it drifted:
/// the repository copy gained a tenth file and a day of edits while the bundled
/// one kept shipping the initial commit's nine. An installer that carries a
/// stale catalogue is worse than one that carries none, because nothing in the
/// build fails. Making it a build artifact removes the second copy from git and
/// makes the build the only thing that can produce it — so it cannot be out of
/// date, only missing.
fn sync_catalogues() {
    let crate_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    println!("cargo:rerun-if-changed={}", crate_dir.join("build.rs").display());

    for name in ["tweaks", "apps", "blocklists"] {
        let from = crate_dir.join("..").join("..").join("..").join(name);
        println!("cargo:rerun-if-changed={}", from.display());

        if !from.is_dir() {
            // Fail loudly rather than bundle an installer with no catalogue.
            panic!(
                "catalogue directory {} does not exist; the bundle would ship without {}",
                from.display(),
                name
            );
        }

        let to = crate_dir.join(name);
        fs::create_dir_all(&to).unwrap_or_else(|e| panic!("could not create {}: {}", to.display(), e));

        let mut keep = Vec::new();
        for entry in fs::read_dir(&from).expect("read_dir") {
            let entry = entry.expect("dir entry");
            let path = entry.path();
            if path.extension().and_then(|e| e.to_str()) != Some("json") {
                continue;
            }
            let file_name = entry.file_name();
            let target = to.join(&file_name);
            if !same_contents(&path, &target) {
                fs::copy(&path, &target).unwrap_or_else(|e| {
                    panic!("could not copy {} to {}: {}", path.display(), target.display(), e)
                });
            }
            keep.push(file_name);
        }

        // Anything left over from an earlier revision of the catalogue has to
        // go: a removed tweak that is still bundled is a tweak that can still
        // be applied by a shipped installer.
        for entry in fs::read_dir(&to).expect("read_dir") {
            let entry = entry.expect("dir entry");
            let file_name = entry.file_name();
            if file_name.to_string_lossy().ends_with(".json") && !keep.contains(&file_name) {
                fs::remove_file(entry.path()).ok();
            }
        }
    }
}

fn same_contents(a: &Path, b: &Path) -> bool {
    match (fs::read(a), fs::read(b)) {
        (Ok(x), Ok(y)) => x == y,
        _ => false,
    }
}
