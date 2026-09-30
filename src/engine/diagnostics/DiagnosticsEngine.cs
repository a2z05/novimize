using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using WinOpt.Core.Models;
using WinOpt.Engine.Detection;
using WinOpt.Engine.Logging;

namespace WinOpt.Engine.Diagnostics;

/// <summary>
/// Comprehensive system diagnostics: health checks, network diagnostics,
/// startup analysis, and benchmarking.
/// </summary>
public sealed class DiagnosticsEngine
{
    private readonly SystemDetector _detector;
    private readonly WinOptLogger _logger;

    public DiagnosticsEngine(SystemDetector detector, WinOptLogger logger)
    {
        _detector = detector;
        _logger = logger;
    }

    /// <summary>
    /// Run full system health check.
    /// </summary>
    public async Task<HealthReport> HealthCheckAsync()
    {
        var systemInfo = await _detector.DetectAsync();
        var checks = new List<HealthCheck>();

        // Critical services
        checks.Add(await CheckServiceAsync("WinDefend", "Windows Defender", true));
        checks.Add(await CheckServiceAsync("MpsSvc", "Windows Firewall", true));
        checks.Add(await CheckServiceAsync("BFE", "Base Filtering Engine", true));
        checks.Add(await CheckServiceAsync("wuauserv", "Windows Update", true));
        checks.Add(await CheckServiceAsync("RpcSs", "RPC", true));
        checks.Add(await CheckServiceAsync("DcomLaunch", "DCOM Launcher", true));
        checks.Add(await CheckServiceAsync("CryptSvc", "Cryptographic Services", true));
        checks.Add(await CheckServiceAsync("EventLog", "Event Log", true));

        // Disk health
        checks.Add(await CheckDiskHealthAsync());

        // Memory
        checks.Add(await CheckMemoryAsync(systemInfo));

        // Security
        checks.Add(CheckSecurityStatus());

        // Uptime
        checks.Add(await CheckUptimeAsync());

        var report = new HealthReport
        {
            SystemInfo = systemInfo,
            Checks = checks,
            OverallStatus = checks.All(c => c.Status == HealthStatus.Ok) ? HealthStatus.Ok :
                           checks.Any(c => c.Status == HealthStatus.Critical) ? HealthStatus.Critical :
                           HealthStatus.Warning
        };

        _logger.Info($"Health check: {report.OverallStatus} ({checks.Count(c => c.Status == HealthStatus.Ok)}/{checks.Count} passed)", "diagnostic");

        return report;
    }

    /// <summary>
    /// Run network diagnostics.
    /// </summary>
    public async Task<NetworkReport> NetworkDiagnosticsAsync(string target = "8.8.8.8")
    {
        var report = new NetworkReport
        {
            Target = target,
            AdapterInfo = await GetNetworkAdaptersAsync(),
            PingResults = await PingAsync(target, 10),
            DnsResolution = await DnsResolveAsync("example.com"),
            TcpSettings = await GetTcpSettingsAsync()
        };

        _logger.Info($"Network diagnostics: avg latency {report.PingResults.AverageLatencyMs:F1}ms, {report.AdapterInfo.Count} adapters", "diagnostic");
        return report;
    }

    /// <summary>
    /// Run startup diagnostics.
    /// </summary>
    public async Task<StartupReport> StartupDiagnosticsAsync()
    {
        var items = new List<StartupItem>();

        // Registry Run keys
        items.AddRange(await GetRegistryStartupAsync(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Machine"));
        items.AddRange(await GetRegistryStartupAsync(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "User"));

        // Startup folder
        var startupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        if (Directory.Exists(startupPath))
        {
            foreach (var file in Directory.GetFiles(startupPath))
            {
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Location = "Startup Folder",
                    Command = file,
                    Source = "StartupFolder"
                });
            }
        }

        var report = new StartupReport
        {
            Items = items,
            TotalCount = items.Count,
            Recommendation = items.Count > 10 ? "Many startup items detected — consider disabling unnecessary ones" : "Startup looks clean"
        };

        _logger.Info($"Startup check: {items.Count} items found", "diagnostic");
        return report;
    }

