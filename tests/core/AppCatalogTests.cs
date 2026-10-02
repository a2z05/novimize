using WinOpt.Engine.Installer;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The catalogue is structured data on disk: adding an app must be a JSON edit
/// and nothing else. These tests check the rules that keep that true — the
/// category comes from the file name, _meta.json orders the categories without
/// owning them, and one broken file does not take the catalogue down.
/// </summary>
public class AppCatalogTests : IDisposable
{
    private readonly string _dir;

    public AppCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novimize-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private void Write(string fileName, string contents) =>
        File.WriteAllText(Path.Combine(_dir, fileName), contents);

    [Fact]
    public void Load_TakesTheCategoryFromTheFileName()
    {
        Write("browsers.json", """[{"id":"Mozilla.Firefox","name":"Firefox"}]""");
        Write("media.json", """[{"id":"VideoLAN.VLC","name":"VLC"}]""");

        var catalog = new AppCatalog(_dir);
        var count = catalog.Load();

        Assert.Equal(2, count);
        Assert.Equal("browsers", catalog.Find("Mozilla.Firefox")!.Category);
        Assert.Equal("media", catalog.Find("VideoLAN.VLC")!.Category);
    }

    [Fact]
    public void Load_OrdersCategoriesFromMetaAndKeepsTheRest()
    {
        Write("_meta.json", """{"categories":[{"id":"media","label":"Media","description":"Players"}]}""");
        Write("media.json", """[{"id":"VideoLAN.VLC","name":"VLC"}]""");
        Write("browsers.json", """[{"id":"Mozilla.Firefox","name":"Firefox"}]""");

        var catalog = new AppCatalog(_dir);
        catalog.Load();

        Assert.Equal(new[] { "media", "browsers" }, catalog.Categories.Select(c => c.Id));
        Assert.Equal("Media", catalog.Categories[0].Label);
        Assert.Equal("Players", catalog.Categories[0].Description);
    }

    [Fact]
    public void Load_AFileMissingFromMetaStillBecomesACategory()
    {
        // Dropping a file in must be enough. A catalogue that needs its index
        // updated in a second place is one that will drift.
        Write("_meta.json", """{"categories":[{"id":"browsers","label":"Browsers"}]}""");
        Write("browsers.json", """[{"id":"Mozilla.Firefox","name":"Firefox"}]""");
        Write("hardware.json", """[{"id":"CPUID.CPU-Z","name":"CPU-Z"}]""");

        var catalog = new AppCatalog(_dir);
        catalog.Load();

        Assert.Contains(catalog.Categories, c => c.Id == "hardware");
        Assert.NotNull(catalog.Find("CPUID.CPU-Z"));
    }

    [Fact]
    public void Load_RecordsWhyAnAppIsMissing()
    {
        Write("_meta.json", """{"absent":[{"name":"MusicBee","reason":"No package in the winget catalogue."}]}""");
        Write("media.json", """[{"id":"VideoLAN.VLC","name":"VLC"}]""");

        var catalog = new AppCatalog(_dir);
        catalog.Load();

        var absence = Assert.Single(catalog.Absent);
        Assert.Equal("MusicBee", absence.Name);
        Assert.Equal("No package in the winget catalogue.", absence.Reason);
    }

    [Fact]
    public void Load_SkipsAMalformedFileAndKeepsTheOthers()
    {
        Write("good.json", """[{"id":"7zip.7zip","name":"7-Zip"}]""");
        Write("bad.json", """[{"id": "not closed""");

        var catalog = new AppCatalog(_dir);
        var count = catalog.Load();

        Assert.Equal(1, count);
        Assert.NotNull(catalog.Find("7zip.7zip"));
    }

    [Fact]
    public void Load_SkipsEntriesWithoutAnId()
    {
        Write("browsers.json", """[{"name":"no id"},{"id":"Google.Chrome","name":"Chrome"}]""");

        var catalog = new AppCatalog(_dir);

        Assert.Equal(1, catalog.Load());
        Assert.Null(catalog.Find(""));
    }

    [Fact]
    public void Load_ReadsTheOptionalFields()
    {
        Write("utilities.json", """
            [{
              "id": "7zip.7zip",
              "name": "7-Zip",
              "publisher": "Igor Pavlov",
              "description": "Archiver.",
              "homepage": "https://www.7-zip.org/",
              "tags": ["archive", "open-source"]
            }]
            """);

        var catalog = new AppCatalog(_dir);
        catalog.Load();

        var entry = catalog.Find("7zip.7zip")!;
        Assert.Equal("Igor Pavlov", entry.Publisher);
        Assert.Equal("https://www.7-zip.org/", entry.Homepage);
        Assert.Equal(new[] { "archive", "open-source" }, entry.Tags);
    }

    [Fact]
    public void ResolveDirectory_DoesNotThrowWhenNothingExists()
    {
        // Resolution walks up the tree; in a test host there may be no apps/
        // directory anywhere above it. The answer must be a path, never an
        // exception, because every command calls this first.
        var resolved = AppCatalog.ResolveDirectory();

        Assert.False(string.IsNullOrWhiteSpace(resolved));
    }

    [Fact]
    public void IsSafeId_AcceptsPackageIdsAndRefusesShellSyntax()
    {
        Assert.True(WinGet.IsSafeId("7zip.7zip"));
        Assert.True(WinGet.IsSafeId("Notepad++.Notepad++"));
        Assert.True(WinGet.IsSafeId("Microsoft.VisualStudio.2022.Community"));

        Assert.False(WinGet.IsSafeId("7zip.7zip & del /f *"));
        Assert.False(WinGet.IsSafeId(@"ARP\Machine\X86\Steam App 244210"));
        Assert.False(WinGet.IsSafeId("\"; calc; \""));
        Assert.False(WinGet.IsSafeId(""));
        Assert.False(WinGet.IsSafeId("   "));
        Assert.False(WinGet.IsSafeId("-starts-with-a-dash"));
    }
}
