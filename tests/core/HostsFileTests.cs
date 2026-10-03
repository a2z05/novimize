using WinOpt.Core.Models;
using WinOpt.Engine.Blocker;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The hosts file belongs to the user; Novimize borrows a corner of it. These
/// tests pin the two properties that decide whether borrowing is safe: bytes
/// outside the markers survive every operation, and a file whose markers do not
/// pair up is refused rather than guessed at.
/// </summary>
public class HostsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"novimize-hosts-{Guid.NewGuid():N}");

    private HostsFile Subject(string text)
    {
        Directory.CreateDirectory(_dir);
        var hosts = Path.Combine(_dir, "hosts");
        File.WriteAllText(hosts, text);
        return new HostsFile(hosts, Path.Combine(_dir, "hosts.bak"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* already gone */ }
    }

    private static HostsGroup Group(params string[] hosts) => new(
        "test-list",
        BlockCategory.Ads,
        "https://example.test/list.txt",
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        "A test list.",
        hosts.Select(h => new BlockRule { Id = h, Value = HostsFile.Address }).ToList());

    // --- splitting ---------------------------------------------------------

    [Fact]
    public void Split_WithoutMarkers_IsAnAppendNotAMalformation()
    {
        var document = HostsFile.Split("# mine\n0.0.0.0 example.test\n");

        Assert.False(document.Malformed);
        Assert.Null(document.Inner);
        Assert.Equal("# mine\n0.0.0.0 example.test\n", document.Prefix);
        Assert.Equal(string.Empty, document.Suffix);
    }

    [Fact]
    public void Split_KeepsEverythingOutsideTheMarkersByteForByte()
    {
        // Note the \r\n on the user's line and the \n inside the section: the
        // halves must not be normalised, or a "no-op" rewrite rewrites.
        var text = "127.0.0.1 localhost\r\n# mine, added 2020\r\n"
                 + HostsFile.StartMarker + "\n"
                 + "# novimize-list: ads | test-list | 2026-01-02T03:04:05.0000000+00:00 | 2026-03-04T05:06:07.0000000+00:00 | https://example.test/list.txt | A test list.\n"
                 + "0.0.0.0 tracker.test\n"
                 + HostsFile.EndMarker + "\n"
                 + "0.0.0.0 user-kept.test\n";

        var document = HostsFile.Split(text);

        Assert.False(document.Malformed);
        Assert.Equal("127.0.0.1 localhost\r\n# mine, added 2020\r\n", document.Prefix);
        Assert.Equal("0.0.0.0 user-kept.test\n", document.Suffix);
        Assert.NotNull(document.Inner);
        Assert.Contains("tracker.test", document.Inner);
        Assert.DoesNotContain("user-kept.test", document.Inner);
    }

    [Fact]
    public void Split_TakesTheEndMarkerLineOutOfBothHalves()
    {
        // The end marker spans from its own first character to just past its
        // newline. Ending at Start instead would print the marker twice.
        var text = "a\n" + HostsFile.StartMarker + "\nb\n" + HostsFile.EndMarker + "\nc\n";

        var document = HostsFile.Split(text);

        Assert.Equal("a\n", document.Prefix);
        Assert.Equal("b\n", document.Inner);
        Assert.Equal("c\n", document.Suffix);
    }

    [Fact]
    public void Split_WithOnlyOneMarkerRefusesRatherThanGuesses()
    {
        Assert.True(HostsFile.Split("a\n" + HostsFile.StartMarker + "\nb\n").Malformed);
        Assert.True(HostsFile.Split("a\n" + HostsFile.EndMarker + "\nb\n").Malformed);
    }

    [Fact]
    public void Split_WithThePairBackwardsRefuses()
    {
        var text = HostsFile.EndMarker + "\nbody\n" + HostsFile.StartMarker + "\n";

        Assert.True(HostsFile.Split(text).Malformed);
    }

    [Fact]
    public void Split_WithASecondSectionRefuses()
    {
        var text = HostsFile.StartMarker + "\nx\n" + HostsFile.EndMarker + "\n"
                 + HostsFile.StartMarker + "\ny\n" + HostsFile.EndMarker + "\n";

        Assert.True(HostsFile.Split(text).Malformed);
    }

    [Fact]
    public void Split_AQuotedMarkerInACommentIsNotAMarker()
    {
        // A user who pasted the marker into a note would otherwise find their
        // file declared unrepairable.
        var text = "# " + HostsFile.StartMarker + "\n"
                 + HostsFile.StartMarker + "\n"
                 + "0.0.0.0 x.test\n"
                 + HostsFile.EndMarker + "\n";

        var document = HostsFile.Split(text);

        Assert.False(document.Malformed);
        Assert.StartsWith("# ", document.Prefix);
        Assert.Equal("0.0.0.0 x.test\n", document.Inner);
    }

    [Fact]
    public void Split_PrefersCrLfWhenTheFileUsesIt()
    {
        var text = "a\r\n" + HostsFile.StartMarker + "\r\nb\r\n" + HostsFile.EndMarker + "\r\nc\r\n";

        var document = HostsFile.Split(text);

        Assert.Equal("\r\n", document.NewLine);
        Assert.Equal("a\r\n", document.Prefix);
        Assert.Equal("b\r\n", document.Inner);
        Assert.Equal("c\r\n", document.Suffix);
    }

    // --- parsing the section ----------------------------------------------

    [Fact]
    public void ParseInner_GivesEveryRuleTheListHeaderItSitsUnder()
    {
        var inner = "# novimize-list: trackers | some-list | 2026-01-02T03:04:05.0000000+00:00 | 2026-01-02T03:04:05.0000000+00:00 | https://example.test/t.txt | Follows users between sites.\n"
                  + "0.0.0.0 one.test\n"
                  + "# novimize-off 0.0.0.0 two.test\n";

        var rules = HostsFile.ParseInner(inner);

        Assert.Equal(2, rules.Count);
        Assert.All(rules, r =>
        {
            Assert.Equal(BlockCategory.Trackers, r.Category);
            Assert.Equal("some-list", r.Source);
            Assert.Equal("https://example.test/t.txt", r.SourceUrl);
            Assert.Equal("Follows users between sites.", r.Purpose);
            Assert.True(r.Managed);
        });
        Assert.True(rules[0].Enabled);
        Assert.False(rules[1].Enabled);
        Assert.Equal("two.test", rules[1].Id);
    }

    [Fact]
    public void ParseInner_LetsARuleHeaderOverrideItsList()
    {
        var inner = "# novimize-list: ads | some-list | 2026-01-02T03:04:05.0000000+00:00 | 2026-01-02T03:04:05.0000000+00:00 | - | Ads.\n"
                  + "0.0.0.0 plain.test\n"
                  + "# novimize-rule: high-risk | some-list | medium | 2026-02-01T00:00:00.0000000+00:00 | Calls home.\n"
                  + "0.0.0.0 special.test\n";

        var rules = HostsFile.ParseInner(inner);

        Assert.Equal(2, rules.Count);
        Assert.Equal(BlockCategory.Ads, rules[0].Category);
        Assert.Equal(BlockSeverity.Low, rules[0].Severity);
        Assert.Equal(BlockCategory.Custom, rules[1].Category);
        Assert.Equal(BlockSeverity.Medium, rules[1].Severity);
        Assert.Equal("Calls home.", rules[1].Purpose);
        Assert.Null(rules[1].SourceUrl);
    }

    [Fact]
    public void ParseInner_KeepsALineItDoesNotUnderstandRatherThanDroppingIt()
    {
        // A hand-added block outside any Novimize comment is still enforced by
        // Windows; reporting the section as smaller than it is would be a lie.
        var inner = "# novimize-list: ads | some-list | 2026-01-02T03:04:05.0000000+00:00 | 2026-01-02T03:04:05.0000000+00:00 | - | Ads.\n"
                  + "# a plain comment\n"
                  + "0.0.0.0 managed.test\n"
                  + "127.0.0.1 stray.test\n";

        var rules = HostsFile.ParseInner(inner);

        Assert.Equal(2, rules.Count);
        Assert.Equal("managed.test", rules[0].Id);
        Assert.Equal("stray.test", rules[1].Id);
    }

    [Fact]
    public void ParseInner_IgnoresLinesThatAreNotAddressLines()
    {
        var inner = "# novimize-list: ads | some-list | 2026-01-02T03:04:05.0000000+00:00 | 2026-01-02T03:04:05.0000000+00:00 | - | Ads.\n"
                  + "not-an-address\n"
                  + "0.0.0.0\n"
                  + "0.0.0.0 ok.test # trailing comment\n";

        var rules = HostsFile.ParseInner(inner);

        var only = Assert.Single(rules);
        Assert.Equal("ok.test", only.Id);
    }

    [Fact]
    public void ParseInner_AnEmptyOrNullSectionIsEmptyNotAnError()
    {
        Assert.Empty(HostsFile.ParseInner(null));
        Assert.Empty(HostsFile.ParseInner(string.Empty));
    }

    // --- writing -----------------------------------------------------------

    [Fact]
    public void Write_AppendsAManagedSectionAndLeavesTheRestAlone()
    {
        var subject = Subject("# user rule\n0.0.0.0 mine.test\n");
        var original = File.ReadAllText(subject.HostsPath);

        var change = subject.Write(new[] { Group("a.test", "b.test") });

        Assert.True(change.Success);
        Assert.Equal(2, change.Affected);

        var written = File.ReadAllText(subject.HostsPath);
        Assert.StartsWith(original, written);
        Assert.Contains(HostsFile.StartMarker, written);
        Assert.Contains(HostsFile.EndMarker, written);
        Assert.Contains("0.0.0.0 a.test", written);
        Assert.Contains("0.0.0.0 b.test", written);
        // The marker must not land on the same line as the user's last entry.
        Assert.Contains("0.0.0.0 mine.test\n" + HostsFile.StartMarker, written);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsTheSameRules()
    {
        var subject = Subject("127.0.0.1 localhost\r\n");

        Assert.True(subject.Write(new[] { Group("a.test", "b.test") }).Success);

        var rules = subject.ReadManagedRules();
        Assert.Equal(2, rules.Count);
        Assert.Equal(new[] { "a.test", "b.test" }, rules.Select(r => r.Id));
        // Every rule inherits the list it was written under: same category,
        // same source, same purpose, same URL.
        Assert.All(rules, r =>
        {
            Assert.Equal(BlockCategory.Ads, r.Category);
            Assert.Equal("test-list", r.Source);
            Assert.Equal("https://example.test/list.txt", r.SourceUrl);
            Assert.Equal("A test list.", r.Purpose);
        });
    }

    [Fact]
    public void Write_ASecondTimeReplacesRatherThanStacks()
    {
        var subject = Subject("127.0.0.1 localhost\r\n");

        Assert.True(subject.Write(new[] { Group("a.test") }).Success);
        Assert.True(subject.Write(new[] { Group("b.test") }).Success);

        var rules = subject.ReadManagedRules();
        var only = Assert.Single(rules);
        Assert.Equal("b.test", only.Id);
        Assert.Equal(1, File.ReadAllText(subject.HostsPath).Split(HostsFile.StartMarker).Length - 1);
    }

    [Fact]
    public void Write_IsRefusedWhenTheMarkersDoNotPair()
    {
        var subject = Subject("127.0.0.1 localhost\r\n" + HostsFile.StartMarker + "\n0.0.0.0 orphan.test\n");

        var change = subject.Write(new[] { Group("a.test") });

        Assert.False(change.Success);
        Assert.Contains("unbalanced", change.Message);
        // Nothing was written, so the file is exactly as it was.
        Assert.Equal(
            "127.0.0.1 localhost\r\n" + HostsFile.StartMarker + "\n0.0.0.0 orphan.test\n",
            File.ReadAllText(subject.HostsPath));
    }

    [Fact]
    public void Write_TakesTheBackupOnTheFirstWriteOnly()
    {
        var subject = Subject("# original\n");

        Assert.True(subject.Write(new[] { Group("a.test") }).Success);
        Assert.True(subject.BackupExists);
        Assert.Equal("# original\n", File.ReadAllText(subject.BackupPath));

        // A later update must not redefine "original" as the state after the
        // first blocklist went in.
        Assert.True(subject.Write(new[] { Group("b.test") }).Success);
        Assert.Equal("# original\n", File.ReadAllText(subject.BackupPath));
    }

    // --- unmerge and restore ----------------------------------------------

    [Fact]
    public void RemoveManaged_TakesOutOnlyTheManagedSection()
    {
        var subject = Subject("# before\n0.0.0.0 mine.test\n");
        Assert.True(subject.Write(new[] { Group("a.test") }).Success);
        var original = File.ReadAllText(subject.BackupPath);

        var change = subject.RemoveManaged();

        Assert.True(change.Success);
        Assert.Equal(0, change.Affected);
        Assert.Equal(original, File.ReadAllText(subject.HostsPath));
        Assert.Empty(subject.ReadManagedRules());
    }

    [Fact]
    public void RemoveManaged_WhenThereIsNothingToRemoveSaysSo()
    {
        var subject = Subject("# plain file\n");

        var change = subject.RemoveManaged();

        Assert.True(change.Success);
        Assert.True(change.Unchanged);
        Assert.Equal("# plain file\n", File.ReadAllText(subject.HostsPath));
    }

    [Fact]
    public void RestoreBackup_PutsBackTheFirstWrite()
    {
        var subject = Subject("# original\n");
        Assert.True(subject.Write(new[] { Group("a.test") }).Success);

        var change = subject.RestoreBackup();

        Assert.True(change.Success);
        Assert.Equal("# original\n", File.ReadAllText(subject.HostsPath));
    }

    [Fact]
    public void RestoreBackup_WithoutABackupIsHonestAboutIt()
    {
        var subject = Subject("# plain file\n");

        var change = subject.RestoreBackup();

        Assert.False(change.Success);
        Assert.Contains("No backup", change.Message);
    }

    // --- counting ----------------------------------------------------------

    [Fact]
    public void CountUnmanaged_OnlyCountsRealAddressLines()
    {
        var unmanaged = HostsFile.CountUnmanaged(
            "# comment\n0.0.0.0 user.test\nnot-a-host\n",
            "0.0.0.0 also-user.test\n");

        Assert.Equal(2, unmanaged);
    }
}
