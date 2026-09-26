using Pgcheckup.Checks;
using Pgcheckup.Cli;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Cli;

public class CommandLineTests
{
    private static readonly Dictionary<string, string?> NoEnvironment = [];

    internal static async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = await PgcheckupCli.RunAsync(args, output, error, NoEnvironment, outputRedirected: true, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Lists_every_check()
    {
        var (exitCode, output, _) = await RunAsync("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("replication-slot-inactive", output);
        Assert.Contains("Inactive replication slot", output);
    }

    [Theory]
    [InlineData("scan", "--nope")]
    [InlineData("scan", "--fail-on", "sometimes")]
    [InlineData("scan", "--format", "yaml")]
    [InlineData("frobnicate")]
    public async Task Exits_2_when_the_arguments_are_wrong(params string[] args)
    {
        var (exitCode, _, error) = await RunAsync(args);

        Assert.Equal(2, exitCode);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Exits_2_when_the_connection_input_is_wrong()
    {
        var (exitCode, _, error) = await RunAsync("scan", "mysql://db.example.com/app");

        Assert.Equal(2, exitCode);
        Assert.Contains("postgres://", error);
    }

    [Fact]
    public async Task Exits_2_when_it_cannot_connect()
    {
        var (exitCode, _, error) = await RunAsync("scan", "postgres://checkup:hunter2@127.0.0.1:1/app?connect_timeout=2");

        Assert.Equal(2, exitCode);
        Assert.Contains("couldn't connect to 127.0.0.1", error);
        Assert.DoesNotContain("hunter2", error);
    }
}

public class ScanCommandTests(InactiveSlotFixture postgres) : IClassFixture<InactiveSlotFixture>
{
    private static readonly CheckDefinition SlotCheck = CheckCatalog.All.Single(c => c.Id == "replication-slot-inactive");

    // Npgsql can't read NaN into a decimal, so this check errors on every run.
    private static readonly CheckDefinition BrokenCheck = new(
        "nan-check", "NaN check", "wal", Severity.Critical, 14, [], [], [],
        "SELECT 'a' AS subject, 'NaN'::numeric AS size",
        new Template([new ValuePart("size", ValueFormat.Bytes)]),
        new Template([new TextPart("nothing")]),
        "");

    // Pinning the checks keeps the summary stable as the catalog grows.
    private async Task<(int ExitCode, string Output, string Error)> ScanAsync(CheckDefinition[] checks, params string[] options)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = await PgcheckupCli.RunAsync(
            ["scan", await postgres.CheckupUrlAsync(), .. options], checks, output, error,
            new Dictionary<string, string?>(), outputRedirected: true, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Reports_a_warning_and_exits_0_below_the_default_fail_on()
    {
        var server = await postgres.ServerAsync();
        var (exitCode, output, error) = await ScanAsync([SlotCheck]);

        Assert.Equal("", error);
        Assert.Equal(0, exitCode);
        Assert.StartsWith($"pgcheckup · app on {server.Checkup.Host} · PostgreSQL {PostgresServer.Version}.", output);
        Assert.Contains("WARNING   replication-slot-inactive", output);
        Assert.Contains("Slot debezium has been inactive", output);
        Assert.Contains("SELECT pg_drop_replication_slot('debezium');", output);
        Assert.EndsWith("0 passed · 1 warning\n", output);
    }

    [Fact]
    public async Task Exits_1_when_a_finding_reaches_fail_on()
    {
        Assert.Equal(1, (await ScanAsync([SlotCheck], "--fail-on", "warning")).ExitCode);
    }

    [Fact]
    public async Task Writes_json_with_the_same_exit_codes()
    {
        var (exitCode, output, _) = await ScanAsync([SlotCheck], "--format", "json", "--fail-on", "warning");

        Assert.Equal(1, exitCode);
        using var json = System.Text.Json.JsonDocument.Parse(output);
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal("warning", json.RootElement.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Writes_markdown()
    {
        var (exitCode, output, _) = await ScanAsync([SlotCheck], "--format", "markdown");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("## pgcheckup · app on ", output);
        Assert.Contains("| Warning | `replication-slot-inactive` |", output);
    }

    [Fact]
    public async Task Exits_2_when_a_check_errored_and_no_finding_reached_fail_on()
    {
        var (exitCode, output, error) = await ScanAsync([BrokenCheck, SlotCheck]);

        Assert.Equal(2, exitCode);
        Assert.Equal("", error);
        Assert.Contains("ERRORED   nan-check", output);
        Assert.Contains("WARNING   replication-slot-inactive", output);
    }

    [Fact]
    public async Task Exits_1_when_a_finding_reaches_fail_on_even_if_a_check_errored()
    {
        Assert.Equal(1, (await ScanAsync([BrokenCheck, SlotCheck], "--fail-on", "warning")).ExitCode);
    }
}
