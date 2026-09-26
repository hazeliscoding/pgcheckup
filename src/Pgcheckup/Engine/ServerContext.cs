using System.Globalization;

namespace Pgcheckup.Engine;

/// <summary>A managed Postgres service, detected from SQL.</summary>
/// <param name="Id">The id that check.md lists under <c>skip_on</c>, such as <c>rds</c>.</param>
/// <param name="Name">The name the report prints, such as "Amazon RDS".</param>
public sealed record Provider(string Id, string Name)
{
    /// <summary>Every provider pgcheckup detects, in detection order.</summary>
    public static IReadOnlyList<Provider> Known { get; } =
    [
        new("aurora", "Amazon Aurora"),
        new("rds", "Amazon RDS"),
        new("cloudsql", "Google Cloud SQL"),
        new("azure", "Azure Database for PostgreSQL"),
        new("supabase", "Supabase"),
        new("neon", "Neon"),
    ];
}

/// <summary>What pgcheckup knows about the server before any check runs.</summary>
/// <param name="Database">The database scanned.</param>
/// <param name="Host">The host as given, never the full connection string.</param>
/// <param name="VersionNumber">The server's <c>server_version_num</c>, such as 170006.</param>
/// <param name="Provider">The managed service, or <see langword="null"/> when none was detected.</param>
/// <param name="Privileges">
/// The privileges a check can declare that this role has: the predefined roles it is a member of,
/// and <c>select_on_sequences</c> when it can read every sequence's counter.
/// </param>
public sealed record ServerContext(
    string Database,
    string Host,
    int VersionNumber,
    Provider? Provider,
    IReadOnlySet<string> Privileges)
{
    private static readonly string[] PrivilegeColumns =
        ["pg_monitor", "pg_read_all_settings", "pg_read_all_stats", "pg_stat_scan_tables", "select_on_sequences"];

    // One statement, so it runs in one guarded transaction. Provider signals are roles, a function
    // or settings that each service creates; a self-managed server has none of them.
    private const string Sql = """
        SELECT current_database() AS database,
               current_setting('server_version_num')::int AS version,
               pg_has_role(current_user, 'pg_monitor', 'USAGE') AS pg_monitor,
               pg_has_role(current_user, 'pg_read_all_settings', 'USAGE') AS pg_read_all_settings,
               pg_has_role(current_user, 'pg_read_all_stats', 'USAGE') AS pg_read_all_stats,
               pg_has_role(current_user, 'pg_stat_scan_tables', 'USAGE') AS pg_stat_scan_tables,
               -- Starting from pg_sequence, because Postgres may test the privilege before a
               -- relkind filter and fail on a relation that isn't a sequence.
               NOT EXISTS (
                   SELECT FROM pg_sequence AS s JOIN pg_class AS c ON c.oid = s.seqrelid
                   WHERE c.relpersistence <> 't' AND NOT has_sequence_privilege(s.seqrelid, 'SELECT,USAGE')
               ) AS select_on_sequences,
               EXISTS (SELECT FROM pg_proc WHERE proname = 'aurora_version') AS aurora,
               EXISTS (SELECT FROM pg_roles WHERE rolname = 'rds_superuser') AS rds,
               EXISTS (SELECT FROM pg_roles WHERE rolname = 'cloudsqlsuperuser') AS cloudsql,
               EXISTS (SELECT FROM pg_roles WHERE rolname = 'azure_pg_admin') AS azure,
               EXISTS (SELECT FROM pg_roles WHERE rolname = 'supabase_admin') AS supabase,
               current_setting('neon.tenant_id', true) IS NOT NULL AS neon
        """;

    /// <summary>The major version, such as 17.</summary>
    public int Major => VersionNumber / 10000;

    /// <summary>The version as people write it, such as "17.6".</summary>
    public string Version => string.Create(CultureInfo.InvariantCulture, $"{Major}.{VersionNumber % 10000}");

    /// <summary>Reads the server's version, provider and the role's privileges in one query.</summary>
    /// <param name="session">The guarded session to query through.</param>
    /// <param name="host">The host to name in the report.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The context every check's applicability is decided from.</returns>
    /// <exception cref="Npgsql.NpgsqlException">The query failed.</exception>
    public static async Task<ServerContext> ReadAsync(ReadOnlySession session, string host, CancellationToken cancellationToken)
    {
        var row = (await session.QueryAsync(Sql, [], cancellationToken)).Single();
        return new ServerContext(
            (string)row["database"]!,
            host,
            (int)row["version"]!,
            // Aurora comes first in Provider.Known because it also has rds_superuser.
            Provider.Known.FirstOrDefault(p => (bool)row[p.Id]!),
            PrivilegeColumns.Where(p => (bool)row[p]!).ToHashSet(StringComparer.Ordinal));
    }
}
