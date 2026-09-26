using Pgcheckup.Checks;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Checks;

public class CatalogTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    // M1's "Done when": the least-privilege role gets a clean scan, where every check runs or
    // says why it was skipped, and none errors.
    [Fact]
    public async Task A_pg_monitor_role_runs_or_skips_every_check_without_errors()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, cancel);

        var report = await Scanner.ScanAsync(session, "db.example.com", CheckCatalog.All, cancel);

        Assert.Equal(CheckCatalog.All.Count, report.Results.Count);
        Assert.All(report.Results, r => Assert.True(r.Status != CheckStatus.Errored, $"{r.Check.Id} errored: {r.Reason}"));
        Assert.All(report.Results.Where(r => r.Status == CheckStatus.Skipped), r => Assert.False(string.IsNullOrEmpty(r.Reason)));
    }
}
