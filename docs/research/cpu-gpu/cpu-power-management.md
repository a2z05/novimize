# CPU Power Management on Windows

Comprehensive reference for Windows CPU power plans, processor settings, frequency
technologies, platform differences, power throttling, and risk assessment.

---

## Table of Contents

1. [Power Plans](#1-power-plans)
2. [Processor Power Settings (SUB_PROCESSOR)](#2-processor-power-settings-sub_processor)
3. [CPU Frequency Technologies](#3-cpu-frequency-technologies)
4. [Desktop vs Laptop Differences](#4-desktop-vs-laptop-differences)
5. [Power Throttling](#5-power-throttling)
6. [Risk Assessment](#6-risk-assessment)

---

## 1. Power Plans

### Built-In Power Plan GUIDs

| Plan Name              | GUID                                  | Description                                    |
|------------------------|---------------------------------------|------------------------------------------------|
| Power Saver            | `a1841308-3541-4fab-bc81-f71556f20b4a` | Minimizes power consumption; throttles performance |
| Balanced               | `381b4222-f694-41f0-9685-ff5bb260df2e` | Dynamically scales between performance and power |
| High Performance       | `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` | Favors maximum performance; minimal throttling    |
| Ultimate Performance   | `e9a42b02-d5df-448d-aa00-03f14749eb61` | No throttling; eliminates micro-latencies         |
| Power Saver (Legacy)   | `9596fb26-9850-41fd-ac3e-f7c3c00afd4b` | Older GUID, same behavior as a1841308            |

> **Note:** Ultimate Performance is hidden by default on consumer Windows. It
> originates from Windows 10 Pro for Workstations and Windows 11. See the
> activation command below.

### Power Plan Differences

| Setting                | Power Saver | Balanced   | High Performance | Ultimate Performance |
|------------------------|-------------|------------|------------------|----------------------|
| Min Processor State     | 5%          | 5%         | 100%             | 100%                 |
| Max Processor State     | 100%        | 100%       | 100%             | 100%                 |
| Energy Performance Pref | 0 (Max E)   | 50 (Bal.)  | 100 (Max Perf)   | 100 (Max Perf)       |
| Core Parking Min Cores  | 0%          | 10%        | 100%             | 100%                 |
| Core Parking Max Cores  | 100%        | 100%       | 100%             | 100%                 |
| Turbo Boost             | Aggressive  | Enabled    | Aggressive       | Aggressive           |
| System Cooling Policy   | Passive     | Active (D) | Active           | Active               |
| Display Off Timer       | 5 min       | 10 min     | 15 min           | 15 min               |
| Sleep Timer             | 15 min      | 30 min     | Never            | Never                |
| Hibernate Timer         | 60 min      | 180 min    | Never            | Never                |
| PCI Express Link State  | Max Savings | Moderate   | Off              | Off                  |

### powercfg Command Reference

```powershell
# ---- Plan Management ----

# List all power plans
powercfg /list

# Show the active plan
powercfg /getactivescheme

# Activate a plan by GUID
powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c

# Duplicate an existing plan (creates a new GUID)
powercfg /duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c

# Rename the duplicated plan (use the new GUID from /list)
powercfg /changename GUID "My Custom Plan" "Description"

# Delete a custom plan (cannot delete built-in plans)
powercfg /delete GUID

# ---- Querying Settings ----

# Full query of a plan (verbose, sub-group and setting names resolved)
powercfg /query

# Query specific sub-group (e.g., SUB_PROCESSOR)
powercfg /query SCHEME_CURRENT SUB_PROCESSOR

# Query with GUID
powercfg /query 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c SUB_PROCESSOR

# Query a specific setting by GUID
powercfg /query SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 bc5038f7-4520-4b63-82b1-97ff4bfb40c1

# Query in hex (for scripts parsing output)
powercfg /qH SCHEME_CURRENT SUB_PROCESSOR

# ---- Export / Import ----

# Export a plan to an XML file
powercfg /export D:\backups\power-plan.xml 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c

# Import a plan from XML
powercfg /import D:\backups\power-plan.xml

# Import and assign a custom GUID
powercfg /import D:\backups\power-plan.xml 12345678-abcd-1234-abcd-123456789abc

# ---- Modifying Settings ----

# Change the active plan (legacy syntax)
powercfg /change monitor-timeout-ac 10
powercfg /change standby-timeout-ac 0
powercfg /change processor-throttle-min 5
powercfg /change processor-throttle-max 100

# Set specific sub-group/setting (modern syntax)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5
powercfg /setactive SCHEME_CURRENT

# ---- Diagnostics ----

# Hibernate on/off
powercfg /h on
powercfg /h off
powercfg /h size 100          # Set hibernate file size (% of RAM)
powercfg /h type full         # Full or reduced hibernation

# Energy report (generates HTML in current directory)
powercfg /energy

# Battery report (generates HTML in current directory)
powercfg /batteryreport

# Sleep study (generates HTML for connected standby / Modern Standby)
powercfg /sleepstudy

# View power requests (who is blocking sleep)
powercfg /requests

# View sleep capabilities
powercfg /availablesleepstates

# View device wake sources
powercfg /devicewake

# Disable a wake source
powercfg /devicedisablewake "Realtek PCIe GbE Family Controller"
```

### Enable Ultimate Performance Plan

```powershell
# Step 1: Create the hidden Ultimate Performance plan
powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61

# Step 2: Note the new GUID from the output, then activate it
powercfg /setactive <new-GUID>

# Verify
powercfg /getactivescheme
```

---

## 2. Processor Power Settings (SUB_PROCESSOR)

The SUB_PROCESSOR power sub-group controls all CPU-related power behavior.

```
Sub-GUID:  54533251-82be-4824-96c1-47b60b740d00
           (SUB_PROCESSOR)
```

### Setting GUIDs and Reference

#### 2.1 Minimum Processor State

```
GUID:    bc5038f7-4520-4b63-82b1-97ff4bfb40c1
Values:  0-100 (percent)
Default: 100% (High Perf), 5% (Balanced/Saver)
         DC (battery): typically 5% regardless of plan
```

The minimum processor state sets the lowest frequency the CPU can drop to. A value
of 100% forces the CPU to run at its base clock or higher at all times. A value
of 0% allows deep idle states.

```powershell
# Set minimum to 100% on AC, 5% on DC
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5
powercfg /setactive SCHEME_CURRENT
```

#### 2.2 Maximum Processor State

```
GUID:    75b0ae3f-ce98-4a65-a695-ed0bafca55c4
Values:  0-100 (percent)
Default: 100% (all plans)
```

Maximum frequency cap. Reducing this below 100% effectively disables Turbo Boost
and underclocks the CPU. Useful for thermal management.

```powershell
# Cap at 90% on AC for thermal reduction
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 90
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setactive SCHEME_CURRENT
```

#### 2.3 Processor Performance Boost Mode

```
GUID:    be337238-0d82-4146-a960-4f3749d470c7
Values:  
  0 = Disabled             (no boost at all)
  1 = Enabled              (standard aggressive boost)
  2 = Efficient Enabled    (energy-efficient boost, Intel HWP)
  3 = Aggressive at Guaranteed (boost only above guaranteed freq.)
  4 = Efficient Aggressive (efficient aggressive, best perf/watt boost)
Default: 2 (Efficient Enabled) on most modern plans
```

This controls how aggressively the CPU boosts beyond base frequency.

```powershell
# Disable turbo boost entirely
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0

# Aggressive boost (max performance)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 1

# Efficient enabled (default, balanced)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2

# Efficient aggressive (best perf/watt)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 4
powercfg /setactive SCHEME_CURRENT
```

#### 2.4 Processor Performance Boost Policy

```
GUID:    45bcc044-d885-43e2-8605-9484eeba01a5
Values:  0-100 (percent)
Default: 50-60 depending on plan
```

Controls the aggressiveness of turbo boost. Higher values push the CPU to sustain
boost clocks longer under load.

```powershell
# Max boost policy (sustain turbo)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTPOL 100

# Conservative boost
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTPOL 25
powercfg /setactive SCHEME_CURRENT
```

#### 2.5 Core Parking

Core Parking allows the OS to put individual CPU cores into a deep sleep (C6/C8)
when they have no work.

```powershell
# Core Parking Min Cores - minimum percentage of cores that stay awake
# GUID: 0cc5b647-c2df-4625-9595-35a4215b4782
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 50

# Core Parking Max Cores - maximum percentage of cores that can be active
# GUID: ea062031-0d34-458f-b074-25e4e0e8b90a
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 80

# Core Parking Processor Performance Increase Threshold
# GUID: fa4d4f19-457a-4b28-8782-d5ae44590ea3
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPPERFPCTTHRESHOLD 100

# Core Parking Processor Performance Decrease Threshold
# GUID: 7cc83f1a-5654-422b-a26e-1f465e9f6d28
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPPERFDECTHRESHOLD 0
powercfg /setactive SCHEME_CURRENT
```

| Setting                      | Power Saver | Balanced | High Perf | Ultimate Perf |
|------------------------------|-------------|----------|-----------|---------------|
| Core Parking Min Cores       | 0%          | 10%      | 100%      | 100%          |
| Core Parking Max Cores       | 100%        | 100%     | 100%      | 100%          |
| Parking Increase Threshold   | 10%         | 40%      | 100%      | 100%          |
| Parking Decrease Threshold   | 100%        | 70%      | 0%        | 0%            |

#### 2.6 Energy Performance Preference (EPP)

```
GUID:    3668cc6e-1f95-4a3c-94f8-188cd33e8a15
Values:  0-100 (0 = max energy savings, 100 = max performance)
Default: 0 (Power Saver), 50 (Balanced), 100 (High/Ultimate Perf)
```

EPP is a hint to hardware (especially Intel Speed Shift / HWP) about the
performance-vs-efficiency tradeoff. The CPU firmware translates this into actual
voltage and frequency decisions in microseconds.

```powershell
# Max energy savings
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR EPP 0

# Balanced
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR EPP 50

# Max performance
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR EPP 100
powercfg /setactive SCHEME_CURRENT
```

#### 2.7 Processor Idle

```powershell
# Processor Idle Disable
# GUID: 5d76a2ca-e8c0-402f-a133-2158492d58ad
# Values: 0 = idle allowed (default), 1 = idle disabled
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR IDLEDISABLE 0

# Processor Idle Threshold (for low-power state selection)
# GUID: 4d2b0126-4d2b-4e0a-ae2c-a409a1daf084
# Values: 0-100 (higher = deeper idle states used)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR IDLETHRESHOLD 50
powercfg /setactive SCHEME_CURRENT
```

#### 2.8 System Cooling Policy

```
GUID:    0cc5b647-c2df-4625-9595-35a4215b4783  (when under SUB_PROCESSOR)
Values:  
  0 = Passive  (throttle CPU before increasing fan speed)
  1 = Active   (increase fan speed before throttling CPU)
Default: 0 (Passive) on DC/battery, 1 (Active) on AC for desktops
```

```powershell
# AC: Active cooling (fans ramp up first)
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR SYSCOOLPOL 1

# DC: Passive cooling (throttle CPU to save battery)
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR SYSCOOLPOL 0
powercfg /setactive SCHEME_CURRENT
```

### Complete SUB_PROCESSOR Modification Example

```powershell
# Create a custom high-performance plan
powercfg /duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
# Note the GUID from output, replace below

$plan = "YOUR-NEW-GUID-HERE"

# All processor settings
powercfg /setacvalueindex $plan SUB_PROCESSOR PROCTHROTTLEMIN 100
powercfg /setacvalueindex $plan SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setacvalueindex $plan SUB_PROCESSOR PERFBOOSTMODE 1
powercfg /setacvalueindex $plan SUB_PROCESSOR PERFBOOSTPOL 100
powercfg /setacvalueindex $plan SUB_PROCESSOR CPMINCORES 100
powercfg /setacvalueindex $plan SUB_PROCESSOR CPMAXCORES 100
powercfg /setacvalueindex $plan SUB_PROCESSOR EPP 100
powercfg /setacvalueindex $plan SUB_PROCESSOR IDLEDISABLE 0
powercfg /setacvalueindex $plan SUB_PROCESSOR SYSCOOLPOL 1

# Activate
powercfg /setactive $plan
```

---

## 3. CPU Frequency Technologies

### 3.1 Intel Turbo Boost

Turbo Boost dynamically increases clock speed above the base frequency when
thermal and power headroom allow.

| Version   | Introduced | Max Turbo Ratio Increase | Notes                                    |
|-----------|------------|--------------------------|------------------------------------------|
| 1.0       | Nehalem    | +2 bins                  | First generation, single/multi-core      |
| 2.0       | Sandy Bridge | +2 bins (further)      | More aggressive, same power envelope     |
| 3.0       | Broadwell  | +2 bins (single-core)    | Per-core turbo, thermal velocity boost   |
| Max 3.0   | Skylake-X  | +4 bins (single-core)    | Up to 400 MHz above Turbo Boost 3.0      |

**Power Limit Registers (PL1/PL2/PL4):**

| Register | Name                        | Default         | Duration | Purpose                                |
|----------|-----------------------------|-----------------|----------|----------------------------------------|
| PL1      | Long Duration Power Limit   | TDP (e.g. 65W)  | Sustained | Long-term sustained power cap          |
| PL2      | Short Duration Power Limit  | ~1.25x TDP      | 28s      | Short burst turbo allowance             |
| PL4      | Peak Power Limit            | ~2x TDP          | <1ms     | Ultra-short transient spike allowance  |

```powershell
# Check current power limits (requires HWiNFO or Intel XTU)
# Windows powercfg does not expose PL values directly.

# Reduce PL2 indirectly via Max Processor State
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 80
```

### 3.2 Intel SpeedStep (EIST)

Enhanced Intel SpeedStep Technology (EIST) is the OS-level DVFS (Dynamic Voltage
and Frequency Scaling) mechanism. The OS requests P-states via ACPI, and the
processor transitions between frequency/voltage pairs.

- Controlled by **Minimum/Maximum Processor State** in powercfg
- Transitions take milliseconds (OS-driven)
- Works with both P-states (performance) and C-states (idle)

### 3.3 Intel Speed Shift (HWP)

Speed Shift moves frequency control from the OS to the CPU hardware, enabling
microsecond-level transitions instead of millisecond-level.

| Aspect         | SpeedStep (OS-driven)  | Speed Shift (HWP)      |
|----------------|------------------------|------------------------|
| Control        | OS firmware            | CPU hardware           |
| Transition     | ~30-50 ms             | ~1-10 us              |
| Granularity    | Full P-state table     | Per-core, continuous   |
| EPP mapping    | Not used               | Maps to EPP value      |
| Enabled by     | BIOS + OS              | BIOS + Windows 10+    |

Speed Shift is activated when:
1. BIOS enables HWP (also called Hardware P-states)
2. Windows sets the EPP value via `SUB_PROCESSOR EPP`
3. CPU supports HWP (Intel Skylake and later)

```powershell
# HWP is automatically active when EPP is set
# Lower EPP = CPU favors deeper sleep states faster
# Higher EPP = CPU stays at higher frequencies longer
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR EPP 100
```

### 3.4 CPPC (Collaborative Processor Performance Control)

CPPC is the ACPI standard that both Intel and AMD use for hardware-controlled
performance. It is the successor to the OSPM (OS Performance Management) model.

**CPPC2 (Performance EPP):** Used by Windows for HWP/Speed Shift. The OS writes
a performance preference value; the CPU hardware decides exact frequency.

**CPPC (Nominal):** Lower-performance fallback. The OS specifies a desired
performance level; the CPU maps it to a frequency.

```powershell
# Windows CPPC is enabled/disabled via registry (advanced users)
# HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings
#   54533251-82be-4824-96c1-47b60b740d00 (SUB_PROCESSOR)
```

### 3.5 AMD Precision Boost

**Precision Boost 2 (PB2):** AMD's equivalent of Turbo Boost. Increases frequency
beyond base clock based on temperature, current, and power delivery. PB2 operates
on a per-core basis and can boost any core as long as the thermal and power
limits allow.

**Precision Boost Overdrive (PBO):** Extends PB2 limits:
- Increases PPT (Package Power Tracking) -- analogous to PL1
- Increases TDC (Thermal Design Current) -- sustained current limit
- Increases EDC (Electrical Design Current) -- peak current limit

```powershell
# PBO cannot be controlled from Windows powercfg.
# PBO settings are in BIOS/UEFI under AMD CBS or OC settings.
# Windows only sees the resulting turbo behavior through standard
# SUB_PROCESSOR settings.
```

| AMD Setting | Intel Equivalent | Purpose                  |
|-------------|------------------|--------------------------|
| PPT         | PL1              | Package power limit       |
| TDC         | PL2 (current)    | Sustained current limit   |
| EDC         | PL4              | Peak current limit        |
| PB2         | Turbo Boost 2.0  | Per-core dynamic boosting |

### 3.6 ACPI P-states and C-states

**P-states** (Performance states) define different frequency/voltage operating
points:

| State | Name        | Description                                    |
|-------|-------------|------------------------------------------------|
| P0    | Maximum     | Highest frequency, highest voltage              |
| P1    | Below Max   | Reduced from P0 by one turbo bin                |
| P2+   | Progressive | Further reduced frequency and voltage           |
| Pn    | Minimum     | Lowest non-idle performance state               |

**C-states** (idle states) define low-power idle depths:

| State | Name      | Power   | Wake Latency | Description                          |
|-------|-----------|---------|--------------|--------------------------------------|
| C0    | Active    | Full    | 0            | CPU is executing instructions        |
| C1    | Halt      | ~30%    | 1 us         | Core clock stopped, pipeline flushed |
| C1E   | Enhanced  | ~20%    | 1 us         | C1 + voltage reduction               |
| C3    | Sleep     | ~10%    | 50-100 us    | L1 cache flushed                     |
| C6    | Deep Sleep | ~5%    | 100-200 us   | L2 cache flushed, voltage near zero  |
| C7    | Deeper    | ~2%     | 200-500 us   | L3 cache shared, deeper sleep        |
| C8    | Deepest   | ~1%     | 500+ us      | Platform-assisted sleep              |
| C10   | Deepest++ | <1%     | >1 ms        | Maximum platform sleep               |

> **Trade-off:** Deeper C-states save more idle power but take longer to exit.
> This is why high-performance plans reduce core parking and minimum processor
> state -- to avoid the latency of waking from deep idle.

---

## 4. Desktop vs Laptop Differences

### Default Power Settings Comparison

| Setting                  | Desktop (Balanced) | Laptop on AC (Balanced) | Laptop on DC (Balanced) |
|--------------------------|--------------------|-------------------------|-------------------------|
| Min Processor State       | 5%                 | 5%                      | 5%                      |
| Max Processor State       | 100%               | 100%                    | 100%                    |
| Core Parking Min Cores    | 10%                | 10%                     | 0%                      |
| System Cooling Policy     | Active             | Active                  | Passive                 |
| EPP                      | 50                 | 50                      | 40                      |
| Display Off (AC/DC)       | 15 min / 10 min    | 15 min / 5 min          |                         |
| Sleep (AC/DC)             | Never / 30 min     | Never / 15 min          |                         |
| Hibernate (AC/DC)         | Never / 300 min    | Never / 180 min         |                         |
| PCI Express ASPM          | Off                | Moderate                | Max Power Savings       |

### AC vs DC Power Differences

```
AC Power (Plugged In)                    DC Power (Battery)
========================                 ========================
- Higher performance budgets             - Aggressive power savings
- Active cooling preferred               - Passive cooling (throttle before fan)
- Turbo Boost sustained longer           - Turbo Boost duration reduced
- Core parking disabled                   - Deep core parking enabled
- Higher EPP (performance preference)     - Lower EPP (energy preference)
- Display timeout extended                - Display timeout short
- No sleep / hibernate timeout            - Short sleep / hibernate timers
```

### Why Laptops Need Different Treatment

1. **Thermal Envelope:** Laptops have constrained cooling (heatpipes, small
   fans). The same TDP cannot be sustained as on a desktop with a tower cooler.

2. **Battery Life:** Every watt matters on battery. Deep C-states and core
   parking can extend battery life by 30-60% at idle.

3. **Skin Temperature:** OEMs cap sustained power to keep the chassis below
   ~45 degrees C at contact points (keyboard deck, bottom). This is separate from
   silicon junction temperature.

4. **Noise:** Laptop fans are small and loud at high RPM. Passive cooling at
   moderate loads keeps the system quiet.

5. **Power Delivery:** Battery provides limited peak current. Aggressive boost
   on DC can cause voltage droop and system instability.

```powershell
# Laptop-specific: aggressive battery savings
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 0
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 80
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR EPP 40
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR SYSCOOLPOL 0
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2
```

---

## 5. Power Throttling

### Registry Location

```
HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle

Key:     HKLM\...\PowerThrottle
Value:   PowerThrottleEnable (REG_DWORD)
         0 = Power throttling disabled
         1 = Power throttling enabled (default)
```

### Power Slider Mapping

Windows 10/11 exposes a power slider in the battery/flyout UI. Each position
maps to a specific Power Throttle Level:

| Slider Position     | Throttle Level | Behavior                                        |
|---------------------|----------------|--------------------------------------------------|
| Best Battery        | 0              | Maximum throttling, minimum performance           |
| Better Battery      | 10-20          | Significant throttling for battery savings        |
| Better Performance  | 40-60          | Moderate throttling, balanced approach             |
| Best Performance    | 100            | No throttling, maximum performance                |

The throttle level controls the CPU utilization budget that background and
foreground applications receive.

### Per-Application Override

Each process can have its own throttle override via the registry:

```
HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle\PerApp\{ProcessName}

Or via the Power Throttle API:
SetProcessInformation(hProcess, ProcessPowerThrottling,
    &powerThrottling, sizeof(POWER_THROTTLING_STATE));
```

```powershell
# Disable power throttling globally
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle" `
    -Name "PowerThrottleEnable" -Value 0 -Type DWord

# Re-enable power throttling
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle" `
    -Name "PowerThrottleEnable" -Value 1 -Type DWord
```

**Note:** Disabling power throttling globally increases power consumption and
thermal output. Consider per-application overrides for specific workloads like
audio processing, DAWs, or real-time applications.

```powershell
# Check current power throttle state
Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle" `
    -Name "PowerThrottleEnable"
```

### Power Throttling and Power Plans

| Power Plan           | Slider Default     | Throttle Level |
|----------------------|--------------------|----------------|
| Power Saver          | Best Battery       | 0-10           |
| Balanced             | Better Performance | 40-60          |
| High Performance     | Best Performance   | 100            |
| Ultimate Performance | Best Performance   | 100            |

---

## 6. Risk Assessment

### Setting Risk Table

| Setting                     | Risk    | Impact if Changed | Mitigation                                    |
|-----------------------------|---------|--------------------|----------------------------------------------|
| Min Processor State = 100%  | LOW     | Higher idle power  | Use only on AC; revert to 5% on battery       |
| Min Processor State = 0%    | LOW     | Slightly slower wake from idle | Negligible on modern CPUs            |
| Max Processor State < 100%  | MEDIUM  | Reduced peak perf  | Test workload; monitor for throttling         |
| Max Processor State = 100%  | LOW     | Full CPU available | Ensure adequate cooling                       |
| Boost Mode = 0 (Disabled)   | HIGH    | No turbo boost     | Massive single-threaded perf loss (30-50%)    |
| Boost Mode = 1 (Aggressive) | LOW     | More heat          | Monitor temps; ensure cooling headroom        |
| Boost Mode = 4 (Eff. Aggr)  | LOW     | Balanced boost     | Best default for modern Intel CPUs            |
| Core Parking Min = 100%     | MEDIUM  | Higher idle power  | All cores stay awake at all times             |
| Core Parking Min = 0%       | MEDIUM  | Added wake latency | Core wake can add 50-200us latency            |
| EPP = 100                  | LOW     | Max frequency bias | CPU may not idle efficiently                  |
| EPP = 0                    | LOW     | Max energy savings | CPU may not boost as aggressively             |
| Idle Disable = 1           | HIGH    | No C-states        | CPU never sleeps; massive idle power waste    |
| System Cooling = Passive   | LOW     | CPU throttles first | Acceptable on laptop; avoid on desktop        |
| System Cooling = Active    | LOW     | Fans ramp first    | More noise, less thermal throttling           |
| Power Throttle = Disabled  | MEDIUM  | No background lim  | All processes get full CPU; battery drains    |
| Hibernation = Off          | LOW     | No hibernate file  | Uses more disk space; slower deep sleep       |
| Hibernation = On           | LOW     | Hiberfil.sys       | Takes disk space (up to RAM size)             |
| PCI Express ASPM = Off     | MEDIUM  | PCIe devices stay  | Higher idle power for GPU, NVMe, NIC          |
| PCI Express ASPM = Max     | LOW     | PCIe link sleep    | Added wake latency for PCIe devices           |
| Ultimate Perf Plan Active  | HIGH    | No throttling ever | Aggressive; use only for sustained workloads  |

### Risk Level Definitions

| Level   | Meaning                                                        |
|---------|----------------------------------------------------------------|
| LOW     | Safe for general use. Reversible with no side effects.         |
| MEDIUM  | May affect performance or power. Test before production use.   |
| HIGH    | Significant system behavior change. Monitor thermals and power.|

### Recommended Configurations

**Workstation (Maximum Performance):**
```powershell
# Start from High Performance, enable Ultimate Performance
powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
powercfg /setactive <new-GUID>
# Disable power throttling
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle" `
    -Name "PowerThrottleEnable" -Value 0
```

**Desktop Balanced (Daily Use):**
```powershell
powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e
# Keep all defaults; only disable PCI Express ASPM for latency-sensitive work
```

**Laptop Battery-Optimized:**
```powershell
powercfg /setactive a1841308-3541-4fab-bc81-f71556f20b4a
# Verify passive cooling on DC
powercfg /query SCHEME_CURRENT SUB_PROCESSOR SYSCOOLPOL
```

**Audio/DAW (Low Latency):**
```powershell
powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR IDLEDISABLE 1
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100
# Disable power throttling globally or per-DAW process
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottle" `
    -Name "PowerThrottleEnable" -Value 0
```

---

## Quick Reference: All Key GUIDs

```
SUB_PROCESSOR GUID:           54533251-82be-4824-96c1-47b60b740d00

  Min Processor State:        bc5038f7-4520-4b63-82b1-97ff4bfb40c1
  Max Processor State:        75b0ae3f-ce98-4a65-a695-ed0bafca55c4
  Boost Mode:                 be337238-0d82-4146-a960-4f3749d470c7
  Boost Policy:               45bcc044-d885-43e2-8605-9484eeba01a5
  Core Parking Min Cores:     0cc5b647-c2df-4625-9595-35a4215b4782
  Core Parking Max Cores:     ea062031-0d34-458f-b074-25e4e0e8b90a
  EPP:                        3668cc6e-1f95-4a3c-94f8-188cd311e889
  Processor Idle Disable:     5d76a2ca-e8c0-402f-a133-2158492d58ad
  System Cooling Policy:      (use SYSCOOLPOL name with powercfg)
  Performance Boost Max:      75b0ae3f-ce98-4a65-a695-ed0bafca55c3
```

---

*Last updated: 2026-08-31*
