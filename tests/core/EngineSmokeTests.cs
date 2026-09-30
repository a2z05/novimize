using WinOpt.Core.Models;
using WinOpt.Engine;
using WinOpt.Engine.Detection;
using WinOpt.Engine.Providers;
using WinOpt.Engine.Tweaks;
using WinOpt.Providers.Registry;
using WinOpt.Providers.Service;
using WinOpt.Providers.PowerShell;
using WinOpt.Providers.NetSh;
using Xunit;

namespace WinOpt.Core.Tests;

public class EngineSmokeTests
{
    [Fact]
    public void TweakDatabase_LoadsSampleTweaks()
    {
        var db = new TweakDatabase(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tweaks"));
        var count = db.LoadAsync().GetAwaiter().GetResult();
        Assert.True(count > 0, $"Expected tweaks to load, got {count}");
        Assert.True(db.Tweaks.ContainsKey("network.tcpip.autoTuning"));
        Assert.True(db.Tweaks.ContainsKey("services.diagtrack.config"));
        Assert.True(db.Tweaks.ContainsKey("visual.transparentEffects"));
    }

    [Fact]
    public void SystemInfo_TierClassification_Works()
    {
        Assert.Equal(HardwareTier.Ultra, SystemInfo.ClassifyCpuTier(16, 4000));
        Assert.Equal(HardwareTier.Mid, SystemInfo.ClassifyCpuTier(6, 3000));
        Assert.Equal(HardwareTier.VeryLow, SystemInfo.ClassifyCpuTier(2, 2000));

        Assert.Equal(HardwareTier.Ultra, SystemInfo.ClassifyRamTier(64));
        Assert.Equal(HardwareTier.Mid, SystemInfo.ClassifyRamTier(16));
        Assert.Equal(HardwareTier.VeryLow, SystemInfo.ClassifyRamTier(2));

        Assert.Equal(HardwareTier.Ultra, SystemInfo.ClassifyStorageTier(StorageType.NVMe));
        Assert.Equal(HardwareTier.Low, SystemInfo.ClassifyStorageTier(StorageType.HDD));
    }

    [Fact]
    public void SystemInfo_VendorDetection_Works()
    {
        Assert.Equal("intel", SystemInfo.DetectCpuVendor("Intel Core i7-12700K"));
        Assert.Equal("amd", SystemInfo.DetectCpuVendor("AMD Ryzen 7 5800X"));
        Assert.Equal("nvidia", SystemInfo.DetectGpuVendor("NVIDIA GeForce RTX 4070"));
        Assert.Equal("amd", SystemInfo.DetectGpuVendor("AMD Radeon RX 7900 XT"));
        Assert.Equal("intel", SystemInfo.DetectGpuVendor("Intel UHD Graphics 770"));
    }

    [Fact]
    public void SecurityBoundaries_ProtectsCriticalServices()
    {
        var (safe1, _, _) = SecurityBoundaries.CheckServiceProtection("WinDefend");
        Assert.False(safe1);

        var (safe2, _, _) = SecurityBoundaries.CheckServiceProtection("MpsSvc");
        Assert.False(safe2);

        var (safe3, _, _) = SecurityBoundaries.CheckServiceProtection("RpcSs");
        Assert.False(safe3);

        var (safe4, _, _) = SecurityBoundaries.CheckServiceProtection("Fax");
        Assert.True(safe4);
    }

    [Fact]
    public void ProviderRegistry_ResolvesByMethod()
    {
        var registry = new ProviderRegistry();
        registry.Register(new RegistryProvider());
        registry.Register(new ServiceProvider());
        registry.Register(new PowerShellProvider());
        registry.Register(new NetShProvider());

        Assert.NotNull(registry.GetProvider(TweakMethod.Registry));
        Assert.NotNull(registry.GetProvider(TweakMethod.Service));
        Assert.NotNull(registry.GetProvider(TweakMethod.PowerShell));
        Assert.NotNull(registry.GetProvider(TweakMethod.NetSh));
        Assert.Null(registry.GetProvider(TweakMethod.Dism));
    }

    [Fact]
    public void TweakDatabase_FiltersByProfile()
    {
        var db = new TweakDatabase(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tweaks"));
        db.LoadAsync().GetAwaiter().GetResult();

        var profile = BuiltInProfiles.All.First(p => p.Id == "daily");
        var tweaks = db.GetForProfile(profile);
        Assert.NotNull(tweaks);
        Assert.True(tweaks.Count > 0);
    }

    [Fact]
    public void BuiltInProfiles_AllHaveIds()
    {
        Assert.Equal(8, BuiltInProfiles.All.Count);
        foreach (var profile in BuiltInProfiles.All)
        {
            Assert.False(string.IsNullOrEmpty(profile.Id));
            Assert.False(string.IsNullOrEmpty(profile.Name));
        }
    }

    [Fact]
    public void SystemDetector_DetectsHardware()
    {
        var detector = new SystemDetector();
        var info = detector.DetectAsync().GetAwaiter().GetResult();

        Assert.False(string.IsNullOrEmpty(info.OsCaption));
        Assert.True(info.BuildNumber > 0);
        Assert.True(info.CpuCores > 0);
        Assert.True(info.RamTotalGb > 0);
    }

    [Fact]
    public void OptimizeEngine_CanScan()
    {
        var engine = new OptimizeEngine(
            tweaksDir: Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tweaks"));

        engine.Database.LoadAsync().GetAwaiter().GetResult();
        engine.Providers.Register(new RegistryProvider());
        engine.Providers.Register(new ServiceProvider());
        engine.Providers.Register(new PowerShellProvider());
        engine.Providers.Register(new NetShProvider());

        var results = engine.ScanAsync().GetAwaiter().GetResult();
        Assert.NotNull(results);
        Assert.True(results.Count > 0);

        // Should detect at least some as not applied or applied
        Assert.Contains(results, r => r.State == TweakState.NotApplied || r.State == TweakState.Applied);
    }
}
