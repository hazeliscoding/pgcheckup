namespace Pgcheckup.Checks;

// Ordered so that a higher value is more severe.
public enum Severity
{
    Info,
    Warning,
    Critical,
}

public enum ThresholdKind
{
    Bytes,
    Duration,
    Integer,
    Number,
}

// Durations are held in microseconds, as the generator parsed them.
public sealed record Threshold(string Name, ThresholdKind Kind, decimal Value, string Text);

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
