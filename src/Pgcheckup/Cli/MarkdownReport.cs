using System.Text;
using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>The <c>--format markdown</c> report, shaped for a pull request comment.</summary>
public static class MarkdownReport
{
    /// <summary>
    /// Writes a heading, a summary line, a table of findings and errored checks, then each fix in
    /// a code block. With nothing found or errored, only the heading and summary.
    /// </summary>
    /// <param name="report">What the scan found.</param>
    /// <returns>The Markdown text.</returns>
    public static string Write(ScanReport report)
    {
        var server = report.Server;
        var markdown = new StringBuilder();
        Line(markdown, $"## pgcheckup · {server.Database} on {server.Host}");
        Line(markdown);
        var provider = server.Provider is { } managed ? $" · {managed.Name}" : "";
        Line(markdown, $"PostgreSQL {server.Version}{provider} · {ReportText.Summary(report)}");

        var findings = ReportText.OrderedFindings(report).ToList();
        var errored = ReportText.Errored(report).ToList();
        if (findings.Count == 0 && errored.Count == 0)
        {
            return markdown.ToString();
        }

        Line(markdown);
        Line(markdown, "| Severity | Check | Finding |");
        Line(markdown, "| --- | --- | --- |");
        foreach (var finding in findings)
        {
            var severity = finding.Severity.ToString();
            Line(markdown, $"| {(finding.Severity == Severity.Critical ? $"**{severity}**" : severity)} | `{finding.CheckId}` | {Cell(finding.Message)} |");
        }

        foreach (var result in errored)
        {
            Line(markdown, $"| Errored | `{result.Check.Id}` | {Cell(ReportText.Sentence(result.Reason))} |");
        }

        if (findings.Count > 0)
        {
            Line(markdown);
            Line(markdown, "### Fixes");
            foreach (var finding in findings)
            {
                Line(markdown);
                Line(markdown, $"**`{finding.CheckId}`** · {Cell(finding.Subject)}");
                Line(markdown);
                Line(markdown, "```");
                Line(markdown, finding.Fix);
                Line(markdown, "```");
            }
        }

        return markdown.ToString();
    }

    // Written with \n on every platform, so the text is the same wherever it is produced.
    private static void Line(StringBuilder markdown, string text = "") => markdown.Append(text).Append('\n');

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", "<br>");
}
