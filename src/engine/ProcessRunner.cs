using System.Diagnostics;
using System.Text;

namespace WinOpt.Engine;

/// <summary>Outcome of one external command.</summary>
public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;

    public bool Success => ExitCode == 0;

    /// <summary>stdout, else stderr, else a line saying the command failed.</summary>
    public string Output => !string.IsNullOrWhiteSpace(StdOut)
        ? StdOut
        : !string.IsNullOrWhiteSpace(StdErr)
            ? StdErr
            : $"Command exited with code {ExitCode} and produced no output.";
}

/// <summary>
/// Runs an external command and captures what it said.
///
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/> rather than a
/// concatenated <c>Arguments</c> string, because the commands used here contain
/// spaces, quotes and paths, and hand-built command lines are where quoting bugs
/// turn a path into a different command.
/// </summary>
public static class ProcessRunner
{
    public static async Task<CommandResult> RunAsync(string fileName,
        IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"{fileName} did not finish within {(timeout ?? TimeSpan.FromSeconds(60)).TotalSeconds:0}s.");
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = await stdoutTask,
            StdErr = await stderrTask,
        };
    }

    /// <summary>Runs the command and throws with its output when the exit code is non-zero.</summary>
    public static async Task<CommandResult> RunStrictAsync(string fileName,
        IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var result = await RunAsync(fileName, arguments, timeout);
        if (!result.Success)
            throw new InvalidOperationException($"{fileName} failed (exit {result.ExitCode}): {result.Output.Trim()}");
        return result;
    }
}
