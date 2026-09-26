using Pgcheckup.Checks;
using Pgcheckup.Cli;

namespace Pgcheckup.Tests.Cli;

public class CatalogCommandTests
{
    private static readonly CheckDefinition Sample = new(
        "sample-check", "A sample check", "wal", Severity.Warning, 15, ["pg_monitor"], ["rds"],
        [new Threshold("min_retained_wal", ThresholdKind.Bytes, 1_073_741_824m, "1GB")],
        "SELECT 1",
        new Template([]),
        new Template([]),
        "## What breaks\n\nDisks fill.\n\n## Fix\n\nDrop it.\n\n## Seen in\n\n- [Docs](https://www.postgresql.org/docs/current/)");

    private static readonly CheckDefinition Other = Sample with
    {
        Id = "other-check", Title = "Another check", Category = "ids", Severity = Severity.Critical, MinVersion = 14,
        Privileges = [], SkipOn = [], Thresholds = [],
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = await PgcheckupCli.RunAsync(
            args, [Sample, Other], output, error, new Dictionary<string, string?>(), outputRedirected: true, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Lists_each_check_with_its_severity_category_and_minimum_version()
    {
        var (exitCode, output, _) = await RunAsync("list");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            check         severity  category  postgres  title
            sample-check  warning   wal       15+       A sample check
            other-check   critical  ids       14+       Another check

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public async Task Explains_a_check_with_its_details_and_note()
    {
        var (exitCode, output, _) = await RunAsync("explain", "sample-check");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            sample-check · A sample check

            Severity:    warning
            Category:    wal
            Postgres:    15 or later
            Needs:       pg_monitor
            Skipped on:  Amazon RDS
            Thresholds:  min_retained_wal = 1GB

            ## What breaks

            Disks fill.

            ## Fix

            Drop it.

            ## Seen in

            - [Docs](https://www.postgresql.org/docs/current/)

            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public async Task Says_when_a_check_needs_nothing()
    {
        var (_, output, _) = await RunAsync("explain", "other-check");

        Assert.Contains("Needs:       no extra privileges\n", output);
        Assert.DoesNotContain("Skipped on:", output);
        Assert.DoesNotContain("Thresholds:", output);
    }

    [Fact]
    public async Task Exits_2_for_a_check_that_does_not_exist()
    {
        var (exitCode, _, error) = await RunAsync("explain", "no-such-check");

        Assert.Equal(2, exitCode);
        Assert.Contains("no-such-check", error);
        Assert.Contains("pgcheckup list", error);
    }
}