    /// <summary>
    /// Run quick benchmark (network latency, DNS, disk, memory).
    /// </summary>
    public async Task<BenchmarkResult> BenchmarkAsync()
    {
        var sw = Stopwatch.StartNew();
        var result = new BenchmarkResult();

        // Network
        var ping = await PingAsync("8.8.8.8", 20);
        result.Network = new BenchmarkNetwork
        {
            AvgLatencyMs = ping.AverageLatencyMs,
            MinLatencyMs = ping.MinLatencyMs,
            MaxLatencyMs = ping.MaxLatencyMs,
            PacketLossPercent = ping.PacketLossPercent
        };

        // DNS
        var dns = await DnsResolveAsync("example.com");
        result.DnsResolutionMs = dns.ResolutionMs;

        // System
        var systemInfo = await _detector.DetectAsync();
        result.System = new BenchmarkSystem
        {
            CpuCores = systemInfo.CpuCores,
            RamGb = systemInfo.RamTotalGb,
            StorageType = systemInfo.PrimaryStorageType.ToString(),
            OverallTier = systemInfo.OverallTier.ToString()
        };

        // Memory
        result.Memory = await GetMemoryBenchmarkAsync();

        sw.Stop();
        result.Duration = sw.Elapsed;

        _logger.Info($"Benchmark complete in {sw.ElapsedMilliseconds}ms", "benchmark");
        return result;
    }

    // --- Helpers ---

    private async Task<HealthCheck> CheckServiceAsync(string name, string displayName, bool critical)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {name}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var isRunning = output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);

            return new HealthCheck
            {
                Name = displayName,
                Details = isRunning ? "Running" : "Not running",
                Status = isRunning ? HealthStatus.Ok :
                         critical ? HealthStatus.Critical : HealthStatus.Warning
            };
        }
        catch (Exception ex)
        {
            return new HealthCheck
            {
                Name = displayName,
                Details = $"Error: {ex.Message}",
                Status = HealthStatus.Warning
            };
        }
    }

    private async Task<HealthCheck> CheckDiskHealthAsync()
    {
        try
        {
            var drive = new DriveInfo("C");
            var freePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
            var freeGb = drive.AvailableFreeSpace / 1073741824.0;

            var status = freePercent < 5 ? HealthStatus.Critical :
                         freePercent < 15 ? HealthStatus.Warning : HealthStatus.Ok;

            return new HealthCheck
            {
                Name = "Disk Space (C:)",
                Details = $"{freeGb:F1}GB free ({freePercent:F1}%)",
                Status = status
            };
        }
        catch (Exception ex)
        {
            return new HealthCheck { Name = "Disk Space", Details = ex.Message, Status = HealthStatus.Warning };
        }
    }

    private async Task<HealthCheck> CheckMemoryAsync(SystemInfo info)
    {
        await Task.CompletedTask;

        return new HealthCheck
        {
            Name = "Memory",
            Details = $"{info.RamTotalGb}GB total ({info.RamTier} tier)",
            Status = info.RamTier <= HardwareTier.Low ? HealthStatus.Warning : HealthStatus.Ok
        };
    }

    private HealthCheck CheckSecurityStatus()
    {
        return new HealthCheck
        {
            Name = "Security",
            Details = "Run 'winopt doctor security' for detailed check",
            Status = HealthStatus.Ok
        };
    }

    private async Task<HealthCheck> CheckUptimeAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-CimInstance Win32_OperatingSystem).LastBootUpTime\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            if (DateTime.TryParse(output, out var lastBoot))
            {
                var uptime = DateTime.Now - lastBoot;
                var status = uptime.TotalDays > 30 ? HealthStatus.Warning : HealthStatus.Ok;
                return new HealthCheck
                {
                    Name = "Uptime",
                    Details = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m",
                    Status = status
                };
            }
        }
        catch { }
        return new HealthCheck { Name = "Uptime", Details = "Unknown", Status = HealthStatus.Ok };
    }

    private async Task<PingResult> PingAsync(string target, int count)
    {
        try
        {
            using var ping = new Ping();
            var times = new List<long>();
            var sent = 0;
            var lost = 0;

            for (int i = 0; i < count; i++)
            {
                sent++;
                try
                {
                    var reply = await ping.SendPingAsync(target, 2000);
                    if (reply.Status == IPStatus.Success)
                        times.Add(reply.RoundtripTime);
                    else
                        lost++;
                }
                catch { lost++; }
                await Task.Delay(100);
            }

            return new PingResult
            {
                Target = target,
                AverageLatencyMs = times.Count > 0 ? times.Average() : 0,
                MinLatencyMs = times.Count > 0 ? times.Min() : 0,
                MaxLatencyMs = times.Count > 0 ? times.Max() : 0,
                PacketLossPercent = sent > 0 ? (double)lost / sent * 100 : 100,
                PacketsSent = sent,
                PacketsLost = lost
            };
        }
        catch
        {
            return new PingResult { Target = target, PacketLossPercent = 100 };
        }
    }

    private async Task<DnsResult> DnsResolveAsync(string domain)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Resolve-DnsName {domain} | Select-Object -First 1 | Format-Table Name,IPAddress -AutoSize\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            sw.Stop();

            return new DnsResult
            {
                Domain = domain,
                ResolutionMs = sw.ElapsedMilliseconds,
                Resolved = !string.IsNullOrWhiteSpace(output)
            };
        }
        catch
        {
            sw.Stop();
            return new DnsResult { Domain = domain, ResolutionMs = sw.ElapsedMilliseconds, Resolved = false };
        }
    }

    private async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                adapters.Add(new NetworkAdapterInfo
                {
                    Name = nic.Description,
                    Type = nic.NetworkInterfaceType.ToString(),
                    Speed = nic.Speed,
                    Status = nic.OperationalStatus.ToString(),
                    IsWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                });
            }
        }
        catch { }
        return adapters;
    }

    private async Task<List<string>> GetTcpSettingsAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "int tcp show global",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        }
        catch { return new List<string>(); }
    }

    private async Task<List<StartupItem>> GetRegistryStartupAsync(string keyPath, string source)
    {
        var items = new List<StartupItem>();
        try
        {
            var regKey = keyPath.Replace("HKCU:\\", "").Replace("HKLM:\\", "")
                              .Replace("HKLM\\", "").Replace("HKCU\\", "");
            var root = keyPath.StartsWith("HKLM") ? "HKLM" : "HKCU";

            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"query \"{root}\\{regKey}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("REG_") && line.Contains("\\"))
                {
                    var parts = line.Split(new[] { "    " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        items.Add(new StartupItem
                        {
                            Name = parts[0].Trim(),
                            Location = source,
                            Command = parts[parts.Length - 1].Trim(),
                            Source = source
                        });
                    }
                }
            }
        }
        catch { }
        return items;
    }

    private async Task<BenchmarkMemory> GetMemoryBenchmarkAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"$os = Get-CimInstance Win32_OperatingSystem; @{Total=[math]::Round($os.TotalVisibleMemorySize/1MB,2); Free=[math]::Round($os.FreePhysicalMemory/1MB,2); Used=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB,2)} | ConvertTo-Json\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            return new BenchmarkMemory
            {
                TotalGb = root.GetProperty("Total").GetDouble(),
                FreeGb = root.GetProperty("Free").GetDouble(),
                UsedGb = root.GetProperty("Used").GetDouble()
            };
        }
        catch
        {
            return new BenchmarkMemory();
        }
    }
}

