using WinOpt.Core.Models;
using WinOpt.Engine.Debloat;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The allowlist and denylist. It is an opinion file, so what matters is that
/// a bad entry costs one entry rather than the catalogue, that a verdict is
/// spelled the way the enum expects, and that the file shipped in the
/// repository actually loads.
/// </summary>
public class DebloatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"novimize-debloat-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* already gone */ }
    }

    private DebloatPolicy Catalogue(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, DebloatPolicy.FileName), json);
        var policy = new DebloatPolicy(_dir);
        policy.Load();
        return policy;
    }

    [Fact]
    public void Load_ReadsTheWrappedEntriesFormTheShippedFileUses()
    {
        var policy = Catalogue("""
            {
              "entries": [
                { "id": "Microsoft.WindowsStore", "verdict": "protected", "reason": "The Store." },
                { "id": "Microsoft.BingNews", "verdict": "safe", "reason": "News widget." }
              ]
            }
            """);

        Assert.Equal(2, policy.Load());
        Assert.Equal(DebloatVerdict.Protected, policy.Find("Microsoft.WindowsStore")!.Verdict);
        Assert.Equal(DebloatVerdict.Safe, policy.Find("Microsoft.BingNews")!.Verdict);
        Assert.Equal("News widget.", policy.Find("Microsoft.BingNews")!.Reason);
    }

    [Fact]
    public void Load_AlsoAcceptsABareArray()
    {
        var policy = Catalogue("""
            [ { "id": "X", "verdict": "keep", "reason": "Noticed when gone." } ]
            """);

        Assert.Equal(1, policy.Load());
        Assert.Equal(DebloatVerdict.Keep, policy.Find("X")!.Verdict);
    }

    [Fact]
    public void Load_SkipsAnEntryItCannotReadWithoutLosingTheRest()
    {
        var policy = Catalogue("""
            [ { "id": "bad", "verdict": "NotAVerdict", "reason": "typo" },
              { "id": "good", "verdict": "safe", "reason": "ok" } ]
            """);

        Assert.Equal(1, policy.Load());
        Assert.Null(policy.Find("bad"));
        Assert.NotNull(policy.Find("good"));
    }

    [Fact]
    public void Load_WithABrokenFileReturnsZeroRatherThanThrowing()
    {
        var policy = Catalogue("not json at all");

        Assert.Equal(0, policy.Load());
        Assert.Null(policy.Find("anything"));
    }

    [Fact]
    public void Load_WithNoFileReturnsZero()
    {
        Directory.CreateDirectory(_dir);

        Assert.Equal(0, new DebloatPolicy(_dir).Load());
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndSkipsBlankIds()
    {
        var policy = Catalogue("""
            [ { "id": "Microsoft.BingNews", "verdict": "safe", "reason": "x" },
              { "id": "  ", "verdict": "safe", "reason": "y" } ]
            """);

        Assert.Equal(1, policy.Load());
        Assert.NotNull(policy.Find("MICROSOFT.BINGNEWS"));
        Assert.Null(policy.Find("  "));
    }

    /// <summary>
    /// The file in the repository is what ships, so it has to load, it has to
    /// cover the packages it claims to, and every verdict has to be one the
    /// enum knows — a typo would make the entry read as Unknown and quietly
    /// offer a package the file meant to protect.
    /// </summary>
    [Fact]
    public void ShippedPolicy_LoadsAndUsesOnlyRealVerdicts()
    {
        var directory = DebloatPolicy.ResolveDirectory();
        var policy = new DebloatPolicy(directory);
        var count = policy.Load();

        Assert.True(count > 0, $"no policy found at {directory}");
        Assert.Equal(count, policy.Entries.Count);

        foreach (var entry in policy.Entries.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.True(
                entry.Verdict is DebloatVerdict.Safe or DebloatVerdict.Keep
                    or DebloatVerdict.Protected or DebloatVerdict.Unknown,
                $"{entry.Id} has a verdict the enum does not know");
            // Every entry says something, so the row can show why.
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"{entry.Id} has no reason");
        }

        // The ones that must never be offered.
        Assert.Equal(DebloatVerdict.Protected, policy.Find("Microsoft.WindowsStore")!.Verdict);
        Assert.Equal(DebloatVerdict.Protected, policy.Find("Microsoft.Windows.StartMenuExperienceHost")!.Verdict);
    }
}
