using System.Globalization;
using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

/// <summary>What the report's header says about the server.</summary>
/// <param name="Database">The database scanned.</param>
/// <param name="Host">The host as given, never the full connection string.</param>
/// <param name="Version">The Postgres version, such as "17.6".</param>
public sealed record ServerInfo(string Database, string Host, string Version);

/// <summary>One check's findings.</summary>
/// <param name="Check">The check that ran.</param>
/// <param name="Findings">What it found. Empty when it passed.</param>
public sealed record CheckResult(CheckDefinition Check, IReadOnlyList<Finding> Findings)
{
    /// <summary>The most severe finding's severity, or <see langword="null"/> when the check passed.</summary>
    public Severity? Worst => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);
}

/// <summary>Everything a scan found.</summary>
/// <param name="Server">The server scanned.</param>
/// <param name="Results">Each check's findings, in the order the checks ran.</param>
public sealed record ScanReport(ServerInfo Server, IReadOnlyList<CheckResult> Results);

/// <summary>A check that couldn't run. In M0 this ends the scan; M1 reports it as errored and goes on.</summary>
/// <param name="checkId">The check that failed.</param>
/// <param name="inner">What went wrong.</param>
public sealed class CheckFailedException(string checkId, Exception inner)
    : Exception($"{checkId} couldn't run: {inner.Message}", inner)
{
    /// <summary>The check that failed.</summary>
    public string CheckId { get; } = checkId;
}

/// <summary>Runs checks against one database.</summary>
public static class Scanner
{
    /// <summary>Reads the server's version and database, then runs each check in turn.</summary>
    /// <param name="session">The guarded session to query through.</param>
    /// <param name="host">The host to name in the report.</param>
    /// <param name="checks">The checks to run, in order.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The server and each check's findings.</returns>
    /// <exception cref="CheckFailedException">A check failed for any reason. The scan stops there.</exception>
    /// <exception cref="Npgsql.NpgsqlException">The server's version or database couldn't be read.</exception>
    public static async Task<ScanReport> ScanAsync(
        ReadOnlySession session, string host, IReadOnlyList<CheckDefinition> checks, CancellationToken cancellationToken)
    {
        var row = (await session.QueryAsync(
            "SELECT current_database() AS database, current_setting('server_version_num')::int AS version",
            [],
            cancellationToken)).Single();

        // server_version_num is major * 10000 + minor from Postgres 10 on.
        var version = (int)row["version"]!;
        var server = new ServerInfo(
            (string)row["database"]!,
            host,
            string.Create(CultureInfo.InvariantCulture, $"{version / 10000}.{version % 10000}"));

        var results = new List<CheckResult>();
        foreach (var check in checks)
        {
            try
            {
                results.Add(new CheckResult(check, await CheckRunner.RunAsync(session, check, cancellationToken)));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new CheckFailedException(check.Id, error);
            }
        }

        return new ScanReport(server, results);
    }
}
