using WinOpt.Engine.Gaming;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// Steam writes both its library list and its per-game manifests as KeyValues.
/// These documents are shaped like the real ones — tabs for separators, doubled
/// backslashes in paths, <c>//</c> comments — because the escape handling is
/// the entire reason this is a parser and not a pattern.
/// </summary>
public class VdfTests
{
    [Fact]
    public void Parse_ReadsEscapedBackslashesAndNesting()
    {
        var node = Vdf.Parse(
            "\"libraryfolders\"\n" +
            "{\n" +
            "\t\"0\"\t\t{\n" +
            "\t\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n" +
            "\t\t}\n" +
            "}\n");

        var folders = node.Children("libraryfolders").First();
        var first = folders.Children("0").First();
        Assert.Equal(@"D:\SteamLibrary", first.Get("path"));
    }

    [Fact]
    public void Parse_IgnoresComments()
    {
        var node = Vdf.Parse(
            "\"AppState\"\n" +
            "{\n" +
            "\t// this line used to be a library\n" +
            "\t\"name\"\t\t\"Among Us\"\n" +
            "}\n");

        Assert.Equal("Among Us", node.Children("AppState").First().Get("name"));
        Assert.Null(node.Children("AppState").First().Get("// this line used to be a library"));
    }

    [Fact]
    public void Parse_KeyLookup_IsCaseInsensitive()
    {
        // Older documents spell the root "LibraryFolders"; matching only the
        // modern lowercase spelling would silently return no libraries at all.
        var node = Vdf.Parse("\"LibraryFolders\"\n{\n\t\"1\"\t\t\"E:\\\\Games\"\n}");
        var folders = node.Children("libraryfolders").First();
        Assert.Equal(@"E:\Games", folders.Get("1"));
    }

    [Fact]
    public void Parse_KeepsRepeatedKeysInDocumentOrder()
    {
        // The format allows the same key twice; a dictionary would collapse
        // them and lose whichever entry the consumer wanted.
        var node = Vdf.Parse("\"root\"\n{\n\t\"item\"\t\t\"a\"\n\t\"item\"\t\t\"b\"\n}");
        var root = node.Children("root").First();

        var scalars = root.Entries
            .Where(e => e.Scalar != null && e.Key.Equals("item", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Scalar!)
            .ToList();

        Assert.Equal(new[] { "a", "b" }, scalars);
    }

    [Fact]
    public void ParseAppManifest_ReadsTheFieldsAGameNeeds()
    {
        var manifest = SteamLibrary.ParseAppManifest(
            "\"AppState\"\n" +
            "{\n" +
            "\t\"appid\"\t\t\"730\"\n" +
            "\t\"name\"\t\t\"Counter-Strike 2\"\n" +
            "\t\"StateFlags\"\t\t\"4\"\n" +
            "\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n" +
            "\t\"InstalledDepots\"\n" +
            "\t{\n" +
            "\t\t\"731\"\n" +
            "\t\t{\n" +
            "\t\t\t\"manifest\"\t\t\"1967034811910000000\"\n" +
            "\t\t}\n" +
            "\t}\n" +
            "}\n");

        Assert.NotNull(manifest);
        Assert.Equal("730", manifest!.AppId);
        Assert.Equal("Counter-Strike 2", manifest.Name);
        Assert.Equal("Counter-Strike Global Offensive", manifest.InstallDir);
        Assert.Equal("4", manifest.StateFlags);
    }

    [Fact]
    public void ParseAppManifest_TruncatedDownload_ReturnsNull()
    {
        // A file that is not a manifest must not become a game with an empty
        // name — that would offer to optimise a folder Steam never claimed.
        Assert.Null(SteamLibrary.ParseAppManifest(""));
        Assert.Null(SteamLibrary.ParseAppManifest("\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n}"));
        Assert.Null(SteamLibrary.ParseAppManifest("this is not a document"));
    }

    [Fact]
    public void ParseLibraryFolders_AlwaysIncludesTheSteamInstallItself()
    {
        var paths = SteamLibrary.ParseLibraryFolders(
            "\"libraryfolders\"\n{\n\t\"0\"\t\t{\n\t\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t}\n}",
            @"C:\Program Files (x86)\Steam");

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam" }, paths);
    }

    [Fact]
    public void ParseLibraryFolders_CollectsEverySecondaryLibrary()
    {
        var paths = SteamLibrary.ParseLibraryFolders(
            "\"libraryfolders\"\n" +
            "{\n" +
            "\t\"0\"\t\t{\n\t\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t}\n" +
            "\t\"1\"\t\t{\n\t\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t}\n" +
            "\t\"2\"\t\t{\n\t\t\t\"path\"\t\t\"K:\\\\SteamLibrary\"\n\t\t}\n" +
            "}");

        Assert.Equal(
            new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"K:\SteamLibrary" },
            paths);
    }

    [Fact]
    public void ParseLibraryFolders_OldIndexAsScalarFormat()
    {
        // Steam used to write libraries as scalar entries keyed by index. Only
        // the blocks would be read otherwise, which reports a machine with three
        // drives as one with the default install.
        var paths = SteamLibrary.ParseLibraryFolders(
            "\"LibraryFolders\"\n" +
            "{\n" +
            "\t\"TimeNextStatsReport\"\t\t\"1759300000\"\n" +
            "\t\"ContentStatsID\"\t\t\"-12345\"\n" +
            "\t\"1\"\t\t\"D:\\\\SteamLibrary\"\n" +
            "\t\"2\"\t\t\"K:\\\\SteamLibrary\"\n" +
            "}",
            @"C:\Program Files (x86)\Steam");

        Assert.Equal(
            new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"K:\SteamLibrary" },
            paths);
    }

    [Fact]
    public void ParseLibraryFolders_DropsEntriesThatAreNotPaths()
    {
        var paths = SteamLibrary.ParseLibraryFolders(
            "\"LibraryFolders\"\n" +
            "{\n" +
            "\t\"TimeNextStatsReport\"\t\t\"1759300000\"\n" +
            "\t\"1\"\t\t\"D:\\\\SteamLibrary\"\n" +
            "}",
            @"C:\Steam");

        Assert.DoesNotContain("1759300000", paths);
        Assert.Equal(new[] { @"C:\Steam", @"D:\SteamLibrary" }, paths);
    }

    [Fact]
    public void ParseLibraryFolders_ForwardSlashesAndDuplicates()
    {
        // Steam's own registry value uses forward slashes, and the same library
        // can appear twice — once as steamPath, once from the file.
        var paths = SteamLibrary.ParseLibraryFolders(
            "\"libraryfolders\"\n{\n\t\"0\"\t\t{\n\t\t\t\"path\"\t\t\"d:/steamlibrary\"\n\t\t}\n}",
            @"D:\SteamLibrary");

        Assert.Equal(new[] { @"D:\SteamLibrary" }, paths);
    }
}
