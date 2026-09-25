using Pgcheckup.Checks;
using Pgcheckup.Cli;
using Pgcheckup.Engine;

namespace Pgcheckup.Tests.Cli;

public class TerminalReportTests
{
    private static readonly ServerInfo Server = new("app", "db.example.com", "17.6");

    private static CheckDefinition Check(string id, Severity severity = Severity.Warning) => new(
        id, id, "wal", severity, 14, [], [], [], "SELECT 1", new Template([]), new Template([]), "");

    private static Finding Finding(string checkId, Severity severity, string message, string fix) =>
        new(checkId, "subject", severity, message, fix, new Dictionary<string, object?>());

    private static string Render(ScanReport report, bool color = false)
    {
        var output = new StringWriter { NewLine = "\n" };
        TerminalReport.Write(output, report, color);
        return output.ToString();
    }

    [Fact]
    public void Writes_each_finding_with_its_fix_under_a_header_and_above_a_summary()
    {
        var slot = Check("replication-slot-inactive");
        var report = new ScanReport(Server,
        [
            new CheckResult(Check("connection-saturation"), []),
            new CheckResult(slot,
            [
                Finding(slot.Id, Severity.Warning,
                    "Slot debezium has been inactive for 3 days and is holding 48 GB of WAL.",
                    "restart its consumer, or drop the slot:\nSELECT pg_drop_replication_slot('debezium');"),
            ]),
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
    public void Lists_critical_findings_first_and_counts_each_check_once_at_its_worst()
    {
        var slot = Check("replication-slot-inactive");
        var xid = Check("xid-wraparound", Severity.Critical);
        var report = new ScanReport(Server,
        [
            new CheckResult(slot,
            [
                Finding(slot.Id, Severity.Warning, "Slot a is inactive.", "drop a"),
                Finding(slot.Id, Severity.Critical, "Slot b is inactive.", "drop b"),
            ]),
            new CheckResult(xid, [Finding(xid.Id, Severity.Critical, "Table orders is old.", "vacuum orders")]),
            new CheckResult(Check("other-slot"), [Finding("other-slot", Severity.Warning, "Other.", "fix")]),
        ]);

        var lines = Render(report).Split('\n');

        Assert.Equal(
            ["CRITICAL  replication-slot-inactive", "CRITICAL  xid-wraparound", "WARNING   other-slot", "WARNING   replication-slot-inactive"],
            lines.Where(l => l.Length > 0 && !l.StartsWith(' ') && (l.StartsWith("CRITICAL") || l.StartsWith("WARNING"))));
        Assert.Equal("0 passed · 2 critical · 1 warning", lines[^2]);
    }

    [Fact]
    public void Says_how_many_passed_when_nothing_is_found()
    {
        var report = new ScanReport(Server, [new CheckResult(Check("a"), []), new CheckResult(Check("b"), [])]);

        Assert.Equal("pgcheckup · app on db.example.com · PostgreSQL 17.6\n\n2 passed\n", Render(report));
    }

    [Fact]
    public void Pluralises_warnings()
    {
        var report = new ScanReport(Server,
        [
            new CheckResult(Check("a"), [Finding("a", Severity.Warning, "A.", "fix")]),
            new CheckResult(Check("b"), [Finding("b", Severity.Warning, "B.", "fix")]),
        ]);

        Assert.EndsWith("0 passed · 2 warnings\n", Render(report));
    }

    [Fact]
    public void Colours_the_severity_word_only_when_asked()
    {
        var report = new ScanReport(Server, [new CheckResult(Check("a"), [Finding("a", Severity.Warning, "A.", "fix")])]);

        Assert.DoesNotContain("\u001b", Render(report, color: false));
        var coloured = Render(report, color: true);
        Assert.Contains("\u001b[1;33mWARNING\u001b[0m", coloured);
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
