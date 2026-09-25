using Npgsql;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Engine;

public class ReadOnlySessionTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(string sql, params object[] parameters)
    {
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, Cancel);
        return await session.QueryAsync(sql, parameters, Cancel);
    }

    [Fact]
    public async Task Runs_each_query_read_only_with_statement_and_lock_timeouts()
    {
        var row = Assert.Single(await QueryAsync("""
            SELECT current_setting('transaction_read_only') AS read_only,
                   current_setting('statement_timeout') AS statement_timeout,
                   current_setting('lock_timeout') AS lock_timeout,
                   current_setting('application_name') AS application_name
            """));

        Assert.Equal("on", row["read_only"]);
        Assert.Equal("5s", row["statement_timeout"]);
        Assert.Equal("1s", row["lock_timeout"]);
        Assert.Equal("pgcheckup", row["application_name"]);
    }

    [Fact]
    public async Task Ends_the_transaction_after_each_query()
    {
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, Cancel);

        var first = Assert.Single(await session.QueryAsync("SELECT now() AS started", [], Cancel))["started"];
        var second = Assert.Single(await session.QueryAsync("SELECT now() AS started", [], Cancel))["started"];

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Sets_nothing_for_the_whole_session()
    {
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, Cancel);
        await session.QueryAsync("SELECT 1 AS one", [], Cancel);

        // Behind a transaction pooler, anything set for the session reaches the app's next
        // transaction. reset_val shows settings sent when connecting.
        var row = Assert.Single(await session.QueryAsync("""
            SELECT current_setting('default_transaction_read_only') AS read_only,
                   (SELECT reset_val FROM pg_settings WHERE name = 'statement_timeout') AS statement_timeout,
                   (SELECT reset_val FROM pg_settings WHERE name = 'lock_timeout') AS lock_timeout
            """, [], Cancel));

        Assert.Equal("off", row["read_only"]);
        Assert.Equal("0", row["statement_timeout"]);
        Assert.Equal("0", row["lock_timeout"]);
    }

    [Fact]
    public async Task Rejects_a_write_even_when_the_role_may_write()
    {
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "CREATE TABLE written (n int)", "GRANT INSERT ON written TO checkup");

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            QueryAsync("WITH w AS (INSERT INTO written VALUES (1) RETURNING n) SELECT n FROM w"));

        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
    }

    [Fact]
    public async Task Rejects_a_second_statement()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => QueryAsync("SELECT 1; SELECT 2"));

        Assert.Equal(PostgresErrorCodes.SyntaxError, error.SqlState);
    }

    [Fact]
    public async Task Gives_up_on_a_lock_instead_of_queueing_behind_it()
    {
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "CREATE TABLE migrating (n int)", "GRANT SELECT ON migrating TO checkup");
        await using var migration = await postgres.Server.OpenSuperuserAsync(Cancel);
        await using var transaction = await migration.BeginTransactionAsync(Cancel);
        await using (var lockTable = new NpgsqlCommand("LOCK TABLE migrating IN ACCESS EXCLUSIVE MODE", migration, transaction))
        {
            await lockTable.ExecuteNonQueryAsync(Cancel);
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => QueryAsync("SELECT count(*) AS n FROM migrating"));

        Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
    }

    [Fact]
    public async Task Binds_threshold_parameters_by_position()
    {
        var row = Assert.Single(await QueryAsync(
            "SELECT $1 AS bytes, $2 AS age, $3 AS ratio",
            1_073_741_824L,
            TimeSpan.FromHours(1),
            0.9m));

        Assert.Equal(1_073_741_824L, row["bytes"]);
        Assert.Equal(TimeSpan.FromHours(1), row["age"]);
        Assert.Equal(0.9m, row["ratio"]);
    }

    [Fact]
    public async Task Returns_null_for_sql_null()
    {
        var row = Assert.Single(await QueryAsync("SELECT NULL::interval AS missing"));

        Assert.Null(row["missing"]);
    }
}
