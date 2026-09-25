using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

public enum ThresholdKind
{
    Bytes,
    Duration,
    Integer,
    Number,
}

// Bytes and durations use Postgres's own units (as in postgresql.conf), so a threshold reads
// the same as the setting it is usually compared with. Durations are held in microseconds.
public readonly struct ThresholdValue
{
    private static readonly Regex Pattern = new(@"^(\d+(?:\.\d+)?)\s*([A-Za-z]*)$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, (ThresholdKind Kind, decimal Factor)> Units = new(StringComparer.Ordinal)
    {
        ["B"] = (ThresholdKind.Bytes, 1m),
        ["kB"] = (ThresholdKind.Bytes, 1024m),
        ["MB"] = (ThresholdKind.Bytes, 1024m * 1024),
        ["GB"] = (ThresholdKind.Bytes, 1024m * 1024 * 1024),
        ["TB"] = (ThresholdKind.Bytes, 1024m * 1024 * 1024 * 1024),
        ["us"] = (ThresholdKind.Duration, 1m),
        ["ms"] = (ThresholdKind.Duration, 1_000m),
        ["s"] = (ThresholdKind.Duration, 1_000_000m),
        ["min"] = (ThresholdKind.Duration, 60_000_000m),
        ["h"] = (ThresholdKind.Duration, 3_600_000_000m),
        ["d"] = (ThresholdKind.Duration, 86_400_000_000m),
    };

    public ThresholdValue(ThresholdKind kind, decimal value, string text)
    {
        Kind = kind;
        Value = value;
        Text = text;
    }

    public ThresholdKind Kind { get; }

    public decimal Value { get; }

    public string Text { get; }

    public static bool TryParse(string text, out ThresholdValue value, out string error)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The threshold is empty.";
            return false;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            error = $"'{text}' isn't a number with an optional unit (B, kB, MB, GB, TB, us, ms, s, min, h, d).";
            return false;
        }

        // Checked before any unit is applied, so the multiplication below can't overflow decimal.
        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || number > long.MaxValue)
        {
            error = $"'{text}' is too large for a threshold.";
            return false;
        }

        var unit = match.Groups[2].Value;
        if (unit.Length == 0)
        {
            var kind = match.Groups[1].Value.Contains(".") ? ThresholdKind.Number : ThresholdKind.Integer;
            return InRange(new ThresholdValue(kind, number, text), out value, out error);
        }

        if (!Units.TryGetValue(unit, out var known))
        {
            error = $"'{text}' has an unknown unit. Use B, kB, MB, GB, TB, us, ms, s, min, h or d.";
            return false;
        }

        return InRange(new ThresholdValue(known.Kind, Math.Round(number * known.Factor, MidpointRounding.AwayFromZero), text), out value, out error);
    }

    // Thresholds bind as bigint or interval, so they must fit in 64 bits.
    private static bool InRange(ThresholdValue candidate, out ThresholdValue value, out string error)
    {
        if (candidate.Value > long.MaxValue)
        {
            value = default;
            error = $"'{candidate.Text}' is too large for a threshold.";
            return false;
        }

        value = candidate;
        error = "";
        return true;
    }
}
