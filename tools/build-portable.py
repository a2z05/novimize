"""Package the built app as a portable zip.

The flat layout is what the CLI's catalogue lookup expects at runtime:
AppContext.BaseDirectory/<dir> must find tweaks/, apps/, blocklists/ and
debloat/ beside winopt-cli.exe. The installer ships the same files through
tauri.conf.json's bundle.resources.
"""

import json
import os
import shutil
import sys
import zipfile

ROOT = r"D:\CLaude\winopt"
STAGE = os.path.join(ROOT, "Novimize")

# Read the version from tauri.conf.json rather than repeating it here — a
# second copy is a second thing to forget to update.
CONFIG = os.path.join(ROOT, "src", "ui", "src-tauri", "tauri.conf.json")


def version() -> str:
    with open(CONFIG, encoding="utf-8") as fh:
        value = json.load(fh).get("version")
    if not value:
        raise SystemExit(f"no version in {CONFIG}")
    return value


ZIP = os.path.join(ROOT, f"Novimize_{version()}_portable.zip")

# Built binaries -> the names they ship under.
BINARIES = [
    (
        os.path.join(
            ROOT, "src", "ui", "src-tauri", "target",
            "x86_64-pc-windows-msvc", "release", "winopt.exe",
        ),
        "Novimize.exe",
    ),
    (
        os.path.join(
            ROOT, "src", "ui", "src-tauri", "binaries",
            "winopt-cli-x86_64-pc-windows-msvc.exe",
        ),
        "winopt-cli.exe",
    ),
]

# Catalogue folders copied beside the binaries, as (source directory,
# destination name). Everything but icons/ lives at the repo root; icons/ is
# part of the Tauri crate because bundle.resources points at it.
CATALOGUES = [
    ("tweaks", "tweaks"),
    ("apps", "apps"),
    ("blocklists", "blocklists"),
    ("debloat", "debloat"),
    (os.path.join("src", "ui", "src-tauri", "icons"), "icons"),
]


def copy_file(src: str, dst: str) -> int:
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)
    return os.path.getsize(dst)


def main() -> int:
    missing = [src for src, _ in BINARIES if not os.path.isfile(src)]
    missing += [
        os.path.join(ROOT, src)
        for src, _ in CATALOGUES
        if not os.path.isdir(os.path.join(ROOT, src))
    ]
    if missing:
        for path in missing:
            print(f"MISSING: {path}", file=sys.stderr)
        return 2

    if os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)

    for src, name in BINARIES:
        size = copy_file(src, os.path.join(STAGE, name))
        print(f"  + {name:<20} {size:>12,} bytes")

    for src_name, dst_name in CATALOGUES:
        src_dir = os.path.join(ROOT, src_name)
        total = 0
        for entry in sorted(os.listdir(src_dir)):
            src = os.path.join(src_dir, entry)
            if os.path.isfile(src):
                total += copy_file(src, os.path.join(STAGE, dst_name, entry))
        print(f"  + {dst_name + '/':<20} {total:>12,} bytes")

    if os.path.exists(ZIP):
        os.remove(ZIP)

    with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for base, _dirs, files in os.walk(STAGE):
            for entry in sorted(files):
                full = os.path.join(base, entry)
                zf.write(full, os.path.relpath(full, ROOT))

    print(f"\n{ZIP}  {os.path.getsize(ZIP):,} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())