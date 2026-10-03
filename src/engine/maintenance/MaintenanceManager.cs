using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Maintenance;

/// <summary>
/// The one-click maintenance actions, each with the size it would free and
/// the command it would run — shown before it runs.
///
/// Two kinds of action live here and they are not the same thing. Clearing a
/// cache deletes files Windows regenerates; repairing with DISM or SFC
/// rewrites system files. The second kind carries <c>Repairs</c> and gets a
/// louder confirmation, and neither is ever run without the exact command
/// line being shown first.
///
/// Sizes are measured when they can be measured and reported as unknown when
/// they cannot, rather than as zero — an empty-looking number reads as
/// "nothing to gain here" and stops the user looking further.
/// </summary>
public sealed class MaintenanceManager
{
    /// <summary>
    /// A command as an executable plus its own argv. Not a command line that
    /// gets split: several of these contain quoted PowerShell with spaces in
    /// the paths, and splitting one by whitespace turns it into a different
    /// command than the one written here.
    /// </summary>
    private sealed record Cmd(string FileName, string[] Args)
    {
        public string Display => FileName + " " + string.Join(" ", Args.Select(a =>
            a.Contains(' ') || a.Contains('"') ? $"\"{a}\"" : a));
    }

    private sealed record ToolSpec(
        string Id,
        string Name,
        string What,
        string? Deletes,
        string Effort,
        bool RestartRequired,
        bool Repairs,
        Cmd[] Commands);

    private static readonly string[] Ps = { "-NoProfile", "-NonInteractive", "-Command" };

    private static Cmd P(string script) => new("powershell.exe", Ps.Append(script).ToArray());

