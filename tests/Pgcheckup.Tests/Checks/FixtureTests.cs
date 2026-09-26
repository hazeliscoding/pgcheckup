using Npgsql;
using Pgcheckup.Checks;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Checks;

// Every check runs against its own fixtures on a fresh Postgres, through the same session and
// runner as a scan. `fires` must produce a finding and `healthy` must not.
public class FixtureTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // PGCHECKUP_TEST_CHECK=<id> runs one check's fixtures, which is quicker while writing a check.
    private static IEnumerable<CheckDefinition> Selected =>
        Environment.GetEnvironmentVariable("PGCHECKUP_TEST_CHECK") is { Length: > 0 } id
            ? CheckCatalog.All.Where(c => c.Id == id)
            : CheckCatalog.All;

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var check in Selected)
        {
            data.Add(check.Id, "fires");
            data.Add(check.Id, "healthy");
        }

        return data;
    }

    public static TheoryData<string> Checks() => new(Selected.Select(c => c.Id));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fires_on_its_fires_fixture_and_stays_quiet_on_healthy(string checkId, string fixture)
    {
        await using var prepared = await PrepareAsync(checkId, fixture);

        var findings = await RunAsync(prepared, prepared.Server.Checkup);

        if (fixture == "fires")
        {
            Assert.NotEmpty(findings);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    // A check can pass while seeing nothing: without pg_read_all_stats, for example,
    // pg_stat_activity hides other users' sessions. So the declared privileges must be enough.
    [Theory]
    [MemberData(nameof(Checks))]
    public async Task Fires_as_a_role_with_only_its_declared_privileges(string checkId)
    {
        await using var prepared = await PrepareAsync(checkId, "fires");
        var grants = prepared.Check.Privileges.Select(p => p == Applicability.SelectOnSequences
            ? "GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO declared"
            : $"GRANT {p} TO declared");
        await prepared.Server.ExecuteAsSuperuserAsync(Cancel, ["CREATE ROLE declared LOGIN PASSWORD 'declared'", .. grants]);

        var declared = prepared.Server.Checkup;
        declared.Username = "declared";
        declared.Password = "declared";

        Assert.NotEmpty(await RunAsync(prepared, declared));
    }

    private static async Task<PreparedFixture> PrepareAsync(string checkId, string fixture)
    {
        var check = CheckCatalog.All.Single(c => c.Id == checkId);
        if (int.Parse(PostgresServer.Version) < check.MinVersion)
        {
            Assert.Skip($"{checkId} needs Postgres {check.MinVersion} or later.");
        }

        var script = FixtureScript.Load(checkId, fixture);
        var server = await PostgresServer.StartAsync(script.ServerSettings, Cancel);

        // Stays open until the check has run, so a fixture can hold a transaction or lock open.
        var setup = await server.OpenSuperuserAsync(Cancel);
        foreach (var statement in script.Statements)
        {
            await using var command = new NpgsqlCommand(statement.Sql, setup);
            if (!statement.MayFail)
            {
                await command.ExecuteNonQueryAsync(Cancel);
                continue;
            }

            // The error is the fixture's point, so a statement that succeeds means the fixture is broken.
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Cancel));
        }

        return new PreparedFixture(server, setup, script.Apply(check));
    }

    private static async Task<IReadOnlyList<Finding>> RunAsync(PreparedFixture prepared, NpgsqlConnectionStringBuilder role)
    {
        await using var session = await ReadOnlySession.OpenAsync(role, Cancel);
        return await CheckRunner.RunAsync(session, prepared.Check, Cancel);
    }

    private sealed record PreparedFixture(PostgresServer Server, NpgsqlConnection Setup, CheckDefinition Check) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Setup.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
