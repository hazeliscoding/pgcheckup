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
    [Fact]
    public async Task Reports_a_warning_and_exits_0_below_the_default_fail_on()
    {
        var server = await postgres.ServerAsync();
        var (exitCode, output, error) = await CommandLineTests.RunAsync("scan", await postgres.CheckupUrlAsync());

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
        var (exitCode, _, _) = await CommandLineTests.RunAsync("scan", await postgres.CheckupUrlAsync(), "--fail-on", "warning");

        Assert.Equal(1, exitCode);
    }
}
