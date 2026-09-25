using Npgsql;

namespace Pgcheckup.Engine;

/// <summary>
/// The only way pgcheckup talks to Postgres. Every query runs in its own <c>READ ONLY</c>
/// transaction with transaction-local timeouts and search path, then rolls back.
/// </summary>
/// <remarks>
/// Nothing is set for the session: behind a transaction pooler, a session setting would reach
/// the application's next transaction.
/// </remarks>
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

    /// <summary>Connects as <c>application_name = pgcheckup</c>, without loading types or pooling.</summary>
    /// <param name="settings">Where and how to connect. The caller's builder isn't changed.</param>
    /// <param name="cancellationToken">Cancels connecting.</param>
    /// <returns>An open session. Dispose it to close the connection.</returns>
    /// <exception cref="Npgsql.NpgsqlException">The server can't be reached, or refuses the login.</exception>
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

    /// <summary>Runs one statement in its own guarded transaction and returns every row.</summary>
    /// <param name="sql">
    /// A single statement. With SQL rewriting off, Postgres rejects a second one. Parameters are
    /// <c>$1</c>, <c>$2</c> and so on.
    /// </param>
    /// <param name="parameters">The parameter values, bound by position and typed by their .NET type.</param>
    /// <param name="cancellationToken">Cancels the query. The rollback still runs.</param>
    /// <returns>The rows, by column name. SQL NULL is <see langword="null"/>.</returns>
    /// <exception cref="Npgsql.PostgresException">
    /// The statement failed: it tried to write (25006), ran past 5 seconds (57014), waited over
    /// 1 second for a lock (55P03), or raised any other error.
    /// </exception>
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

    /// <summary>Closes the connection.</summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        await dataSource.DisposeAsync();
    }
}
