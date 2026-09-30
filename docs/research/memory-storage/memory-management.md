# Windows Memory Management

## Evidence Score System

Evidence scores reflect the quality and consistency of documentation across Microsoft, independent testing, and expert consensus. They guide confidence in recommendations.

| Score | Meaning |
|-------|---------|
| 10/10 | Universally agreed, well-documented by Microsoft, independent benchmarks confirm |
| 9/10 | Strong consensus, minor edge cases or evolving best practices |
| 8/10 | Well-supported but workload-dependent or hardware-dependent nuances |
| 7/10 | Generally agreed with some conflicting data or outdated sources |

---

## 1. Virtual Memory / Pagefile

**Evidence Score: 10/10**

### Overview

The pagefile (`pagefile.sys`) is a reserved portion of disk that Windows uses as an extension of physical RAM. It is not simply "slow memory" -- it is an integral part of the Windows memory manager's architecture and serves purposes far beyond overflow storage.

### The Myth: "Disabling the Pagefile Improves Performance"

This is one of the most persistent and harmful optimization myths. Disabling the pagefile does **not** improve performance and actively causes problems:

1. **Crash dump generation fails.** Windows writes crash dump data to the pagefile during a blue screen. Without a pagefile, you get a `0x0000007F` (UNEXPECTED_KERNEL_MODE_TRAP) or simply no dump at all. You will have zero diagnostic data for post-mortem analysis.

2. **Commit charge ceiling is enforced.** Every process has a commit limit equal to physical RAM plus pagefile size. With no pagefile, the ceiling is RAM alone. Programs that reserve virtual address space (common in large databases, development tools, and games) can fail with out-of-memory errors even when physical RAM is mostly free.

3. **No performance gain.** Windows does not "use the pagefile more" when it exists. The memory manager places **modified pages** (dirty data) and **standby data** (cached, clean pages that could be discarded) into the pagefile. Clean standby pages cost nothing to discard and re-read from disk. Modified pages must be written somewhere regardless.

4. **Application compatibility breaks.** Many applications (Visual Studio, SQL Server, large Java workloads) allocate more virtual memory than they actively use. Without a pagefile, these allocations can fail.

### The Commit Charge Model

Commit charge is the total amount of virtual memory that has been reserved or committed by all processes. It is bounded by:

```
Commit Limit = Physical RAM + Pagefile Size(s)
```

- **Peak Commit Charge** shows the historical high-water mark.
- A healthy system shows commit usage well below the commit limit.
- If commit usage approaches the limit, Windows begins aggressively trimming working sets and paging aggressively -- this is the "low memory" condition people sometimes mistake for needing more RAM.

### Optimal Pagefile Sizing

**Microsoft recommendation: System Managed.** This is the correct default for virtually all use cases.

| Scenario | Recommendation |
|----------|---------------|
| General desktop use | System managed (default) |
| Development workstation | System managed |
| Gaming PC | System managed |
| File server | System managed |
| Database server | System managed, or fixed size equal to RAM for predictable crash dumps |
| Crash dump analysis workstation | At minimum 1.5x RAM for complete kernel dumps |

**Do not manually set a small pagefile** (e.g., 100-400 MB). This is a holdover from the Windows XP era when a minimal pagefile was "enough" for crash dumps. Modern Windows versions and applications commit far more virtual memory.

### Registry Configuration

```
Key:    HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management
Value:  PagingFiles    (REG_MULTI_SZ)
Default: System managed (the value is absent or set automatically)
```

**PowerShell commands:**

```powershell
# View current pagefile configuration
Get-WmiObject Win32_PageFileSetting | Select-Object Name, InitialSize, MaximumSize

# Reset to system-managed (remove manual override)
# Requires registry manipulation:
Remove-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "PagingFiles" -ErrorAction SilentlyContinue
# Then reboot -- Windows will recreate system-managed pagefiles
```

