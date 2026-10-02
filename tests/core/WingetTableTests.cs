using WinOpt.Engine.Installer;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Winget prints fixed-width tables whose column widths are re-measured for
/// every invocation, prints its errors to stdout ahead of the table, and mixes
/// CRLF with LF. Every sample below is real output captured from winget
/// v1.29.380 on this machine, so the parser is checked against the thing it
/// parses rather than against a table someone typed from memory.
/// </summary>
public class WingetTableTests
{

    /// <summary>A full <c>winget list</c>: an error line on stdout, a wide table, and rows with no Available column.</summary>
    private static readonly string[] ListSample =
    {
            "Failed when searching source; results will not be included: msstore",
            "Name                                                                     Id                                                                                   Version              Available           Source",
            "-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------",
            "7-Zip 25.01 (x64)                                                        7zip.7zip                                                                            25.01                26.03               winget",
            "9Remote                                                                  ARP\\User\\X64\\9Remote                                                                 3.0.3                                    ",
            "Adobe Photoshop 2024                                                     ARP\\Machine\\X86\\PHSP_25_12                                                           25.12.0.806                              ",
    };

    /// <summary><c>winget search</c>, which has a Match column and no Source. Version must not swallow it.</summary>
    private static readonly string[] SearchSample =
    {
            "Name                               Id                        Version         Match",
            "------------------------------------------------------------------------------------------",
            "7-Zip                              7zip.7zip                 26.03           Moniker: 7zip",
            "Advanced Archive Password Recovery Elcomsoft.ArchivePassword 4.66.266.6965   Tag: 7zip",
            "NanaZip                            M2Team.NanaZip            7.0.1832.0      Tag: 7zip",
            "NanaZip Preview                    M2Team.NanaZip.Preview    7.0.1800.0      Tag: 7zip",
            "Cram                               Nexalit.Cram              1.3.1           Tag: 7zip",
    };

    /// <summary><c>winget list --id X --exact</c>: narrow columns and CRLF line endings.</summary>
    private static readonly string[] ExactSample =
    {
            "Name              Id        Version Available",
            "---------------------------------------------",
            "7-Zip 25.01 (x64) 7zip.7zip 25.01   26.03",
    };

    /// <summary><c>winget source list</c> - a table that is not a package list.</summary>
    private static readonly string[] SourceSample =
    {
            "Name        Argument                                      Explicit",
            "------------------------------------------------------------------",
            "msstore     https://storeedgefd.dsx.mp.microsoft.com/v9.0 false",
            "winget      https://cdn.winget.microsoft.com/cache        false",
            "winget-font https://cdn.winget.microsoft.com/fonts        true",
    };

    /// <summary>A miss: no table at all, only sentences.</summary>
    private static readonly string[] NoMatchSample =
    {
            "Failed when searching source; results will not be included: msstore",
            "No installed package found matching input criteria.",
    };

    private static string Sample(string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void List_ParsesRowsAfterTheNoiseLine()
    {
        var rows = WingetTable.Parse(Sample(ListSample));

        Assert.True(rows.Count >= 3, "expected at least 3 rows, got " + rows.Count);

        var sevenZip = WingetTable.FindById(rows, "7zip.7zip");
        Assert.NotNull(sevenZip);
        Assert.Equal("7-Zip 25.01 (x64)", sevenZip!.Name);
        Assert.Equal("25.01", sevenZip.Version);
        Assert.Equal("26.03", sevenZip.Available);
        Assert.Equal("winget", sevenZip.Source);
    }

    [Fact]
    public void List_KeepsTheRowWhoseIdLooksLikeARegistryPath()
    {
        // Add/Remove Programs entries have no winget ID, so winget prints the
        // registry path as theirs, backslashes and all.
        var rows = WingetTable.Parse(Sample(ListSample));

        Assert.Contains(rows, r => r.Id == @"ARP\User\X64\9Remote");
        Assert.Contains(rows, r => r.Id == @"ARP\Machine\X86\PHSP_25_12");
    }

    [Fact]
    public void Search_DoesNotSwallowTheMatchColumn()
    {
        var rows = WingetTable.Parse(Sample(SearchSample));

        Assert.True(rows.Count >= 4, "expected at least 4 rows, got " + rows.Count);

        var sevenZip = WingetTable.FindById(rows, "7zip.7zip");
        Assert.NotNull(sevenZip);
        Assert.Null(sevenZip!.Available);   // this table has no Available column
        Assert.Null(sevenZip.Source);       // nor a Source one
        Assert.Equal("26.03", sevenZip.Version);
    }

    [Fact]
    public void Search_ParsesTheColumnAfterVersion()
    {
        var rows = WingetTable.Parse(Sample(SearchSample));
        var nanaZip = WingetTable.FindById(rows, "M2Team.NanaZip");

        Assert.NotNull(nanaZip);
        Assert.Equal("7.0.1832.0", nanaZip!.Version);
        Assert.Equal("NanaZip", nanaZip.Name);
    }

    [Fact]
    public void ExactList_ParsesNarrowColumns()
    {
        // The same command with different widths: hardcode the offsets and this
        // row reads as garbage.
        var rows = WingetTable.Parse(Sample(ExactSample));

        var sevenZip = Assert.Single(rows);
        Assert.Equal("7zip.7zip", sevenZip.Id);
        Assert.Equal("7-Zip 25.01 (x64)", sevenZip.Name);
        Assert.Equal("25.01", sevenZip.Version);
        Assert.Equal("26.03", sevenZip.Available);
    }

    [Fact]
    public void Parse_ReturnsNoRowsWhenThereIsNoTable()
    {
        Assert.Empty(WingetTable.Parse(Sample(NoMatchSample)));
    }

    [Fact]
    public void Parse_IgnoresTrailingRemarks()
    {
        var rows = WingetTable.Parse(Sample(ListSample) + "296 packages installed." + "\n");

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.NotEmpty(r.Id));
    }

    [Fact]
    public void ReadRow_ParsesATableThatIsNotAPackageList()
    {
        var headerIndex = WingetTable.FindHeaderLine(SourceSample);
        Assert.True(headerIndex >= 0);

        var values = WingetTable.ReadRow(SourceSample[headerIndex], SourceSample[headerIndex + 2]);

        Assert.Equal("msstore", values["Name"]);
        Assert.Equal("https://storeedgefd.dsx.mp.microsoft.com/v9.0", values["Argument"]);
        Assert.Equal("false", values["Explicit"]);
    }

    [Fact]
    public void ReadRow_FindsTheExplicitSource()
    {
        var headerIndex = WingetTable.FindHeaderLine(SourceSample);
        var values = WingetTable.ReadRow(SourceSample[headerIndex], SourceSample[headerIndex + 4]);

        Assert.Equal("winget-font", values["Name"]);
        Assert.Equal("true", values["Explicit"]);
    }

    [Fact]
    public void FindHeaderLine_SkipsTheNoiseLine()
    {
        Assert.Equal(1, WingetTable.FindHeaderLine(ListSample));
        Assert.Equal(0, WingetTable.FindHeaderLine(SearchSample));
        Assert.Equal(-1, WingetTable.FindHeaderLine(NoMatchSample));
    }
}
