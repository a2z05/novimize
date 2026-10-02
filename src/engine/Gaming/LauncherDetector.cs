using Microsoft.Win32;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Gaming;

/// <summary>
/// Finds game launchers, their libraries, and the games they publish.
///
/// Every launcher is probed twice — once by registry key, once by path — and the
/// result records which probe matched. A launcher is reported only when
/// something actually matched; a launcher that is not installed is absent from
/// the answer rather than present with a guessed install location.
/// </summary>
public sealed class LauncherDetector
{
    /// <summary>
    /// One launcher's probes.
    ///
    /// <c>GameRoots</c> are directories that hold that launcher's games and
    /// contain no manifest of their own, so games inside them can only ever be
    /// candidates. A root that does not exist contributes nothing.
    /// </summary>
    private sealed record LauncherSpec(
        string Id,
        string Name,
        IReadOnlyList<(string Key, string Value)> RegistryProbes,
        IReadOnlyList<string> PathProbes,
        IReadOnlyList<string> GameRoots);

    private static readonly IReadOnlyList<LauncherSpec> Specs = new LauncherSpec[]
    {
        new("steam", "Steam",
            new[] { (@"Software\Valve\Steam", "SteamPath") },
            new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
            },
            Array.Empty<string>()),

        new("ubisoft", "Ubisoft Connect",
            new[]
            {
                (@"Software\WOW6432Node\Ubisoft\Launcher", "InstallDir"),
                (@"Software\Ubisoft\Launcher", "InstallDir"),
            },
            new[] { @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher" },
            // Ubisoft installs titles into its own launcher directory.
            new[] { @"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher" }),

        new("epic", "Epic Games Launcher",
            new[]
            {
                (@"Software\WOW6432Node\Epic Games\EpicGamesLauncher", "InstallLocation"),
                (@"Software\Epic Games\EpicGamesLauncher", "InstallLocation"),
            },
            new[] { @"C:\Program Files (x86)\Epic Games\Launcher" },
            new[] { @"C:\Program Files (x86)\Epic Games" }),

        new("gog", "GOG Galaxy",
            new[]
            {
                (@"Software\WOW6432Node\GOG.com\GalaxyClient", "defaultInstallPath"),
            },
            new[] { @"C:\Program Files (x86)\GOG Galaxy" },
            new[]
            {
                @"C:\Program Files (x86)\GOG Games",
                @"C:\GOG Games",
            }),

        new("ea", "EA app",
            new[]
            {
                (@"Software\WOW6432Node\Electronic Arts\EA Desktop", "InstallLocation"),
            },
            new[]
            {
                @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop",
                @"C:\Program Files (x86)\Electronic Arts\EA Desktop\EA Desktop",
            },
            new[]
            {
                @"C:\Program Files\Electronic Arts",
                @"C:\Program Files (x86)\Electronic Arts",
            }),

        new("battlenet", "Battle.net",
            new[]
            {
                (@"Software\WOW6432Node\Blizzard Entertainment\Battle.net", "InstallPath"),
            },
            new[] { @"C:\Program Files (x86)\Battle.net" },
            // Battle.net titles install to their own directories, which no
            // path above enumerates without guessing. Manual folders cover
            // them instead of a speculative walk of Program Files (x86).
            Array.Empty<string>()),
    };

