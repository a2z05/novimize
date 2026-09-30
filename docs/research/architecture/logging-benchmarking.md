# WinOpt Logging & Benchmarking Strategy

## Logging Architecture

### Log Levels

| Level | Purpose | Example |
|-------|---------|---------|
| **DEBUG** | Detailed technical info for debugging | "Registry key HKLM\...\Foo read value: 42" |
| **INFO** | Normal operation records | "Tweak network.tcpip.autoTuning applied successfully" |
| **WARN** | Non-critical issues | "Service DiagTrack was already disabled, skipping" |
| **ERROR** | Operation failures | "Failed to set registry value: access denied" |
| **CRITICAL** | System-affecting failures | "Rollback failed for service SysMain — manual intervention required" |

### Log Storage

```
%LOCALAPPDATA%\WinOpt\logs\
├── winopt-2026-08-31.log       (current day)
├── winopt-2026-08-30.log       (yesterday)
├── winopt-2026-08-29.log       (2 days ago)
├── ...
└── archive\                     (compressed old logs)
```

### Log Rotation Policy
- Keep 30 days of daily log files
- Compress logs older than 7 days
- Delete logs older than 90 days
- Maximum total log size: 100MB
- When exceeding limit, delete oldest first

### Log Entry Format

```json
{
  "timestamp": "2026-08-31T12:00:00.123Z",
  "level": "INFO",
  "category": "apply",
  "tweakId": "network.tcpip.autoTuning",
  "operation": "apply",
  "message": "TCP Auto-Tuning set to 'normal'",
  "details": {
    "method": "netsh",
    "command": "netsh int tcp set global autotuninglevel=normal",
    "previousValue": "disabled",
    "newValue": "normal",
    "duration": "127ms",
    "elevationUsed": true
  },
  "result": "success",
  "error": null,
  "sessionId": "uuid",
  "snapshotId": "uuid"
}
```

### Human-Readable Log Format (for UI display)

```
[2026-08-31 12:00:00] INFO  Applied: TCP Auto-Tuning → normal (was: disabled)
[2026-08-31 12:00:00] INFO  Verified: TCP Auto-Tuning = normal ✓
[2026-08-31 12:00:01] INFO  Applied: SysMain service → Disabled (was: Auto)
[2026-08-31 12:00:01] WARN  Service DiagTrack: Windows may re-enable after update
[2026-08-31 12:00:02] ERROR Failed: Registry write to HKLM\...\Protected → Access Denied
```

### Operation Audit Trail

Every system modification produces an immutable audit entry:

```json
{
  "auditId": "uuid",
  "timestamp": "ISO-8601",
  "operation": "apply | rollback | detect",
  "tweakId": "string",
  "target": "string — what was modified",
  "oldValue": "string | null",
  "newValue": "string",
  "method": "string — registry | powercfg | netsh | powershell | service | dism | appx",
  "command": "string — exact command executed",
  "result": "success | failure | partial",
  "verificationResult": "verified | failed | skipped",
  "errorDetails": "string | null",
  "elevationUsed": true,
  "userInitiated": true,
  "sessionId": "uuid",
  "snapshotId": "uuid",
  "duration": "127ms"
}
```

Audit logs are:
- Append-only (never modified or deleted by the application)
- Stored separately from regular logs
- Retained indefinitely (or per user policy)
- Exportable as JSON or CSV

### Log Export

```bash
# Export logs as JSON
winopt logs export --format json --output logs.json

# Export logs as CSV
winopt logs export --format csv --output logs.csv

# Export logs for date range
winopt logs export --from 2026-08-01 --to 2026-08-31

# Export audit trail
winopt logs audit --output audit.json
```

---

## Benchmarking Strategy

### Philosophy

WinOpt should NEVER claim performance improvements without measurable evidence. The benchmarking system provides before/after measurements that let users verify the impact of optimizations.

### Measurement Categories

#### 1. Network Metrics

