using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

public enum EntryKind
{
    Scalar,
    List,
    Map,
}

public sealed class FrontmatterEntry(string key, int line, EntryKind kind)
{
    public string Key { get; } = key;

    public int Line { get; } = line;

    // For block scalars, the line where the content starts.
    public int ValueLine { get; set; } = line;

    public EntryKind Kind { get; } = kind;

    public string Scalar { get; set; } = "";

    public List<string> Items { get; } = [];

    public List<(string Key, string Value, int Line)> Map { get; } = [];
}

public sealed class FrontmatterDocument
{
    public List<FrontmatterEntry> Entries { get; } = [];

    public string Body { get; set; } = "";

    public List<SourceError> Errors { get; } = [];
}

// A strict subset of YAML: top-level `key: value`, `[a, b]` lists, one level of nested
// `name: value` maps, quoted scalars, and `|` or `>` block scalars. Anything else is an error
// with a line number, rather than something a full YAML parser would read differently.
public static class Frontmatter
{
    private static readonly Regex TopLevel = new(@"^([A-Za-z_][A-Za-z0-9_]*):(.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Nested = new(@"^\s+([^:\s]+):(.*)$", RegexOptions.CultureInvariant);

    public static FrontmatterDocument Parse(string markdown)
    {
        var document = new FrontmatterDocument();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        if (lines[0].TrimEnd() != "---")
        {
            document.Errors.Add(new SourceError(1, "check.md must start with frontmatter between --- lines."));
            return document;
        }

        var close = System.Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
        if (close < 0)
        {
            document.Errors.Add(new SourceError(1, "The frontmatter's closing --- line is missing."));
            return document;
        }

        document.Body = string.Join("\n", lines.Skip(close + 1)).Trim();

        var i = 1;
        while (i < close)
        {
            var line = lines[i];
            var lineNumber = i + 1;
            i++;

            if (IsBlankOrComment(line))
            {
                continue;
            }

            var match = TopLevel.Match(line);
            if (!match.Success)
            {
                document.Errors.Add(new SourceError(lineNumber, $"Expected `key: value`, not `{line.Trim()}`."));
                continue;
            }

            var key = match.Groups[1].Value;
            var rest = match.Groups[2].Value.Trim();
            if (document.Entries.Any(e => e.Key == key))
            {
                document.Errors.Add(new SourceError(lineNumber, $"{key} appears more than once."));
            }

            FrontmatterEntry entry;
            if (rest.Length == 0)
            {
                entry = new FrontmatterEntry(key, lineNumber, EntryKind.Map);
                while (i < close && (IsBlankOrComment(lines[i]) || char.IsWhiteSpace(lines[i][0])))
                {
                    if (!IsBlankOrComment(lines[i]))
                    {
                        var nested = Nested.Match(lines[i]);
                        if (nested.Success)
                        {
                            entry.Map.Add((nested.Groups[1].Value, Unquote(nested.Groups[2].Value.Trim()), i + 1));
                        }
                        else
                        {
                            document.Errors.Add(new SourceError(i + 1, $"Expected `name: value` under {key}."));
                        }
                    }

                    i++;
                }
            }
            else if (rest is "|" or "|-" or "|+" or ">" or ">-" or ">+")
            {
                entry = new FrontmatterEntry(key, lineNumber, EntryKind.Scalar) { ValueLine = lineNumber + 1 };
                var block = new List<string>();
                while (i < close && (lines[i].Trim().Length == 0 || char.IsWhiteSpace(lines[i][0])))
                {
                    block.Add(lines[i]);
                    i++;
                }

                entry.Scalar = BlockScalar(block, folded: rest[0] == '>');
            }
            else if (rest.StartsWith("["))
            {
                entry = new FrontmatterEntry(key, lineNumber, EntryKind.List);
                if (!rest.EndsWith("]"))
                {
                    document.Errors.Add(new SourceError(lineNumber, $"The list for {key} must end with ] on the same line."));
                }
                else
                {
                    var inner = rest.Substring(1, rest.Length - 2);
                    entry.Items.AddRange(inner.Split(',').Select(s => Unquote(s.Trim())).Where(s => s.Length > 0));
                }
            }
            else
            {
                entry = new FrontmatterEntry(key, lineNumber, EntryKind.Scalar) { Scalar = Unquote(rest) };
            }

            document.Entries.Add(entry);
        }

        return document;
    }

    private static bool IsBlankOrComment(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed[0] == '#';
    }

    private static string BlockScalar(List<string> lines, bool folded)
    {
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        var content = lines.Select(l => l.Length >= indent ? l.Substring(indent).TrimEnd() : "").ToList();
        if (!folded)
        {
            return string.Join("\n", content).Trim('\n');
        }

        var text = new StringBuilder();
        foreach (var line in content)
        {
            if (line.Length == 0)
            {
                text.Append('\n');
            }
            else
            {
                if (text.Length > 0 && text[text.Length - 1] != '\n')
                {
                    text.Append(' ');
                }

                text.Append(line);
            }
        }

        return text.ToString().Trim('\n');
    }

    private static string Unquote(string value)
    {
        if (value.Length > 0 && (value[0] == '"' || value[0] == '\''))
        {
            var quote = value[0];
            var end = value.LastIndexOf(quote);
            var after = value.Substring(end + 1).Trim();
            if (end > 0 && (after.Length == 0 || after[0] == '#'))
            {
                var inner = value.Substring(1, end - 1);
                return quote == '"'
                    ? inner.Replace("\\\"", "\"").Replace("\\\\", "\\")
                    : inner.Replace("''", "'");
            }
        }

        var comment = value.IndexOf(" #", System.StringComparison.Ordinal);
        return comment >= 0 ? value.Substring(0, comment).TrimEnd() : value;
    }
}
