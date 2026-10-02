using WinOpt.Core.Models;
using WinOpt.Engine.Logging;

namespace WinOpt.Engine.Security;

/// <summary>
/// Reads and changes Windows Defender exclusion paths, with the gates the
/// operation earns: this is the one feature that turns a scanner off for a
/// folder, so it is never done quietly.
///
/// The read path exists largely because of a single string. Unprivileged,
/// <c>Get-MpPreference</c> does not error and does not return an empty list —
/// it returns the sentence <c>N/A: Must be an administrator to view
/// exclusions</c>. Read as a list, that sentence becomes one exclusion path and
/// the UI offers to remove a folder that was never excluded.
/// </summary>
public sealed class DefenderExclusions
{
    /// <summary>What an unprivileged read returns instead of the exclusion list.</summary>
    public const string ElevationNotice = "Must be an administrator";

    private readonly WinOptLogger? _logger;

    public DefenderExclusions(WinOptLogger? logger = null) => _logger = logger;

    public async Task<DefenderExclusionState> ListAsync()
    {
        var paths = await QueryAsync("ExclusionPath");
        var processes = await QueryAsync("ExclusionProcess");

        if (paths.Error != null)
            return new DefenderExclusionState
            {
                Readable = false,
                ElevationRequired = paths.ElevationRequired,
                Message = paths.Error,
            };

        return new DefenderExclusionState
        {
            Paths = paths.Values,
            Processes = processes.Values,
            Readable = true,
            ElevationRequired = paths.ElevationRequired || processes.ElevationRequired,
            Message = paths.ElevationRequired
                ? "Reading exclusions requires administrator rights."
                : null,
        };
    }

    public async Task<ExclusionChange> AddAsync(string path, bool confirm)
    {
        var before = await ListAsync();
        var action = "add";

        var failure = await GateAsync(path, confirm, before, removing: false);
        if (failure != null)
            return new ExclusionChange { Action = action, Path = path, Success = false, Message = failure, Before = before.Paths };

        var result = await RunMpAsync($"Add-MpPreference -ExclusionPath '{Escape(path)}'");
        if (!result.Ok)
        {
            Audit(action, path, success: false, error: result.Error);
            return new ExclusionChange
            {
                Action = action, Path = path, Success = false,
                Message = result.Error, Before = before.Paths,
            };
        }

        var after = await ListAsync();
        var alreadyThere = before.Paths.Contains(path, StringComparer.OrdinalIgnoreCase);
        Audit(action, path, success: true);

        return new ExclusionChange
        {
            Action = action,
            Path = path,
            Success = true,
            Unchanged = alreadyThere,
            Message = alreadyThere
                ? "That path was already excluded — nothing was added."
                : "Excluded. Defender will not scan this path.",
            Before = before.Paths,
            After = after.Paths,
        };
    }

    public async Task<ExclusionChange> RemoveAsync(string path)
    {
        var before = await ListAsync();
        var action = "remove";

        if (before.ElevationRequired || !before.Readable)
        {
            var reason = before.Message ?? "Reading exclusions requires administrator rights.";
            Audit(action, path, success: false, error: reason);
            return new ExclusionChange { Action = action, Path = path, Success = false, Message = reason, Before = before.Paths };
        }

        if (!before.Paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return new ExclusionChange
            {
                Action = action, Path = path, Success = true, Unchanged = true,
                Message = "That path is not excluded — nothing to remove.",
                Before = before.Paths, After = before.Paths,
            };
        }

        var result = await RunMpAsync($"Remove-MpPreference -ExclusionPath '{Escape(path)}'");
        if (!result.Ok)
        {
            Audit(action, path, success: false, error: result.Error);
            return new ExclusionChange
            {
                Action = action, Path = path, Success = false,
                Message = result.Error, Before = before.Paths,
            };
        }

        var after = await ListAsync();
        Audit(action, path, success: true);

        return new ExclusionChange
        {
            Action = action, Path = path, Success = true,
            Message = "Removed. Defender scans this path again.",
            Before = before.Paths,
            After = after.Paths,
        };
    }