| Metric | Method | Command | Unit |
|--------|--------|---------|------|
| **Latency** | ICMP ping to target | `ping -n 20 8.8.8.8` | ms (avg, min, max, stddev) |
| **Jitter** | Standard deviation of ping times | Calculated from ping results | ms |
| **Packet Loss** | Ping loss percentage | `ping -n 100 <target>` | % |
| **DNS Resolution** | DNS query time | `nslookup example.com` or PowerShell `Resolve-DnsName` | ms |
| **Download Throughput** | HTTP download test | PowerShell download from known URL | Mbps |
| **Upload Throughput** | HTTP upload test | PowerShell upload to known URL | Mbps |
| **TCP Configuration** | Read current settings | `netsh int tcp show global` | Enumerated values |

**Network Test Targets (configurable):**
- Default: 8.8.8.8 (Google DNS), 1.1.1.1 (Cloudflare DNS)
- Throughput test URLs: configurable mirror servers
- DNS test domains: configurable

**Implementation:**
```powershell
# Latency test
$pings = ping -n 20 8.8.8.8 | Select-String "time[=<](\d+)" | ForEach-Object {
    [int]($_.Matches[0].Groups[1].Value)
}
$result = @{
    Average = ($pings | Measure-Object -Average).Average
    Min = ($pings | Measure-Object -Minimum).Minimum
    Max = ($pings | Measure-Object -Maximum).Maximum
    StdDev = [Math]::Sqrt(($pings | ForEach-Object { [Math]::Pow($_ - $avg, 2) } | Measure-Object -Average).Average)
    PacketLoss = (ping -n 100 8.8.8.8 | Select-String "Lost = (\d+)").Matches[0].Groups[1].Value
}
```

#### 2. System Metrics

| Metric | Method | Command | Unit |
|--------|--------|---------|------|
| **Boot Time** | Last boot timestamp | `Get-CimInstance Win32_OperatingSystem` → LastBootUpTime | seconds |
| **Idle CPU** | CPU utilization at idle | Performance counters or `Get-Process` | % |
| **RAM Usage** | Memory utilization | `Get-CimInstance Win32_OperatingSystem` | MB, % |
| **Available RAM** | Free memory | `Get-CimInstance Win32_OperatingSystem` | MB |
| **Page File Usage** | Page file utilization | `Get-CimInstance Win32_OperatingSystem` | MB |
| **System Uptime** | Time since last boot | Calculated from LastBootUpTime | hours:minutes |
| **Process Count** | Number of running processes | `(Get-Process).Count` | count |
| **Service Count** | Running services | `(Get-Service | Where-Object Status -eq 'Running').Count` | count |
| **Startup Items** | Number of startup items | Enumerated from registry + tasks | count |

**Implementation:**
```powershell
# System snapshot
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor
$result = @{
    OSVersion = $os.Caption
    BuildNumber = $os.BuildNumber
    TotalRAM = [Math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    FreeRAM = [Math]::Round($os.FreePhysicalMemory / 1MB, 2)
    RAMUsagePercent = [Math]::Round((1 - $os.FreePhysicalMemory / $os.TotalVisibleMemorySize) * 100, 1)
    CPUName = $cpu.Name
    CPUCores = $cpu.NumberOfCores
    CPULogicalProcessors = $cpu.NumberOfLogicalProcessors
    LastBoot = $os.LastBootUpTime
    Uptime = (Get-Date) - $os.LastBootUpTime
}
```

#### 3. Storage Metrics

| Metric | Method | Command | Unit |
|--------|--------|---------|------|
| **Sequential Read** | Disk performance test | `winsat disk -drive <letter>` or PowerShell counters | MB/s |
| **Sequential Write** | Disk performance test | `winsat disk -drive <letter>` | MB/s |
| **Random Read IOPS** | Disk performance test | `winsat disk -drive <letter> -ran -read` | IOPS |
| **Random Write IOPS** | Disk performance test | `winsat disk -drive <letter> -ran -write` | IOPS |
| **Disk Latency** | Average disk response time | Performance counters | ms |
| **Disk Space** | Volume usage | `Get-Volume` | GB, % |
| **TRIM Status** | TRIM enabled check | `fsutil behavior query DisableDeleteNotify` | enabled/disabled |

**Note:** WinSAT is built into Windows and provides standardized storage benchmarks. Use `winsat disk -drive C -ran -read` for random reads, etc.

#### 4. GPU Metrics