// === Result Types ===

public sealed class HealthReport
{
    public SystemInfo? SystemInfo { get; init; }
    public List<HealthCheck> Checks { get; init; } = new();
    public HealthStatus OverallStatus { get; init; }
}

public sealed class HealthCheck
{
    public string Name { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public HealthStatus Status { get; init; }
}

public enum HealthStatus { Ok, Warning, Critical }

public sealed class NetworkReport
{
    public string Target { get; init; } = string.Empty;
    public List<NetworkAdapterInfo> AdapterInfo { get; init; } = new();
    public PingResult PingResults { get; init; } = new();
    public DnsResult DnsResolution { get; init; } = new();
    public List<string> TcpSettings { get; init; } = new();
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long Speed { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsWireless { get; init; }
}

public sealed class PingResult
{
    public string Target { get; init; } = string.Empty;
    public double AverageLatencyMs { get; init; }
    public double MinLatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
    public double PacketLossPercent { get; init; }
    public int PacketsSent { get; init; }
    public int PacketsLost { get; init; }
}

public sealed class DnsResult
{
    public string Domain { get; init; } = string.Empty;
    public long ResolutionMs { get; init; }
    public bool Resolved { get; init; }
}

public sealed class StartupReport
{
    public List<StartupItem> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class StartupItem
{
    public string Name { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed class BenchmarkResult
{
    public BenchmarkNetwork Network { get; set; } = new();
    public double DnsResolutionMs { get; set; }
    public BenchmarkSystem System { get; set; } = new();
    public BenchmarkMemory Memory { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public sealed class BenchmarkNetwork
{
    public double AvgLatencyMs { get; init; }
    public double MinLatencyMs { get; init; }
    public double MaxLatencyMs { get; init; }
    public double PacketLossPercent { get; init; }
}

public sealed class BenchmarkSystem
{
    public int CpuCores { get; init; }
    public double RamGb { get; init; }
    public string StorageType { get; init; } = string.Empty;
    public string OverallTier { get; init; } = string.Empty;
}

public sealed class BenchmarkMemory
{
    public double TotalGb { get; init; }
    public double FreeGb { get; init; }
    public double UsedGb { get; init; }
}
