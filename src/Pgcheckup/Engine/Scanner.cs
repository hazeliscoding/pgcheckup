using Npgsql;
using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

/// <summary>How a check's run ended.</summary>
public enum CheckStatus
{
    /// <summary>It ran and found nothing.</summary>
    Passed,

    /// <summary>It ran and found at least one problem.</summary>
    Found,

    /// <summary>It didn't run here, for the reason given. A skip never fails a scan.</summary>
    Skipped,

    /// <summary>It ran and failed, for the reason given. The rest of the scan still ran.</summary>
    Errored,
}

/// <summary>One check's outcome.</summary>
/// <param name="Check">The check.</param>
/// <param name="Status">How its run ended.</param>
/// <param name="Findings">What it found. Empty unless <paramref name="Status"/> is <see cref="CheckStatus.Found"/>.</param>
/// <param name="Reason">Why it was skipped or errored, or <see langword="null"/> when it ran.</param>
public sealed record CheckResult(CheckDefinition Check, CheckStatus Status, IReadOnlyList<Finding> Findings, string? Reason = null)
{
    /// <summary>The most severe finding's severity, or <see langword="null"/> when there are no findings.</summary>
    public Severity? Worst => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);
}

/// <summary>Everything a scan found.</summary>
/// <param name="Server">The server scanned, and the role's privileges.</param>
/// <param name="Results">Each check's outcome, in the order the checks were given.</param>
public sealed record ScanReport(ServerContext Server, IReadOnlyList<CheckResult> Results);

/// <summary>Runs checks against one database.</summary>
public static class Scanner
{
    /// <summary>
    /// Reads the server context, then runs every check that applies. A check that fails is
    /// reported as errored, and the others still run.
    /// </summary>
    /// <param name="session">The guarded session to query through.</param>
    /// <param name="host">The host to name in the report.</param>
    /// <param name="checks">The checks, in the order to run and report them.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The server context and every check's outcome.</returns>
    /// <exception cref="NpgsqlException">The server context couldn't be read, so no check ran.</exception>
    /// <exception cref="OperationCanceledException">The scan was cancelled.</exception>
    public static async Task<ScanReport> ScanAsync(
        ReadOnlySession session, string host, IReadOnlyList<CheckDefinition> checks, CancellationToken cancellationToken)
    {
        var context = await ServerContext.ReadAsync(session, host, cancellationToken);
        var results = new List<CheckResult>();
        foreach (var check in checks)
        {
            if (Applicability.SkipReason(check, context) is { } reason)
            {
                results.Add(new CheckResult(check, CheckStatus.Skipped, [], reason));
                continue;
            }

            try
            {
                var findings = await CheckRunner.RunAsync(session, check, cancellationToken);
                results.Add(new CheckResult(check, findings.Count == 0 ? CheckStatus.Passed : CheckStatus.Found, findings));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                results.Add(new CheckResult(check, CheckStatus.Errored, [], Describe(error)));
            }
        }

        return new ScanReport(context, results);
    }

    private static string Describe(Exception error) => error switch
    {
        PostgresException { SqlState: PostgresErrorCodes.QueryCanceled } => "timed out after 5 s",
        PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } => "waited over 1 s for a lock",
        PostgresException postgres => $"{postgres.SqlState}: {postgres.MessageText}",
        _ => error.Message,
    };
}
