using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

/// <summary>One problem a check found, ready to print.</summary>
/// <param name="CheckId">The id of the check that found it.</param>
/// <param name="Subject">The object it is about, such as a slot or table name.</param>
/// <param name="Severity">How urgent it is.</param>
/// <param name="Message">What is wrong and why it matters.</param>
/// <param name="Fix">What to do about it. pgcheckup prints it and never runs it.</param>
/// <param name="Values">The row the check returned, by column name, for machine-readable output.</param>
public sealed record Finding(
    string CheckId,
    string Subject,
    Severity Severity,
    string Message,
    string Fix,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>A check whose query or template doesn't hold up its side of the contract.</summary>
/// <param name="message">What the check did wrong, naming the check.</param>
/// <param name="inner">The error that exposed it, if any.</param>
public sealed class CheckException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Runs one check and turns its rows into findings.</summary>
public static class CheckRunner
{
    /// <summary>Runs a check's query with its thresholds and renders a finding per row.</summary>
    /// <param name="session">The guarded session to query through.</param>
    /// <param name="check">The check to run.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A finding per row, in the query's order. Empty when the check passes.</returns>
    /// <exception cref="CheckException">A row has no subject, an unknown severity, or doesn't fit a template.</exception>
    /// <exception cref="Npgsql.NpgsqlException">The query failed, timed out or couldn't get a lock.</exception>
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
