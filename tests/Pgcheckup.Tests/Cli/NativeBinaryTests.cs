using System.Diagnostics;
using Pgcheckup.Tests.Postgres;

namespace Pgcheckup.Tests.Cli;

// M0's acceptance test: the published NativeAOT binary reports an inactive slot and exits 1 with
// --fail-on warning. CI's AOT job points PGCHECKUP_BINARY at the binary it just published.
public class NativeBinaryTests(InactiveSlotFixture postgres) : IClassFixture<InactiveSlotFixture>
{
    private const string NoBinary = "Set PGCHECKUP_BINARY to a published pgcheckup binary to run this test.";

    public static bool HasBinary => Binary.Length > 0;

    private static string Binary => Environment.GetEnvironmentVariable("PGCHECKUP_BINARY") ?? "";

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo(Binary) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Reports_the_inactive_slot_and_exits_1_with_fail_on_warning()
    {
        var (exitCode, output) = await RunAsync("scan", await postgres.CheckupUrlAsync(), "--fail-on", "warning");

        Assert.Equal(1, exitCode);
        Assert.Contains("WARNING   replication-slot-inactive", output);
        Assert.Contains("Slot debezium has been inactive", output);
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Prints_its_version()
    {
        var (exitCode, output) = await RunAsync("--version");

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output.Trim());
    }
}