**Note:** Directly editing pagefile settings is not reliably supported via PowerShell without registry changes. The System Properties GUI (`sysdm.cpl` > Advanced > Performance > Virtual Memory) is the supported interface.

### SSD vs HDD Considerations

| Factor | SSD | HDD |
|--------|-----|-----|
| Pagefile access latency | ~0.1 ms | ~5-10 ms |
| Impact of pagefile on performance | Minimal | Significant if heavily used |
| Wear concerns | Negligible -- modern SSDs handle pagefile writes for years | N/A |
| Recommendation | System managed | System managed; ensure sufficient RAM to minimize paging |

**SSD wear:** The pagefile generates modest write volumes. A typical desktop workload writes 1-5 GB/day to the pagefile. Modern SSDs are rated for hundreds of terabytes of writes (TBW). This is a non-issue.

**HDD:** If you have an HDD and experience heavy paging (high "Hard Faults/sec" in Performance Monitor), the bottleneck is seek time. Adding RAM is the real fix, not disabling the pagefile.

---

## 2. DisablePagingExecutive

**Evidence Score: 8/10**

### Overview

The DisablePagingExecutive setting controls whether the Windows kernel (executive) and device drivers are kept in physical RAM or allowed to be paged out to disk.

### Registry Configuration

```
Key:    HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management
Value:  DisablePagingExecutive    (REG_DWORD)
Type:   DWORD
Default:  0 (disabled -- kernel CAN be paged out)
```

| Value | Meaning |
|-------|---------|
| 0 | Kernel and drivers may be paged to disk (default for desktop Windows) |
| 1 | Kernel and drivers are locked in physical RAM (default for Windows Server) |

### Recommendation by RAM Size

| Physical RAM | Recommended Value | Risk Level | Rationale |
|-------------|-------------------|------------|-----------|
| < 4 GB | 0 (default) | N/A | Insufficient RAM to lock kernel in memory |
| 4-8 GB | 0 or 1 | Low | Marginal; 1 helps but may pressure application memory |
| 8-16 GB | 1 | Low | Enough RAM to comfortably hold kernel (~200-400 MB) |
| 16 GB+ | 1 | Very Low | Plenty of RAM; kernel stays resident for best performance |

### PowerShell Commands

```powershell
# Check current value
Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "DisablePagingExecutive" -ErrorAction SilentlyContinue

# Enable (lock kernel in RAM)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "DisablePagingExecutive" -Value 1 -Type DWord

# Disable (allow kernel paging -- revert to default)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "DisablePagingExecutive" -Value 0 -Type DWord
```

**Requires reboot to take effect.**

### Risk Assessment

- **Desktop with 8+ GB RAM:** Low risk. The kernel consumes approximately 200-400 MB of RAM depending on drivers loaded. On a system with 8 GB, this is a small fraction. The benefit is that kernel code is always in fast RAM, never causing a page fault when handling an interrupt or system call.
- **Desktop with 4 GB RAM:** Medium risk. Locking the kernel in memory reduces the pool available for applications. On a heavily loaded 4 GB system, this can increase application-level paging.
- **Laptops with connected standby / modern standby:** Some users report that setting this to 1 on laptops increases standby power consumption slightly because the kernel cannot be paged out during sleep. This effect is minor and system-dependent.

### What It Does NOT Do

- It does NOT prevent user-mode memory from being paged out.
- It does NOT increase total available memory.
- It does NOT prevent the pagefile from being used.
- It is NOT a substitute for having adequate RAM.

---

## 3. LargeSystemCache

**Evidence Score: 8/10**

### Overview

LargeSystemCache controls how Windows manages the system file cache (the pool of memory used to cache NTFS metadata, file contents, and other kernel objects).

### Registry Configuration

```
Key:    HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management
Value:  LargeSystemCache    (REG_DWORD)
Type:   DWORD
Default:  0
```

| Value | Behavior |
|-------|----------|
| 0 | System cache is sized for workstation workload. Working set trimming favors applications. |
| 1 | System cache is allowed to grow larger. File server workloads benefit from larger cache. |

