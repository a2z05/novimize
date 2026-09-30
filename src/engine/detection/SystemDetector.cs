using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Detection;

/// <summary>
/// Detects complete system information via WMI/CIM, registry,
/// and PowerShell commands. Used for profile selection and
/// tweak compatibility filtering.
/// </summary>
public sealed class SystemDetector
{
    /// <summary>
    /// Detect complete system information.
    /// </summary>
    public async Task<SystemInfo> DetectAsync()
    {
        var os = DetectOs();
        var cpu = DetectCpu();
        var (ramGb, ramTier) = DetectRam();
        var (storageType, storageModel, storageSizeGb, storageTier) = DetectStorage();
        var (gpuName, gpuVendor, gpuRamMb, gpuTier) = DetectGpu();
        var formFactor = DetectFormFactor();
        var (nicName, nicSpeed, isWireless) = DetectNetwork();
        var (powerPlan, powerGuid, onAc) = DetectPower();
        var powerSettings = DetectPowerSettings();

        var cpuTier = SystemInfo.ClassifyCpuTier(cpu.cores, cpu.clockMhz);
        var overallTier = SystemInfo.CalculateOverallTier(cpuTier, ramTier, storageTier, gpuTier);

        return new SystemInfo
        {
            OsCaption = os.caption,
            OsVersion = os.version,
            BuildNumber = os.build,
            OsArchitecture = os.arch,
            OsEdition = SystemInfo.DetectEdition(os.caption),

            CpuName = cpu.name,
            CpuVendor = SystemInfo.DetectCpuVendor(cpu.name),
            CpuCores = cpu.cores,
            CpuLogicalProcessors = cpu.logical,
            CpuMaxClockMhz = cpu.clockMhz,
            CpuTier = cpuTier,

            RamTotalGb = ramGb,
            RamTier = ramTier,

            PrimaryStorageType = storageType,
            PrimaryStorageModel = storageModel,
            PrimaryStorageSizeGb = storageSizeGb,
            StorageTier = storageTier,

            GpuName = gpuName,
            GpuVendor = gpuVendor,
            GpuRamMb = gpuRamMb,
            GpuTier = gpuTier,

            FormFactor = formFactor,

            PrimaryNicName = nicName,
            PrimaryNicSpeed = nicSpeed,
            IsWireless = isWireless,

            ActivePowerPlan = powerPlan,
            ActivePowerPlanGuid = powerGuid,
            OnAcPower = onAc,
            PowerSettings = powerSettings,

            OverallTier = overallTier
        };
    }

    private (string caption, string version, int build, string arch) DetectOs()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
            using var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

            if (obj == null) return ("Unknown", "Unknown", 0, "Unknown");

            var buildStr = obj["BuildNumber"]?.ToString() ?? "0";
            int.TryParse(buildStr, out var build);

