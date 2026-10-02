using WinOpt.Core.Models;
using Microsoft.Win32;

namespace WinOpt.Engine.Gaming;

/// <summary>
/// Reads Steam's own records: where its libraries live, and which games are in
/// each of them.
///
/// Every answer here comes from a file Steam wrote. Nothing is inferred from
/// directory names, because a folder called "common" is not a claim that Steam
/// installed what is inside it — only <c>appmanifest_*.acf</c> is.
/// </summary>
public static class SteamLibrary
{
    /// <summary>Steam's install directory, from the value Steam itself writes.</summary>
    public static string? GetSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(path)) return null;

        // Steam writes forward slashes ("c:/program files (x86)/steam"); the
        // rest of this codebase hands paths to APIs that expect backslashes.
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary>
    /// Library roots from a <c>libraryfolders.vdf</c> document, in file order.
    ///
    /// The first library (index 0) is the Steam install itself and is included
    /// even though older documents put it there by convention rather than as a
    /// separate entry — a missing entry would hide every game in the default
    /// library.
    /// </summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText, string? steamPath = null)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var full = raw.Replace('/', '\\').TrimEnd('\\');
            if (seen.Add(full)) paths.Add(full);
        }

        Add(steamPath);

        VdfNode root;
        try { root = Vdf.Parse(vdfText); }
        catch { return paths; }

        var folders = root.Children("libraryfolders").FirstOrDefault() ?? root;

        // Current format: a numbered child block carrying a "path" scalar.
        foreach (var lib in folders.AllChildren())
            Add(lib.Get("path"));

        // Very old documents put the index itself on a scalar — "1" "D:\Games".
        // Reading only the blocks above would drop every secondary library and
        // report a machine with three drives as one with the default install.
        foreach (var entry in folders.Entries)
            if (entry.Scalar != null && LooksLikePath(entry.Scalar) && int.TryParse(entry.Key, out _))
                Add(entry.Scalar);

        return paths;
    }

    private static bool LooksLikePath(string value)
        => value.IndexOf('\\') >= 0
           || (value.Length > 1 && value[1] == ':')
           || value.StartsWith('/');

    public static IReadOnlyList<string> ReadLibraries(string steamPath)
        => ReadLibrariesWithStatus(steamPath).Present;

    /// <summary>
    /// Libraries split into the ones this machine can see and the ones that have
    /// gone — an unplugged drive, an external that is not inserted. The missing
    /// half is reported rather than dropped, because "you own 13 games" that
    /// quietly means "13 games on the drive that is plugged in" is a wrong
    /// answer, not a smaller true one.
    /// </summary>
    public static (IReadOnlyList<string> Present, IReadOnlyList<string> Missing) ReadLibrariesWithStatus(string steamPath)
    {
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return (new[] { steamPath }, Array.Empty<string>());

        try
        {
            var parsed = ParseLibraryFolders(File.ReadAllText(vdf), steamPath);
            var present = parsed.Where(Directory.Exists).ToList();
            var missing = parsed.Where(p => !Directory.Exists(p)).ToList();
            if (present.Count == 0) present.Add(steamPath);
            return (present, missing);
        }
        catch
        {
            return (new[] { steamPath }, Array.Empty<string>());
        }
    }

    /// <summary>Fields a Steam manifest is known to carry.</summary>
    public sealed record AppManifest(string AppId, string Name, string InstallDir, string StateFlags);

    /// <summary>
    /// Apps Steam installs that are not games: its own support files. They have
    /// manifests and folders like anything else, and a list that offered to
    /// optimise them would be wrong rather than merely generous.
    /// </summary>
    private static readonly HashSet<string> NonGameAppIds = new(StringComparer.Ordinal)
    {
        "228980", // Steamworks Common Redistributables
        "1007",   // Steam Play compatibility tools
        "1628350", // Steam Linux Runtime (present if Proton is installed)
    };

    /// <summary>
    /// Reads one <c>appmanifest_*.acf</c>. Returns null when the file is not a
    /// manifest (a truncated download, an unrelated document) rather than
    /// producing a game with an empty name.
    /// </summary>
    public static AppManifest? ParseAppManifest(string acfText)
    {
        VdfNode root;
        try { root = Vdf.Parse(acfText); }
        catch { return null; }

        var state = root.Children("AppState").FirstOrDefault() ?? root;

        var appId = state.Get("appid");
        var name = state.Get("name");
        var installDir = state.Get("installdir");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
            return null;

        return new AppManifest(appId, name.Trim(), installDir?.Trim() ?? string.Empty,
            state.Get("StateFlags") ?? string.Empty);
    }

    /// <summary>
    /// Every installed Steam game on this machine: manifests across every
    /// library, reported as <c>manifest</c> because that is what they are.
    ///
    /// A manifest whose <c>installdir</c> folder is gone describes an app that
    /// is no longer there, so it is skipped rather than reported as installed.
    /// </summary>
    public static IReadOnlyList<GameEntry> ReadGames(string steamPath)
    {
        var games = new List<GameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in ReadLibraries(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (var file in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                AppManifest? manifest;
                try { manifest = ParseAppManifest(File.ReadAllText(file)); }
                catch { continue; }
                if (manifest == null) continue;
                if (NonGameAppIds.Contains(manifest.AppId)) continue;

                // Steam names some manifests ".acf.N.tmp" mid-download; those
                // are excluded by the extension filter above, but a completed
                // manifest can still name a folder that was removed.
                var install = string.IsNullOrWhiteSpace(manifest.InstallDir)
                    ? null
                    : Path.Combine(steamapps, "common", manifest.InstallDir);
                if (install == null || !Directory.Exists(install)) continue;
                if (!seen.Add(install)) continue;

                games.Add(new GameEntry
                {
                    Name = manifest.Name,
                    InstallPath = install,
                    Launcher = "steam",
                    Source = "manifest",
                    Folder = library,
                });
            }
        }

        return games;
    }
}
