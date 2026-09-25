using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

// Writes template parts back in a compact form so expectations can be written by hand.
internal static class TemplateText
{
    public static string Describe(IEnumerable<TemplatePart> parts) => string.Concat(parts.Select(p => p switch
    {
        TextPart t => $"'{t.Text}'",
        ValuePart { Format: null } v => $"<{v.Name}>",
        ValuePart v => $"<{v.Name}:{v.Format}>",
        SectionPart s => $"[{Describe(s.Parts)}]",
        _ => throw new InvalidOperationException(),
    }));
}
