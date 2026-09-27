using Pgcheckup.Checks;
using Pgcheckup.Cli;
using Pgcheckup.Engine;

namespace Pgcheckup.Tests.Cli;

public class TerminalReportTests
{
    private static readonly ServerContext Server = new("app", "db.example.com", 170006, null, new HashSet<string>());

    private static CheckDefinition Check(string id, Severity severity = Severity.Warning) => new(
        id, id, "wal", severity, 14, [], [], [], "SELECT 1", new Template([]), new Template([]), "");

    private static Finding Finding(string checkId, Severity severity, string message, string fix) =>
        new(checkId, "subject", severity, message, fix, new Dictionary<string, object?>());

    private static CheckResult Passed(string id) => new(Check(id), CheckStatus.Passed, []);

    private static CheckResult Found(string id, params Finding[] findings) => new(Check(id), CheckStatus.Found, findings);

    private static string Render(ScanReport report, bool color = false)
    {
        var output = new StringWriter { NewLine = "\n" };
        TerminalReport.Write(output, report, color);
        return output.ToString();
    }

    [Fact]
    public void Writes_each_finding_with_its_fix_under_a_header_and_above_a_summary()
    {
        var report = new ScanReport(Server,
        [
            Passed("connection-saturation"),
            Found("replication-slot-inactive", Finding("replication-slot-inactive", Severity.Warning,
                "Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.",
                "restart its consumer, or drop the slot:\nSELECT pg_drop_replication_slot('debezium');")),
        ]);

        Assert.Equal(
            """
            pgcheckup · app on db.example.com · PostgreSQL 17.6

            WARNING   replication-slot-inactive
                      Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.
                      Fix: restart its consumer, or drop the slot:
                           SELECT pg_drop_replication_slot('debezium');

            1 passed · 1 warning

            """.ReplaceLineEndings("\n"),
            Render(report));
    }

    [Fact]
    public void Names_a_managed_provider_in_the_header()
    {
        var report = new ScanReport(Server with { Provider = new Provider("rds", "Amazon RDS") }, []);

        Assert.StartsWith("pgcheckup · app on db.example.com · PostgreSQL 17.6 · Amazon RDS\n", Render(report));
    }

    [Fact]
    public void Lists_critical_findings_first_and_counts_each_check_once_at_its_worst()
    {
        var report = new ScanReport(Server,
        [
            Found("replication-slot-inactive",
                Finding("replication-slot-inactive", Severity.Warning, "Slot a is inactive.", "drop a"),
                Finding("replication-slot-inactive", Severity.Critical, "Slot b is inactive.", "drop b")),
            Found("xid-wraparound", Finding("xid-wraparound", Severity.Critical, "Table orders is old.", "vacuum orders")),
            Found("other-slot", Finding("other-slot", Severity.Warning, "Other.", "fix")),
        ]);

        var lines = Render(report).Split('\n');

        Assert.Equal(
            ["CRITICAL  replication-slot-inactive", "CRITICAL  xid-wraparound", "WARNING   other-slot", "WARNING   replication-slot-inactive"],
            lines.Where(l => l.StartsWith("CRITICAL", StringComparison.Ordinal) || l.StartsWith("WARNING", StringComparison.Ordinal)));
        Assert.Equal("0 passed · 2 critical · 1 warning", lines[^2]);
    }

    [Fact]
    public void Shows_an_errored_check_after_the_findings_with_its_reason()
    {
        var report = new ScanReport(Server,
        [
            new CheckResult(Check("xid-wraparound"), CheckStatus.Errored, [], "timed out after 5 s"),
            Found("replication-slot-inactive", Finding("replication-slot-inactive", Severity.Warning, "Slot a is inactive.", "drop a")),
        ]);

        Assert.EndsWith(
            """
            WARNING   replication-slot-inactive
                      Slot a is inactive.
                      Fix: drop a

            ERRORED   xid-wraparound
                      Timed out after 5 s.

            0 passed · 1 warning · 1 errored

            """.ReplaceLineEndings("\n"),
            Render(report));
    }

    [Fact]
    public void Escapes_control_characters_from_the_server()
    {
        var report = new ScanReport(Server with { Database = "app\u001b]52;c;x\u0007" },
            [new CheckResult(Check("a"), CheckStatus.Errored, [], "42P01: relation \"t\u001b[2K\" does not exist")]);

        var output = Render(report);

        Assert.DoesNotContain("\u001b", output);
        Assert.DoesNotContain("\u0007", output);
        Assert.Contains("app\\u001b]52;c;x\\u0007 on", output);
    }

    [Fact]
    public void Lists_skipped_checks_with_their_reasons_in_the_summary()
    {
        var report = new ScanReport(Server,
        [
            Passed("dangerous-settings"),
            new CheckResult(Check("wal-archiving-failing"), CheckStatus.Skipped, [], "managed by Amazon RDS"),
            new CheckResult(Check("integer-exhaustion"), CheckStatus.Skipped, [], "can't read sequence counters; see pgcheckup grant"),
        ]);

        Assert.EndsWith(
            "\n1 passed · 2 skipped (wal-archiving-failing: managed by Amazon RDS, integer-exhaustion: can't read sequence counters; see pgcheckup grant)\n",
            Render(report));
    }

    [Fact]
    public void Says_how_many_passed_when_nothing_is_found()
    {
        var report = new ScanReport(Server, [Passed("a"), Passed("b")]);

        Assert.Equal("pgcheckup · app on db.example.com · PostgreSQL 17.6\n\n2 passed\n", Render(report));
    }

    [Fact]
    public void Pluralises_warnings()
    {
        var report = new ScanReport(Server,
        [
            Found("a", Finding("a", Severity.Warning, "A.", "fix")),
            Found("b", Finding("b", Severity.Warning, "B.", "fix")),
        ]);

        Assert.EndsWith("0 passed · 2 warnings\n", Render(report));
    }

    [Fact]
    public void Colours_the_status_word_only_when_asked()
    {
        var report = new ScanReport(Server,
        [
            Found("a", Finding("a", Severity.Warning, "A.", "fix")),
            new CheckResult(Check("b"), CheckStatus.Errored, [], "timed out after 5 s"),
        ]);

        Assert.DoesNotContain("\u001b", Render(report, color: false));
        var coloured = Render(report, color: true);
        Assert.Contains("\u001b[1;33mWARNING\u001b[0m", coloured);
        Assert.Contains("\u001b[1;35mERRORED\u001b[0m", coloured);
    }

    [Theory]
    [InlineData(false, null, null, true)]
    [InlineData(true, null, null, false)]
    [InlineData(false, "1", null, false)]
    [InlineData(false, "", null, true)]
    [InlineData(false, null, "dumb", false)]
    public void Uses_colour_only_on_a_terminal_without_NO_COLOR(bool redirected, string? noColor, string? term, bool expected)
    {
        var environment = new Dictionary<string, string?> { ["NO_COLOR"] = noColor, ["TERM"] = term };

        Assert.Equal(expected, TerminalReport.UseColor(redirected, environment));
    }
}
