using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Engine;

public class ServerContextTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<ServerContext> ReadAsync(Npgsql.NpgsqlConnectionStringBuilder? role = null)
    {
        await using var session = await ReadOnlySession.OpenAsync(role ?? postgres.Server.Checkup, Cancel);
        return await ServerContext.ReadAsync(session, "db.example.com", Cancel);
    }

    // Roles are cluster-wide, so each case removes what it created.
    public static TheoryData<string[], string[], string?> Providers() => new()
    {
        { [], [], null },
        { ["CREATE ROLE rds_superuser"], ["DROP ROLE rds_superuser"], "rds" },
        {
            ["CREATE ROLE rds_superuser", "CREATE FUNCTION public.aurora_version() RETURNS text LANGUAGE sql AS $$ SELECT '16.4.0' $$"],
            ["DROP FUNCTION public.aurora_version()", "DROP ROLE rds_superuser"],
            "aurora"
        },
        { ["CREATE ROLE cloudsqlsuperuser"], ["DROP ROLE cloudsqlsuperuser"], "cloudsql" },
        { ["CREATE ROLE azure_pg_admin"], ["DROP ROLE azure_pg_admin"], "azure" },
        { ["CREATE ROLE supabase_admin"], ["DROP ROLE supabase_admin"], "supabase" },
        { ["ALTER DATABASE app SET neon.tenant_id = 'placeholder'"], ["ALTER DATABASE app RESET neon.tenant_id"], "neon" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Detects_the_managed_provider_from_its_roles_and_settings(string[] setup, string[] teardown, string? expected)
    {
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, setup);
        try
        {
            Assert.Equal(expected, (await ReadAsync()).Provider?.Id);
        }
        finally
        {
            await postgres.Server.ExecuteAsSuperuserAsync(Cancel, teardown);
        }
    }

    [Fact]
    public async Task Reads_the_database_and_version()
    {
        var context = await ReadAsync();

        Assert.Equal("app", context.Database);
        Assert.Equal("db.example.com", context.Host);
        Assert.Equal(int.Parse(PostgresServer.Version), context.Major);
        Assert.StartsWith($"{PostgresServer.Version}.", context.Version);
    }

    [Fact]
    public async Task Knows_the_privileges_that_pg_monitor_brings()
    {
        var context = await ReadAsync();

        Assert.Superset(
            new HashSet<string> { "pg_monitor", "pg_read_all_settings", "pg_read_all_stats", "pg_stat_scan_tables" },
            new HashSet<string>(context.Privileges));
    }

    [Fact]
    public async Task Knows_a_plain_role_has_none_of_them()
    {
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "CREATE ROLE plain LOGIN PASSWORD 'plain'");
        var plain = postgres.Server.Checkup;
        plain.Username = "plain";
        plain.Password = "plain";

        Assert.DoesNotContain((await ReadAsync(plain)).Privileges, p => p.StartsWith("pg_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Can_read_sequence_counters_only_once_granted()
    {
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "CREATE SEQUENCE public.counter");
        try
        {
            Assert.DoesNotContain("select_on_sequences", (await ReadAsync()).Privileges);

            await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "GRANT SELECT ON SEQUENCE public.counter TO checkup");
            Assert.Contains("select_on_sequences", (await ReadAsync()).Privileges);
        }
        finally
        {
            await postgres.Server.ExecuteAsSuperuserAsync(Cancel, "DROP SEQUENCE public.counter");
        }
    }
}
