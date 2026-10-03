# Runs the freshly built NSIS installer and waits for it to finish.
# The previous install is machine-wide, so the installer needs elevation.
# Start-Process with -Verb RunAs is what raises the UAC prompt; the exit code
# it prints back is the installer's own, not the shell's.
$ErrorActionPreference = 'Stop'

$installer = 'D:\CLaude\winopt\src\ui\src-tauri\target\x86_64-pc-windows-msvc\release\bundle\nsis\Novimize_1.0.0_x64-setup.exe'

if (-not (Test-Path $installer)) {
    Write-Output "MISSING: $installer"
    exit 2
}

Write-Output "Installer: $installer"
Write-Output "Size: $((Get-Item $installer).Length) bytes"

$p = Start-Process -FilePath $installer -Wait -PassThru -Verb RunAs
Write-Output "INSTALLER EXIT CODE: $($p.ExitCode)"