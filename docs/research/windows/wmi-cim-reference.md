# WinOpt WMI/CIM Infrastructure Reference

## Overview

WMI (Windows Management Instrumentation) is Microsoft's implementation of WBEM (Web-Based Enterprise Management). CIM (Common Information Model) is the modern replacement. WinOpt should use CIM (Get-CimInstance) over legacy WMI (Get-WmiObject).

**Key distinction:** Get-WmiObject uses DCOM (port 135, legacy). Get-CimInstance uses WinRM/WS-Man (port 5985/5986, modern). Always prefer Get-CimInstance.

## Primary Namespace: root\cimv2

This is the main namespace for hardware and OS queries.

### Essential CIM Classes

#### Operating System
```powershell
Get-CimInstance Win32_OperatingSystem
# Properties: Caption, Version, BuildNumber, OSArchitecture, TotalVisibleMemorySize,
# FreePhysicalMemory, LastBootUpTime, NumberOfUsers, FreeVirtualMemory
```

#### Processor
```powershell
Get-CimInstance Win32_Processor
# Properties: Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed,
# CurrentClockSpeed, Architecture, Manufacturer, L2CacheSize, L3CacheSize
```

#### Video Controller (GPU)
```powershell
Get-CimInstance Win32_VideoController
# Properties: Name, AdapterRAM, DriverVersion, DriverDate, VideoProcessor,
# CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate
```

#### Physical Disk
```powershell
Get-CimInstance Win32_DiskDrive
# Properties: Model, InterfaceType (IDE/SCSI/USB), MediaType (Fixed hard disk/External hard disk),
# Size, Partitions, SerialNumber, FirmwareRevision

Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3"
# Properties: DeviceID, Size, FreeSpace, FileSystem, VolumeName, VolumeSerialNumber

Get-PhysicalDisk  # Storage module (preferred)
# Properties: FriendlyName, MediaType (SSD/HDD), BusType (NVMe/SATA/USB), Size, HealthStatus
```

#### Network Adapter
```powershell
Get-CimInstance Win32_NetworkAdapter -Filter "PhysicalAdapter=True"
# Properties: Name, MacAddress, Speed, AdapterType, NetConnectionID, PNPDeviceID

Get-CimInstance Win32_NetworkAdapterConfiguration -Filter "IPEnabled=True"
# Properties: IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder,
# DHCPEnabled, DHCPServer, MACAddress
```

#### Service
```powershell
Get-CimInstance Win32_Service
# Properties: Name, DisplayName, State, StartMode, PathName, StartName,
# Dependencies, FailureActions, ServiceSidType
```

#### Process
```powershell
Get-CimInstance Win32_Process
# Properties: Name, ProcessId, ParentProcessId, CreationDate, CommandLine,
# WorkingSetSize, ThreadCount, HandleCount
```

#### Base Board / BIOS
```powershell
Get-CimInstance Win32_BaseBoard    # Manufacturer, Product, Version
Get-CimInstance Win32_BIOS          # SMBIOSBIOSVersion, Manufacturer, ReleaseDate
Get-CimInstance Win32_ComputerSystem # Manufacturer, Model, TotalPhysicalMemory, NumberOfProcessors
```

#### Battery (Laptop Detection)
```powershell
Get-CimInstance Win32_Battery
# Returns null/empty on desktops — primary laptop detection method
# Properties: BatteryStatus, EstimatedChargeRemaining, BatteryRechargeTime

Get-CimInstance Win32_SystemEnclosure
# ChassisTypes: 8=Portable, 9=Laptop, 10=Notebook, 14=SubNotebook, 30=Tablet
# Desktop types: 3=Mini Tower, 4=Tower, 5=Desktop, 6=Low Profile Desktop
```

#### Memory
```powershell
Get-CimInstance Win32_PhysicalMemory
# Properties: Capacity, Speed, Manufacturer, PartNumber, DeviceLocator, MemoryType
# MemoryType: 20=DDR, 21=DDR2, 24=DDR3, 26=DDR4, 34=DDR5
```

#### Startup Items
```powershell
Get-CimInstance Win32_StartupCommand
# Properties: Name, Command, Location, User
```

## Secondary Namespaces

### root\wmi
Power management and hardware-specific data.

```powershell
Get-CimInstance -Namespace root\wmi -ClassName MSAcpi_ThermalZoneTemperature
# CurrentTemperature (tenths of Kelvin) — CPU thermal monitoring

Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceEnable
# Enables/disables power management per device
```

### root\default
Registry and system configuration.

```powershell
Get-CimInstance -Namespace root\default -ClassName StdRegProv
# Method: GetDWORDValue, GetStringValue, GetBinaryValue
# Alternative to direct registry access in some contexts
```

### root\cimv2\mdm
MDM/Intune device management (Enterprise).

## CIM Session vs Instance

```powershell
# Local queries (default)
Get-CimInstance Win32_OperatingSystem

# Remote queries (requires WinRM)
Get-CimInstance Win32_OperatingSystem -ComputerName "Server01"
$cimSession = New-CimSession -ComputerName "Server01"
Get-CimInstance Win32_OperatingSystem -CimSession $cimSession
Remove-CimSession $cimSession
```

