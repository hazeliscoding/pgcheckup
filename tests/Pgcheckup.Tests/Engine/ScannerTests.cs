using Pgcheckup.Checks;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Engine;

public class ScannerTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static CheckDefinition Check(string id, string sql, int minVersion = 14) => new(
        id, id, "wal", Severity.Warning, minVersion, [], [], [], sql,
        new Template([new ValuePart("subject", ValueFormat.Default)]),
        new Template([new TextPart("fix it")]),
        "");

    private async Task<ScanReport> ScanAsync(params CheckDefinition[] checks)
    {
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, Cancel);
        return await Scanner.ScanAsync(session, "db.example.com", checks, Cancel);
    }

    [Fact]
    public async Task Reports_a_failing_check_as_errored_and_runs_the_rest()
    {
        var report = await ScanAsync(
            Check("broken-check", "SELECT (1 / 0)::text AS subject"),
            Check("working-check", "SELECT 'orders' AS subject"));

        Assert.Collection(
            report.Results,
            broken =>
            {
                Assert.Equal(CheckStatus.Errored, broken.Status);
                Assert.Contains("division by zero", broken.Reason);
            },
            working =>
            {
                Assert.Equal(CheckStatus.Found, working.Status);
                Assert.Equal("orders", Assert.Single(working.Findings).Subject);
            });
    }

    [Fact]
    public async Task Reports_a_check_that_runs_too_long_as_timed_out()
    {
        var result = Assert.Single((await ScanAsync(Check("slow-check", "SELECT 'a' AS subject FROM pg_sleep(6)"))).Results);

        Assert.Equal(CheckStatus.Errored, result.Status);
        Assert.Equal("timed out after 5 s", result.Reason);
    }

    [Fact]
    public async Task Skips_a_check_that_does_not_apply_without_running_it()
    {
        // The query would fail if it ran.
        var result = Assert.Single((await ScanAsync(Check("future-check", "SELECT (1 / 0)::text AS subject", minVersion: 99))).Results);

        Assert.Equal(CheckStatus.Skipped, result.Status);
        Assert.Equal("needs Postgres 99 or later", result.Reason);
    }

    [Fact]
    public async Task Passes_a_check_that_finds_nothing()
    {
        var result = Assert.Single((await ScanAsync(Check("quiet-check", "SELECT 'a' AS subject WHERE false"))).Results);

        Assert.Equal(CheckStatus.Passed, result.Status);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Carries_the_server_context()
    {
        var report = await ScanAsync();

        Assert.Equal("app", report.Server.Database);
        Assert.Equal(int.Parse(PostgresServer.Version), report.Server.Major);
    }
}
