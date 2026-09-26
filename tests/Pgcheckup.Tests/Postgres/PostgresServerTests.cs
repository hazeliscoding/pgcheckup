using Npgsql;

namespace Pgcheckup.Tests.Postgres;

// Fixtures must run on stock Postgres, or a check that looks for risky settings would fire on
// every fixture. Testcontainers turns fsync, full_page_writes and synchronous_commit off by default.
public class PostgresServerTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<string> ShowAsync(PostgresServer server, string setting)
    {
        await using var connection = await server.OpenSuperuserAsync(Cancel);
        await using var command = new NpgsqlCommand($"SHOW {setting}", connection);
        return (string)(await command.ExecuteScalarAsync(Cancel))!;
    }

    [Fact]
    public async Task Starts_with_stock_settings()
    {
        await using var server = await PostgresServer.StartAsync(Cancel);

        Assert.Equal("on", await ShowAsync(server, "fsync"));
        Assert.Equal("on", await ShowAsync(server, "full_page_writes"));
        Assert.Equal("on", await ShowAsync(server, "synchronous_commit"));
    }

    [Fact]
    public async Task Starts_with_the_settings_a_fixture_asks_for()
    {
        await using var server = await PostgresServer.StartAsync(new Dictionary<string, string> { ["max_prepared_transactions"] = "5" }, Cancel);

        Assert.Equal("5", await ShowAsync(server, "max_prepared_transactions"));
    }
}
