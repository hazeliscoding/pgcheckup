using System.Globalization;
using Pgcheckup.Checks;

namespace Pgcheckup.Engine;

public sealed record ServerInfo(string Database, string Host, string Version);

public sealed record CheckResult(CheckDefinition Check, IReadOnlyList<Finding> Findings)
{
    public Severity? Worst => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);
}

public sealed record ScanReport(ServerInfo Server, IReadOnlyList<CheckResult> Results);

// A check that couldn't run. In M0 this ends the scan; M1 reports it as errored and goes on.
public sealed class CheckFailedException(string checkId, Exception inner)
    : Exception($"{checkId} couldn't run: {inner.Message}", inner)
{
    public string CheckId { get; } = checkId;
}

public static class Scanner
{
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
            catch (Exception error) when (error is CheckException or Npgsql.NpgsqlException)
            {
                throw new CheckFailedException(check.Id, error);
            }
        }

        return new ScanReport(server, results);
    }
}
