namespace WinOpt.Core;

/// <summary>
/// The version this build claims to be.
///
/// The .NET assembly version is 1.0.0 by default and the Tauri bundle has its
/// own field, so there were three places a version could live and none of them
/// agreed. This is the one the updater compares against a release tag, and a
/// test asserts it matches <c>src/ui/src-tauri/tauri.conf.json</c> — because a
/// version that disagrees with the installer's is worse than no version: it
/// reports an update that is already installed.
/// </summary>
public static class AppVersion
{
    public const string Value = "1.0.0";
}
