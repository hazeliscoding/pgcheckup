using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

public abstract class TemplatePart
{
}

public sealed class TextPart(string text) : TemplatePart
{
    public string Text { get; } = text;
}

public sealed class ValuePart(string name, string? format) : TemplatePart
{
    public string Name { get; } = name;

    public string? Format { get; } = format;
}

public sealed class SectionPart(IReadOnlyList<TemplatePart> parts) : TemplatePart
{
    public IReadOnlyList<TemplatePart> Parts { get; } = parts;
}

// Message and fix templates: {name} or {name:format} inserts a value, [ … ] is left out when a
// value inside it is NULL, and doubled braces or brackets stand for themselves.
public static class TemplateParser
{
    public static readonly string[] Formats = ["bytes", "count"];

    private static readonly Regex Placeholder = new(@"^([a-z_][a-z0-9_]*)(?::([a-z]+))?$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<TemplatePart> Parse(string template, out List<string> errors)
    {
        errors = [];
        if (template.Trim().Length == 0)
        {
            errors.Add("The template is empty.");
            return [];
        }

        var top = new List<TemplatePart>();
        List<TemplatePart>? section = null;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length > 0)
            {
                (section ?? top).Add(new TextPart(text.ToString()));
                text.Clear();
            }
        }

        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            var next = i + 1 < template.Length ? template[i + 1] : '\0';

            if ((c == '{' || c == '}' || c == '[' || c == ']') && next == c)
            {
                text.Append(c);
                i += 2;
                continue;
            }

            switch (c)
            {
                case '{':
                    var close = template.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        errors.Add($"A {{ at position {i + 1} is never closed.");
                        return top;
                    }

                    var inner = template.Substring(i + 1, close - i - 1);
                    var match = Placeholder.Match(inner);
                    if (!match.Success)
                    {
                        errors.Add($"{{{inner}}} isn't a value. Write {{name}} or {{name:format}} with a lowercase column name.");
                    }
                    else if (match.Groups[2].Success && !Formats.Contains(match.Groups[2].Value))
                    {
                        errors.Add($"{{{inner}}} uses the unknown format {match.Groups[2].Value}. Use {string.Join(" or ", Formats)}.");
                    }
                    else
                    {
                        FlushText();
                        (section ?? top).Add(new ValuePart(match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null));
                    }

                    i = close + 1;
                    break;

                case '}':
                    errors.Add($"A }} at position {i + 1} has no matching {{. Write }}}} for a literal brace.");
                    i++;
                    break;

                case '[':
                    if (section != null)
                    {
                        errors.Add($"A [ at position {i + 1} is inside another [ … ] section.");
                        return top;
                    }

                    FlushText();
                    section = [];
                    i++;
                    break;

                case ']':
                    if (section == null)
                    {
                        errors.Add($"A ] at position {i + 1} has no matching [. Write ]] for a literal bracket.");
                        i++;
                        break;
                    }

                    FlushText();
                    if (!section.OfType<ValuePart>().Any())
                    {
                        errors.Add("A [ … ] section has no value in it, so it would never be left out.");
                    }

                    top.Add(new SectionPart(section));
                    section = null;
                    i++;
                    break;

                default:
                    text.Append(c);
                    i++;
                    break;
            }
        }

        if (section != null)
        {
            errors.Add("A [ … ] section is never closed.");
            return top;
        }

        FlushText();
        return top;
    }
}
