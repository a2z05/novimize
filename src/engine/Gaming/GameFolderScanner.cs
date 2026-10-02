using WinOpt.Core.Models;

namespace WinOpt.Engine.Gaming;

/// <summary>
/// Looks inside a folder the user chose for things that look like games.
///
/// The result is always labelled <c>candidate</c>. A directory that happens to
/// contain an executable is not a claim by any launcher that it holds a game,
/// and the UI is not allowed to present it as one.
/// </summary>
public static class GameFolderScanner
{
    /// <summary>
    /// Executables that identify a folder as "a program lives here" only in the
    /// sense that every folder containing one would match.
    /// </summary>
    private static readonly HashSet<string> IgnoredExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "unins000", "uninstall", "setup", "install", "crashreporter",
        "vcredist", "dxsetup", "dotNetFx", "oalinst", "ue4prereq",
        "easyanti cheats", "eac_eos", "eosinstaller",
    };

    private const int MaxDepth = 2;
    private const int MaxCandidates = 200;

    public static IReadOnlyList<GameEntry> Scan(string folder, string launcher = "manual")
    {
        var results = new List<GameEntry>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return results;

        var rootName = new DirectoryInfo(folder).Name;
        Walk(new DirectoryInfo(folder), depth: 0, rootName, launcher, results);
        return results;
    }

    private static void Walk(DirectoryInfo dir, int depth, string displayName,
        string launcher, List<GameEntry> results)
    {
        if (results.Count >= MaxCandidates) return;

        var executables = ExecutablesIn(dir);

        if (executables.Count > 0)
        {
            results.Add(new GameEntry
            {
                Name = displayName,
                InstallPath = dir.FullName,
                Launcher = launcher,
                Source = "candidate",
                Folder = dir.FullName,
            });
            // The folder already produced a candidate — descending further
            // would only re-report the same install as a nested one.
            return;
        }

        if (depth >= MaxDepth) return;

        foreach (var child in SafeEnumerate(dir))
        {
            if (results.Count >= MaxCandidates) break;
            // Common/Download/cache trees are installer output, not games.
            if (child.Name.Equals("common", StringComparison.OrdinalIgnoreCase)) continue;
            Walk(child, depth + 1, child.Name, launcher, results);
        }
    }

    /// <summary>
    /// Executables directly in a folder. One is enough — the count and names
    /// are not used to decide anything, only their presence.
    /// </summary>
    private static List<string> ExecutablesIn(DirectoryInfo dir)
    {
        var found = new List<string>();
        try
        {
            foreach (var file in dir.EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly))
            {
                if (IgnoredExecutables.Contains(Path.GetFileNameWithoutExtension(file.Name))) continue;
                found.Add(file.Name);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return found;
    }

    private static IEnumerable<DirectoryInfo> SafeEnumerate(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories().ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DirectoryInfo>();
        }
        catch (IOException)
        {
            return Array.Empty<DirectoryInfo>();
        }
    }
}
