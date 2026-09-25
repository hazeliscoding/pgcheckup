using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

public class ThresholdValueTests
{
    [Theory]
    [InlineData("0B", 0)]
    [InlineData("1GB", 1073741824)]
    [InlineData("512 MB", 536870912)]
    [InlineData("1.5kB", 1536)]
    [InlineData("2TB", 2199023255552)]
    public void Parses_bytes_with_postgres_units(string text, long bytes)
    {
        Assert.True(ThresholdValue.TryParse(text, out var value, out _));
        Assert.Equal(ThresholdKind.Bytes, value.Kind);
        Assert.Equal(bytes, value.Value);
    }

    [Theory]
    [InlineData("0s", 0)]
    [InlineData("250ms", 250_000)]
    [InlineData("5s", 5_000_000)]
    [InlineData("30min", 1_800_000_000)]
    [InlineData("1h", 3_600_000_000)]
    [InlineData("2 d", 172_800_000_000)]
    [InlineData("100us", 100)]
    public void Parses_durations_to_microseconds(string text, long microseconds)
    {
        Assert.True(ThresholdValue.TryParse(text, out var value, out _));
        Assert.Equal(ThresholdKind.Duration, value.Kind);
        Assert.Equal(microseconds, value.Value);
    }

    [Fact]
    public void Parses_a_plain_integer()
    {
        Assert.True(ThresholdValue.TryParse("1500000000", out var value, out _));
        Assert.Equal(ThresholdKind.Integer, value.Kind);
        Assert.Equal(1_500_000_000m, value.Value);
    }

    [Fact]
    public void Parses_a_decimal_number()
    {
        Assert.True(ThresholdValue.TryParse("0.9", out var value, out _));
        Assert.Equal(ThresholdKind.Number, value.Kind);
        Assert.Equal(0.9m, value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1gb")]
    [InlineData("1 GiB")]
    [InlineData("-1h")]
    [InlineData("1e9")]
    [InlineData("90%")]
    [InlineData("h")]
    [InlineData("1.5.1s")]
    public void Rejects_values_postgres_would_not_accept(string text)
    {
        Assert.False(ThresholdValue.TryParse(text, out _, out var error));
        Assert.Contains(text.Length == 0 ? "empty" : text, error);
    }
}
