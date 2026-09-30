using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;

namespace WinOpt.Providers.PowerShell;

/// <summary>
/// Provider for PowerShell/script-based tweaks. Executes arbitrary
/// PowerShell commands for detection and application.
/// </summary>
public sealed class PowerShellProvider : ITweakProvider
{
    public string Name => "PowerShell";
    public IReadOnlyList<TweakMethod> SupportedMethods => new[] { TweakMethod.PowerShell, TweakMethod.Script };

    public async Task<DetectionResult> DetectAsync(TweakDefinition tweak)
    {
        var command = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
        if (string.IsNullOrEmpty(command))
            return DetectionResult.Failed(tweak.Id, "Missing command in detection spec.");

        try
        {
            var output = await RunPowerShellAsync(command);

            // Apply regex extraction if specified
            if (!string.IsNullOrEmpty(tweak.Detection.ExtractPattern))
            {
                var match = Regex.Match(output, tweak.Detection.ExtractPattern);
                if (match.Success)
                {
                    var extracted = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    extracted = extracted.Trim();

                    if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                        string.Equals(extracted, tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
                    {
                        return DetectionResult.Success(tweak.Id, TweakState.Applied, extracted);
                    }

                    if (!string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) &&
                        string.Equals(extracted, tweak.Detection.ExpectedDefault, StringComparison.OrdinalIgnoreCase))
                    {
                        return DetectionResult.Success(tweak.Id, TweakState.NotApplied, extracted);
                    }

                    return DetectionResult.Success(tweak.Id, TweakState.PartiallyApplied, extracted);
                }

                return DetectionResult.Failed(tweak.Id, $"Pattern '{tweak.Detection.ExtractPattern}' did not match output.");
            }

            // No regex — compare raw output
            var trimmed = output.Trim();
            if (!string.IsNullOrEmpty(tweak.Detection.ExpectedApplied) &&
                trimmed.Contains(tweak.Detection.ExpectedApplied, StringComparison.OrdinalIgnoreCase))
            {
                return DetectionResult.Success(tweak.Id, TweakState.Applied, trimmed);
            }

            return DetectionResult.Success(tweak.Id, TweakState.NotApplied, trimmed);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed(tweak.Id, ex.Message);
        }
    }

    public async Task<ApplyResult> ApplyAsync(TweakDefinition tweak, SnapshotEntry? previousState = null)
    {
        var command = tweak.Apply.Command ?? tweak.Params.GetValueOrDefault("applyCommand");
        if (string.IsNullOrEmpty(command))
            return ApplyResult.Error(tweak.Id, "Missing command in apply spec.");

        try
        {
            // Capture old state first (best-effort)
            string? oldValue = null;
            var detectCmd = tweak.Detection.Command ?? tweak.Params.GetValueOrDefault("detectCommand");
            if (!string.IsNullOrEmpty(detectCmd))
            {
                try { oldValue = (await RunPowerShellAsync(detectCmd)).Trim(); }
                catch { /* Best effort */ }
            }

            var output = await RunPowerShellAsync(command, strict: true);

            var entry = new SnapshotEntry
            {
                TweakId = tweak.Id,
                Target = tweak.Id,
                OldValue = oldValue,
                NewValue = tweak.TargetValue,
                Method = tweak.Method,
                Command = command,
                RestoreCommand = tweak.Rollback.Command ?? tweak.Params.GetValueOrDefault("rollbackCommand")
            };

            return ApplyResult.Ok(tweak.Id, entry);
        }
        catch (Exception ex)
        {
            // The registry/service providers report elevation separately; do the
            // same here so an unelevated HKLM write is not shown as a broken
            // command but as "needs to run as administrator".
            if (IsElevationProblem(ex.Message))
                return ApplyResult.NeedElevation(tweak.Id);
            return ApplyResult.Error(tweak.Id, ex.Message);
        }
    }

