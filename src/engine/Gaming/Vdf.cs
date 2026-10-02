using System.Text;

namespace WinOpt.Engine.Gaming;

/// <summary>
/// A node in a Valve KeyValues (VDF) document — the format Steam writes both its
/// library list and its per-game manifests in.
///
/// Entries keep their document order because the format allows the same key to
/// appear more than once and consumers enumerate children positionally; a
/// dictionary would quietly collapse the repeats.
/// </summary>
public sealed class VdfNode
{
    private readonly List<(string Key, string? Scalar, VdfNode? Child)> _entries = new();

    public IReadOnlyList<(string Key, string? Scalar, VdfNode? Child)> Entries => _entries;

    internal void AddScalar(string key, string value) => _entries.Add((key, value, null));
    internal void AddChild(string key, VdfNode child) => _entries.Add((key, null, child));

    /// <summary>Last scalar bound to <paramref name="key"/>, or null when there is none.</summary>
    public string? Get(string key)
    {
        string? found = null;
        foreach (var e in _entries)
            if (e.Scalar != null && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                found = e.Scalar;
        return found;
    }

    /// <summary>Every child bound to <paramref name="key"/>, in document order.</summary>
    public IEnumerable<VdfNode> Children(string key)
    {
        foreach (var e in _entries)
            if (e.Child != null && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                yield return e.Child;
    }

    /// <summary>Every child node whatever its key — how a numerically-keyed list is enumerated.</summary>
    public IEnumerable<VdfNode> AllChildren()
    {
        foreach (var e in _entries)
            if (e.Child != null)
                yield return e.Child;
    }
}

/// <summary>
/// Reader for the quoted, brace-delimited KeyValues syntax.
///
/// Deliberately a parser and not a regular expression: the values here contain
/// escaped backslashes (<c>C:\\Program Files</c>), embedded quotes, <c>//</c>
/// comments and arbitrary nesting, and a pattern matches all of those only in
/// the documents that do not matter.
/// </summary>
public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var i = 0;
        return ParseBlock(text, ref i, isRoot: true);
    }

    private static VdfNode ParseBlock(string text, ref int i, bool isRoot)
    {
        var node = new VdfNode();

        while (true)
        {
            SkipTrivia(text, ref i);
            if (i >= text.Length) break;
            if (text[i] == '}')
            {
                if (!isRoot) i++; // consume the brace that closes this block
                break;
            }
            if (text[i] != '"')
            {
                i++; // stray token between entries — skip rather than guess
                continue;
            }

            var key = ReadQuoted(text, ref i);
            SkipTrivia(text, ref i);
            if (i >= text.Length) break;

            if (text[i] == '{')
            {
                i++;
                node.AddChild(key, ParseBlock(text, ref i, isRoot: false));
            }
            else if (text[i] == '"')
            {
                node.AddScalar(key, ReadQuoted(text, ref i));
            }
            else
            {
                // An unquoted value. Some writers emit them; read to the next
                // delimiter instead of refusing the whole document.
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}')
                    i++;
                node.AddScalar(key, text[start..i]);
            }
        }

        return node;
    }

    private static void SkipTrivia(string text, ref int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            // Steam writes // comments; without handling them a commented-out
            // path is read as a key and its comment as an unquoted value.
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            break;
        }
    }

    private static string ReadQuoted(string text, ref int i)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '\\' || next == '"')
                {
                    sb.Append(next);
                    i += 2;
                    continue;
                }
            }
            if (c == '"') { i++; return sb.ToString(); }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
