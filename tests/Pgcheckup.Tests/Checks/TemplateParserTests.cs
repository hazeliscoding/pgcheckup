using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

public class TemplateParserTests
{
    private static string Parse(string template)
    {
        var parts = TemplateParser.Parse(template, out var errors);
        Assert.Empty(errors);
        return TemplateText.Describe(parts);
    }

    private static IReadOnlyList<string> Errors(string template)
    {
        TemplateParser.Parse(template, out var errors);
        return errors;
    }

    [Fact]
    public void Splits_text_values_and_optional_sections()
    {
        Assert.Equal(
            "'Slot '<subject>' has been inactive'[' for '<inactive_for>]' and holds '<retained_wal:bytes>'.'",
            Parse("Slot {subject} has been inactive[ for {inactive_for}] and holds {retained_wal:bytes}."));
    }

    [Fact]
    public void Doubled_braces_and_brackets_are_literal()
    {
        Assert.Equal("'SELECT ARRAY[1] {x}'", Parse("SELECT ARRAY[[1]] {{x}}"));
    }

    [Fact]
    public void Accepts_the_count_format()
    {
        Assert.Equal("<age:count>' IDs'", Parse("{age:count} IDs"));
    }

    [Theory]
    [InlineData("Slot {subject", "never closed")]
    [InlineData("Slot {Subject}", "Subject")]
    [InlineData("Slot {}", "{}")]
    [InlineData("{size:megabytes}", "megabytes")]
    [InlineData("a } b", "}")]
    [InlineData("[ for {x}", "never closed")]
    [InlineData("a ] b", "]")]
    [InlineData("[a [b {x}]]", "inside")]
    [InlineData("a[ no values ]", "no value")]
    [InlineData("", "empty")]
    public void Rejects_malformed_templates(string template, string expected)
    {
        Assert.Contains(Errors(template), e => e.Contains(expected));
    }
}
