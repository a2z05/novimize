using WinOpt.Engine;

namespace WinOpt.Engine.Installer;

/// <summary>One row of the Start menu: the name a person sees and the AUMID underneath it.</summary>
public sealed record StartEntry(string Name, string AppId);

/// <summary>What a launch attempt did, including which Start entry it settled on.</summary>
public sealed record LaunchResult(bool Success, string Message, string? MatchedName, string? MatchedAppId);

/// <summary>
/// Opening a program that has no window of its own to click.
///
/// The catalogue knows a tool by the name we display for it, which is not
/// necessarily the name the Start menu filed it under — PowerToys is listed as
/// "PowerToys Settings", and a versioned tool is often filed as "Fences 6"
/// while the card says "Fences". So a launch reads the Start menu, resolves the
/// catalogue name against it, and reports which entry it chose instead of
/// pretending the first guess was the right one.
/// </summary>
public static class StartMenu
{
    private const string Script = "Get-StartApps | ForEach-Object { $_.AppID + [char]9 + $_.Name }";

    /// <summary>Every app the Start menu knows about, straight from the shell.</summary>
    public static async Task<List<StartEntry>> ListAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command", Script,
            }, TimeSpan.FromSeconds(30));

            if (!result.Success) return new List<StartEntry>();
            return Parse(result.StdOut);
        }
        catch
        {
            // A shell that will not start is not a reason to crash the page.
            return new List<StartEntry>();
        }
    }

    /// <summary>
    /// Turns the tab-separated output of <see cref="Script"/> into entries.
    /// Split on the first tab only: a display name may contain one, an AppID may not.
    /// </summary>
    public static List<StartEntry> Parse(string output)
    {
        var entries = new List<StartEntry>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;
            var tab = trimmed.IndexOf('\t');
            if (tab <= 0) continue;
            var appId = trimmed[..tab].Trim();
            var name = trimmed[(tab + 1)..].Trim();
            if (appId.Length == 0 || name.Length == 0) continue;
            entries.Add(new StartEntry(name, appId));
        }
        return entries;
    }

    /// <summary>
    /// The best Start entry for a candidate name, or null.
    ///
    /// Exact first, then prefix, then substring — in that order and never the
    /// other way round, because a prefix match on "Fences" would happily return
    /// "Fences 6" while an exact match for a differently-filed tool sits unused.
    /// Among equals the shortest name wins: when a shortcut and its features
    /// share a prefix, the shorter is normally the application. The rule can be
    /// wrong for a tool that ships no base shortcut at all, but it is stable,
    /// and a stable answer is one the card can explain.
    /// </summary>
    public static StartEntry? Resolve(IEnumerable<StartEntry> entries, IEnumerable<string> candidates)
    {
        var all = entries.ToList();
        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var match = Best(all, raw.Trim());
            if (match is not null) return match;
        }
        return null;
    }

    private static StartEntry? Best(IEnumerable<StartEntry> entries, string candidate)
    {
        var list = entries.ToList();
        return Shortest(list.Where(e => string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            ?? Shortest(list.Where(e => e.Name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
            ?? Shortest(list.Where(e => e.Name.Contains(candidate, StringComparison.OrdinalIgnoreCase)));
    }

    private static StartEntry? Shortest(IEnumerable<StartEntry> matches)
        => matches.OrderBy(e => e.Name.Length).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    /// <summary>Resolve a name and open it, or say why nothing was opened.</summary>
    public static async Task<LaunchResult> LaunchAsync(params string[] candidates)
    {
        var wanted = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
        if (wanted is null)
            return new LaunchResult(false, "No name was given to look for in the Start menu.", null, null);

        var entries = await ListAsync();
        if (entries.Count == 0)
            return new LaunchResult(false, "The Start menu could not be read, so nothing was opened.", null, null);

        var match = Resolve(entries, candidates);
        if (match is null)
            return new LaunchResult(false, $"Not found in the Start menu under '{wanted}'.", null, null);

        try
        {
            // The documented way to open a Start entry: the shell's own
            // AppsFolder takes the AUMID, and explorer resolves it. The AppID
            // came from the shell a moment ago, so it is one it will accept.
            await ProcessRunner.RunAsync("explorer.exe", new[]
            {
                $"shell:AppsFolder\\{match.AppId}",
            }, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, $"Could not start {match.Name}: {ex.Message}", match.Name, match.AppId);
        }

        return new LaunchResult(true, $"Opened {match.Name}.", match.Name, match.AppId);
    }
}