    /// <summary>
    /// Writes the current list to a file one path per line — the format
    /// <c>Add-MpPreference -ExclusionPath (Get-Content …)</c> reads back, so the
    /// export is the rollback rather than a record of one.
    /// </summary>
    public async Task<string> ExportAsync(string? outputPath)
    {
        var state = await ListAsync();
        if (!state.Readable)
            throw new InvalidOperationException(state.Message ?? "Exclusions cannot be read.");

        var file = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinOpt", $"defender-exclusions-{DateTime.Now:yyyyMMdd-HHmmss}.txt")
            : Path.GetFullPath(outputPath);

        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(file, state.Paths);
        return file;
    }

    // --- Gates ---

    private async Task<string?> GateAsync(string path, bool confirm, DefenderExclusionState state, bool removing)
    {
        // 1. Elevation — before anything else, because every later gate needs a
        //    path Defender will accept, and an unelevated call cannot read what
        //    it would be comparing against.
        if (state.ElevationRequired || !state.Readable)
            return state.Message ?? "This requires administrator rights. Re-run as administrator.";

        if (removing) return null;

        // 2. Confirmation. The caller has to have seen the path.
        if (!confirm)
            return "Confirmation required. The resolved path this would exclude is shown so it can be checked first.";

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { return $"'{path}' is not a usable path: {ex.Message}"; }

        // 3. Existence — excluding a folder that is not there excludes nothing
        //    and usually means the path was mistyped.
        if (!Directory.Exists(full) && !File.Exists(full))
            return $"Nothing exists at {full}.";

        // 4. Path validation.
        var (allowed, reason) = ValidatePath(full);
        if (!allowed) return reason;

        return null;
    }

    /// <summary>
    /// Where an exclusion may never point. Game libraries are the intended use
    /// and are not named as preferred roots — a user's own <c>D:\Games</c> has
    /// no reason to be second-class — but system locations are refused outright.
    /// </summary>
    public static (bool Allowed, string Reason) ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "No path was given.");

        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) { return (false, $"'{path}' is not a usable path: {ex.Message}"); }

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return (false, "Network and UNC paths cannot be excluded — Defender policy does not apply to them.");

        var root = Path.GetPathRoot(full);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return (false, "An entire drive cannot be excluded.");

        var blocked = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var candidate in blocked)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (IsUnder(full, normalized))
                return (false, $"'{full}' is inside '{normalized}', which cannot be excluded — " +
                               "excluding it would stop Defender from watching system or user files.");
        }

        return (true, string.Empty);
    }

    private static bool IsUnder(string candidate, string root)
        => string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
           || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // --- PowerShell plumbing ---

    private sealed record QueryResult(List<string> Values, bool ElevationRequired, string? Error);

    private async Task<QueryResult> QueryAsync(string property)
    {
        var result = await RunPowerShellAsync(
            $"try {{ (Get-MpPreference -ErrorAction Stop).{property} }} catch {{ $_.Exception.Message }}");

        if (!result.Ok)
            return new QueryResult(new List<string>(), false, result.Error);

        var lines = result.Output
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Any(l => l.Contains(ElevationNotice, StringComparison.OrdinalIgnoreCase)))
            return new QueryResult(new List<string>(), true,
                "Reading Defender exclusions requires administrator rights.");

        if (lines.Any(l => l.Contains("Get-MpPreference", StringComparison.OrdinalIgnoreCase)
                           || l.Contains("not recognized", StringComparison.OrdinalIgnoreCase)))
            return new QueryResult(new List<string>(), false,
                "Defender's PowerShell module (ConfigDefender) is not available: " + string.Join(" ", lines));

        return new QueryResult(lines, false, null);
    }

    private sealed record RunResult(bool Ok, string Output, string? Error);

    private async Task<RunResult> RunMpAsync(string command)
        => await RunPowerShellAsync($"{command}; if (-not $?) {{ exit 1 }}");

    private async Task<RunResult> RunPowerShellAsync(string command)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command", command,
            }, TimeSpan.FromSeconds(45));

            if (!result.Success)
                return new RunResult(false, result.Output,
                    string.IsNullOrWhiteSpace(result.StdErr) ? result.Output.Trim() : result.StdErr.Trim());

            return new RunResult(true, result.StdOut, null);
        }
        catch (Exception ex)
        {
            return new RunResult(false, string.Empty, ex.Message);
        }
    }

    private void Audit(string action, string path, bool success, string? error = null)
    {
        _logger?.AuditFeature(
            featureId: "defender:exclusion",
            operation: action,
            target: path,
            oldValue: null,
            newValue: action == "add" ? path : null,
            result: success ? "success" : "failure",
            error: error);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