### Recommendation by Workload

| Workload | Value | Rationale |
|----------|-------|-----------|
| Desktop / Workstation | 0 | Applications need priority for RAM |
| File Server ( SMB / DFS ) | 1 | Caching file data improves throughput significantly |
| Web Server (IIS) | 0 | IIS has its own caching; system cache competes uselessly |
| Database Server | 0 | SQL Server manages its own buffer pool |
| General Purpose Server | 0 | Only dedicated file servers benefit |

### Do NOT Set to 1 on Desktop

Setting `LargeSystemCache = 1` on a desktop or gaming system is counterproductive:

- It allows the system cache to consume RAM that applications need.
- On desktops, file access patterns are random and varied -- a larger system cache provides no measurable benefit.
- Games and applications may experience increased memory pressure, leading to more pagefile usage.
- There is zero performance improvement for typical desktop workloads.

### PowerShell Commands

```powershell
# Check current value
Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "LargeSystemCache" -ErrorAction SilentlyContinue

# Set to 0 (desktop -- recommended)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "LargeSystemCache" -Value 0 -Type DWord

# Set to 1 (file server only)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name "LargeSystemCache" -Value 1 -Type DWord
```

**Requires reboot to take effect.**

---

## 4. Memory Compression (Windows 10+)

**Evidence Score: 9/10**

### Overview

Introduced in Windows 10 (build 10525), the Memory Compression service compresses pages that would otherwise be written to the pagefile. Instead of paging modified pages to disk, Windows compresses them and keeps them in RAM. This significantly reduces disk I/O and is one of the most impactful memory management features in modern Windows.

### How It Works

1. When memory pressure increases, the memory manager identifies modified (dirty) pages.
2. Instead of writing them to the pagefile, it compresses them (typically achieving approximately 2:1 compression ratio).
3. Compressed pages are stored in a dedicated in-memory store (`MemCompression` process).
4. When a compressed page is needed, it is decompressed back into RAM.
5. Only when the compressed store is full are pages actually written to the pagefile.

### Why It Matters

- **Reduces disk I/O:** A system that would have been paging heavily may instead compress, which is orders of magnitude faster (RAM speed vs. disk speed).
- **Extends effective RAM:** 16 GB of RAM behaves more like 20-24 GB under moderate memory pressure.
- **No configuration needed:** It is enabled by default and self-tuning.

### Management Commands

```powershell
# Check memory compression status
Get-MMAgent

# Example output:
# MemoryCompression    : True
# OperationProfiler    : False
# PagefilePrediction   : True
# PagefilePredictionApp: True
# Reagent               : True
# Scripts               : True
# ScenarioExecution    : False
# SlabConsolidation    : True

# Disable memory compression (NOT recommended)
Disable-MMAgent -MemoryCompression

# Enable memory compression
Enable-MMAgent -MemoryCompression

# Check the compression process
Get-Process MemCompression -ErrorAction SilentlyContinue

# View compressed memory in RAMMap
# (RAMMap shows compressed memory in the "Nonpaged Pool" or dedicated sections)
```

### Should It Be Disabled?

**No.** There is no scenario where disabling memory compression improves performance on a typical system. The only situation where you might consider disabling it is on a system with extremely fast NVMe storage where you want predictable pagefile I/O patterns for benchmarking, and even then the benefit is questionable.

### Compression Ratio

The actual compression ratio varies by workload:

| Workload Type | Typical Ratio | Notes |
|--------------|---------------|-------|
| General desktop | 1.8:1 to 2.2:1 | Web browsers, Office apps compress well |
| Development (IDE + compilers) | 1.5:1 to 2.0:1 | Code and text compress well |
| Gaming | 1.2:1 to 1.8:1 | Already-compressed game assets compress less |
| Database (in-memory) | 1.3:1 to 2.0:1 | Depends on data types |

---

## 5. SysMain / Superfetch

