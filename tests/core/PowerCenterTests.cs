using WinOpt.Engine.Power;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Everything the Power Center reads out of powercfg. The output is localized
/// — the labels around each value are translated — so these tests are about
/// the parts that are not: GUIDs, indentation, hex, and the trailing asterisk
/// that marks the active plan.
/// </summary>
public class PowerCenterTests
{
    private const string PlanList = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance) *
        Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)
        Power Scheme GUID: 1847a927-a30d-4c23-a784-a7324282a8a8  (Ultimate Performance)
        """;

    [Fact]
    public void ParsePlanList_TakesEverySchemeAndMarksTheActiveOne()
    {
        var plans = PowerCenter.ParsePlanList(PlanList);

        Assert.Equal(4, plans.Count);
        Assert.Equal(
            new[] { "Balanced", "High performance", "Power saver", "Ultimate Performance" },
            plans.Select(p => p.Name));

        var active = Assert.Single(plans, p => p.Active);
        Assert.Equal("High performance", active.Name);
        Assert.Equal(PowerCenter.HighPerformance, active.Guid, ignoreCase: true);

        // Three of the four are the built-in schemes; the Ultimate copy on
        // this machine is not the canonical GUID, so it is not "built-in".
        Assert.True(plans.Single(p => p.Name == "Balanced").BuiltIn);
        Assert.False(plans.Single(p => p.Name == "Ultimate Performance").BuiltIn);
    }

    /// <summary>
    /// A machine that has been told to duplicate Ultimate Performance six times
    /// ends up with six schemes of the same name. Anything that picks "the
    /// Ultimate plan" by name would have to break that tie.
    /// </summary>
    [Fact]
    public void ParsePlanList_KeepsSchemesThatShareAName()
    {
        var output = """
            Power Scheme GUID: 1847a927-a30d-4c23-a784-a7324282a8a8  (Ultimate Performance)
            Power Scheme GUID: 3cb5cb67-fa56-4085-9422-14acaa4ef415  (Ultimate Performance) *
            Power Scheme GUID: 5b240572-50c0-44aa-80f4-5cd162b35215  (Ultimate Performance)
            """;

        var plans = PowerCenter.ParsePlanList(output);

        Assert.Equal(3, plans.Count);
        Assert.Equal(3, plans.Select(p => p.Guid).Distinct().Count());
        Assert.Equal("3cb5cb67-fa56-4085-9422-14acaa4ef415",
            Assert.Single(plans, p => p.Active).Guid, ignoreCase: true);
    }

    [Fact]
    public void ParsePlanList_WithNoParentheticalNameFallsBackToTheGuid()
    {
        var plans = PowerCenter.ParsePlanList("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e");

        var only = Assert.Single(plans);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", only.Guid, ignoreCase: true);
        Assert.Equal(only.Guid, only.Name, ignoreCase: true);
        Assert.False(only.Active);
    }

    [Fact]
    public void ParsePlanList_IgnoresLinesWithNoGuid()
    {
        var plans = PowerCenter.ParsePlanList(PlanList.Split('\n').First());

        Assert.Empty(plans);
    }

    private const string SettingQuery = """
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
          GUID Alias: SCHEME_MIN
          Subgroup GUID: 7516b95f-f776-4464-8c53-06167f40cc99  (Display)
            GUID Alias: SUB_VIDEO
            Power Setting GUID: 3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e  (Turn off display after)
              GUID Alias: VIDEOIDLE
              Minimum Possible Setting: 0x00000000
              Maximum Possible Setting: 0xffffffff
              Possible Settings increment: 0x00000001
              Possible Settings units: Seconds
            Current AC Power Setting Index: 0x00000708
            Current DC Power Setting Index: 0x00000000

        """;

    [Fact]
    public void ParseSetting_ReadsTheTwoValuesAtTheSettingsOwnIndentation()
    {
        var parsed = PowerCenter.ParseSetting(SettingQuery, "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

        // 0x708 = 1800 s = 30 min; the DC value is 0, i.e. never.
        Assert.Equal(1800, parsed.Ac);
        Assert.Equal(0, parsed.Dc);
        Assert.Equal("Seconds", parsed.Units);
        Assert.Equal(0, parsed.Min);
    }

    [Fact]
    public void ParseSetting_TakesThePossibleNamesSoAnEnumReadsAsAWord()
    {
        const string output = """
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
              Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
                Power Setting GUID: be337238-0d82-4146-a960-4f3749d470c7  (Processor performance boost mode)
                  GUID Alias: PERFBOOSTMODE
                  Possible Setting Index: 000
                  Possible Setting Friendly Name: Disabled
                  Possible Setting Index: 002
                  Possible Setting Friendly Name: Enabled
                  Possible Setting Index: 003
                  Possible Setting Friendly Name: Aggressive
                Current AC Power Setting Index: 0x00000003
                Current DC Power Setting Index: 0x00000000
            """;

        var parsed = PowerCenter.ParseSetting(output, "be337238-0d82-4146-a960-4f3749d470c7");

        Assert.Equal(3, parsed.Ac);
        Assert.Equal(0, parsed.Dc);
        Assert.Equal(3, parsed.Possible.Count);
        Assert.Equal("Aggressive", PowerCenter.Format(parsed.Ac, parsed, "enum"));
        Assert.Equal("Disabled", PowerCenter.Format(parsed.Dc, parsed, "enum"));
    }

    /// <summary>
    /// The maximum and the "possible" entries sit deeper than the setting line
    /// and carry hex of their own. Reading the first hex anywhere would report
    /// 0xffffffff as somebody's setting.
    /// </summary>
    [Fact]
    public void ParseSetting_DoesNotMistakeTheRangeForTheValue()
    {
        const string output = """
            Power Setting GUID: 29f6c1db-86da-48c5-9fdb-f2b67b1f44da  (Sleep after)
              Minimum Possible Setting: 0x00000000
              Maximum Possible Setting: 0xffffffff
              Possible Settings increment: 0x00000001
            Current AC Power Setting Index: 0x00000e10
            Current DC Power Setting Index: 0x0000012c
            """;

        var parsed = PowerCenter.ParseSetting(output, "29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        Assert.Equal(3600, parsed.Ac);
        Assert.Equal(300, parsed.Dc);
        Assert.Equal(0xffffffffL, parsed.Max);
    }

    [Fact]
    public void ParseSetting_WithNoMatchReturnsNothingRatherThanThrowing()
    {
        var parsed = PowerCenter.ParseSetting("no powercfg output here", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

        Assert.Null(parsed.Ac);
        Assert.Null(parsed.Dc);
        Assert.Empty(parsed.Possible);
    }

    [Theory]
    [InlineData(0, "Never")]
    [InlineData(60, "1 min")]
    [InlineData(120, "2 min")]
    [InlineData(1800, "30 min")]
    [InlineData(3600, "60 min")]
    [InlineData(90, "1 min 30 s")]
    [InlineData(45, "45 s")]
    public void Format_TurnsSecondsIntoSomethingAPersonCanRead(int seconds, string expected)
    {
        var parsed = new ParsedSetting(seconds, 0, "Seconds", new Dictionary<int, string>(), 0, null);

        Assert.Equal(expected, PowerCenter.Format(seconds, parsed, "seconds"));
        Assert.Equal("Never", PowerCenter.Format(0, parsed, "seconds"));
    }

    [Fact]
    public void Format_PercentKeepsThePercentSign()
    {
        var parsed = new ParsedSetting(100, 5, "%", new Dictionary<int, string>(), 0, 100);

        Assert.Equal("100%", PowerCenter.Format(parsed.Ac, parsed, "percent"));
        Assert.Equal("5%", PowerCenter.Format(parsed.Dc, parsed, "percent"));
    }

    [Fact]
    public void ParseGuid_FindsAGuidAnywhereInALine()
    {
        Assert.Equal(
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            PowerCenter.ParseGuid("Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)"),
            ignoreCase: true);

        Assert.Null(PowerCenter.ParseGuid("nothing here"));
        Assert.Null(PowerCenter.ParseGuid(""));
    }
}