| Metric | Method | Command | Unit |
|--------|--------|---------|------|
| **GPU Name** | Device enumeration | `Get-CimInstance Win32_VideoController` | string |
| **GPU Driver Version** | Device info | `Get-CimInstance Win32_VideoController` | version string |
| **GPU Utilization** | Performance counters | `\GPU Engine(*)\Utilization Percentage` | % |
| **GPU Memory** | Device info | `Get-CimInstance Win32_VideoController` | MB |
| **HAGS Status** | Registry check | `HKLM\...\GraphicsDrivers\HwSchMode` | enabled/disabled |

**Note:** Detailed GPU benchmarking (FPS, frame time) requires in-game measurement tools (MSI Afterburner, PresentMon) which are outside WinOpt's scope. WinOpt can measure GPU configuration state but not in-game performance.

#### 5. DPC Latency (Advanced)

| Metric | Method | Command | Unit |
|--------|--------|---------|------|
| **DPC Latency** | Kernel timer resolution | Requires driver-level access | μs |

**Note:** DPC latency measurement requires kernel-mode drivers (like LatencyMon uses). WinOpt should recommend LatencyMon for DPC analysis rather than implementing its own kernel driver.

### Benchmark Execution

#### Before/After Comparison

```json
{
  "benchmark": {
    "id": "uuid",
    "timestamp": "2026-08-31T12:00:00Z",
    "type": "full | quick | network | storage | system",
    "results": {
      "network": {
        "latency": { "avg": 12.3, "min": 8, "max": 25, "stddev": 3.2 },
        "packetLoss": 0,
        "dnsResolution": 15.7,
        "downloadMbps": 94.5,
        "uploadMbps": 23.1
      },
      "system": {
        "idleCpu": 2.1,
        "ramUsagePercent": 34.5,
        "processCount": 145,
        "serviceCount": 89,
        "uptime": "3d 12h 45m"
      },
      "storage": {
        "sequentialReadMBs": 3500,
        "sequentialWriteMBs": 2800,
        "trimEnabled": true
      }
    }
  }
}
```

#### Comparison Report

```json
{
  "comparison": {
    "before": "benchmark-id-1",
    "after": "benchmark-id-2",
    "changes": {
      "network.latency.avg": { "before": 18.5, "after": 12.3, "delta": -6.2, "improvement": true },
      "network.dnsResolution": { "before": 25.1, "after": 15.7, "delta": -9.4, "improvement": true },
      "system.idleCpu": { "before": 5.2, "after": 2.1, "delta": -3.1, "improvement": true },
      "system.ramUsagePercent": { "before": 45.0, "after": 34.5, "delta": -10.5, "improvement": true }
    },
    "summary": {
      "improved": 4,
      "degraded": 0,
      "unchanged": 2,
      "overallVerdict": "positive"
    }
  }
}
```

### CLI Commands

```bash
# Run quick benchmark
winopt bench --quick

# Run full benchmark
winopt bench --full

# Run network benchmark only
winopt bench --network

# Run storage benchmark only
winopt bench --storage

# Compare two benchmarks
winopt bench compare <before-id> <after-id>

# Show benchmark history
winopt bench history

# Export benchmark results
winopt bench export --format json --output bench.json
```

### Measurement Reliability

**High confidence measurements:**
- Network latency (ICMP ping is reliable)
- DNS resolution time
- Disk space usage
- RAM usage percentages
- Service/process counts
- Registry values

**Medium confidence measurements:**
- Throughput (depends on server, time of day, ISP)
- Disk sequential read/write (depends on queue depth, block size)
- Boot time (varies by startup configuration)

**Low confidence measurements (avoid claiming):**
- FPS improvement (requires in-game testing, not measurable from desktop)
- Application launch time (highly variable)
- Real-world "responsiveness" (subjective)

### Anti-Patterns

**DO NOT:**
- Claim "X% faster" without running benchmarks
- Cherry-pick metrics that improved while ignoring degraded ones
- Use synthetic benchmarks that don't reflect real usage
- Compare across different hardware configurations
- Claim gaming FPS improvements from desktop tweaks

**DO:**
- Always show before/after data
- Let users run their own benchmarks
- Be honest about measurement limitations
- Clearly state margin of error
- Note when a tweak's impact is too small to measure reliably
