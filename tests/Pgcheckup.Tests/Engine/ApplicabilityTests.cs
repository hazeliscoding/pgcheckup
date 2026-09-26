using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Tests.Engine;

public class ApplicabilityTests
{
    private static readonly Provider Rds = new("rds", "Amazon RDS");

    private static CheckDefinition Check(int minVersion = 14, string[]? privileges = null, string[]? skipOn = null) => new(
        "sample-check", "Sample", "wal", Severity.Warning, minVersion, privileges ?? [], skipOn ?? [], [], "SELECT 1",
        new Template([]), new Template([]), "");

    private static ServerContext Server(int versionNumber = 170006, Provider? provider = null, params string[] privileges) =>
        new("app", "db.example.com", versionNumber, provider, privileges.ToHashSet());

    [Fact]
    public void Runs_a_check_the_server_and_role_can_run()
    {
        Assert.Null(Applicability.SkipReason(Check(privileges: ["pg_monitor"], skipOn: ["neon"]), Server(provider: Rds, privileges: "pg_monitor")));
    }

    [Fact]
    public void Skips_a_check_that_needs_a_newer_postgres()
    {
        Assert.Equal("needs Postgres 17 or later", Applicability.SkipReason(Check(minVersion: 17), Server(versionNumber: 160004)));
    }

    [Fact]
    public void Skips_a_check_on_a_provider_that_manages_it()
    {
        Assert.Equal("managed by Amazon RDS", Applicability.SkipReason(Check(skipOn: ["rds"]), Server(provider: Rds)));
    }

    [Fact]
    public void Skips_a_check_the_role_lacks_privileges_for()
    {
        Assert.Equal(
            "needs pg_read_all_stats and pg_stat_scan_tables",
            Applicability.SkipReason(Check(privileges: ["pg_read_all_stats", "pg_stat_scan_tables"]), Server()));
    }

    [Fact]
    public void Points_to_grant_when_sequence_counters_are_unreadable()
    {
        Assert.Equal(
            "can't read sequence counters; see pgcheckup grant",
            Applicability.SkipReason(Check(privileges: ["select_on_sequences"]), Server()));
    }

    [Fact]
    public void Gives_the_version_reason_before_the_others()
    {
        Assert.Equal(
            "needs Postgres 17 or later",
            Applicability.SkipReason(Check(minVersion: 17, privileges: ["pg_monitor"], skipOn: ["rds"]), Server(versionNumber: 140012, provider: Rds)));
    }
}
