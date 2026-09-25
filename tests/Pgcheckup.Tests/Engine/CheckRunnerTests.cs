using Pgcheckup.Checks;
using Pgcheckup.Engine;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Engine;

public class CheckRunnerTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static CheckDefinition Check(string sql, params Threshold[] thresholds) => new(
        Id: "sample-check",
        Title: "Sample check",
        Category: "wal",
        Severity: Severity.Warning,
        MinVersion: 14,
        Privileges: [],
        SkipOn: [],
        Thresholds: thresholds,
        Sql: sql,
        Message: new Template([new TextPart("Thing "), new ValuePart("subject", ValueFormat.Default), new TextPart(" holds "), new ValuePart("size", ValueFormat.Bytes), new TextPart(".")]),
        Fix: new Template([new TextPart("Drop "), new ValuePart("subject", ValueFormat.Default), new TextPart(".")]),
        Note: "");

    private async Task<IReadOnlyList<Finding>> RunAsync(CheckDefinition check)
    {
        await using var session = await ReadOnlySession.OpenAsync(postgres.Server.Checkup, Cancel);
        return await CheckRunner.RunAsync(session, check, Cancel);
    }

    [Fact]
    public async Task Reports_a_finding_per_row_with_its_message_and_fix()
    {
        var findings = await RunAsync(Check("SELECT 'a' AS subject, 1024::bigint AS size UNION ALL SELECT 'b', 2048"));

        Assert.Collection(
            findings,
            f =>
            {
                Assert.Equal("sample-check", f.CheckId);
                Assert.Equal("a", f.Subject);
                Assert.Equal(Severity.Warning, f.Severity);
                Assert.Equal("Thing a holds 1 kB.", f.Message);
                Assert.Equal("Drop a.", f.Fix);
            },
            f => Assert.Equal("Thing b holds 2 kB.", f.Message));
    }

    [Fact]
    public async Task Reports_nothing_when_the_query_returns_no_rows()
    {
        Assert.Empty(await RunAsync(Check("SELECT 'a' AS subject, 1 AS size WHERE false")));
    }

    [Fact]
    public async Task Lets_a_row_raise_its_severity()
    {
        var findings = await RunAsync(Check(
            "SELECT 'a' AS subject, 1 AS size, 'critical' AS severity UNION ALL SELECT 'b', 1, NULL"));

        Assert.Equal([Severity.Critical, Severity.Warning], findings.Select(f => f.Severity));
    }

    [Fact]
    public async Task Binds_thresholds_as_typed_parameters_in_order()
    {
        var check = Check(
            "SELECT 'a' AS subject, 1 AS size WHERE $1 = 1073741824::bigint AND $2 = interval '1 hour' AND $3 = 5 AND $4 = 0.9",
            new Threshold("min_size", ThresholdKind.Bytes, 1_073_741_824m, "1GB"),
            new Threshold("min_age", ThresholdKind.Duration, 3_600_000_000m, "1h"),
            new Threshold("min_count", ThresholdKind.Integer, 5m, "5"),
            new Threshold("min_ratio", ThresholdKind.Number, 0.9m, "0.9"));

        Assert.Single(await RunAsync(check));
    }

    [Fact]
    public async Task Rejects_a_row_without_a_subject()
    {
        var error = await Assert.ThrowsAsync<CheckException>(() => RunAsync(Check("SELECT NULL::text AS subject, 1 AS size")));

        Assert.Contains("subject", error.Message);
    }

    [Fact]
    public async Task Rejects_an_unknown_severity()
    {
        var error = await Assert.ThrowsAsync<CheckException>(() =>
            RunAsync(Check("SELECT 'a' AS subject, 1 AS size, 'high' AS severity")));

        Assert.Contains("high", error.Message);
    }
}
