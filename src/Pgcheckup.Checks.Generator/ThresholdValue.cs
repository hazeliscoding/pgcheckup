using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

/// <summary>What a threshold measures, which decides the parameter type it binds as.</summary>
public enum ThresholdKind
{
    /// <summary>A size in bytes, written with B, kB, MB, GB or TB. Binds as <c>bigint</c>.</summary>
    Bytes,

    /// <summary>A length of time, written with us, ms, s, min, h or d. Binds as <c>interval</c>.</summary>
    Duration,

    /// <summary>A whole number without a unit. Binds as <c>bigint</c>.</summary>
    Integer,

    /// <summary>A number with a decimal point and no unit. Binds as <c>numeric</c>.</summary>
    Number,
}

/// <summary>
/// A threshold from a check's frontmatter, such as <c>1GB</c> or <c>30min</c>, parsed at build time.
/// </summary>
/// <remarks>
/// Bytes and durations use Postgres's own units (as in postgresql.conf), so a threshold reads the
/// same as the setting it is usually compared with. Units are case-sensitive, and 1 kB is 1024 bytes.
/// </remarks>
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

    /// <summary>Creates a threshold that has already been parsed.</summary>
    /// <param name="kind">What the threshold measures.</param>
    /// <param name="value">The value in bytes, microseconds, or as written for plain numbers.</param>
    /// <param name="text">The threshold as written in check.md, for messages.</param>
    public ThresholdValue(ThresholdKind kind, decimal value, string text)
    {
        Kind = kind;
        Value = value;
        Text = text;
    }

    /// <summary>What the threshold measures.</summary>
    public ThresholdKind Kind { get; }

    /// <summary>
    /// The value in the kind's base unit: bytes for <see cref="ThresholdKind.Bytes"/>, microseconds
    /// for <see cref="ThresholdKind.Duration"/>. Always fits in a 64-bit integer.
    /// </summary>
    public decimal Value { get; }

    /// <summary>The threshold as written in check.md.</summary>
    public string Text { get; }

    /// <summary>Parses a threshold such as <c>1GB</c>, <c>30min</c>, <c>1500000000</c> or <c>0.9</c>.</summary>
    /// <param name="text">The value from check.md or a fixture's <c>-- threshold</c> line.</param>
    /// <param name="value">The parsed threshold, or <c>default</c> when parsing fails.</param>
    /// <param name="error">Why the text isn't a threshold, or an empty string on success.</param>
    /// <returns>
    /// <see langword="true"/> for a non-negative number with an optional known unit that fits in
    /// 64 bits once converted to the base unit.
    /// </returns>
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