**Evidence Score: 9/10**

### Overview

SysMain (formerly Superfetch) is a Windows service that analyzes usage patterns and preloads frequently accessed data into RAM. It maintains a history of application launches, file access patterns, and boot sequences to predict what data will be needed next.

### The Myth: "Disable SysMain on SSDs"

This is another persistent myth from the early SSD era (circa 2010-2012). The reasoning was:

> "SSDs are fast, so preloading data is unnecessary, and the constant writes will wear out the SSD."

**Both premises are wrong:**

1. **Preloading still helps.** Even with an NVMe SSD at 7 GB/s sequential read, preloading avoids the latency of initiating I/O, going through the storage stack, and waiting for the data to arrive in RAM. For applications with many small random reads (launching Outlook, opening Visual Studio), SysMain's prefetching eliminates dozens of small I/Os that would otherwise serialize.

2. **SSD wear is negligible.** Modern SSDs are rated for 300-600 TBW (terabytes written) for consumer drives, and much more for enterprise. SysMain writes a few hundred megabytes per day of prefetch data. A modern SSD would last decades before SysMain caused meaningful wear.

3. **Microsoft adapted SysMain for SSDs.** Since Windows 10, SysMain adjusts its behavior based on storage type. On SSDs, it is less aggressive about writing large prefetch files but still maintains the usage history and performs memory optimization. It knows the difference.

### Service Management

```powershell
# Check SysMain status
Get-Service -Name SysMain | Select-Object Name, Status, StartType

# Stop SysMain
Stop-Service -Name SysMain -Force

# Disable SysMain (prevents automatic start)
Set-Service -Name SysMain -StartupType Disabled

# Enable SysMain (recommended)
Set-Service -Name SysMain -StartupType Automatic

# Start SysMain
Start-Service -Name SysMain

# Check via services.msc
services.msc  # Find "SysMain" in the list
```

### Should You Disable It?

| Scenario | Recommendation |
|----------|---------------|
| SSD system, general use | Keep enabled |
| HDD system, general use | Keep enabled -- benefits are even larger on HDD |
| Server (dedicated role) | May disable if workload is well-understood and predictable |
| Troubleshooting memory issues | Temporarily disable to isolate variables, then re-enable |
| Low-RAM system (4 GB or less) | Keep enabled -- it helps the most when RAM is scarce |

### SysMain vs. ReadyBoost

These are separate features. SysMain is the memory management intelligence. ReadyBoost uses USB flash drives as supplemental cache and is largely irrelevant on SSD systems. Do not confuse disabling one with disabling both.

---

## 6. Standby Memory

**Evidence Score: 10/10**

### Overview

Standby memory is one of the most misunderstood categories in Windows memory reporting. It shows up as "In Use" in Task Manager, leading people to believe their RAM is full when it is actually free and available.

### What Standby Memory Actually Is

When a process closes or releases memory, Windows does not immediately zero it out. Instead, the pages remain in RAM with their previous content intact, placed in the **standby list**. These pages are:

- **Not in use by any process.**
- **Immediately available** for any new allocation.
- **Cheaply discardable** -- zero I/O required to reclaim.
- **Potentially useful** -- if the same application is re-launched, the cached data is still there, avoiding a disk read.

**Standby memory is free memory.** It is RAM that Windows is using intelligently as a cache, but it can be claimed by any application at any time with zero penalty.

### Why Standby Cleaners Are Harmful

Utilities like "RAM Booster," "MemClean," "Intelligent Standby List Cleaner (ISLC)," and similar tools forcibly trim the standby list, forcing Windows to discard cached pages.

**This is counterproductive:**

- **It wastes RAM.** You paid for that RAM. Letting it sit empty provides zero benefit over letting it cache useful data.
- **It increases disk I/O.** When a previously-cached application is re-launched, Windows must read from disk instead of using the cached standby pages.
- **It can cause performance stutters.** Forcing memory trimming interrupts the memory manager's normal, optimized page replacement algorithms.
- **Windows already manages this.** The memory manager has 30+ years of refinement. It trims standby pages automatically when applications need memory.

