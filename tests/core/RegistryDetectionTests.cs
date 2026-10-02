using WinOpt.Core.Interfaces;
using WinOpt.Core.Models;
using WinOpt.Providers.Registry;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Detection reads the registry; these tests are the ones that can only be
/// written by creating a value and looking again. They also cover
/// <c>expectedAbsent</c>, which exists because a setting expressed by the mere
/// presence of a value has no default number to declare.
/// </summary>
public class RegistryDetectionTests : IDisposable
{
    private const string ValueName = "WinOptProbe";
    private readonly string _keyPath;
    private readonly string _subKey;

    public RegistryDetectionTests()
    {
        _subKey = $@"Software\WinOpt\Tests\{Guid.NewGuid():N}";
        _keyPath = $@"HKCU\{_subKey}";
    }

    /// <summary>Leaves the value absent, which is the state most of these test.</summary>
    private void Write(string data)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_subKey, true);
        key.SetValue(ValueName, data, Microsoft.Win32.RegistryValueKind.String);
    }

    private void WriteEmptyDefault()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_subKey, true);
        key.SetValue(string.Empty, string.Empty, Microsoft.Win32.RegistryValueKind.String);
    }

    private TweakDefinition Probe(string applied, string? notApplied = null, bool absent = false,
                                  string? valueName = null)
        => new()
    {
        Id = "test.probe",
        Method = TweakMethod.Registry,
        Detection = new DetectionSpec
        {
            RegistryKey = _keyPath,
            RegistryValue = valueName ?? ValueName,
            ExpectedApplied = applied,
            ExpectedDefault = notApplied,
            ExpectedAbsent = absent,
        },
    };

    private DetectionResult Detect(TweakDefinition tweak)
        => new RegistryProvider().DetectAsync(tweak).GetAwaiter().GetResult();

    // --- absence as an answer ---

    [Fact]
    public void AbsentValue_WithExpectedAbsent_ReportsNotApplied()
    {
        // The default for these settings is for the value not to be there. There
        // is no default number to write down, so without an explicit statement
        // that absence counts, detection reports a failure for a perfectly
        // ordinary machine.
        var result = Detect(Probe(applied: "", absent: true));

        Assert.Equal(TweakState.NotApplied, result.State);
        Assert.True(result.DetectionSucceeded);
        Assert.Null(result.CurrentValue);
    }

    [Fact]
    public void AbsentKey_WithExpectedAbsent_ReportsNotApplied()
    {
        // The whole key can be missing, not just the value.
        var result = Detect(Probe(applied: "1", absent: true));

        Assert.Equal(TweakState.NotApplied, result.State);
        Assert.True(result.DetectionSucceeded);
    }

    [Fact]
    public void AbsentValue_WithNoExpectation_ReportsFailure()
    {
        // The other side of the same coin: saying nothing about the default
        // still means "I cannot answer", and that must stay a failure rather
        // than quietly reading as NotApplied.
        var result = Detect(Probe(applied: "1"));

        Assert.Equal(TweakState.DetectionFailed, result.State);
        Assert.False(result.DetectionSucceeded);
    }

    // --- an applied state of empty string ---

    [Fact]
    public void EmptyValue_MatchingAnEmptyExpectation_ReportsApplied()
    {
        // "" is a real answer, and must not be mistaken for "this expectation
        // was never set".
        Write("");

        var result = Detect(Probe(applied: "", absent: true));

        Assert.Equal(TweakState.Applied, result.State);
        Assert.True(result.DetectionSucceeded);
    }

    [Fact]
    public void EmptyDefaultValue_MatchingAnEmptyExpectation_ReportsApplied()
    {
        // The default value has no name: an empty string addresses it rather
        // than meaning "the spec left this out". The classic context menu is
        // enabled by nothing but an empty InprocServer32, so a guard that
        // rejects an empty value name would make that tweak undetectable and
        // unappliable at the same time.
        WriteEmptyDefault();

        var result = Detect(Probe(applied: "", absent: true, valueName: ""));

        Assert.Equal(TweakState.Applied, result.State);
        Assert.True(result.DetectionSucceeded);
    }

    [Fact]
    public void EmptyDefaultValue_AppliesVerifiesAndRollsBack()
    {
        // Detection passing is only half the guarantee: a guard that rejects an
        // empty value name also fails at apply time, and the user would not
        // learn that until after confirming. Round-trip it end to end, on a
        // throwaway key rather than the real CLSID.
        var tweak = new TweakDefinition
        {
            Id = "test.roundtrip",
            Method = TweakMethod.Registry,
            TargetValue = "",
            Detection = new DetectionSpec
            {
                RegistryKey = _keyPath,
                RegistryValue = "",
                ExpectedApplied = "",
                ExpectedAbsent = true,
            },
            Apply = new OperationSpec
            {
                RegistryKey = _keyPath,
                RegistryValue = "",
                RegistryData = "",
                RegistryType = "SZ",
            },
        };

        var provider = new RegistryProvider();

        Assert.Equal(TweakState.NotApplied, Detect(tweak).State);

        var applied = provider.ApplyAsync(tweak).GetAwaiter().GetResult();
        Assert.True(applied.Success, applied.Message);
        Assert.NotNull(applied.SnapshotEntry);

        Assert.True(provider.VerifyAsync(tweak).GetAwaiter().GetResult());

        var rolled = provider.RollbackAsync(applied.SnapshotEntry!).GetAwaiter().GetResult();
        Assert.True(rolled.Success, rolled.Message);
        Assert.Equal(TweakState.NotApplied, Detect(tweak).State);
    }

    [Fact]
    public void ValuePresent_WhenAbsenceIsTheDefault_ReportsPartiallyApplied()
    {
        // Not applied, not absent — something else set it. Reporting that as
        // either end of the scale would be inventing an answer.
        Write("not-empty");

        var result = Detect(Probe(applied: "", absent: true));

        Assert.Equal(TweakState.PartiallyApplied, result.State);
        Assert.Equal("not-empty", result.CurrentValue);
    }

    // --- the behaviour every other catalogue entry relies on ---

    [Fact]
    public void AbsentValue_WithExpectedDefault_ReportsNotApplied()
    {
        var result = Detect(Probe(applied: "1", notApplied: "0"));

        Assert.Equal(TweakState.NotApplied, result.State);
        Assert.True(result.DetectionSucceeded);
    }

    [Fact]
    public void PresentValueMatchingDefault_ReportsNotApplied()
    {
        Write("0");

        var result = Detect(Probe(applied: "1", notApplied: "0"));

        Assert.Equal(TweakState.NotApplied, result.State);
        Assert.Equal("0", result.CurrentValue);
    }

    [Fact]
    public void PresentValueMatchingApplied_ReportsApplied()
    {
        Write("1");

        var result = Detect(Probe(applied: "1", notApplied: "0"));

        Assert.Equal(TweakState.Applied, result.State);
        Assert.Equal("1", result.CurrentValue);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
        }
        catch (ArgumentException)
        {
            // Already gone, or never created.
        }
    }
}

