using System.Text.Json;
using System.Text.Json.Serialization;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Blocker;

/// <summary>
/// The blocklists Novimize knows how to fetch.
///
/// This is a catalogue of URLs, not a bundle of content: nothing a third party
/// publishes is shipped inside the installer, so a list that turns bad is fixed
/// by editing one JSON file rather than by patching every copy already on disk.
/// The entry says where the list comes from, what it claims to block, what it
/// might break, and under what terms — all of which is shown before anything is
/// downloaded, because the decision to trust a source should not be made after
/// the bytes have already arrived.
/// </summary>
public sealed class BlocklistCatalog
{
    public const string FileName = "sources.json";

    private readonly List<BlockSource> _sources = new();

    public string Directory { get; }

    public IReadOnlyList<BlockSource> Sources => _sources;

    public BlocklistCatalog(string directory) => Directory = directory;

    /// <summary>Same resolution order as the tweak and app catalogues.</summary>
    public static string ResolveDirectory()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "blocklists");
        if (System.IO.Directory.Exists(portable)) return portable;

        var bundled = Path.Combine(AppContext.BaseDirectory, "resources", "blocklists");
        if (System.IO.Directory.Exists(bundled)) return bundled;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "blocklists");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return portable;
    }

    public int Load()
    {
        _sources.Clear();
        var path = Path.Combine(Directory, FileName);
        if (!File.Exists(path)) return 0;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        try
        {
            // Deserialized one entry at a time: a single mistyped category in
            // a hand-edited file must cost that entry, not the catalogue.
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array) return 0;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                try
                {
                    var entry = element.Deserialize<BlockSource>(options);
                    if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id)) _sources.Add(entry);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skipped an unreadable source in {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {path}: {ex.Message}");
        }

        return _sources.Count;
    }

    public BlockSource? Find(string id) =>
        _sources.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where a fetched copy is cached, and what extension it carries. The
    /// extension follows the format so a cached hosts file and a cached address
    /// set cannot be mistaken for each other when somebody opens the folder.
    /// </summary>
    public static string CacheFileName(BlockSource source) =>
        source.Id + (source.Format.Equals("ips", StringComparison.OrdinalIgnoreCase) ? ".ips" : ".hosts");
}