### When Standby Cleaners Are Marginally Justified

ISLC (Intelligent Standby List Cleaner) is sometimes recommended for gaming systems that experience stuttering due to memory pressure spikes. The logic is:

- Preemptively trim standby memory to keep a large pool of genuinely free pages.
- This can reduce the latency spike when a game suddenly allocates a large block.

**However**, this is treating a symptom. The real fix is:
1. Close background applications.
2. Upgrade RAM if consistently running low.
3. Let Windows manage memory naturally.

### Analysis with RAMMap

**RAMMap** (from Sysinternals) is the definitive tool for understanding standby memory.

```powershell
# Download and run RAMMap (requires elevation)
# Sysinternals: https://learn.microsoft.com/en-us/sysinternals/downloads/rammap

# RAMMap columns:
# - Active: In use by processes
# - Standby: Cached, immediately available
# - Modified: Dirty, waiting to be written
# - Zeroed: Zeroed and ready for use
# - Free: Truly unused
```

**Key RAMMap views:**

| Tab | Purpose |
|-----|---------|
| Use Counts | Summary of all memory categories |
| Processes | Per-process memory breakdown |
| Priority Summary | Standby pages organized by priority (0-7) |
| Physical Pages | Page-level detail |
| File Summary | Which files are cached in standby |
| File Details | Per-file cache information |

### Verifying Standby Memory Status

```powershell
# Get summary memory information
Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize, FreePhysicalMemory, FreeVirtualMemory

# More detailed view via systeminfo
systeminfo | findstr /C:"Memory"

# Detailed memory stats via PowerShell
$os = Get-CimInstance Win32_OperatingSystem
$totalMB = [math]::Round($os.TotalVisibleMemorySize / 1024)
$freeMB = [math]::Round($os.FreePhysicalMemory / 1024)
Write-Host "Total Physical RAM: $totalMB MB"
Write-Host "Free Physical RAM:  $freeMB MB"
```

---

## 7. Working Set Management

**Evidence Score: 10/10**

### Overview

A process's **working set** is the set of pages that are currently resident in physical RAM and mapped into that process's address space. Windows automatically manages working sets, trimming them when memory pressure increases and allowing them to grow when RAM is available.

### How Working Set Trimming Works

1. **Soft trim:** When memory pressure is low to moderate, Windows trims processes that haven't been active recently, reducing their working set.
2. **Hard trim:** Under high memory pressure, Windows aggressively trims all processes, including active ones.
3. **Standby priority:** Trimmed pages go to the standby list (see Section 6) and remain useful as a cache.

Windows uses a sophisticated algorithm that considers:
- Process priority
- Time since last access
- Whether the process is foreground or background
- System-wide memory pressure signals

### The Myth: "EmptyWorkingSet Helps Performance"

Tools like "Empty Working Set" or RAM optimizers that call `EmptyWorkingSet()` are counterproductive:

- **They force all process pages to the standby list,** meaning the next user interaction will cause a burst of page faults as the application reloads its data from disk.
- **They create a false "low memory" state** that triggers aggressive memory management by Windows, paradoxically increasing disk I/O.
- **They cause visible stuttering** as applications re-populate their working sets.

### Tools for Working Set Analysis

**RAMMap** (Sysinternals):
- Shows per-process working set sizes.
- Identifies processes with large active memory footprints.

**VMMap** (Sysinternals):
- Provides a detailed breakdown of a single process's virtual memory.
- Shows committed vs. reserved vs. shared vs. private memory.
- Identifies which memory types are consuming the most space.

```powershell
# Download VMMap from Sysinternals
# https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap

# VMMap categories:
# - Image: Mapped executable code (.exe, .dll)
# - Mapped File: Data files mapped into memory
# - Shareable: Memory that can be shared between processes
# - Private: Memory unique to this process
# - Free: Uncommitted virtual address space
```

