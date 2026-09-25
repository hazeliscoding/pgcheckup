using Npgsql;

namespace Pgcheckup.Tests.Postgres;

// A Postgres whose inactive slot holds more WAL than replication-slot-inactive's default 1 GB,
// so a scan with default thresholds reports it. It starts on first use, so skipped tests cost nothing.
public sealed class InactiveSlotFixture : IAsyncDisposable
{
    private readonly Lazy<Task<PostgresServer>> server = new(StartAsync);

    public Task<PostgresServer> ServerAsync() => server.Value;

    public async Task<string> CheckupUrlAsync()
    {
        var checkup = (await ServerAsync()).Checkup;
        return $"postgres://checkup:checkup@{checkup.Host}:{checkup.Port}/app";
    }

    public async ValueTask DisposeAsync()
    {
        if (server.IsValueCreated)
        {
            await (await server.Value).DisposeAsync();
        }
    }

    // Not tied to the first test's cancellation, because every test in the class shares it.
    private static async Task<PostgresServer> StartAsync()
    {
        var server = await PostgresServer.StartAsync(CancellationToken.None);
        await server.ExecuteAsSuperuserAsync(CancellationToken.None, "SELECT pg_create_physical_replication_slot('debezium', true)");

        // Logical messages add WAL without writing table data, which keeps this fast.
        await using var connection = await server.OpenSuperuserAsync(CancellationToken.None);
        for (var i = 0; i < 17; i++)
        {
            await using var command = new NpgsqlCommand("SELECT pg_logical_emit_message(false, 'pgcheckup', repeat('x', 64 * 1024 * 1024))", connection);
            await command.ExecuteNonQueryAsync();
        }

        return server;
    }
}
