using WinOpt.Core.Models;
using WinOpt.Engine.Blocker;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// A blocklist is somebody else's prose, and the hosts file is ours to keep
/// intact. These tests are the set of things that must not be imported from a
/// downloaded file: loopback names, wildcards, addresses pretending to be
/// domains, and lines that cannot be read.
/// </summary>
public class BlocklistFormatsTests
{
    [Fact]
    public void ParseDomains_ReadsTheThreeShapesAHostsFileUses()
    {
        var text = "0.0.0.0 ads.test\n" +
                   "127.0.0.1 track.test # with a comment\n" +
                   "bare.test\n";

        var parsed = BlocklistFormats.ParseDomains(text);

        Assert.Equal(new[] { "ads.test", "track.test", "bare.test" }, parsed.Values);
        Assert.Equal(3, parsed.Lines);
        Assert.Equal(0, parsed.Skipped);
    }

    [Fact]
    public void ParseDomains_TakesEveryNameOnALine()
    {
        var parsed = BlocklistFormats.ParseDomains("0.0.0.0 one.test two.test\n");

        Assert.Equal(new[] { "one.test", "two.test" }, parsed.Values);
    }

    [Fact]
    public void ParseDomains_NeverBlocksTheMachineOffItsOwnName()
    {
        // The single most damaging line a hosts file can contain. A list that
        // ships `127.0.0.1 localhost` must not turn that into a block.
        var parsed = BlocklistFormats.ParseDomains(
            "127.0.0.1 localhost\n0.0.0.0 localhost\n127.0.0.1 broadcasthost\n127.0.0.1 ip6-localhost\n");

        Assert.Empty(parsed.Values);
    }

    [Fact]
    public void ParseDomains_RefusesWildcardsAddressesAndBareLabels()
    {
        var parsed = BlocklistFormats.ParseDomains(
            "*.wild.test\n" +
            "1.2.3.4\n" +
            "intranet\n" +
            "under_score.test\n" +
            "good.test\n");

        var only = Assert.Single(parsed.Values);
        Assert.Equal("good.test", only);
        Assert.Equal(4, parsed.Skipped);
    }

    [Fact]
    public void ParseDomains_CountsCommentsWithoutCountingThemAsSkipped()
    {
        var parsed = BlocklistFormats.ParseDomains("# a header\n# another\n0.0.0.0 real.test\n");

        var only = Assert.Single(parsed.Values);
        Assert.Equal("real.test", only);
        Assert.Equal(0, parsed.Skipped);
        Assert.Equal(3, parsed.Lines);
    }

    [Fact]
    public void ParseDomains_DeduplicatesCaseInsensitivelyAndLowercases()
    {
        var parsed = BlocklistFormats.ParseDomains("0.0.0.0 ADS.Test\n0.0.0.0 ads.test.\n");

        var only = Assert.Single(parsed.Values);
        Assert.Equal("ads.test", only);
    }

    [Fact]
    public void NormalizeAddress_AcceptsSingleAddressesAndRanges()
    {
        Assert.Equal("1.2.3.4", BlocklistFormats.NormalizeAddress("1.2.3.4"));
        Assert.Equal("1.2.3.0/24", BlocklistFormats.NormalizeAddress("1.2.3.0/24"));
        Assert.Equal("::1", BlocklistFormats.NormalizeAddress("::1"));
    }

    [Fact]
    public void NormalizeAddress_RefusesBadRangesAndDomainNames()
    {
        Assert.Null(BlocklistFormats.NormalizeAddress("1.2.3.0/33"));
        Assert.Null(BlocklistFormats.NormalizeAddress("example.test"));
        Assert.Null(BlocklistFormats.NormalizeAddress(""));
    }

    [Fact]
    public void ParseAddresses_TakesTheAddressFromAHostsStyleLine()
    {
        var parsed = BlocklistFormats.ParseAddresses("# comment\n0.0.0.0 not-an-address\n1.2.3.4\n5.6.7.0/24\n");

        Assert.Equal(new[] { "0.0.0.0", "1.2.3.4", "5.6.7.0/24" }, parsed.Values);
    }

