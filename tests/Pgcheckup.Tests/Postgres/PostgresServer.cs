using Npgsql;
using Testcontainers.PostgreSql;

namespace Pgcheckup.Tests.Postgres;

// A throwaway Postgres with the placeholder database `app` and a `checkup` role that has only
// pg_monitor, the least privilege pgcheckup promises to need.
public sealed class PostgresServer : IAsyncDisposable
{
    private readonly PostgreSqlContainer container;

    private PostgresServer(PostgreSqlContainer container) => this.container = container;

    // CI runs the suite once per supported major, for example PGCHECKUP_TEST_POSTGRES=14.
    public static string Version =>
        Environment.GetEnvironmentVariable("PGCHECKUP_TEST_POSTGRES") is { Length: > 0 } version ? version : "18";

    public string SuperuserConnectionString => container.GetConnectionString();

    public NpgsqlConnectionStringBuilder Checkup => new(container.GetConnectionString())
    {
        Username = "checkup",
        Password = "checkup",
    };

    public static async Task<PostgresServer> StartAsync(CancellationToken cancellationToken)
    {
        var container = new PostgreSqlBuilder($"postgres:{Version}-alpine").WithDatabase("app").Build();
        await container.StartAsync(cancellationToken);
        var server = new PostgresServer(container);
        await server.ExecuteAsSuperuserAsync(cancellationToken, "CREATE ROLE checkup LOGIN PASSWORD 'checkup' IN ROLE pg_monitor");
        return server;
    }

    public async Task<NpgsqlConnection> OpenSuperuserAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(SuperuserConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // One statement per string: SQL rewriting is off in tests too, as it is in pgcheckup.
    public async Task ExecuteAsSuperuserAsync(CancellationToken cancellationToken, params string[] statements)
    {
        await using var connection = await OpenSuperuserAsync(cancellationToken);
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();
}

public sealed class PostgresServerFixture : IAsyncLifetime
{
    public PostgresServer Server { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Server = await PostgresServer.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
