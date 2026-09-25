using Pgcheckup.Checks;

namespace Pgcheckup.Tests.Checks;

public class TemplateRenderTests
{
    private static readonly Template SlotMessage = new(
    [
        new TextPart("Slot "),
        new ValuePart("subject", ValueFormat.Default),
        new TextPart(" has been inactive"),
        new SectionPart([new TextPart(" for "), new ValuePart("inactive_for", ValueFormat.Default)]),
        new TextPart(" and is holding "),
        new ValuePart("retained_wal", ValueFormat.Bytes),
        new TextPart(" of WAL."),
    ]);

    private static string Render(Template template, params (string Name, object? Value)[] values) =>
        template.Render(values.ToDictionary(v => v.Name, v => v.Value));

    private static string RenderOne(object? value, ValueFormat format = ValueFormat.Default) =>
        Render(new Template([new ValuePart("v", format)]), ("v", value));

    [Fact]
    public void Renders_values_into_the_text()
    {
        Assert.Equal(
            "Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.",
            Render(SlotMessage, ("subject", "debezium"), ("inactive_for", new TimeSpan(3, 4, 12, 33)), ("retained_wal", 51_539_607_552L)));
    }

    [Fact]
    public void Leaves_out_a_section_whose_value_is_null()
    {
        Assert.Equal(
            "Slot debezium has been inactive and is holding 48 GB of WAL.",
            Render(SlotMessage, ("subject", "debezium"), ("inactive_for", null), ("retained_wal", 51_539_607_552L)));
    }

    [Fact]
    public void Refuses_a_null_value_outside_a_section()
    {
        var error = Assert.Throws<TemplateException>(() =>
            Render(SlotMessage, ("subject", null), ("inactive_for", null), ("retained_wal", 1L)));

        Assert.Contains("subject", error.Message);
    }

    [Fact]
    public void Refuses_a_value_the_query_did_not_return()
    {
        var error = Assert.Throws<TemplateException>(() => Render(SlotMessage, ("subject", "debezium")));

        Assert.Contains("inactive_for", error.Message);
    }

    [Theory]
    [InlineData(3, 4, 0, 0, "3 days")]
    [InlineData(1, 23, 59, 0, "1 day")]
    [InlineData(0, 4, 30, 0, "4 hours")]
    [InlineData(0, 1, 0, 0, "1 hour")]
    [InlineData(0, 0, 12, 59, "12 minutes")]
    [InlineData(0, 0, 1, 0, "1 minute")]
    [InlineData(0, 0, 0, 45, "45 seconds")]
    [InlineData(0, 0, 0, 1, "1 second")]
    [InlineData(0, 0, 0, 0, "0 seconds")]
    public void Prints_intervals_in_their_largest_whole_unit(int days, int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, RenderOne(new TimeSpan(days, hours, minutes, seconds)));
    }

    [Theory]
    [InlineData(4127L, "4127")]
    [InlineData("orders", "orders")]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public void Prints_other_values_plainly(object value, string expected)
    {
        Assert.Equal(expected, RenderOne(value));
    }

    [Fact]
    public void Prints_decimals_and_timestamps_without_culture()
    {
        Assert.Equal("0.95", RenderOne(0.95m));
        Assert.Equal("2026-09-25 14:03 UTC", RenderOne(new DateTime(2026, 9, 25, 14, 3, 59, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(512L, "512 bytes")]
    [InlineData(1536L, "1.5 kB")]
    [InlineData(1_610_612_736L, "1.5 GB")]
    [InlineData(51_539_607_552L, "48 GB")]
    [InlineData(1_073_741_823L, "1 GB")]
    [InlineData(1_825_361_101L, "1.7 GB")]
    [InlineData(13_421_772_800L, "12.5 GB")]
    [InlineData(1_125_899_906_842_624L, "1 PB")]
    public void Prints_bytes_like_postgres_sizes(long bytes, string expected)
    {
        Assert.Equal(expected, RenderOne(bytes, ValueFormat.Bytes));
    }

    [Fact]
    public void Prints_numeric_bytes()
    {
        Assert.Equal("48 GB", RenderOne(51_539_607_552m, ValueFormat.Bytes));
    }

    [Theory]
    [InlineData(48_213L, "48,213")]
    [InlineData(999_999L, "999,999")]
    [InlineData(1_000_000L, "1 million")]
    [InlineData(48_000_000L, "48 million")]
    [InlineData(123_456_789L, "123 million")]
    [InlineData(1_610_000_000L, "1.61 billion")]
    [InlineData(2_147_483_647L, "2.15 billion")]
    [InlineData(999_999_999L, "1 billion")]
    [InlineData(4_000_000_000_000L, "4 trillion")]
    public void Prints_large_counts_in_words(long count, string expected)
    {
        Assert.Equal(expected, RenderOne(count, ValueFormat.Count));
    }
}