**PowerShell -- per-process memory:**

```powershell
# Top 20 processes by working set
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 20 Name, @{N='WorkingSetMB'; E={[math]::Round($_.WorkingSet64/1MB)}}, @{N='PeakMB'; E={[math]::Round($_.PeakWorkingSet64/1MB)}}

# Top 20 processes by private memory (more accurate for memory pressure)
Get-Process | Sort-Object PrivateMemorySize64 -Descending | Select-Object -First 20 Name, @{N='PrivateMB'; E={[math]::Round($_.PrivateMemorySize64/1MB)}}, @{N='VirtualMB'; E={[math]::Round($_.VirtualMemorySize64/1MB)}}
```

### Process Memory Fields Explained

| Field | What It Measures | Usefulness |
|-------|-----------------|------------|
| WorkingSet64 | Pages currently in physical RAM | Snapshot only, fluctuates rapidly |
| PrivateMemorySize64 | Memory unique to this process (committed) | Best indicator of true memory consumption |
| VirtualMemorySize64 | Total virtual address space reserved | Often misleadingly large (64-bit processes can reserve TBs) |
| PeakWorkingSet64 | Highest working set ever recorded | Useful for capacity planning |

---

## 8. Memory Diagnostics

### Built-in Windows Memory Diagnostic

**Tool:** `mdsched.exe`

```
# Launch Windows Memory Diagnostic
mdsched.exe

# Or via PowerShell
Start-Process mdsched.exe
```

**What it does:**
- Reboots the system and runs a basic memory test during boot.
- Tests basic read/write patterns across all physical RAM.
- Runs multiple passes with different test patterns.
- Results appear in Event Viewer after reboot: `Application and Services Logs > Microsoft > Windows > MemoryDiagnostic-Results`

**Limitations:**
- Basic pattern testing only -- may miss intermittent or timing-related errors.
- Single-pass results are insufficient; run the extended test (multiple passes).
- Cannot detect all types of errors (e.g., row hammer, certain refresh-related issues).

**Verdict:** Useful as a quick first pass. Not sufficient for definitive memory testing.

### MemTest86 / MemTest86+

**The gold standard for memory testing.**

| Feature | MemTest86 | MemTest86+ |
|---------|-----------|------------|
| License | Free (basic), paid (Pro) | Free (open source, GPL) |
| Boot method | UEFI, BIOS | UEFI, BIOS |
| Test coverage | Excellent | Excellent (fork, actively maintained) |
| Multi-pass testing | Yes | Yes |
| Error reporting | Detailed | Detailed |
| Website | memtest86.com | memtestplus.org |

**How to use:**

1. Download the ISO or USB installer from memtest86.com or memtestplus.org.
2. Create bootable media (USB drive recommended).
3. Boot from the USB drive.
4. Let it run for **at least 4 complete passes** (overnight is ideal).
5. Any errors indicate a hardware problem with RAM.

**When to run MemTest86:**
- Experiencing unexplained crashes, blue screens, or application corruption.
- After installing new RAM.
- After building a new system.
- When troubleshooting intermittent issues that might be memory-related.

**Important:** MemTest86+ (the open-source fork) is recommended over the original MemTest86 for most users. The free version of original MemTest86 has been limited in features and uses a confusing paid tier model.

### RAMMap for Usage Analysis

```powershell
# RAMMap is the best tool for analyzing HOW memory is being used
# Download from: https://learn.microsoft.com/en-us/sysinternals/downloads/rammap

# Run with elevation (Administrator)
# The Use Counts tab gives the definitive breakdown:
# - Active: Currently in use by processes
# - Standby: Cached (free, usable immediately)
# - Modified: Dirty (waiting to be written to pagefile)
# - Zeroed: Clean, ready for allocation
# - Free: Not in use
# - Total: All physical RAM
```

### Performance Monitor Counters