/// <summary>
/// Detect must read the same thing apply writes. A pair that does not is the
/// single most common source of a tweak that reports PartiallyApplied forever,
/// and no amount of planner logic can rescue it afterwards.
/// </summary>
public class CatalogueDetectionTests
{
    private static string TweakDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tweaks");

    private static WinOpt.Engine.Tweaks.TweakDatabase Catalogue()
    {
        var db = new WinOpt.Engine.Tweaks.TweakDatabase(TweakDir);
        db.LoadAsync().GetAwaiter().GetResult();
        return db;
    }

    [Fact]
    public void EveryRegistryTweak_DetectsTheValueApplyWrites()
    {
        var db = Catalogue();
        var offenders = new List<string>();

        foreach (var tweak in db.Tweaks.Values.Where(t => t.Method == TweakMethod.Registry))
        {
            var key = tweak.Apply.RegistryKey ?? tweak.Params.GetValueOrDefault("registryKey");
            var value = tweak.Apply.RegistryValue ?? tweak.Params.GetValueOrDefault("registryValue");
            var dKey = tweak.Detection.RegistryKey;
            var dValue = tweak.Detection.RegistryValue;

            if (!string.Equals(key, dKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(value, dValue, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{tweak.Id}: apply writes {key}\\{value}, detect reads {dKey}\\{dValue}");
                continue;
            }

            // One of the two ways of saying what "not applied" looks like.
            var hasDefault = !string.IsNullOrEmpty(tweak.Detection.ExpectedDefault) ||
                             tweak.Detection.ExpectedAbsent;
            if (!hasDefault || tweak.Detection.ExpectedApplied == null)
                offenders.Add($"{tweak.Id}: detection does not name both the applied and the default state");
        }

        Assert.True(offenders.Count == 0,
            "These tweaks cannot tell applied from not-applied: " + string.Join("; ", offenders));
    }

    [Fact]
    public void EveryTweakNamesAnAppliedAndADefaultState()
    {
        var db = Catalogue();

        // TargetValue is deliberately empty for the tweaks whose applied state
        // is "the value exists and is empty", so the absence test lives on
        // ExpectedApplied — a null there means detection has nothing to match.
        var offenders = db.Tweaks.Values
            .Where(t => t.Detection.ExpectedApplied == null)
            .Select(t => t.Id)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These tweaks declare no target: " + string.Join(", ", offenders));
    }
}