    private static readonly ToolSpec[] Specs =
    {
        new("temp", "Temporary files",
            "Files programs left in the user temporary folder. Windows and the programs that own them recreate what they still need.",
            @"%TEMP% — everything your account can write there",
            "seconds", false, false,
            new[] { P(@"Remove-Item -LiteralPath ([System.IO.Path]::GetTempPath())\* -Recurse -Force -ErrorAction SilentlyContinue") }),

        new("recycle", "Recycle Bin",
            "Empties the bin. Anything in it stops being recoverable.",
            "everything currently in the Recycle Bin",
            "seconds", false, false,
            new[] { P("Clear-RecycleBin -Force") }),

        new("thumbnails", "Thumbnail cache",
            "The pictures Windows keeps of your files. Explorer regenerates them the next time a folder is opened.",
            @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db",
            "seconds", false, false,
            new[] { P(@"Remove-Item -Path ""$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db"" -Force -ErrorAction SilentlyContinue") }),

        new("icons", "Icon cache",
            "Asks Windows to repaint every icon from its own source rather than from a stale cache. Nothing is deleted.",
            null,
            "seconds", false, false,
            new[] { new Cmd("ie4uinit.exe", new[] { "-show" }) }),

        new("updateCache", "Windows Update download cache",
            "Stops the update service, clears what it already downloaded, and starts it again. Installed updates are not touched.",
            @"%WINDIR%\SoftwareDistribution\Download",
            "under a minute", false, false,
            new[]
            {
                P("Stop-Service -Name wuauserv -Force"),
                P(@"Remove-Item -LiteralPath ""$env:WINDIR\SoftwareDistribution\Download\*"" -Recurse -Force -ErrorAction SilentlyContinue"),
                P("Start-Service -Name wuauserv"),
            }),

        new("searchIndex", "Windows Search index",
            "Stops the indexer, empties its database, and starts it back. Search stays empty until it finishes rebuilding — on a large disk that takes hours.",
            @"%LOCALAPPDATA%\Microsoft\Windows\Search\Data",
            "the index rebuilds in the background afterwards", true, false,
            new[]
            {
                P("Stop-Service -Name WSearch -Force"),
                P(@"Remove-Item -LiteralPath ""$env:LOCALAPPDATA\Microsoft\Windows\Search\Data\*"" -Recurse -Force -ErrorAction SilentlyContinue"),
                P("Start-Service -Name WSearch"),
            }),

        new("componentStore", "Component store cleanup",
            "DISM removes superseded Windows components it no longer needs. It does not use /ResetBase, so updates can still be uninstalled.",
            "superseded component files the servicing stack has replaced",
            "five to twenty minutes", false, false,
            new[] { new Cmd("DISM.exe", new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup" }) }),

        new("healthCheck", "DISM health check",
            "Asks the servicing stack whether the image is intact. Nothing is deleted and nothing is repaired.",
            null,
            "a few minutes", false, false,
            new[] { new Cmd("DISM.exe", new[] { "/Online", "/Cleanup-Image", "/ScanHealth" }) }),

        new("restoreHealth", "DISM restore health",
            "Repairs a damaged component store from Windows Update. This rewrites system files.",
            null,
            "ten to thirty minutes", false, true,
            new[] { new Cmd("DISM.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }) }),

        new("sfc", "System File Checker",
            "Scans every protected system file and replaces the ones that are wrong. This rewrites system files.",
            null,
            "ten to fifteen minutes", false, true,
            new[] { new Cmd("sfc.exe", new[] { "/scannow" }) }),

        new("diskCleanup", "Disk Cleanup",
            "Opens Windows' own Disk Cleanup so you choose what goes. Novimize does not pick for you.",
            "whatever you tick in the dialog",
            "however long you take", false, false,
            new[] { new Cmd("cleanmgr.exe", Array.Empty<string>()) }),
    };

    private readonly string _stateDirectory;

    public MaintenanceManager(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOpt", "maintenance");
    }

    public async Task<MaintenanceStatus> ReadAsync(CancellationToken cancel = default)
    {
        var raw = await RunAsync(new { op = "read" }, cancel);
        var read = raw is null ? null : JsonSerializer.Deserialize<ReadResult>(raw, Raw);

        var sizes = read?.Sizes ?? new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);

        var tools = Specs.Select(spec => new MaintenanceTool
        {
            Id = spec.Id,
            Name = spec.Name,
            What = spec.What,
            Deletes = spec.Deletes,
            Bytes = sizes.TryGetValue(spec.Id, out var bytes) ? bytes : null,
            MeasuredNote = spec.Deletes is null
                ? null
                : sizes.ContainsKey(spec.Id)
                    ? null
                    : "the size could not be measured here",
            Effort = spec.Effort,
            RestartRequired = spec.RestartRequired,
            Repairs = spec.Repairs,
            Available = true,
        }).ToList();

        // DNS flushing already has a home in the Network section; saying so
        // beats offering the same command twice under two names.
        tools.Add(new MaintenanceTool
        {
            Id = "dnsFlush",
            Name = "DNS resolver cache",
            What = "Already available as `dns flush` in the Network section, where the resolver it " +
                   "affects is shown next to it.",
            Deletes = null,
            Effort = "instant",
            Available = false,
            UnavailableReason = "use `dns flush` from Network",
        });

        return new MaintenanceStatus
        {
            Tools = tools,
            ReclaimableBytes = tools
                .Where(t => t.Id != "searchIndex" && t.Bytes is not null)
                .Select(t => t.Bytes)
                .Sum(),
            RepairCount = tools.Count(t => t.Repairs),
            RestartCount = tools.Count(t => t.RestartRequired),
            Error = read?.Error,
        };
    }

    /// <summary>What this action would run, without running it.</summary>
    private static List<string> Show(Cmd[] commands) =>
        commands.Select(c => c.Display).ToList();

    public MaintenanceChange Preview(string id)
    {
        var spec = Specs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return Fail("preview", $"'{id}' is not a maintenance action Novimize knows about.");

        return new MaintenanceChange
        {
            Action = id,
            Success = true,
            Unchanged = true,
            Affected = spec.Commands.Length,
            // The deletion sentence lives here, not only on the refusal: the
            // dialog is where somebody decides whether to say yes, and a
            // field that is blank reads as an unanswered question.
            Message = (spec.Repairs ? $"{spec.Name} rewrites system files: {spec.What}" : $"{spec.Name}: {spec.What}")
                      + (spec.Deletes is null ? " Nothing is deleted." : $" This deletes: {spec.Deletes}."),
            Preview = Show(spec.Commands),
            RestartRequired = spec.RestartRequired,
        };
    }

    public async Task<MaintenanceChange> RunAsync(string id, bool confirm, CancellationToken cancel = default)
    {
        var spec = Specs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return Fail(id, $"'{id}' is not a maintenance action Novimize knows about.");

        var preview = Preview(id);

        if (!confirm)
            return preview with
            {
                Success = false,
                Message = preview.Message + " Nothing has been run.",
            };

        if (spec.Repairs && !Elevation.IsElevated())
            return preview with
            {
                Success = false,
                NeedsElevation = true,
                Message = $"{spec.Name} repairs system files, which needs administrator rights. " +
                          "Nothing was changed.",
            };

        var before = await ReadAsync(cancel);

        Directory.CreateDirectory(_stateDirectory);
        var logPath = Path.Combine(_stateDirectory, $"{spec.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new List<string>();
        var failed = new List<string>();

        foreach (var command in spec.Commands)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                var timeout = spec.Repairs || spec.Id is "restoreHealth" or "componentStore"
                    ? TimeSpan.FromMinutes(45)
                    : TimeSpan.FromMinutes(5);

                var result = await ProcessRunner.RunAsync(command.FileName, command.Args, timeout);
                log.Add($"$ {command.Display}\n{result.Output.Trim()}");

                // DISM and sfc report failure in their own prose rather than in
                // an exit code, so a non-zero code alone is not the whole story;
                // what is looked for is an explicit failure line.
                var output = result.Output;
                var failedInProse = output.Contains("Error:", StringComparison.OrdinalIgnoreCase)
                                    || output.Contains("failed", StringComparison.OrdinalIgnoreCase)
                                    || (!result.Success && output.Trim().Length == 0);

                if (failedInProse) failed.Add(command.Display);
            }
            catch (Exception ex)
            {
                failed.Add($"{command.Display} — {ex.Message}");
            }
        }

        var text = string.Join("\n\n", log);
        try { await File.WriteAllTextAsync(logPath, text, cancel); } catch { /* a missing log is not a failed action */ }

        // Sizes are stale the moment something is deleted; read them again so
        // the number the page shows next is the one that is now true, and so
        // the report can say how much actually went.
        var after = await ReadAsync(cancel);
        var freed = BytesOf(before, spec.Id) is long was && BytesOf(after, spec.Id) is long now
            ? Math.Max(0, was - now)
            : (long?)null;

        return new MaintenanceChange
        {
            Action = spec.Id,
            Success = failed.Count == 0,
            Affected = spec.Commands.Length - failed.Count,
            Message = failed.Count == 0
                ? $"{spec.Name} finished."
                  + (freed is long amount ? $" {Format(amount)} freed." : "")
                  + (spec.RestartRequired ? " A restart finishes this." : "")
                : $"{failed.Count} step(s) did not report success: {string.Join("; ", failed)}",
            Preview = Show(spec.Commands),
            RestartRequired = spec.RestartRequired,
            Log = $"log: {logPath}\n{text}",
            Freed = freed,
        };
    }

    private static long? BytesOf(MaintenanceStatus status, string id) =>
        status.Tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Bytes;

    /// <summary>Bytes as a person would say them, used in the result line.</summary>
    public static string Format(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.#} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.#} kB";
        return $"{bytes} B";
    }

    private static MaintenanceChange Fail(string action, string message) => new()
    {
        Action = action,
        Success = false,
        Message = message,
    };

    // --- PowerShell ---------------------------------------------------------

    private static readonly JsonSerializerOptions Raw = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<string?> RunAsync(object payload, CancellationToken cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"novimize-maint-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(directory, "payload.json");
        var scriptPath = Path.Combine(directory, "maint.ps1");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(payloadPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                cancel);
            await File.WriteAllTextAsync(scriptPath, Script, cancel);

            var result = await ProcessRunner.RunAsync("powershell.exe", new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", scriptPath, "-Payload", payloadPath,
            }, TimeSpan.FromSeconds(90));

            var start = result.StdOut.IndexOf('{');
            return start < 0 ? null : result.StdOut[start..];
        }
        catch { return null; }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// Sizes only. Every measurement is bounded — a temporary folder with
    /// three hundred thousand files is not worth a page load — and a folder
    /// that cannot be read is simply absent from the answer rather than zero.
    /// </summary>
    private const string Script = """
        param([string]$Payload)
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $null = Get-Content -LiteralPath $Payload -Raw -Encoding UTF8

        function Reply([object]$body) { $body | ConvertTo-Json -Depth 8 -Compress }

        function DirSize([string]$path, [int]$cap) {
            if (-not $path -or -not (Test-Path -LiteralPath $path)) { return $null }
            try {
                $files = @(Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue |
                           Select-Object -First $cap)
                if ($files.Count -eq 0) { return [long]0 }
                $sum = ($files | Measure-Object -Property Length -Sum).Sum
                if ($null -eq $sum) { return [long]0 }
                return [long]$sum
            } catch { return $null }
        }

        function GlobSize([string]$directory, [string]$pattern) {
            if (-not $directory -or -not (Test-Path -LiteralPath $directory)) { return $null }
            try {
                $files = @(Get-ChildItem -LiteralPath $directory -Filter $pattern -Force -File -ErrorAction SilentlyContinue)
                if ($files.Count -eq 0) { return [long]0 }
                $sum = ($files | Measure-Object -Property Length -Sum).Sum
                if ($null -eq $sum) { return [long]0 }
                return [long]$sum
            } catch { return $null }
        }

        function RecycleSize {
            try {
                $shell = New-Object -ComObject Shell.Application
                $bin = $shell.Namespace(0xA)
                if (-not $bin) { return $null }
                $total = [long]0
                $items = @($bin.Items())
                foreach ($item in $items) {
                    $size = $bin.GetDetailsOf($item, 2)
                    if ($size) {
                        $parsed = [long]0
                        if ([long]::TryParse(($size -replace '[^\d]',''), [ref]$parsed)) { $total += $parsed }
                    }
                }
                return $total
            } catch { return $null }
        }

        try {
            $sizes = @{}

            $sizes['temp'] = DirSize ([System.IO.Path]::GetTempPath()) 20000
            $sizes['recycle'] = RecycleSize
            $sizes['thumbnails'] = GlobSize (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer') 'thumbcache_*.db'
            $sizes['icons'] = GlobSize $env:LOCALAPPDATA 'IconCache.db'
            $updateCache = Join-Path $env:WINDIR 'SoftwareDistribution\Download'
            $sizes['updateCache'] = DirSize $updateCache 20000
            $sizes['searchIndex'] = DirSize (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Search\Data') 20000

            Reply @{ sizes = $sizes; error = $null }
        } catch {
            Reply @{ sizes = @{}; error = [string]$_.Exception.Message }
        }
        """;

    private sealed class ReadResult
    {
        public Dictionary<string, long?>? Sizes { get; init; }
        public string? Error { get; init; }
    }
}
