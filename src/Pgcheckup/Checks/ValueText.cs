using System.Globalization;

namespace Pgcheckup.Checks;

/// <summary>Prints values the same way in every check, so numbers and durations read alike.</summary>
public static class ValueText
{
    private static readonly string[] ByteUnits = ["bytes", "kB", "MB", "GB", "TB", "PB"];
    private static readonly string[] CountUnits = ["million", "billion", "trillion"];

    /// <summary>Prints a value from a query row.</summary>
    /// <param name="value">A non-null value as Npgsql read it.</param>
    /// <param name="format">How to print it.</param>
    /// <returns>The text, without culture-specific formatting.</returns>
    /// <exception cref="TemplateException"><paramref name="format"/> needs a number and <paramref name="value"/> isn't one.</exception>
    public static string Format(object value, ValueFormat format) => format switch
    {
        ValueFormat.Bytes => Bytes(ToDecimal(value)),
        ValueFormat.Count => Count(ToDecimal(value)),
        _ => value switch
        {
            TimeSpan span => Duration(span),
            DateTime time => time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
            bool flag => flag ? "on" : "off",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        },
    };

    /// <summary>Prints a size with Postgres's units (1024-based, as in pg_size_pretty) and three significant digits.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>Such as "512 bytes", "1.5 GB" or "48 GB".</returns>
    public static string Bytes(decimal bytes)
    {
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{Significant(bytes)} bytes";
        }

        var unit = 0;
        var value = bytes;
        while (unit < ByteUnits.Length - 1 && (value >= 1024 || Round(value) >= 1024))
        {
            value /= 1024;
            unit++;
        }

        return $"{Significant(value)} {ByteUnits[unit]}";
    }

    /// <summary>Prints a count with thousands separators, or in words from a million on.</summary>
    /// <param name="count">The count.</param>
    /// <returns>Such as "48,213", "48 million" or "1.61 billion".</returns>
    public static string Count(decimal count)
    {
        if (count < 1_000_000)
        {
            return Math.Round(count).ToString("#,0", CultureInfo.InvariantCulture);
        }

        var unit = 0;
        var value = count / 1_000_000;
        while (unit < CountUnits.Length - 1 && (value >= 1000 || Round(value) >= 1000))
        {
            value /= 1000;
            unit++;
        }

        return $"{Significant(value)} {CountUnits[unit]}";
    }

    /// <summary>Prints a duration in its largest whole unit, rounded down.</summary>
    /// <param name="span">The duration. A negative one prints as zero.</param>
    /// <returns>Such as "3 days", "1 hour" or "45 seconds".</returns>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span switch
        {
            { TotalDays: >= 1 } => Plural((long)span.TotalDays, "day"),
            { TotalHours: >= 1 } => Plural((long)span.TotalHours, "hour"),
            { TotalMinutes: >= 1 } => Plural((long)span.TotalMinutes, "minute"),
            _ => Plural((long)span.TotalSeconds, "second"),
        };
    }

    private static string Plural(long n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

    private static decimal Round(decimal value) =>
        Math.Round(value, value >= 100 ? 0 : value >= 10 ? 1 : 2, MidpointRounding.AwayFromZero);

    private static string Significant(decimal value) => Round(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ToDecimal(object value) => value switch
    {
        decimal d => d,
        long l => l,
        int i => i,
        short s => s,
        double d => (decimal)d,
        float f => (decimal)f,
        _ => throw new TemplateException($"The value {value} isn't a number, so it can't be formatted as a size or count."),
    };
}
