using WinOpt.Core.Models;
using WinOpt.Engine.Blocker;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The Blocker's hosts-side operations, run against a file that is not the
/// machine's. The behaviour that matters is the same either way: a rule that
/// is refused is refused before anything is written, and a rule that is
/// removed leaves everything outside the managed section alone.
/// </summary>
public class BlockerManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"novimize-blocker-{Guid.NewGuid():N}");

    private BlockerManager Subject(string initial = "# mine\n0.0.0.0 hand-added.test\n")
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(HostsPath, initial);
        return new BlockerManager(
            new HostsFile(HostsPath, BackupPath),
            Path.Combine(_dir, "cache"),
            Path.Combine(_dir, "ips"));
    }

    private string HostsPath => Path.Combine(_dir, "hosts");

    /// <summary>
    /// Its own backup file. The default lives in %LOCALAPPDATA%, so a test that
    /// used it would share one backup with the machine it is running on and
    /// could restore over it.
    /// </summary>
    private string BackupPath => Path.Combine(_dir, "hosts.bak");

    private string Original => "# mine\n0.0.0.0 hand-added.test\n";

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* already gone */ }
    }

    [Fact]
    public void AddDomain_WritesAManagedRuleAndLeavesTheHandAddedOne()
    {
        var manager = Subject();

        var change = manager.AddDomain("tracker.test", BlockCategory.Trackers, "Follows users.", BlockSeverity.Low);

        Assert.True(change.Success);
        var text = File.ReadAllText(HostsPath);
        Assert.StartsWith(Original, text);
        Assert.Contains("0.0.0.0 tracker.test", text);
        Assert.Contains(HostsFile.StartMarker, text);

        var rules = HostsFile.ParseInner(HostsFile.Split(text).Inner);
        var added = Assert.Single(rules);
        Assert.Equal("tracker.test", added.Id);
        Assert.Equal(BlockCategory.Trackers, added.Category);
        Assert.Equal("Follows users.", added.Purpose);
        Assert.Equal("custom", added.Source);
        Assert.True(added.Enabled);
    }

    [Fact]
    public void AddDomain_ThatIsAlreadyThereSaysSoInsteadOfDuplicating()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("tracker.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);

        var again = manager.AddDomain("TRACKER.test", BlockCategory.Ads, null, BlockSeverity.Low);

        Assert.True(again.Success);
        Assert.True(again.Unchanged);
        Assert.Contains("already in the managed section", again.Message);
        Assert.Single(HostsFile.ParseInner(HostsFile.Split(File.ReadAllText(HostsPath)).Inner));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("*.ads.test")]
    [InlineData("intranet")]
    public void AddDomain_RefusesNamesThatWouldDoHarm(string domain)
    {
        var manager = Subject();

        var change = manager.AddDomain(domain, BlockCategory.Ads, null, BlockSeverity.Low);

        Assert.False(change.Success);
        Assert.Contains("not a domain", change.Message);
        // Nothing was written at all.
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void SetDomainEnabled_TogglesTheLineRatherThanRemovingIt()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("tracker.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);

        var off = manager.SetDomainEnabled("tracker.test", enabled: false);

        Assert.True(off.Success);
        var text = File.ReadAllText(HostsPath);
        Assert.Contains(HostsFile.DisabledPrefix + "0.0.0.0 tracker.test", text);

        var rule = Assert.Single(HostsFile.ParseInner(HostsFile.Split(text).Inner));
        Assert.False(rule.Enabled);

        Assert.True(manager.SetDomainEnabled("tracker.test", enabled: true).Success);
        text = File.ReadAllText(HostsPath);
        rule = Assert.Single(HostsFile.ParseInner(HostsFile.Split(text).Inner));
        Assert.True(rule.Enabled);
        Assert.Contains("0.0.0.0 tracker.test", text);
    }

    [Fact]
    public void SetDomainEnabled_OnSomethingWeNeverAddedIsHonestAboutIt()
    {
        var manager = Subject();

        var change = manager.SetDomainEnabled("never.test", enabled: false);

        Assert.False(change.Success);
        Assert.Contains("not in the managed section", change.Message);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RemoveDomain_TakesTheRuleOutAndLeavesTheRest()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("tracker.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);
        Assert.True(manager.AddDomain("other.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);

        var change = manager.RemoveDomain("tracker.test");

        Assert.True(change.Success);
        var rules = HostsFile.ParseInner(HostsFile.Split(File.ReadAllText(HostsPath)).Inner);
        var only = Assert.Single(rules);
        Assert.Equal("other.test", only.Id);
        Assert.StartsWith(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RemoveDomain_ThatIsNotThereDoesNotRewriteTheFile()
    {
        var manager = Subject();

        var change = manager.RemoveDomain("never.test");

        Assert.True(change.Success);
        Assert.True(change.Unchanged);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void Unmerge_PutsTheFileBackExactlyAsItWasFound()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("tracker.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);
        // A backup is taken on the first write, so this holds the original.
        Assert.True(manager.Hosts.BackupExists);

        var change = manager.Unmerge();

        Assert.True(change.Success);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void Unmerge_WithNothingToRemoveSaysSo()
    {
        var manager = Subject();

        var change = manager.Unmerge();

        Assert.True(change.Success);
        Assert.True(change.Unchanged);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void Restore_PutsBackTheFirstWriteFromTheBackup()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("one.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);
        Assert.True(manager.AddDomain("two.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);

        var change = manager.Restore();

        Assert.True(change.Success);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RemoveSource_TakesOneListOffAndLeavesTheOthersAndTheHandWrittenLines()
    {
        var manager = Subject();
        var now = DateTimeOffset.Now;
        BlockRule Row(string id, string source, BlockCategory category, string? purpose = null) => new()
        {
            Id = id,
            Kind = BlockKind.Hosts,
            Value = HostsFile.Address,
            Category = category,
            Purpose = purpose,
            Source = source,
            Severity = BlockSeverity.Low,
            Enabled = true,
            Managed = true,
            AddedAt = now,
            UpdatedAt = now,
        };

        // One applied list, one custom rule the user wrote, side by side.
        Assert.True(manager.Hosts.Write(HostsFile.ToGroups(new[]
        {
            Row("a-one.test", "stevenblack", BlockCategory.Ads),
            Row("a-two.test", "stevenblack", BlockCategory.Ads),
            Row("solo.test", "custom", BlockCategory.Custom, "mine"),
        }, now)).Success);

        var change = manager.RemoveSourceAsync("stevenblack").GetAwaiter().GetResult();

        Assert.True(change.Success, change.Message);
        Assert.Equal(2, change.Affected);

        var rules = HostsFile.ParseInner(HostsFile.Split(File.ReadAllText(HostsPath)).Inner);
        var only = Assert.Single(rules);
        Assert.Equal("solo.test", only.Id);
        Assert.Equal("mine", only.Purpose);
        Assert.StartsWith(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void RemoveSource_OnAListThatIsNotAppliedSaysSo()
    {
        var manager = Subject();

        var change = manager.RemoveSourceAsync("never-listed").GetAwaiter().GetResult();

        Assert.True(change.Success);
        Assert.True(change.Unchanged);
        Assert.Contains("not applied", change.Message);
        Assert.Equal(Original, File.ReadAllText(HostsPath));
    }

    [Fact]
    public void ExportAndImport_MovesTheManagedSectionToAnotherFile()
    {
        var source = Subject();
        Assert.True(source.AddDomain("one.test", BlockCategory.Ads, "first", BlockSeverity.Medium).Success);
        Assert.True(source.AddDomain("two.test", BlockCategory.Malware, null, BlockSeverity.Low).Success);

        var file = Path.Combine(_dir, "export.json");
        var exported = source.ExportAsync(file).GetAwaiter().GetResult();
        Assert.True(exported.Success);
        Assert.True(File.Exists(file));

        var other = Path.Combine(_dir, "other-hosts.txt");
        File.WriteAllText(other, "# somewhere else\n");
        var target = new BlockerManager(
            new HostsFile(other, Path.Combine(_dir, "hosts2.bak")),
            Path.Combine(_dir, "cache2"),
            Path.Combine(_dir, "ips2"));

        var imported = target.ImportAsync(file).GetAwaiter().GetResult();
        Assert.True(imported.Success, imported.Message);

        var rules = HostsFile.ParseInner(HostsFile.Split(File.ReadAllText(other)).Inner);
        Assert.Equal(2, rules.Count);
        Assert.Equal(new[] { "one.test", "two.test" }, rules.Select(r => r.Id).OrderBy(x => x));
        Assert.Equal("first", rules.Single(r => r.Id == "one.test").Purpose);
        Assert.Equal(BlockSeverity.Medium, rules.Single(r => r.Id == "one.test").Severity);
        // The other file's own content survives.
        Assert.StartsWith("# somewhere else\n", File.ReadAllText(other));
    }

    [Fact]
    public void Import_WithNoSuchFileSaysSoRatherThanThrowing()
    {
        var manager = Subject();

        var change = manager.ImportAsync(Path.Combine(_dir, "nope.json")).GetAwaiter().GetResult();

        Assert.False(change.Success);
        Assert.Contains("No export file", change.Message);
    }

    [Fact]
    public void Status_CountsWhatIsOursAndWhatIsNot()
    {
        var manager = Subject();
        Assert.True(manager.AddDomain("one.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);
        Assert.True(manager.AddDomain("two.test", BlockCategory.Ads, null, BlockSeverity.Low).Success);
        Assert.True(manager.SetDomainEnabled("two.test", enabled: false).Success);

        var status = manager.StatusAsync().GetAwaiter().GetResult();

        Assert.False(status.HostsMalformed);
        Assert.Equal(2, status.Managed);
        Assert.Equal(1, status.Enabled);
        Assert.Equal(1, status.Unmanaged);
        Assert.True(status.BackupExists);
        Assert.Equal(2, status.Rules.Count);
        Assert.All(status.Rules, r => Assert.Equal(BlockKind.Hosts, r.Kind));
    }

    [Fact]
    public void Status_AdmitsItCannotWriteWhenTheFileIsNotWritable()
    {
        var manager = Subject();
        File.SetAttributes(HostsPath, FileAttributes.ReadOnly);
        try
        {
            var status = manager.StatusAsync().GetAwaiter().GetResult();
            Assert.False(status.Writable);
        }
        finally
        {
            File.SetAttributes(HostsPath, FileAttributes.Normal);
        }
    }
}
