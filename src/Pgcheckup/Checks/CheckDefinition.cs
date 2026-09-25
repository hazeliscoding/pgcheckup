namespace Pgcheckup.Checks;

/// <summary>How urgent a finding is. A higher value is more severe, so severities compare directly.</summary>
public enum Severity
{
    /// <summary>Housekeeping.</summary>
    Info,

    /// <summary>Heading toward an outage, or a safety net is gone.</summary>
    Warning,

    /// <summary>Can take the database down or lose data soon.</summary>
    Critical,
}

/// <summary>What a threshold measures, which decides the parameter type it binds as.</summary>
public enum ThresholdKind
{
    /// <summary>A size in bytes. Binds as <c>bigint</c>.</summary>
    Bytes,

    /// <summary>A length of time, held in microseconds. Binds as <c>interval</c>.</summary>
    Duration,

    /// <summary>A whole number. Binds as <c>bigint</c>.</summary>
    Integer,

    /// <summary>A decimal number. Binds as <c>numeric</c>.</summary>
    Number,
}

/// <summary>A threshold that a check's query reads as a parameter.</summary>
/// <param name="Name">The name, as check.sql reads it with <c>@name</c>.</param>
/// <param name="Kind">What it measures.</param>
/// <param name="Value">The value in bytes, microseconds, or as written for plain numbers.</param>
/// <param name="Text">The value as written in check.md, such as <c>1GB</c>.</param>
public sealed record Threshold(string Name, ThresholdKind Kind, decimal Value, string Text);

/// <summary>A check, compiled from its <c>checks/&lt;id&gt;/</c> folder into the binary at build time.</summary>
/// <param name="Id">The stable kebab-case id. Baselines and ignore lists depend on it.</param>
/// <param name="Title">A short title for <c>pgcheckup list</c>.</param>
/// <param name="Category">The group the check belongs to: ids, cleanup, wal or capacity.</param>
/// <param name="Severity">The severity of a finding unless its row says otherwise.</param>
/// <param name="MinVersion">The oldest Postgres major version the check runs on.</param>
/// <param name="Privileges">The predefined roles the check needs. Empty when any role can run it.</param>
/// <param name="SkipOn">The managed providers where the check is skipped.</param>
/// <param name="Thresholds">The thresholds in parameter order, so index 0 binds to <c>$1</c>.</param>
/// <param name="Sql">One read-only statement that returns a row per finding.</param>
/// <param name="Message">What is wrong, rendered from each row.</param>
/// <param name="Fix">What to do about it, rendered from each row. pgcheckup prints it and never runs it.</param>
/// <param name="Note">The Markdown body of check.md.</param>
public sealed record CheckDefinition(
    string Id,
    string Title,
    string Category,
    Severity Severity,
    int MinVersion,
    IReadOnlyList<string> Privileges,
    IReadOnlyList<string> SkipOn,
    IReadOnlyList<Threshold> Thresholds,
    string Sql,
    Template Message,
    Template Fix,
    string Note);