    /// <summary>
    /// Recognise the access-denied wording PowerShell produces for HKLM writes
    /// and other privileged operations.
    /// </summary>
    private static bool IsElevationProblem(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("is denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase)
            || message.Contains("must be an administrator", StringComparison.OrdinalIgnoreCase)
            || message.Contains("run as administrator", StringComparison.OrdinalIgnoreCase)
            || message.Contains("requires elevation", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<RollbackResult> RollbackAsync(SnapshotEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RestoreCommand))
        {
            return new RollbackResult
            {
                TweakId = entry.TweakId,
                Success = false,
                Message = "No rollback command available."
            };
        }

        try
        {
            await RunPowerShellAsync(entry.RestoreCommand, strict: true);
            return new RollbackResult { TweakId = entry.TweakId, Success = true };
        }
        catch (Exception ex)
        {
            return new RollbackResult { TweakId = entry.TweakId, Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> VerifyAsync(TweakDefinition tweak)
    {
        var result = await DetectAsync(tweak);
        return result.State == TweakState.Applied;
    }

    /// <param name="strict">
    /// When true, a non-zero exit from any statement in the command fails the run.
    /// Apply and rollback use this so a rejected powercfg/netsh argument is
    /// reported instead of being recorded as a successful change. Detection stays
    /// lenient: probes are allowed to fail (missing files, access denied) and
    /// would otherwise report errors that <c>-ErrorAction SilentlyContinue</c>
    /// deliberately swallows.
    /// </param>
    private static async Task<string> RunPowerShellAsync(string command, bool strict = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Pass the command as a single argv entry rather than embedding it in
        // -Command. Tweak commands contain quotes and braces (script blocks, e.g.
        // `Where-Object {...}`) that the nested escaping mangled.
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(strict ? WithExitGuards(command) : command);

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start PowerShell process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        // A single `exit $LASTEXITCODE` is not enough: PowerShell's own exit code
        // is the last statement's, so `a; b; c` that fails on `a` but ends on a
        // success still reports 0. In strict mode the guard marks any failing
        // statement; in lenient mode only the process exit code is consulted, as
        // before.
        var failures = strict ? ReadGuardCode(stdout) : 0;
        if (process.ExitCode != 0 || failures != 0)
        {
            var cleanStdout = (strict ? RemoveGuardLines(stdout) : stdout).Trim();
            var detail = string.IsNullOrWhiteSpace(stderr) ? cleanStdout : stderr.Trim();
            var code = process.ExitCode != 0 ? process.ExitCode : failures;
            throw new InvalidOperationException(
                $"PowerShell command failed (exit {code}): {detail}");
        }

        // Strip the guard's own output so only command output remains.
        return strict ? RemoveGuardLines(stdout) : stdout;
    }

    /// <summary>
    /// Wrap each semicolon-separated statement so a non-zero exit from any of them
    /// is recorded, and append an exit that reports it.
    /// </summary>
    private const string GuardVariable = "$__winoptFailure";

    private static string WithExitGuards(string command)
    {
        var builder = new StringBuilder();
        // Start $LASTEXITCODE from 0: when it is unset it reads as $null, and
        // $null -ne 0 is true, which would fail every command that never called a
        // native tool.
        builder.Append(GuardVariable).Append(" = 0; $global:LASTEXITCODE = 0; ");
        foreach (var statement in SplitStatements(command))
            builder.Append(statement).Append($"; if ($LASTEXITCODE -ne 0) {{ {GuardVariable} = 1 }}; ");
        // $LASTEXITCODE only reflects native tools, so a cmdlet that throws would
        // still look successful; catch those too.
        builder.Append($"trap {{ {GuardVariable} = 1; break }}; ");
        builder.Append($"Write-Output (\"{GuardMarker}{{0}}\" -f {GuardVariable}); ");
        builder.Append($"exit {GuardVariable}");
        return builder.ToString();
    }

    /// <summary>
    /// Split on semicolons that are not inside quotes, brackets or parentheses.
    /// </summary>
    private static IEnumerable<string> SplitStatements(string command)
    {
        var current = new StringBuilder();
        var quote = '\0';
        var depth = 0;

        foreach (var ch in command)
        {
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
            }
            else if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch is '{' or '(' or '[')
            {
                depth++;
            }
            else if (ch is '}' or ')' or ']')
            {
                depth--;
            }
            else if (ch == ';' && depth == 0)
            {
                if (!string.IsNullOrWhiteSpace(current.ToString()))
                    yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (!string.IsNullOrWhiteSpace(current.ToString()))
            yield return current.ToString();
    }

    private const string GuardMarker = "__winopt-failure=";

    private static int ReadGuardCode(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(GuardMarker, StringComparison.Ordinal) &&
                int.TryParse(trimmed.AsSpan(GuardMarker.Length), out var code))
            {
                return code;
            }
        }
        return 0;
    }

    private static string RemoveGuardLines(string stdout)
    {
        var kept = stdout.Split('\n')
            .Where(l => !l.Trim().StartsWith(GuardMarker, StringComparison.Ordinal));
        return string.Join('\n', kept);
    }
}
