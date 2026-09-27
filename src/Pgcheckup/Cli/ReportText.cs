using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>Wording that the terminal and Markdown reports share, so they never disagree.</summary>
public static class ReportText
{
    /// <summary>
    /// Counts each check once at its worst severity, then errored checks, then names each skipped
    /// check with its reason.
    /// </summary>
    /// <param name="report">What the scan found.</param>
    /// <returns>Such as "11 passed · 1 critical · 1 warning · 1 skipped (wal-archiving-failing: managed by Amazon RDS)".</returns>
    public static string Summary(ScanReport report)
    {
        var parts = new List<string> { $"{report.Results.Count(r => r.Status == CheckStatus.Passed)} passed" };
        var critical = report.Results.Count(r => r.Worst == Severity.Critical);
        var warning = report.Results.Count(r => r.Worst == Severity.Warning);
        var info = report.Results.Count(r => r.Worst == Severity.Info);
        var errored = report.Results.Count(r => r.Status == CheckStatus.Errored);
        var skipped = report.Results.Where(r => r.Status == CheckStatus.Skipped).ToList();

        if (critical > 0)
        {
            parts.Add($"{critical} critical");
        }

        if (warning > 0)
        {
            parts.Add(warning == 1 ? "1 warning" : $"{warning} warnings");
        }

        if (info > 0)
        {
            parts.Add($"{info} info");
        }

        if (errored > 0)
        {
            parts.Add($"{errored} errored");
        }

        if (skipped.Count > 0)
        {
            parts.Add($"{skipped.Count} skipped ({string.Join(", ", skipped.Select(r => $"{r.Check.Id}: {ValueText.Printable(r.Reason ?? "")}"))})");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Every finding across all checks, most severe first, then by check id, then in the check's order.</summary>
    /// <param name="report">What the scan found.</param>
    /// <returns>The findings in report order.</returns>
    public static IEnumerable<Finding> OrderedFindings(ScanReport report) => report.Results
        .SelectMany(r => r.Findings)
        .Select((finding, order) => (finding, order))
        .OrderByDescending(f => f.finding.Severity)
        .ThenBy(f => f.finding.CheckId, StringComparer.Ordinal)
        .ThenBy(f => f.order)
        .Select(f => f.finding);

    /// <summary>The errored checks, ordered by id.</summary>
    /// <param name="report">What the scan found.</param>
    /// <returns>The errored results.</returns>
    public static IEnumerable<CheckResult> Errored(ScanReport report) =>
        report.Results.Where(r => r.Status == CheckStatus.Errored).OrderBy(r => r.Check.Id, StringComparer.Ordinal);

    /// <summary>Turns a reason such as "timed out after 5 s" into a sentence: "Timed out after 5 s."</summary>
    /// <param name="reason">A lowercase reason, or <see langword="null"/>. A server error can quote object names in it.</param>
    /// <returns>The reason capitalized, ending in a full stop, and safe to print.</returns>
    public static string Sentence(string? reason)
    {
        var text = ValueText.Printable(string.IsNullOrEmpty(reason) ? "it failed" : reason);
        return char.ToUpperInvariant(text[0]) + text[1..] + (text.EndsWith('.') ? "" : ".");
    }
}
