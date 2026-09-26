using System.Text.Json;
using Pgcheckup.Checks;
using Pgcheckup.Cli;
using Pgcheckup.Engine;

namespace Pgcheckup.Tests.Cli;

public class MachineReportTests
{
    private static readonly ServerContext Server = new("app", "db.example.com", 170006, new Provider("rds", "Amazon RDS"), new HashSet<string>());

    private static readonly Template SlotMessage = new(
    [
        new TextPart("Slot "),
        new ValuePart("subject", ValueFormat.Default),
        new SectionPart([new TextPart(" for "), new ValuePart("inactive_for", ValueFormat.Default)]),
        new TextPart(" holds "),
        new ValuePart("retained_wal", ValueFormat.Bytes),
    ]);

    private static CheckDefinition Check(string id, Template? message = null) => new(
        id, $"Title of {id}", "wal", Severity.Warning, 14, [], [], [], "SELECT 1",
        message ?? new Template([]), new Template([new ValuePart("slot_literal", ValueFormat.Default)]), "");

    private static readonly ScanReport Report = new(Server,
    [
        new CheckResult(Check("replication-slot-inactive", SlotMessage), CheckStatus.Found,
        [
            new Finding("replication-slot-inactive", "debezium", Severity.Warning,
                "Slot debezium for 3 days holds 48 GB",
                "restart its consumer, or drop the slot:\nSELECT pg_drop_replication_slot('debezium');",
                new Dictionary<string, object?>
                {
                    ["subject"] = "debezium",
                    ["slot_literal"] = "'debezium'",
                    ["inactive_for"] = new TimeSpan(3, 0, 0, 0, 500),
                    ["retained_wal"] = 51_539_607_552L,
                    ["seen_at"] = new DateTime(2026, 9, 25, 14, 3, 59, DateTimeKind.Utc),
                }),
        ]),
        new CheckResult(Check("connection-saturation"), CheckStatus.Passed, []),
        new CheckResult(Check("wal-archiving-failing"), CheckStatus.Skipped, [], "managed by Amazon RDS"),
        new CheckResult(Check("xid-wraparound"), CheckStatus.Errored, [], "timed out after 5 s"),
    ]);

    [Fact]
    public void Lists_the_columns_a_template_uses_in_order()
    {
        Assert.Equal(["subject", "inactive_for", "retained_wal"], SlotMessage.ValueNames);
    }

    [Fact]
    public void Writes_json_with_schema_1_the_server_and_a_summary()
    {
        using var json = JsonDocument.Parse(JsonReport.Write(Report, "0.1.0"));
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("0.1.0", root.GetProperty("pgcheckup").GetString());
        var server = root.GetProperty("server");
        Assert.Equal("app", server.GetProperty("database").GetString());
        Assert.Equal("db.example.com", server.GetProperty("host").GetString());
        Assert.Equal("17.6", server.GetProperty("version").GetString());
        Assert.Equal("rds", server.GetProperty("provider").GetProperty("id").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal(
            [("passed", 1), ("critical", 0), ("warning", 1), ("info", 0), ("errored", 1), ("skipped", 1)],
            summary.EnumerateObject().Select(p => (p.Name, p.Value.GetInt32())));
    }

    [Fact]
    public void Writes_each_checks_status_and_reason()
    {
        using var json = JsonDocument.Parse(JsonReport.Write(Report, "0.1.0"));

        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(
            ["warning", "passed", "skipped", "errored"],
            checks.Select(c => c.GetProperty("status").GetString()));
        Assert.Equal("managed by Amazon RDS", checks[2].GetProperty("reason").GetString());
        Assert.Equal("timed out after 5 s", checks[3].GetProperty("reason").GetString());
        Assert.False(checks[1].TryGetProperty("reason", out _));
    }

    [Fact]
    public void Writes_the_message_columns_of_a_finding_as_raw_values()
    {
        using var json = JsonDocument.Parse(JsonReport.Write(Report, "0.1.0"));

        var finding = json.RootElement.GetProperty("checks")[0].GetProperty("findings")[0];
        Assert.Equal("debezium", finding.GetProperty("subject").GetString());
        Assert.Equal("warning", finding.GetProperty("severity").GetString());
        Assert.StartsWith("restart its consumer", finding.GetProperty("fix").GetString());
        var values = finding.GetProperty("values");
        Assert.Equal(["subject", "inactive_for", "retained_wal"], values.EnumerateObject().Select(p => p.Name));
        Assert.Equal(259_200.5m, values.GetProperty("inactive_for").GetDecimal());
        Assert.Equal(51_539_607_552L, values.GetProperty("retained_wal").GetInt64());
    }

    [Fact]
    public void Writes_markdown_for_a_pull_request_comment()
    {
        Assert.Equal(
            """
            ## pgcheckup · app on db.example.com

            PostgreSQL 17.6 · Amazon RDS · 1 passed · 1 warning · 1 errored · 1 skipped (wal-archiving-failing: managed by Amazon RDS)

            | Severity | Check | Finding |
            | --- | --- | --- |
            | Warning | `replication-slot-inactive` | Slot debezium for 3 days holds 48 GB |
            | Errored | `xid-wraparound` | Timed out after 5 s. |

            ### Fixes

            **`replication-slot-inactive`** · debezium

            ```
            restart its consumer, or drop the slot:
            SELECT pg_drop_replication_slot('debezium');
            ```

            """.ReplaceLineEndings("\n"),
            MarkdownReport.Write(Report));
    }

    [Fact]
    public void Escapes_pipes_and_line_breaks_in_markdown_cells()
    {
        var report = new ScanReport(Server,
        [
            new CheckResult(Check("a"), CheckStatus.Found,
                [new Finding("a", "x", Severity.Critical, "one | two\nthree", "fix", new Dictionary<string, object?>())]),
        ]);

        Assert.Contains("| **Critical** | `a` | one \\| two<br>three |", MarkdownReport.Write(report));
    }

    [Fact]
    public void Writes_only_the_summary_when_nothing_was_found()
    {
        var report = new ScanReport(Server with { Provider = null }, [new CheckResult(Check("a"), CheckStatus.Passed, [])]);

        Assert.Equal("## pgcheckup · app on db.example.com\n\nPostgreSQL 17.6 · 1 passed\n", MarkdownReport.Write(report));
    }
}
