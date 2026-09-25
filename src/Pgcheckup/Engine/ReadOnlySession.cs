using Npgsql;

namespace Pgcheckup.Engine;

// The only way pgcheckup talks to Postgres. Every query runs in its own READ ONLY transaction
// with transaction-local timeouts, then rolls back. Nothing is set for the session: behind a
// transaction pooler, a session setting would reach the application's next transaction.
public sealed class ReadOnlySession : IAsyncDisposable
{
    private static readonly string[] Guards =
    [
        "BEGIN TRANSACTION READ ONLY",
        "SET LOCAL statement_timeout = '5s'",

        // Stops a scan from queueing behind a migration's lock and blocking the traffic behind it.
        "SET LOCAL lock_timeout = '1s'",

        // A function or operator planted in another schema can't shadow a built-in and run as
        // the scanning role (CVE-2018-1058). pg_temp goes last so temporary tables can't either.
        "SET LOCAL search_path = pg_catalog, pg_temp",
    ];

    private readonly NpgsqlDataSource dataSource;
    private readonly NpgsqlConnection connection;

    private ReadOnlySession(NpgsqlDataSource dataSource, NpgsqlConnection connection)
    {
        this.dataSource = dataSource;
        this.connection = connection;
    }

    public static async Task<ReadOnlySession> OpenAsync(NpgsqlConnectionStringBuilder settings, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlSlimDataSourceBuilder(settings.ConnectionString);
        builder.ConnectionStringBuilder.ApplicationName = "pgcheckup";
        builder.ConnectionStringBuilder.Pooling = false;
        builder.EnableTransportSecurity();

        // Loading types would run a query outside the guarded transaction; the built-in types suffice.
        builder.ConfigureTypeLoading(options => options.EnableTypeLoading(false));

        var dataSource = builder.Build();
        try
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            return new ReadOnlySession(dataSource, connection);
        }
        catch
        {
            await dataSource.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql, IReadOnlyList<object> parameters, CancellationToken cancellationToken)
    {
        foreach (var guard in Guards)
        {
            await using var command = new NpgsqlCommand(guard, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var query = new NpgsqlCommand(sql, connection);
            foreach (var parameter in parameters)
            {
                query.Parameters.Add(new NpgsqlParameter { Value = parameter });
            }

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return rows;
        }
        finally
        {
            await using var rollback = new NpgsqlCommand("ROLLBACK", connection);
            await rollback.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await dataSource.DisposeAsync();
    }
}