    [Fact]
    public void ParseAddresses_SkipsNamesRatherThanBlockingThem()
    {
        var parsed = BlocklistFormats.ParseAddresses("notanip.test\n");

        Assert.Empty(parsed.Values);
        Assert.Equal(1, parsed.Skipped);
    }

    [Fact]
    public void Diff_CountsBothDirections()
    {
        var (added, removed) = BlocklistFormats.Diff(
            new[] { "keep.test", "gone.test" },
            new[] { "keep.test", "new.test" });

        Assert.Equal(1, added);
        Assert.Equal(1, removed);
    }
}

/// <summary>
/// The catalogue of lists Novimize will fetch. Nothing here touches the
/// network: the point is that a malformed or missing file fails quietly and
/// visibly rather than taking the page with it.
/// </summary>
public class BlocklistCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"novimize-blocklists-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* already gone */ }
    }

    [Fact]
    public void Load_ReadsEveryFieldTheBriefRequires()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sources.json"), """
            [
              {
                "id": "example",
                "name": "Example list",
                "category": "Ads",
                "format": "hosts",
                "url": "https://example.test/list.txt",
                "homepage": "https://example.test/",
                "license": "MIT",
                "purpose": "Advertising domains.",
                "breakage": "Nothing known.",
                "severity": "Low",
                "tags": ["ads"]
              }
            ]
            """);

        var catalog = new BlocklistCatalog(_dir);
        Assert.Equal(1, catalog.Load());

        var source = catalog.Find("example");
        Assert.NotNull(source);
        Assert.Equal(BlockCategory.Ads, source!.Category);
        Assert.Equal(BlockSeverity.Low, source.Severity);
        Assert.Equal("https://example.test/list.txt", source.Url);
        Assert.Equal("https://example.test/", source.Homepage);
        Assert.Equal("MIT", source.License);
        Assert.Equal("Advertising domains.", source.Purpose);
        Assert.Equal("Nothing known.", source.Breakage);
        Assert.Equal("hosts", source.Format);
        Assert.Equal(new[] { "ads" }, source.Tags);
    }

    [Fact]
    public void Load_TreatsCategoriesAndSeverityCaseInsensitively()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sources.json"),
            """[{"id":"x","name":"X","category":"malware","format":"hosts","url":"https://e.test/x","severity":"medium"}]""");

        var catalog = new BlocklistCatalog(_dir);
        catalog.Load();

        var source = catalog.Find("x");
        Assert.Equal(BlockCategory.Malware, source!.Category);
        Assert.Equal(BlockSeverity.Medium, source.Severity);
    }

    [Fact]
    public void Load_SkipsAnEntryItCannotReadWithoutLosingTheRest()
    {
        // A category that is not in the enum is a typo in a hand-edited file.
        // It must cost that entry, not the ones around it.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sources.json"),
            """[{"id":"bad","name":"Bad","category":"NotACategory","format":"hosts","url":"https://e.test/bad"},""" +
            """{"id":"good","name":"Good","category":"Ads","format":"hosts","url":"https://e.test/good"}]""");

        var catalog = new BlocklistCatalog(_dir);

        Assert.Equal(1, catalog.Load());
        Assert.Null(catalog.Find("bad"));
        Assert.Equal(BlockCategory.Ads, catalog.Find("good")!.Category);
    }

    [Fact]
    public void Load_WithABrokenFileReturnsZeroRatherThanThrowing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sources.json"), "not json at all");

        var catalog = new BlocklistCatalog(_dir);

        Assert.Equal(0, catalog.Load());
        Assert.Empty(catalog.Sources);
    }

    [Fact]
    public void Load_WithNoFileReturnsZero()
    {
        Directory.CreateDirectory(_dir);

        var catalog = new BlocklistCatalog(_dir);

        Assert.Equal(0, catalog.Load());
        Assert.Null(catalog.Find("anything"));
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndSkipsBlankIds()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "sources.json"),
            """[{"id":"StevenBlack","name":"S","category":"Ads","format":"hosts","url":"https://e.test/s"},{"id":" ","name":"N","category":"Ads","format":"hosts","url":"https://e.test/n"}]""");

        var catalog = new BlocklistCatalog(_dir);
        Assert.Equal(1, catalog.Load());
        Assert.NotNull(catalog.Find("STEVENBLACK"));
        Assert.Null(catalog.Find(" "));
    }

    [Fact]
    public void CacheFileName_NamesTheFileAfterItsFormat()
    {
        var hosts = new BlockSource { Id = "a", Format = "hosts" };
        var ips = new BlockSource { Id = "a", Format = "ips" };

        Assert.Equal("a.hosts", BlocklistCatalog.CacheFileName(hosts));
        Assert.Equal("a.ips", BlocklistCatalog.CacheFileName(ips));
    }
}

/// <summary>
/// The firewall rule namespace. Everything that decides whether a rule is
/// Novimize's to touch lives here, so a mistake is a rule left alone or
/// removed — never somebody else's.
/// </summary>
public class FirewallNamespaceTests
{
    [Theory]
    [InlineData("stevenblack")]
    [InlineData("game.exe")]
    [InlineData("Some.App-2")]
    public void SafeIds_RoundTripThroughARuleName(string id)
    {
        Assert.True(FirewallBlocker.IsSafeId(id));
        var name = FirewallBlocker.RuleName(id);
        Assert.Equal($"Novimize.Block.{id}", name);
        Assert.True(FirewallBlocker.IsManaged(name));
        Assert.Equal(id, FirewallBlocker.IdOf(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("quote\"d")]
    [InlineData("path\\to")]
    [InlineData("way-too-long-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void UnsafeIds_AreRefused(string id) => Assert.False(FirewallBlocker.IsSafeId(id));

    [Fact]
    public void ForeignRuleNames_AreNeverOurs()
    {
        Assert.False(FirewallBlocker.IsManaged("Novimize.Block2.stuff"));
        Assert.False(FirewallBlocker.IsManaged("User's own rule"));
        Assert.False(FirewallBlocker.IsManaged(null));
        Assert.False(FirewallBlocker.IsManaged("Novimize.Block."));
        Assert.Null(FirewallBlocker.IdOf("User's own rule"));
    }

    [Fact]
    public void Description_RoundTripsThroughWriteAndRead()
    {
        var written = FirewallBlocker.Describe(
            BlockCategory.Telemetry, BlockSeverity.Medium, "some-list", "Phones home.");

        FirewallBlocker.ParseDescription(written,
            out var category, out var severity, out var source, out var purpose);

        Assert.Equal(BlockCategory.Telemetry, category);
        Assert.Equal(BlockSeverity.Medium, severity);
        Assert.Equal("some-list", source);
        Assert.Equal("Phones home.", purpose);
    }

    [Fact]
    public void Description_WithAnEmptyPurposeAndNoSourceStillParses()
    {
        FirewallBlocker.ParseDescription(FirewallBlocker.Describe(BlockCategory.Ads, BlockSeverity.Low, null, null),
            out var category, out var severity, out var source, out var purpose);

        Assert.Equal(BlockCategory.Ads, category);
        Assert.Equal(BlockSeverity.Low, severity);
        Assert.Equal("custom", source);
        Assert.Null(purpose);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Somebody else's description")]
    [InlineData("Novimize — only — three fields")]
    public void Description_ThatDoesNotOursFallsBackToDefaults(string? description)
    {
        FirewallBlocker.ParseDescription(description,
            out var category, out var severity, out var source, out var purpose);

        Assert.Equal(BlockCategory.Custom, category);
        Assert.Equal(BlockSeverity.Low, severity);
        Assert.Equal("custom", source);
        Assert.Null(purpose);
    }
}
