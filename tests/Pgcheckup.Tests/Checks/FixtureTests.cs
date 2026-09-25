using Npgsql;
using Pgcheckup.Checks;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Checks;

// Every check runs against its own fixtures on a fresh Postgres, as the checkup role, through
// the same session and runner as a scan. `fires` must produce a finding and `healthy` must not.
public class FixtureTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var check in CheckCatalog.All)
        {
            data.Add(check.Id, "fires");
            data.Add(check.Id, "healthy");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fires_on_its_fires_fixture_and_stays_quiet_on_healthy(string checkId, string fixture)
    {
        var check = CheckCatalog.All.Single(c => c.Id == checkId);
        if (int.Parse(PostgresServer.Version) < check.MinVersion)
        {
            Assert.Skip($"{checkId} needs Postgres {check.MinVersion} or later.");
        }

        var script = FixtureScript.Load(checkId, fixture);
        await using var server = await PostgresServer.StartAsync(Cancel);

        // Stays open until the check has run, so a fixture can hold a transaction or lock open.
        await using var setup = await server.OpenSuperuserAsync(Cancel);
        foreach (var statement in script.Statements)
        {
            await using var command = new NpgsqlCommand(statement, setup);
            await command.ExecuteNonQueryAsync(Cancel);
        }

        await using var session = await ReadOnlySession.OpenAsync(server.Checkup, Cancel);
        var findings = await CheckRunner.RunAsync(session, script.Apply(check), Cancel);

        if (fixture == "fires")
        {
            Assert.NotEmpty(findings);
        }
        else
        {
            Assert.Empty(findings);
        }
    }
}
