using WinOpt.Core.Models;

namespace WinOpt.Engine.Installer;

/// <summary>
/// Winget has no <c>--json</c> — it prints a fixed-width table whose column
/// widths are re-measured for every invocation, so <c>winget list</c>,
/// <c>winget list --id X --exact</c> and <c>winget search</c> all have
/// different offsets. The column starts are read off the header line of the
/// output being parsed rather than hardcoded, which is the only thing that
/// works across all of them.
///
/// Two other facts this has to survive: winget can write a line like
/// "Failed when searching source; results will not be included: msstore" to
/// <b>stdout</b> before the header when a source is unhealthy, and every line
/// ends in CRLF. The noise line is skipped by looking for the header rather
/// than assuming it is first, and the CR is stripped per line.
/// </summary>
public static class WingetTable
{
    private const string HeaderWord = "Name";

    /// <summary>Parse winget's stdout into rows. Never throws on junk input —
    /// unparsable lines are dropped, because a malformed row that looks like a
    /// package would be worse than a missing one.</summary>
    public static List<InstalledPackage> Parse(string stdout)
    {
        var rows = new List<InstalledPackage>();
        var lines = Split(stdout);

        var headerIndex = FindHeaderLine(lines);
        if (headerIndex < 0) return rows;

        var header = lines[headerIndex];
        var columns = Columns(header);
        var id = IndexOf(columns, "Id");
        // The Id column is the identity of a row: without it the rest is
        // prose, so anything that cannot supply one is not a package row.
        if (id < 0) return rows;

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || IsSeparator(line)) continue;
            // If the line is too short to have an Id column it is a trailing
            // remark ("No installed package found…"), not a row.
            if (line.Length <= columns[id].Start) continue;

            var values = ReadRow(header, line);
            var packageId = Get(values, "Id");
            if (packageId.Length == 0) continue;

            rows.Add(new InstalledPackage
            {
                Name = Get(values, "Name").Length == 0 ? packageId : Get(values, "Name"),
                Id = packageId,
                Version = Get(values, "Version"),
                Available = NullIfBlank(Get(values, "Available")),
                Source = NullIfBlank(Get(values, "Source")),
            });
        }

        return rows;
    }

    /// <summary>The row with this exact ID, if there is one. Winget's
    /// <c>list --id</c> is not reliable on its own — it also exits non-zero
    /// with an empty table — so the filter is applied here too.</summary>
    public static InstalledPackage? FindById(IEnumerable<InstalledPackage> rows, string id) =>
        rows.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>One row keyed by column title. Used by callers whose table is
    /// not a package list (winget's <c>source list</c>, for instance).</summary>
    public static Dictionary<string, string> ReadRow(string header, string row)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var columns = Columns(header);
        for (var i = 0; i < columns.Count; i++)
        {
            var start = columns[i].Start;
            if (start >= row.Length) continue;
            var end = i + 1 < columns.Count ? Math.Min(columns[i + 1].Start, row.Length) : row.Length;
            if (end <= start) continue;
            var value = row[start..end].Trim();
            if (value.Length > 0) values[columns[i].Title] = value;
        }
        return values;
    }

    /// <summary>The header line of the table in this output, or -1.</summary>
    public static int FindHeaderLine(IEnumerable<string> lines)
    {
        var array = lines as string[] ?? lines.ToArray();
        for (var i = 0; i < array.Length; i++)
        {
            var line = array[i];
            if (!line.StartsWith(HeaderWord, StringComparison.Ordinal)) continue;
            // The header is always followed by a rule of dashes. Requiring it
            // keeps a package literally named "Name" from being mistaken for one.
            // The Id column is deliberately not required: winget's
            // `source list` has no Id column, and Parse() checks for Id itself.
            if (i + 1 < array.Length && IsSeparator(array[i + 1])) return i;
        }
        return -1;
    }

    public static string[] Split(string output) =>
        output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static bool IsSeparator(string line) =>
        line.Length > 0 && line.Trim().Length > 0 && line.Trim().All(c => c == '-');

    private readonly record struct Column(string Title, int Start);

    private static List<Column> Columns(string header)
    {
        var columns = new List<Column>();
        for (var i = 0; i < header.Length;)
        {
            if (char.IsWhiteSpace(header[i])) { i++; continue; }
            var start = i;
            while (i < header.Length && !char.IsWhiteSpace(header[i])) i++;
            columns.Add(new Column(header[start..i], start));
        }
        return columns;
    }

    private static int IndexOf(List<Column> columns, string title) =>
        columns.FindIndex(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));

    private static string Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