```powershell
# Monitor memory performance in real-time
# Key counters to watch:

# Memory\Available MBytes          -- Free + Standby (actual available RAM)
# Memory\Pages/sec                 -- Pagefile read/write rate (high = memory pressure)
# Memory\Pool Nonpaged Bytes       -- Kernel non-paged pool (should be < 256 MB typically)
# Memory\Pool Paged Bytes          -- Kernel paged pool
# Process(*)\Working Set           -- Per-process working set
# Paging File(*)\% Usage          -- Pagefile utilization

# Quick command-line snapshot
Get-Counter '\Memory\Available MBytes','Memory\Pages/sec','Memory\Pool Nonpaged Bytes' -Continuous -MaxSamples 5
```

---

## 9. NUMA and Huge Pages

**Evidence Score: 8/10 (workload-dependent)**

### Overview

**NUMA** (Non-Uniform Memory Access) is a memory architecture where RAM is physically attached to specific CPU sockets. Memory access from a CPU to its local RAM is faster than accessing RAM attached to a different socket.

**Huge Pages** reduce the number of page table entries the CPU must manage by using larger page sizes instead of the default 4 KB.

### Large / Huge Page Sizes

| Page Size | Availability | TLB Entries Covered | Typical Use |
|-----------|-------------|---------------------|-------------|
| 4 KB | Always (default) | 4 KB | General purpose |
| 2 MB | Windows "Large Pages" | 2048 KB | SQL Server, JVM, Redis, game engines |
| 1 GB | Windows "Large Pages" | 1024 MB | SAP HANA, Oracle, specialized databases |

### How Large Pages Work in Windows

Large pages (2 MB) in Windows require:

1. **SeLockMemoryPrivilege** -- A special user right required to allocate large pages.
2. The system must have enough **contiguous physical memory** to satisfy the allocation.
3. The application must explicitly request large pages (they are not automatic).

**Configuring SeLockMemoryPrivilege:**

```
# Via Group Policy:
# secpol.msc > Local Policies > User Rights Assignment > Lock pages in memory

# Add the user account or service account that needs large pages
# Caution: This grants the ability to lock pages in RAM, preventing them
# from being paged out. Only grant to trusted service accounts.
```

```powershell
# Check who currently has SeLockMemoryPrivilege
whoami /priv | findstr "SeLockMemory"

# View via secpol.msc
secpol.msc
# Navigate to: Local Policies > User Rights Assignment > Lock pages in memory
```

### NUMA Awareness

```powershell
# Check NUMA topology
Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors

# Detailed NUMA info (requires PowerShell 7+ or SystemInfo)
# Task Manager > Performance > CPU shows NUMA nodes on multi-socket systems

# numactl equivalent via PowerShell is limited
# For NUMA-aware allocation, applications use the Windows API:
# - GetNumaHighestNodeNumber()
# - GetNumaProcessorNode()
# - VirtualAllocExNuma()
```

### Applications That Benefit from Large Pages

| Application | Large Page Support | Configuration |
|-------------|-------------------|---------------|
| SQL Server | Yes (2 MB) | Enable via "Lock pages in memory" privilege + SQL config |
| Java JVM | Yes (2 MB) | `-XX:+UseLargePages` flag |
| Redis (Windows port) | Yes (2 MB) | `large-page-mode` configuration option |
| Oracle Database | Yes (2 MB, 1 GB) | `LOCK_SGA` parameter |
| SAP HANA | Yes (1 GB) | OS-level configuration |
| Game Engines (UE5, etc.) | Yes (2 MB) | Engine-level configuration |
| .NET / CLR | Yes (2 MB) | `GCSettings.LargeObjectHeapCompactionMode` (indirect) |

### SQL Server Example

```sql
-- SQL Server large page configuration
-- Requires SeLockMemoryPrivilege for the SQL Server service account

-- Check if large pages are in use
SELECT * FROM sys.dm_os_memory_clerks WHERE page_size_in_bytes = 2097152;

-- Enable large pages (requires restart)
EXEC sp_configure 'show advanced options', 1;
RECONFIGURE;
EXEC sp_configure 'awe enabled', 1;  -- For 32-bit (legacy)
RECONFIGURE;
```

