using System.Globalization;

namespace Pgcheckup.Checks;

public static class ValueText
{
    private static readonly string[] ByteUnits = ["bytes", "kB", "MB", "GB", "TB", "PB"];
    private static readonly string[] CountUnits = ["million", "billion", "trillion"];

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

    // Postgres's size units (1024-based, as in pg_size_pretty), with three significant digits.
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
