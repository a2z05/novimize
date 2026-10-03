using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace WinOpt.Engine;

/// <summary>
/// Running things as administrator, without lying about it.
///
/// Two questions matter and they are different: <see cref="IsElevated"/> asks
/// whether this process already is, and <see cref="RunSelfElevatedAsync{T}"/>
/// asks the user, once, and reports back what the elevated run actually did.
/// Declining the prompt is not an error — it is an answer, and it comes back
/// as <c>null</c> rather than as an exception, so every caller has to say what
/// nothing-happened means in its own words.
///
/// The elevated process is this same executable with the same arguments, which
/// keeps one implementation of every operation instead of a second "admin"
/// variant that can drift from the first.
/// </summary>
public static class Elevation
{
    /// <summary>ERROR_CANCELLED: the user dismissed the UAC prompt.</summary>
    private const int Cancelled = 1223;

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Re-run this executable with the given arguments under UAC and parse the
    /// JSON it printed. Returns null when permission was declined, when the
    /// elevated run produced nothing, or when this process is already elevated
    /// — the last case meaning the caller handed back work it could have done
    /// itself, which the caller is expected to have checked first.
    /// </summary>
    public static async Task<T?> RunSelfElevatedAsync<T>(
        IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        if (IsElevated()) return default;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return default;

        // Under `dotnet app.dll`, ProcessPath is dotnet itself. Elevating that
        // with the app's arguments would ask dotnet to open a file called
        // "blocker", which fails after the prompt has already been shown — so
        // the assembly has to ride along as the first argument.
        var prefix = new List<string> { executable };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(assembly)) prefix.Add(assembly);
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"novimize-elev-{Guid.NewGuid():N}.log");
        var line = string.Join(' ', prefix.Concat(arguments).Select(Quote)) + $" > {Quote(logPath)} 2>&1";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + line,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start the elevated command.");

            using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return default;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return default;
        }
        catch
        {
            return default;
        }

        try
        {
            if (!File.Exists(logPath)) return default;
            var text = File.ReadAllText(logPath);
            var start = text.IndexOf('{');
            if (start < 0) return default;
            return JsonSerializer.Deserialize<T>(text[start..], Json);
        }
        catch
        {
            return default;
        }
        finally
        {
            try { File.Delete(logPath); } catch { /* temp file */ }
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Quote an argument for cmd.exe. Arguments reaching here are literals this
    /// code wrote plus ids that have already been through the id checks, so
    /// there is nothing in them that cmd would read as syntax — the quotes are
    /// there for spaces in file paths, which is the one thing that does break.
    /// </summary>
    private static string Quote(string value)
    {
        if (value.Length == 0) return "\"\"";
        return value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? value : $"\"{value.Replace("\"", "'")}\"";
    }
}
