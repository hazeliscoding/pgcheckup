using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

/// <summary>One piece of a parsed message or fix template.</summary>
public abstract class TemplatePart
{
}

/// <summary>Literal text, copied as written.</summary>
/// <param name="text">The text, with doubled braces and brackets already reduced to one.</param>
public sealed class TextPart(string text) : TemplatePart
{
    /// <summary>The text, with doubled braces and brackets already reduced to one.</summary>
    public string Text { get; } = text;
}

/// <summary>A <c>{name}</c> or <c>{name:format}</c> placeholder for a column of the check's query.</summary>
/// <param name="name">The column name.</param>
/// <param name="format">One of <see cref="TemplateParser.Formats"/>, or <see langword="null"/> to format by the value's type.</param>
public sealed class ValuePart(string name, string? format) : TemplatePart
{
    /// <summary>The column name.</summary>
    public string Name { get; } = name;

    /// <summary>One of <see cref="TemplateParser.Formats"/>, or <see langword="null"/> to format by the value's type.</summary>
    public string? Format { get; } = format;
}

/// <summary>A <c>[ … ]</c> section, left out of the message when any value inside it is NULL.</summary>
/// <param name="parts">Its text and values. Sections don't nest.</param>
public sealed class SectionPart(IReadOnlyList<TemplatePart> parts) : TemplatePart
{
    /// <summary>Its text and values. Sections don't nest.</summary>
    public IReadOnlyList<TemplatePart> Parts { get; } = parts;
}

/// <summary>
/// Parses message and fix templates: <c>{name}</c> or <c>{name:format}</c> inserts a value,
/// <c>[ … ]</c> is left out when a value inside it is NULL, and doubled braces or brackets
/// stand for themselves.
/// </summary>
public static class TemplateParser
{
    /// <summary>The formats a placeholder may name: <c>bytes</c> prints "48 GB" and <c>count</c> prints "1.61 billion".</summary>
    public static readonly string[] Formats = ["bytes", "count"];

    private static readonly Regex Placeholder = new(@"^([a-z_][a-z0-9_]*)(?::([a-z]+))?$", RegexOptions.CultureInvariant);

    /// <summary>Parses a template from check.md.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="errors">Receives every problem found. Empty when the template is valid.</param>
    /// <returns>The parts in order. Only trust them when <paramref name="errors"/> is empty.</returns>
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