            return (
                obj["Caption"]?.ToString() ?? "Unknown",
                obj["Version"]?.ToString() ?? "Unknown",
                build,
                obj["OSArchitecture"]?.ToString() ?? "Unknown"
            );
        }
        catch { return ("Unknown", "Unknown", 0, "Unknown"); }
    }

    private (string name, int cores, int logical, int clockMhz) DetectCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            using var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

            if (obj == null) return ("Unknown", 0, 0, 0);

            int.TryParse(obj["NumberOfCores"]?.ToString() ?? "0", out var cores);
            int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString() ?? "0", out var logical);
            int.TryParse(obj["MaxClockSpeed"]?.ToString() ?? "0", out var clock);

            return (
                obj["Name"]?.ToString()?.Trim() ?? "Unknown",
                cores, logical, clock
            );
        }
        catch { return ("Unknown", 0, 0, 0); }
    }

    private (double ramGb, HardwareTier tier) DetectRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
            using var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();

            if (obj == null) return (0, HardwareTier.VeryLow);

            long.TryParse(obj["TotalVisibleMemorySize"]?.ToString() ?? "0", out var kb);
            var gb = Math.Round(kb / 1048576.0, 2);
            return (gb, SystemInfo.ClassifyRamTier(gb));
        }
        catch { return (0, HardwareTier.VeryLow); }
    }

    private (StorageType type, string model, double sizeGb, HardwareTier tier) DetectStorage()
    {
        try
        {
            // Prefer MSFT_PhysicalDisk (accurate MediaType/BusType), fall back to Win32_DiskDrive
            var candidates = TryGetPhysicalDisksViaMsft();
            if (candidates.Count == 0)
                candidates = GetAllWin32DiskDrives();

            if (candidates.Count == 0) return (StorageType.Unknown, "Unknown", 0, HardwareTier.Mid);

            // Prefer the disk hosting C:; otherwise pick highest tier, then largest
            var systemDisk = PickSystemDisk(candidates);
            var best = systemDisk ?? PickBestDisk(candidates);
            return (best.Type, best.Model, best.SizeGb, SystemInfo.ClassifyStorageTier(best.Type));
        }
        catch { return (StorageType.Unknown, "Unknown", 0, HardwareTier.Mid); }
    }

    private sealed record DiskCandidate(StorageType Type, string Model, double SizeGb, int TierRank);

    private List<DiskCandidate> TryGetPhysicalDisksViaMsft()
    {
        var list = new List<DiskCandidate>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk");
            foreach (ManagementObject obj in searcher.Get())
            {
                var model = obj["FriendlyName"]?.ToString()?.Trim() ?? "Unknown";
                var mediaTypeRaw = obj["MediaType"]?.ToString() ?? "";
                var busTypeRaw = obj["BusType"]?.ToString() ?? "";
                long.TryParse(obj["Size"]?.ToString() ?? "0", out var sizeBytes);
                var sizeGb = Math.Round(sizeBytes / 1073741824.0, 2);

                // MediaType: 3=HDD, 4=SSD, 5=SCM — also accept string names
                string mediaType = mediaTypeRaw switch
                {
                    "3" => "HDD", "4" => "SSD", "5" => "SSD", _ => mediaTypeRaw
                };
                string busType = busTypeRaw switch
                {
                    "17" => "NVMe", "1" => "SCSI", "11" => "SATA", "7" => "USB",
                    "8" => "RAID", _ => busTypeRaw
                };

                StorageType type;
                if (mediaType.Equals("SSD", StringComparison.OrdinalIgnoreCase))
                    type = busType.Equals("NVMe", StringComparison.OrdinalIgnoreCase) ? StorageType.NVMe : StorageType.SataSSD;
                else if (mediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase))
                    type = StorageType.HDD;
                else
                    type = DetermineStorageTypeFromBus(busType, model);

                list.Add(new DiskCandidate(type, model, sizeGb, TierRank(type)));
            }
        }
        catch { /* namespace may not exist on some SKUs */ }
        return list;
    }

    private List<DiskCandidate> GetAllWin32DiskDrives()
    {
        var list = new List<DiskCandidate>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType, Size, BusType FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString()?.Trim() ?? "Unknown";
                var mediaType = obj["MediaType"]?.ToString() ?? "Unknown";
                var busType = obj["BusType"]?.ToString() ?? "";
                long.TryParse(obj["Size"]?.ToString() ?? "0", out var sizeBytes);
                var sizeGb = Math.Round(sizeBytes / 1073741824.0, 2);

                var type = mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
                    ? SystemInfo.ClassifyStorageType("SSD", busType)
                    : DetermineStorageTypeFromBus(busType, model);

                // Win32_DiskDrive often leaves BusType/MediaType blank — enrich via model heuristics
                if (type == StorageType.Unknown)
                    type = InferTypeFromModel(model);

                list.Add(new DiskCandidate(type, model, sizeGb, TierRank(type)));
            }
        }
        catch { }
        return list;
    }

    private static StorageType InferTypeFromModel(string model)
    {
        var m = model.ToUpperInvariant();
        if (m.Contains("NVME") || m.Contains("GSM2") || m.Contains("970 EVO") || m.Contains("980") || m.Contains("990"))
            return StorageType.NVMe;
        if (m.Contains("SSD"))
            return StorageType.SataSSD;
        return StorageType.HDD;
    }

    private static int TierRank(StorageType t) => t switch
    {
        StorageType.NVMe => 3, StorageType.SataSSD => 2, StorageType.HDD => 1, _ => 0
    };

    private static DiskCandidate PickBestDisk(List<DiskCandidate> disks)
        => disks.OrderByDescending(d => d.TierRank).ThenByDescending(d => d.SizeGb).First();

    private DiskCandidate? PickSystemDisk(List<DiskCandidate> candidates)
    {
        try
        {
            // Resolve C: → DiskDrive Index via WMI associations
            using var ldSearcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_LogicalDisk WHERE DeviceID='C:'");
            using var ld = ldSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (ld == null) return null;

            var ldPath = ld.Path.RelativePath.Replace("'", "\"");
            using var partSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{{ldPath}}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject part in partSearcher.Get())
            {
                var partPath = part.Path.RelativePath.Replace("'", "\"");
                using var driveSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{{partPath}}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject drive in driveSearcher.Get())
                {
                    var model = drive["Model"]?.ToString()?.Trim() ?? "";
                    var match = candidates.FirstOrDefault(c =>
                        string.Equals(c.Model, model, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                    // Fallback: match by size
                    long.TryParse(drive["Size"]?.ToString() ?? "0", out var sizeBytes);
                    var sizeGb = Math.Round(sizeBytes / 1073741824.0, 2);
                    var bySize = candidates.FirstOrDefault(c => Math.Abs(c.SizeGb - sizeGb) < 1.0);
                    if (bySize != null) return bySize;
                }
            }
        }
        catch { }
        return null;
    }

    private StorageType DetermineStorageTypeFromBus(string busType, string model)
    {
        if (string.Equals(busType, "NVMe", StringComparison.OrdinalIgnoreCase))
            return StorageType.NVMe;
        if (string.Equals(busType, "SCSI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(busType, "SATA", StringComparison.OrdinalIgnoreCase))
        {
            // Check model for SSD indicators
            var m = model.ToUpperInvariant();
            if (m.Contains("SSD") || m.Contains("SATA SSD"))
                return StorageType.SataSSD;
            return StorageType.HDD;
        }
        return StorageType.Unknown;
    }

    private (string name, string vendor, double ramMb, HardwareTier tier) DetectGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
            // Get the primary (first non-generic) GPU
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown";
                long.TryParse(obj["AdapterRAM"]?.ToString() ?? "0", out var adapterRam);
                var ramMb = adapterRam / 1048576.0;

                // Skip generic/virtual GPUs
                if (name.Contains("Microsoft Basic") || name.Contains("VMware") || name.Contains("Hyper-V"))
                    continue;

                var vendor = SystemInfo.DetectGpuVendor(name);
                var tier = SystemInfo.ClassifyGpuTier(ramMb, name);

                // Note: AdapterRAM can be wrong (>4GB wraps). For accurate VRAM,
                // check registry: HKLM\SYSTEM\CurrentControlSet\Control\Video\{guid}\0000\HardwareInformation.MemorySize
                // Or use DXDiag. For now, use name heuristics.
                return (name, vendor, ramMb, tier);
            }

            return ("Unknown", "unknown", 0, HardwareTier.VeryLow);
        }
        catch { return ("Unknown", "unknown", 0, HardwareTier.VeryLow); }
    }

    private FormFactor DetectFormFactor()
    {
        try
        {
            // Method 1: Check for battery
            using (var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery"))
            {
                using var battery = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (battery != null)
                {
                    // Check chassis type for tablet vs laptop
                    using var chassisSearcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                    using var chassis = chassisSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (chassis != null)
                    {
                        var types = chassis["ChassisTypes"] as ushort[];
                        if (types != null)
                        {
                            // 30=Tablet, 8=Portable, 9=Laptop, 10=Notebook, 14=SubNotebook
                            if (types.Any(t => t == 30)) return FormFactor.Tablet;
                            if (types.Any(t => t is 8 or 9 or 10 or 14)) return FormFactor.Laptop;
                        }
                    }
                    return FormFactor.Laptop; // Battery present = laptop
                }
            }

            // Method 2: Check for server
            using (var searcher = new ManagementObjectSearcher("SELECT SystemType FROM Win32_ComputerSystem"))
            using (var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault())
            {
                var type = obj?["SystemType"]?.ToString() ?? "";
                if (type.Contains("Server", StringComparison.OrdinalIgnoreCase))
                    return FormFactor.Server;
            }

            return FormFactor.Desktop;
        }
        catch { return FormFactor.Unknown; }
    }

    private (string name, string speed, bool isWireless) DetectNetwork()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

            if (nic == null) return ("Unknown", "Unknown", false);

            var speed = nic.Speed switch
            {
                >= 10_000_000_000 => "10 Gbps",
                >= 1_000_000_000 => "1 Gbps",
                >= 100_000_000 => "100 Mbps",
                >= 10_000_000 => "10 Mbps",
                _ => $"{nic.Speed / 1_000_000} Mbps"
            };

            var isWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

            return (nic.Description, speed, isWireless);
        }
        catch { return ("Unknown", "Unknown", false); }
    }

    private (string planName, string planGuid, bool onAc) DetectPower()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Output format: "Power Scheme GUID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx  (Scheme Name)"
            var match = System.Text.RegularExpressions.Regex.Match(output,
                @"Power Scheme GUID:\s*([0-9a-f-]+)\s+\((.+?)\)");

            var planName = match.Groups[2].Value;
            var planGuid = match.Groups[1].Value;

            bool onAc = true;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT BatteryChargeStatus FROM Win32_Battery");
                using var battery = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (battery != null)
                {
                    var status = Convert.ToInt32(battery["BatteryChargeStatus"] ?? 0);
                    onAc = (status & 1) != 0; // 1 = AC Online
                }
            }
            catch { /* Desktop — always AC */ }

            return (planName, planGuid, onAc);
        }
        catch { return ("Unknown", "Unknown", true); }
    }

    /// <summary>
    /// List the processor power settings the active scheme actually exposes.
    /// Schemes differ by CPU and by OEM: a desktop VM has no core-parking or
    /// turbo-boost entries, so tweaks targeting them can never be applied.
    /// Returns null when powercfg itself could not be queried, so callers can
    /// tell "no settings" apart from "unknown".
    /// </summary>
    private HashSet<string>? DetectPowerSettings()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/query SCHEME_CURRENT SUB_PROCESSOR",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0) return null;

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         output, @"^\s*GUID Alias:\s*(\S+)\s*$",
                         System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                aliases.Add(m.Groups[1].Value);
            }

            return aliases.Count > 0 ? aliases : null;
        }
        catch { return null; }
    }
}
