using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

public sealed record Finding(
    string CheckId,
    string Subject,
    Severity Severity,
    string Message,
    string Fix,
    IReadOnlyDictionary<string, object?> Values);

// A check whose query or template doesn't hold up its side of the contract.
public sealed class CheckException(string message, Exception? inner = null) : Exception(message, inner);

public static class CheckRunner
{
    public static async Task<IReadOnlyList<Finding>> RunAsync(ReadOnlySession session, CheckDefinition check, CancellationToken cancellationToken)
    {
        var parameters = check.Thresholds.Select(ParameterValue).ToList();
        var rows = await session.QueryAsync(check.Sql, parameters, cancellationToken);
        return rows.Select(row => ToFinding(check, row)).ToList();
    }

    private static object ParameterValue(Threshold threshold) => threshold.Kind switch
    {
        ThresholdKind.Bytes or ThresholdKind.Integer => (long)threshold.Value,
        ThresholdKind.Duration => TimeSpan.FromMicroseconds((long)threshold.Value),
        _ => threshold.Value,
    };

    private static Finding ToFinding(CheckDefinition check, IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue("subject", out var subject) || subject is not string subjectText)
        {
            throw new CheckException($"{check.Id} returned a row without a subject.");
        }

        var severity = check.Severity;
        if (row.TryGetValue("severity", out var value) && value != null)
        {
            severity = value switch
            {
                "critical" => Severity.Critical,
                "warning" => Severity.Warning,
                "info" => Severity.Info,
                _ => throw new CheckException($"{check.Id} returned the severity {value}. Use critical, warning or info."),
            };
        }

        try
        {
            return new Finding(check.Id, subjectText, severity, check.Message.Render(row), check.Fix.Render(row), row);
        }
        catch (TemplateException error)
        {
            throw new CheckException($"{check.Id}: {error.Message}", error);
        }
    }
}
