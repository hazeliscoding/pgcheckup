using System.Text;

namespace Pgcheckup.Checks;

/// <summary>How a template prints a value.</summary>
public enum ValueFormat
{
    /// <summary>By the value's type: intervals as "3 days", timestamps in UTC, everything else plainly.</summary>
    Default,

    /// <summary>A size in bytes, such as "48 GB". Written <c>{name:bytes}</c>.</summary>
    Bytes,

    /// <summary>A count, with words past a million, such as "1.61 billion". Written <c>{name:count}</c>.</summary>
    Count,
}

/// <summary>One piece of a compiled message or fix template.</summary>
public abstract record TemplatePart;

/// <summary>Literal text.</summary>
/// <param name="Text">The text, printed as it is.</param>
public sealed record TextPart(string Text) : TemplatePart;

/// <summary>A value from a column of the check's query.</summary>
/// <param name="Name">The column name.</param>
/// <param name="Format">How to print the value.</param>
public sealed record ValuePart(string Name, ValueFormat Format) : TemplatePart;

/// <summary>A section that is left out when any value inside it is NULL.</summary>
/// <param name="Parts">Its text and values. Sections don't nest.</param>
public sealed record SectionPart(IReadOnlyList<TemplatePart> Parts) : TemplatePart;

/// <summary>A template and a query row that don't fit: a missing column, or NULL outside a section.</summary>
/// <param name="message">Which value is at fault.</param>
public sealed class TemplateException(string message) : Exception(message);

/// <summary>A message or fix template, compiled from check.md at build time.</summary>
/// <param name="parts">The template's parts in order.</param>
public sealed class Template(IReadOnlyList<TemplatePart> parts)
{
    /// <summary>The template's parts in order.</summary>
    public IReadOnlyList<TemplatePart> Parts { get; } = parts;

    /// <summary>Renders the template with one row of the check's query.</summary>
    /// <param name="values">The row, by column name. SQL NULL is <see langword="null"/>.</param>
    /// <returns>The text, with each section left out when a value in it is NULL.</returns>
    /// <exception cref="TemplateException">
    /// The row has no column for a value, or a value outside a section is NULL.
    /// </exception>
    public string Render(IReadOnlyDictionary<string, object?> values)
    {
        var text = new StringBuilder();
        foreach (var part in Parts)
        {
            switch (part)
            {
                case TextPart t:
                    text.Append(t.Text);
                    break;

                case ValuePart v:
                    text.Append(Format(v, values) ?? throw new TemplateException($"The query returned NULL for {v.Name}, which the template needs."));
                    break;

                case SectionPart s:
                    var section = new StringBuilder();
                    var complete = true;
                    foreach (var inner in s.Parts)
                    {
                        var rendered = inner switch
                        {
                            TextPart t => t.Text,
                            ValuePart v => Format(v, values),
                            _ => throw new TemplateException("A template section can't contain another section."),
                        };

                        if (rendered == null)
                        {
                            complete = false;
                            break;
                        }

                        section.Append(rendered);
                    }

                    if (complete)
                    {
                        text.Append(section);
                    }

                    break;
            }
        }

        return text.ToString();
    }

    private static string? Format(ValuePart part, IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue(part.Name, out var value))
        {
            throw new TemplateException($"The template uses {part.Name}, but the query returned no column with that name.");
        }

        return value == null ? null : ValueText.Format(value, part.Format);
    }
}