### JVM Example

```bash
# Java large pages configuration
java -XX:+UseLargePages -jar myapp.jar

# Verify large pages are being used
# JVM will print a warning if SeLockMemoryPrivilege is not granted

# Linux equivalent for reference:
# echo 1024 > /proc/sys/vm/nr_hugepages
```

### Risk Assessment

| Configuration | Risk | Notes |
|--------------|------|-------|
| SeLockMemoryPrivilege on desktop | Low-Medium | Only needed if running apps that use large pages |
| Large pages in SQL Server | Low | Standard production configuration |
| Large pages in JVM | Low | Standard production configuration |
| 1 GB pages | Medium | Requires contiguous 1 GB physical blocks; may fail on fragmented systems |
| NUMA pinning (non-default) | Medium-High | Only for experts who understand their NUMA topology |

### Do You Need Large Pages?

**For most desktop users: No.** The performance benefit of large pages is measurable in microbenchmarks but often imperceptible in general desktop use. The benefit becomes significant for:

- Database servers with large memory footprints (> 16 GB).
- JVM applications that allocate large heaps.
- Real-time or latency-sensitive applications (trading systems, audio processing).
- Workloads with heavy TLB pressure (large datasets, random access patterns).

---

## Quick Reference: All Registry Settings

```
HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management

| Value Name            | Type         | Default | Recommended (Desktop) | Recommended (Server) |
|-----------------------|--------------|---------|-----------------------|----------------------|
| PagingFiles           | REG_MULTI_SZ| System  | System managed        | System managed       |
| DisablePagingExecutive | REG_DWORD   | 0       | 1 (8GB+ RAM)          | 1                    |
| LargeSystemCache      | REG_DWORD   | 0       | 0                     | 1 (file server only) |
| IoPageLockLimit       | REG_DWORD   | 0       | 0 (do not modify)     | 0 (do not modify)    |
| SessionPoolSize       | REG_DWORD   | 48      | 48 (do not modify)    | 48 (do not modify)   |
```

## Quick Reference: PowerShell Commands

```powershell
# ---- Memory Compression ----
Get-MMAgent
Enable-MMAgent -MemoryCompression
Disable-MMAgent -MemoryCompression

# ---- SysMain Service ----
Get-Service SysMain
Set-Service -Name SysMain -StartupType Automatic
Set-Service -Name SysMain -StartupType Disabled

# ---- DisablePagingExecutive ----
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name DisablePagingExecutive
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name DisablePagingExecutive -Value 1 -Type DWord

# ---- LargeSystemCache ----
Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name LargeSystemCache
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management" -Name LargeSystemCache -Value 0 -Type DWord

# ---- Memory Overview ----
Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize, FreePhysicalMemory
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 20 Name, @{N='MB'; E={[math]::Round($_.WorkingSet64/1MB)}}

# ---- Memory Diagnostics ----
mdsched.exe
Get-Counter '\Memory\Available MBytes','Memory\Pages/sec' -Continuous -MaxSamples 5

# ---- NUMA / Large Pages ----
whoami /priv | findstr "SeLockMemory"
```

## Quick Reference: Tools

| Tool | Purpose | Source |
|------|---------|--------|
| RAMMap | Physical memory usage analysis | Sysinternals |
| VMMap | Per-process virtual memory analysis | Sysinternals |
| MemTest86+ | Hardware memory testing (boot media) | memtestplus.org |
| Windows Memory Diagnostic | Quick built-in memory test | `mdsched.exe` |
| Performance Monitor | Real-time memory counters | `perfmon.exe` |
| Task Manager | Basic memory overview | `taskmgr.exe` |
| Resource Monitor | Detailed real-time memory view | `resmon.exe` |

---

*Document version: 2026-08-31*
