using System.Text;

namespace Pgcheckup.Checks;

public enum ValueFormat
{
    Default,
    Bytes,
    Count,
}

public abstract record TemplatePart;

public sealed record TextPart(string Text) : TemplatePart;

public sealed record ValuePart(string Name, ValueFormat Format) : TemplatePart;

public sealed record SectionPart(IReadOnlyList<TemplatePart> Parts) : TemplatePart;

public sealed class TemplateException(string message) : Exception(message);

public sealed class Template(IReadOnlyList<TemplatePart> parts)
{
    public IReadOnlyList<TemplatePart> Parts { get; } = parts;

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
