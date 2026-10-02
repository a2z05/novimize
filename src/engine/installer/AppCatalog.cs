using System.Text.Json;
using WinOpt.Core.Models;

namespace WinOpt.Engine.Installer;

/// <summary>One category of the catalogue, as the UI shows it.</summary>
public sealed class AppCategory
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Description { get; init; }
}

/// <summary>
/// The app catalogue: one JSON file per category under <c>apps/</c>, plus an
/// optional <c>_meta.json</c> that orders the categories and records the
/// packages that were deliberately left out.
///
/// Adding an app is appending an object to one of those files. Nothing in the
/// engine, the CLI, the Tauri layer or the UI changes, which is the whole
/// point — a catalogue that needs a code change to grow is a catalogue that
/// will not grow.
///
/// The category of an entry is its file name, not a field inside it. A field
/// can disagree with the file it lives in; a file name cannot.
/// </summary>
public sealed class AppCatalog
{
    /// <summary>Files starting with this are configuration, not a category.</summary>
    private const string MetaFile = "_meta.json";

    private readonly List<AppEntry> _entries = new();
    private readonly List<AppCategory> _categories = new();

    public string Directory { get; }

    public IReadOnlyList<AppEntry> Entries => _entries;
    public IReadOnlyList<AppCategory> Categories => _categories;

    /// <summary>Packages the catalogue names as intentionally absent, with why.</summary>
    public IReadOnlyList<CatalogueAbsence> Absent { get; private set; } = Array.Empty<CatalogueAbsence>();

    public AppCatalog(string directory) => Directory = directory;

    /// <summary>
    /// Resolve where the catalogue lives. Same order as the tweak catalogue:
    /// what ships next to the executable wins, then the bundled resources, then
    /// a walk up to the repository checkout. The walk matters in development —
    /// a fixed relative depth only matches one bin layout, and for any other
    /// the catalogue silently came back empty.
    /// </summary>
    public static string ResolveDirectory()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "apps");
        if (System.IO.Directory.Exists(portable)) return portable;

        var bundled = Path.Combine(AppContext.BaseDirectory, "resources", "apps");
        if (System.IO.Directory.Exists(bundled)) return bundled;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "apps");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return portable;
    }

    /// <summary>Load everything in the directory. Returns the entry count.</summary>
    public int Load()
    {
        _entries.Clear();
        _categories.Clear();
        Absent = Array.Empty<CatalogueAbsence>();

        if (!System.IO.Directory.Exists(Directory))
            return 0;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var metaPath = Path.Combine(Directory, MetaFile);
        var ordered = new List<AppCategory>();
        var absences = new List<CatalogueAbsence>();
        if (File.Exists(metaPath))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<CatalogueMeta>(File.ReadAllText(metaPath), options);
                if (meta?.Categories is { Count: > 0 } list) ordered = list;
                if (meta?.Absent is { Count: > 0 } gone) absences = gone;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load {metaPath}: {ex.Message}");
            }
        }

        // Every file is a category, whether or not _meta.json mentioned it —
        // dropping a file on disk must be enough to make it appear.
        var byId = new Dictionary<string, List<AppEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json")
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(MetaFile, StringComparison.OrdinalIgnoreCase)) continue;
            var categoryId = Path.GetFileNameWithoutExtension(name);

            List<AppEntry>? items;
            try
            {
                items = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(file), options);
            }
            catch (Exception ex)
            {
                // A broken file must not take the rest of the catalogue with it.
                Console.Error.WriteLine($"Failed to load apps from {file}: {ex.Message}");
                continue;
            }

            if (items is null) continue;
            byId[categoryId] = items.Where(e => !string.IsNullOrWhiteSpace(e.Id)).ToList();
        }

        var orderedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in ordered)
        {
            if (!byId.TryGetValue(category.Id, out var items)) continue;
            orderedIds.Add(category.Id);
            _categories.Add(category);
            foreach (var entry in items)
                _entries.Add(entry with { Category = category.Id });
        }

        foreach (var (id, items) in byId)
        {
            if (orderedIds.Contains(id)) continue;
            _categories.Add(new AppCategory { Id = id, Label = id, Description = null });
            foreach (var entry in items)
                _entries.Add(entry with { Category = id });
        }

        Absent = absences;

        // An ID appearing in two files would make "is it installed" depend on
        // which file won; refuse the second copy rather than pick silently.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in _entries.Where(e => !seen.Add(e.Id)).Select(e => e.Id).Distinct())
            Console.Error.WriteLine($"App catalogue: {duplicate} is listed more than once.");

        return _entries.Count;
    }

    public AppEntry? Find(string id) =>
        _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Shape of <c>apps/_meta.json</c>.</summary>
public sealed class CatalogueMeta
{
    public List<AppCategory>? Categories { get; init; }
    public List<CatalogueAbsence>? Absent { get; init; }
}

/// <summary>A package the brief asked for that has no trustworthy package source.</summary>
public sealed class CatalogueAbsence
{
    public string Name { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
