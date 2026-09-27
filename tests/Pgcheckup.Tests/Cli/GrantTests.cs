using Npgsql;
using Pgcheckup.Checks;
using Pgcheckup.Cli;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Cli;

public class GrantTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void Prints_a_least_privilege_role()
    {
        Assert.Equal(
            """
            -- A least-privilege role for pgcheckup. Review it, then run it as a superuser
            -- (rds_superuser on Amazon RDS, cloudsqlsuperuser on Cloud SQL).
            CREATE ROLE checkup LOGIN;
            -- Set its password with \password checkup, or use your provider's IAM login.
            GRANT pg_monitor TO checkup;
            GRANT CONNECT ON DATABASE app TO checkup;
            -- Every session of this role is read-only, even outside pgcheckup.
            ALTER ROLE checkup SET default_transaction_read_only = on;

            -- Only integer-exhaustion needs these. They show sequence counters, never table rows.
            -- Repeat them for each schema with sequences. The second covers sequences that app,
            -- the role that owns your tables, creates later; name another with --owner.
            GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO checkup;
            ALTER DEFAULT PRIVILEGES FOR ROLE app IN SCHEMA public GRANT SELECT ON SEQUENCES TO checkup;

            """.ReplaceLineEndings("\n"),
            GrantScript.Build("checkup", "app", "app"));
    }

    [Theory]
    [InlineData("check\nup")]
    [InlineData("check\u001bup")]
    public void Refuses_a_name_with_control_characters(string name)
    {
        // A line break would end the -- comment the name appears in, and run the rest as SQL.
        Assert.Throws<ArgumentException>(() => GrantScript.Build(name, "app", "app"));
        Assert.Throws<ArgumentException>(() => GrantScript.Build("checkup", name, "app"));
        Assert.Throws<ArgumentException>(() => GrantScript.Build("checkup", "app", name));
    }

    [Theory]
    [InlineData("user", "\"user\"")]
    [InlineData("Checkup", "\"Checkup\"")]
    [InlineData("check-up", "\"check-up\"")]
    [InlineData("we\"ird", "\"we\"\"ird\"")]
    [InlineData("checkup_2", "checkup_2")]
    public void Quotes_names_that_need_it(string name, string quoted)
    {
        Assert.Contains($"CREATE ROLE {quoted} LOGIN;", GrantScript.Build(name, "app", "app"));
        Assert.Contains($"GRANT CONNECT ON DATABASE {quoted} TO", GrantScript.Build("checkup", name, "app"));
        Assert.Contains($"FOR ROLE {quoted} IN SCHEMA", GrantScript.Build("checkup", "app", name));
    }

    // The printed SQL must actually produce a role that scans cleanly and can't write.
    [Fact]
    public async Task Creates_a_role_that_scans_every_check_and_cannot_write()
    {
        // The test server's tables belong to its superuser, postgres.
        var statements = FixtureScript.Parse(GrantScript.Build("scanner", "app", "postgres")).Statements.Select(s => s.Sql);
        await postgres.Server.ExecuteAsSuperuserAsync(Cancel, [.. statements, "ALTER ROLE scanner PASSWORD 'scanner'"]);
        var scanner = postgres.Server.Checkup;
        scanner.Username = "scanner";
        scanner.Password = "scanner";

        await using (var session = await ReadOnlySession.OpenAsync(scanner, Cancel))
        {
            var report = await Scanner.ScanAsync(session, "db.example.com", CheckCatalog.All, Cancel);
            // Only a newer Postgres version may skip a check; the grant must cover every privilege.
            Assert.DoesNotContain(report.Results, r => r.Status == CheckStatus.Errored
                || (r.Status == CheckStatus.Skipped && !r.Reason!.StartsWith("needs Postgres", StringComparison.Ordinal)));
        }

        // Outside pgcheckup's guards, the role's own default still refuses writes.
        await using var plain = new NpgsqlConnection(scanner.ConnectionString);
        await plain.OpenAsync(Cancel);
        await using var write = new NpgsqlCommand("CREATE TABLE public.scanner_wrote (n int)", plain);
        var error = await Assert.ThrowsAsync<PostgresException>(() => write.ExecuteNonQueryAsync(Cancel));
        Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, error.SqlState);
    }
}