    public GameDetection Detect()
    {
        var detection = new GameDetection();
        var games = new List<GameEntry>();

        foreach (var spec in Specs)
        {
            var info = Probe(spec);
            detection.Launchers.Add(info);

            if (!info.Detected) continue;
            if (spec.Id == "steam" && info.InstallPath != null)
            {
                // The libraries are shown next to the launcher, and the ones
                // that are gone are a warning rather than a silent omission —
                // "you own 13 games" that means "on the drive that is plugged
                // in" is a wrong answer, not a smaller true one.
                var (present, missing) = SteamLibrary.ReadLibrariesWithStatus(info.InstallPath);
                info.Libraries.AddRange(present);
                foreach (var gone in missing)
                    detection.Warnings.Add($"Steam library is not present on this machine: {gone}");

                CollectSteam(info.InstallPath, games);
            }

            foreach (var root in spec.GameRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var candidate in GameFolderScanner.Scan(root, spec.Id))
                {
                    // A GameRoot that is the launcher's own directory holds the
                    // launcher's executable too. The scanner reports whatever
                    // folder it finds an exe in, so without this the launcher
                    // would appear as one of its own games.
                    if (info.InstallPath != null
                        && string.Equals(candidate.InstallPath, info.InstallPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    games.Add(candidate);
                }
            }
        }

        foreach (var folder in GetFolders())
        {
            detection.Folders.Add(folder);
            foreach (var candidate in GameFolderScanner.Scan(folder, "manual"))
                games.Add(candidate);
        }

        // Steam manifests and a folder walk can cover the same directory — a
        // manually copied game that Steam also lists. One row, the manifest's.
        var byPath = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (game.InstallPath == null) continue;
            var existing = byPath.GetValueOrDefault(game.InstallPath);
            if (existing == null || existing.Source == "candidate")
                byPath[game.InstallPath] = game;
        }

        detection.Games.AddRange(
            byPath.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase));

        return detection;
    }

    private static LauncherInfo Probe(LauncherSpec spec)
    {
        foreach (var (key, value) in spec.RegistryProbes)
        {
            var read = ReadRegistryValue(key, value);
            if (string.IsNullOrWhiteSpace(read)) continue;
            var full = read.Replace('/', '\\').TrimEnd('\\');
            if (!Directory.Exists(full) && !File.Exists(full)) continue;

            return new LauncherInfo
            {
                Id = spec.Id,
                Name = spec.Name,
                Detected = true,
                Evidence = $"registry:{key}\\{value}",
                InstallPath = Directory.Exists(full) ? full : Path.GetDirectoryName(full),
            };
        }

        foreach (var path in spec.PathProbes)
        {
            if (!Directory.Exists(path)) continue;
            return new LauncherInfo
            {
                Id = spec.Id,
                Name = spec.Name,
                Detected = true,
                Evidence = $"filesystem:{path}",
                InstallPath = path,
            };
        }

        return new LauncherInfo { Id = spec.Id, Name = spec.Name, Detected = false };
    }

    /// <summary>
    /// Reads a value in the 64-bit view. The 32-bit view is deliberately not
    /// consulted as a fallback: reporting a launcher found only there would be
    /// true of a remnant rather than of something installed.
    /// </summary>
    private static string? ReadRegistryValue(string subKey, string valueName)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
                .OpenSubKey(subKey)
                ?? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            // A key that cannot be read is indistinguishable from one that is
            // not there for this purpose — both mean "not found here".
            return null;
        }
    }

    private static void CollectSteam(string steamPath, List<GameEntry> games)
    {
        // Manifests first — they are the only authoritative claim here, and
        // anything else found later has to defer to them.
        foreach (var game in SteamLibrary.ReadGames(steamPath))
            games.Add(game);

        foreach (var library in SteamLibrary.ReadLibraries(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            var common = Path.Combine(steamapps, "common");
            if (!Directory.Exists(common)) continue;

            foreach (var sub in Directory.EnumerateDirectories(common))
            {
                var named = games.Any(g => string.Equals(g.InstallPath, sub, StringComparison.OrdinalIgnoreCase));
                if (named) continue;
                foreach (var candidate in GameFolderScanner.Scan(sub, "steam"))
                    games.Add(candidate);
            }
        }
    }

    // --- Folders the user added by hand ---

    public IReadOnlyList<string> GetFolders()
        => GamingState.Read<List<string>>(GamingState.FoldersFile) ?? new List<string>();

    /// <summary>False when the path is not a readable directory.</summary>
    public bool AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) return false;

        var folders = GetFolders().ToList();
        if (folders.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase))) return true;

        folders.Add(full);
        GamingState.Write(GamingState.FoldersFile, folders);
        return true;
    }

    /// <summary>False when the path was not in the list.</summary>
    public bool RemoveFolder(string folder)
    {
        var folders = GetFolders();
        var kept = folders
            .Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (kept.Count == folders.Count) return false;

        GamingState.Write(GamingState.FoldersFile, kept);
        return true;
    }
}