## WMI Query Language (WQL)

```sql
-- Basic SELECT
SELECT * FROM Win32_OperatingSystem

-- WHERE clause
SELECT * FROM Win32_Service WHERE State = 'Running'

-- LIKE for partial match
SELECT * FROM Win32_Service WHERE Name LIKE '%Defender%'

-- IN clause
SELECT * FROM Win32_Processor WHERE Architecture IN (0, 9)

-- ASSOCIATORS query (find relationships)
ASSOCIATORS OF {Win32_Service.Name='Spooler'} WHERE AssocClass=Win32_DependentService

-- References query
REFERENCES OF {Win32_Service.Name='Spooler'} WHERE ClassDefsOnly
```

## Common WinOpt Detection Patterns

### System Profile Detection
```powershell
# Complete system profile for profile selection
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor
$ram = [Math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
$disks = Get-PhysicalDisk
$gpu = Get-CimInstance Win32_VideoController
$battery = Get-CimInstance Win32_Battery

$profile = @{
    OS = $os.Caption
    Build = $os.BuildNumber
    Architecture = $os.OSArchitecture
    CPUCores = $cpu.NumberOfCores
    CPULogical = $cpu.NumberOfLogicalProcessors
    CPUSpeed = $cpu.MaxClockSpeed
    RAM_GB = $ram
    IsLaptop = ($null -ne $battery)
    StorageType = ($disks | Select-Object -First 1).MediaType
    GPUName = $gpu.Name
    GPURAM_MB = $gpu.AdapterRAM / 1MB
}
```

### Hardware Tier Calculation
```powershell
# CPU Tier
$cpuTier = switch ($cpu.NumberOfCores) {
    { $_ -ge 12 } { "Ultra" }
    { $_ -ge 8 }  { "High" }
    { $_ -ge 6 }  { "Mid" }
    { $_ -ge 4 }  { "Low" }
    default        { "VeryLow" }
}

# RAM Tier
$ramTier = switch ($ram) {
    { $_ -ge 32 } { "Ultra" }
    { $_ -ge 16 } { "High" }
    { $_ -ge 8 }  { "Mid" }
    { $_ -ge 4 }  { "Low" }
    default        { "VeryLow" }
}

# Storage Tier
$storageTier = switch ($disks[0].MediaType) {
    "SSD" { if ($disks[0].BusType -eq "NVMe") { "NVMe" } else { "SATA-SSD" } }
    "HDD" { "HDD" }
    default { "Unknown" }
}
```

### Service State Detection
```powershell
# Get all services with optimization-relevant info
Get-Service | ForEach-Object {
    $svc = $_
    $cim = Get-CimInstance Win32_Service -Filter "Name='$($svc.Name)'" -ErrorAction SilentlyContinue
    [PSCustomObject]@{
        Name = $svc.Name
        Status = $svc.Status
        StartType = $cim.StartMode
        RunAs = $cim.StartName
        PID = $cim.ProcessId
        Dependencies = ($cim.Dependencies | ForEach-Object { $_.Name }) -join ', '
    }
} | Sort-Object StartType | Format-Table -AutoSize
```

### Startup Impact Detection
```powershell
# Enumerate all startup items
$startup = Get-CimInstance Win32_StartupCommand
$startup | Select-Object Name, Location, Command, User |
    Sort-Object Location | Format-Table -AutoSize

# Also check registry Run keys
$regKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
)
foreach ($key in $regKeys) {
    if (Test-Path $key) {
        Get-ItemProperty $key | Select-Object * -ExcludeProperty PS* |
            Format-List
    }
}
```

## Performance Counter Integration

```powershell
# CPU utilization
Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 2 -MaxSamples 5

# Memory
Get-Counter '\Memory\Available MBytes'
Get-Counter '\Memory\% Committed Bytes In Use'

# Disk
Get-Counter '\PhysicalDisk(_Total)\Avg. Disk sec/Read'
Get-Counter '\PhysicalDisk(_Total)\Avg. Disk Queue Length'

# Network
Get-Counter '\Network Interface(*)\Bytes Total/sec'

# GPU
Get-Counter '\GPU Engine(*)\Utilization Percentage'
```

## Key WMI/CIM Gotchas

1. **Use Get-CimInstance** — Get-WmiObject uses legacy DCOM, may fail on newer Windows
2. **AdapterRAM in Win32_VideoController** is limited to 4GB — for GPUs with >4GB, check registry or vendor tools
3. **Win32_Battery returns null on desktops** — use for laptop detection, not battery health
4. **ChassisTypes varies by OEM** — always combine with battery check for reliable laptop detection
5. **Permissions** — some WMI namespaces require elevation (root\wmi especially)
6. **Win32_Processor.Architecture** — 0=x86, 9=AMD64, 12=ARM64
7. **MemoryType values** — 20=DDR, 24=DDR3, 26=DDR4, 34=DDR5
