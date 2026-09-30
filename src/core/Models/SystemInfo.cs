namespace WinOpt.Core.Models;

/// <summary>
/// Form factor of the machine.
/// </summary>
public enum FormFactor
{
    Desktop,
    Laptop,
    Tablet,
    Server,
    Unknown
}

/// <summary>
/// Hardware tier classification based on specs.
/// </summary>
public enum HardwareTier
{
    Ultra,
    High,
    Mid,
    Low,
    VeryLow
}

/// <summary>
/// Storage type detected on the system.
/// </summary>
public enum StorageType
{
    NVMe,
    SataSSD,
    HDD,
    Unknown
}

/// <summary>
/// Complete system information snapshot used for profile
/// selection and tweak compatibility filtering.
/// </summary>
public sealed class SystemInfo
{
    // OS
    public string OsCaption { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public int BuildNumber { get; init; }
    public string OsArchitecture { get; init; } = string.Empty;
    public string OsEdition { get; init; } = string.Empty;

    // Hardware
    public string CpuName { get; init; } = string.Empty;
    public string CpuVendor { get; init; } = string.Empty; // "intel", "amd", "arm"
    public int CpuCores { get; init; }
    public int CpuLogicalProcessors { get; init; }
    public int CpuMaxClockMhz { get; init; }
    public HardwareTier CpuTier { get; init; }

    public double RamTotalGb { get; init; }
    public HardwareTier RamTier { get; init; }

    public StorageType PrimaryStorageType { get; init; }
    public string PrimaryStorageModel { get; init; } = string.Empty;
    public double PrimaryStorageSizeGb { get; init; }
    public HardwareTier StorageTier { get; init; }

    public string GpuName { get; init; } = string.Empty;
    public string GpuVendor { get; init; } = string.Empty; // "nvidia", "amd", "intel"
    public double GpuRamMb { get; init; }
    public HardwareTier GpuTier { get; init; }

    // Form Factor
    public FormFactor FormFactor { get; init; }
    public bool IsLaptop => FormFactor == FormFactor.Laptop || FormFactor == FormFactor.Tablet;

    // Network
    public string PrimaryNicName { get; init; } = string.Empty;
    public string PrimaryNicSpeed { get; init; } = string.Empty; // e.g. "1 Gbps", "10 Gbps"
    public bool IsWireless { get; init; }

    // Power
    public string ActivePowerPlan { get; init; } = string.Empty;
    public string ActivePowerPlanGuid { get; init; } = string.Empty;
    public bool OnAcPower { get; init; }

    /// <summary>
    /// Setting aliases the active power scheme actually exposes under
    /// SUB_PROCESSOR (e.g. PROCTHROTTLEMIN). Null means the probe failed,
    /// in which case no capability filtering happens.
    /// </summary>
    public IReadOnlySet<string>? PowerSettings { get; init; }

    // Overall
    public HardwareTier OverallTier { get; init; }

    /// <summary>
    /// Calculate overall system tier from component tiers.
    /// Uses weighted average: CPU 40%, RAM 25%, Storage 20%, GPU 15%.
    /// </summary>
    public static HardwareTier CalculateOverallTier(
        HardwareTier cpu, HardwareTier ram, HardwareTier storage, HardwareTier gpu)
    {
        int Score(HardwareTier t) => t switch
        {
            HardwareTier.Ultra => 5,
            HardwareTier.High => 4,
            HardwareTier.Mid => 3,
            HardwareTier.Low => 2,
            HardwareTier.VeryLow => 1,
            _ => 1
        };

        double weighted = Score(cpu) * 0.40 + Score(ram) * 0.25
                        + Score(storage) * 0.20 + Score(gpu) * 0.15;

        return weighted switch
        {
            >= 4.5 => HardwareTier.Ultra,
            >= 3.5 => HardwareTier.High,
            >= 2.5 => HardwareTier.Mid,
            >= 1.5 => HardwareTier.Low,
            _ => HardwareTier.VeryLow
        };
    }

    /// <summary>
    /// Classify CPU tier from core count and clock speed.
    /// </summary>
    public static HardwareTier ClassifyCpuTier(int cores, int clockMhz)
    {
        if (cores >= 16) return HardwareTier.Ultra;
        if (cores >= 8 && clockMhz >= 3500) return HardwareTier.High;
        if (cores >= 6) return HardwareTier.Mid;
        if (cores >= 4) return HardwareTier.Low;
        return HardwareTier.VeryLow;
    }

    /// <summary>
    /// Classify RAM tier from total gigabytes.
    /// Uses half-GB tolerance to avoid 15.84GB → Low.
    /// </summary>
    public static HardwareTier ClassifyRamTier(double totalGb)
    {
        if (totalGb >= 64) return HardwareTier.Ultra;
        if (totalGb >= 32) return HardwareTier.High;
        if (totalGb >= 15.5) return HardwareTier.Mid;
        if (totalGb >= 7.5) return HardwareTier.Low;
        return HardwareTier.VeryLow;
    }

    /// <summary>
    /// Classify storage tier from media type and bus type.
    /// </summary>
    public static StorageType ClassifyStorageType(string mediaType, string? busType)
    {
        if (string.Equals(mediaType, "SSD", StringComparison.OrdinalIgnoreCase))
            return string.Equals(busType, "NVMe", StringComparison.OrdinalIgnoreCase)
                ? StorageType.NVMe : StorageType.SataSSD;
        if (string.Equals(mediaType, "HDD", StringComparison.OrdinalIgnoreCase))
            return StorageType.HDD;
        return StorageType.Unknown;
    }

    /// <summary>
    /// Classify GPU tier from VRAM and name heuristics.
    /// </summary>
    public static HardwareTier ClassifyGpuTier(double vramMb, string gpuName)
    {
        // High VRAM = higher tier
        if (vramMb >= 16000) return HardwareTier.Ultra;
        if (vramMb >= 8000) return HardwareTier.High;
        if (vramMb >= 4000) return HardwareTier.Mid;
        if (vramMb >= 2000) return HardwareTier.Low;

        // Name-based heuristics for low VRAM reporting
        var name = gpuName.ToUpperInvariant();
        if (name.Contains("RTX 4") || name.Contains("RTX 3") || name.Contains("RX 7"))
            return HardwareTier.High;
        if (name.Contains("RTX 2") || name.Contains("GTX 16") || name.Contains("RX 6"))
            return HardwareTier.Mid;
        if (name.Contains("GTX 10") || name.Contains("RX 5"))
            return HardwareTier.Low;

        return HardwareTier.VeryLow;
    }

    /// <summary>
    /// Determine storage tier for optimization purposes.
    /// NVMe > SATA SSD > HDD.
    /// </summary>
    public static HardwareTier ClassifyStorageTier(StorageType type) => type switch
    {
        StorageType.NVMe => HardwareTier.Ultra,
        StorageType.SataSSD => HardwareTier.High,
        StorageType.HDD => HardwareTier.Low,
        _ => HardwareTier.Mid
    };

    /// <summary>
    /// Detect CPU vendor from name string.
    /// </summary>
    public static string DetectCpuVendor(string cpuName)
    {
        var name = cpuName.ToUpperInvariant();
        if (name.Contains("INTEL")) return "intel";
        if (name.Contains("AMD")) return "amd";
        if (name.Contains("QUALCOMM") || name.Contains("SNAPDRAGON")) return "arm";
        return "unknown";
    }

    /// <summary>
    /// Detect GPU vendor from name string.
    /// </summary>
    public static string DetectGpuVendor(string gpuName)
    {
        var name = gpuName.ToUpperInvariant();
        if (name.Contains("NVIDIA") || name.Contains("GEFORCE") || name.Contains("QUADRO") || name.Contains("TESLA"))
            return "nvidia";
        if (name.Contains("AMD") || name.Contains("RADEON") || name.Contains("RX"))
            return "amd";
        if (name.Contains("INTEL") || name.Contains("UHD") || name.Contains("IRIS"))
            return "intel";
        return "unknown";
    }

    /// <summary>
    /// Detect Windows edition from OS caption.
    /// </summary>
    public static string DetectEdition(string osCaption)
    {
        if (osCaption.Contains("LTSC")) return "LTSC";
        if (osCaption.Contains("Enterprise")) return "Enterprise";
        if (osCaption.Contains("Education")) return "Education";
        if (osCaption.Contains("Pro")) return "Pro";
        if (osCaption.Contains("Home")) return "Home";
        if (osCaption.Contains("Server")) return "Server";
        return "Unknown";
    }
}
