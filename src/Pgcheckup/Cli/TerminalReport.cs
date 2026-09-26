using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>The report <c>pgcheckup scan</c> prints to a terminal.</summary>
public static class TerminalReport
{
    private const int Indent = 10;
    private const string FixLabel = "Fix: ";

    /// <summary>Whether to color the severity words.</summary>
    /// <param name="outputRedirected">Whether the report goes to a file or pipe.</param>
    /// <param name="environment">The process's environment variables.</param>
    /// <returns>
    /// <see langword="false"/> when output is redirected, NO_COLOR is set to anything but an
    /// empty string, or TERM is dumb.
    /// </returns>
    public static bool UseColor(bool outputRedirected, IReadOnlyDictionary<string, string?> environment) =>
        !outputRedirected
        && !(environment.TryGetValue("NO_COLOR", out var noColor) && !string.IsNullOrEmpty(noColor))
        && !(environment.TryGetValue("TERM", out var term) && term == "dumb");

    /// <summary>
    /// Writes the header, then each finding with its fix, then each errored check, then a
    /// summary that counts each check once at its worst severity and names skipped checks.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the scan found.</param>
    /// <param name="color">Whether to color the status words. The words are always there.</param>
    /// <remarks>Findings are ordered by severity, most severe first, then by check id.</remarks>
    public static void Write(TextWriter output, ScanReport report, bool color)
    {
        var server = report.Server;
        var provider = server.Provider is { } managed ? $" · {managed.Name}" : "";
        output.WriteLine($"pgcheckup · {server.Database} on {server.Host} · PostgreSQL {server.Version}{provider}");
        output.WriteLine();

        var findings = report.Results
            .SelectMany(r => r.Findings)
            .Select((finding, order) => (finding, order))
            .OrderByDescending(f => f.finding.Severity)
            .ThenBy(f => f.finding.CheckId, StringComparer.Ordinal)
            .ThenBy(f => f.order)
            .Select(f => f.finding);

        foreach (var finding in findings)
        {
            WriteLabel(output, finding.Severity.ToString().ToUpperInvariant(), SeverityColor(finding.Severity), finding.CheckId, color);
            WriteIndented(output, finding.Message, new string(' ', Indent), new string(' ', Indent));
            WriteIndented(output, finding.Fix, new string(' ', Indent) + FixLabel, new string(' ', Indent + FixLabel.Length));
            output.WriteLine();
        }

        foreach (var errored in report.Results.Where(r => r.Status == CheckStatus.Errored).OrderBy(r => r.Check.Id, StringComparer.Ordinal))
        {
            WriteLabel(output, "ERRORED", ErroredColor, errored.Check.Id, color);
            WriteIndented(output, Sentence(errored.Reason ?? "it failed"), new string(' ', Indent), new string(' ', Indent));
            output.WriteLine();
        }

        output.WriteLine(Summary(report));
    }

    private static void WriteLabel(TextWriter output, string label, string colorCode, string checkId, bool color)
    {
        var painted = color ? $"\u001b[{colorCode}m{label}\u001b[0m" : label;
        output.WriteLine($"{painted}{new string(' ', Indent - label.Length)}{checkId}");
    }

    private static string Sentence(string reason) =>
        char.ToUpperInvariant(reason[0]) + reason[1..] + (reason.EndsWith('.') ? "" : ".");

    private static void WriteIndented(TextWriter output, string text, string first, string rest)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            output.WriteLine((i == 0 ? first : rest) + lines[i]);
        }
    }

    private static string Summary(ScanReport report)
    {
        var parts = new List<string> { $"{report.Results.Count(r => r.Status == CheckStatus.Passed)} passed" };
        var critical = report.Results.Count(r => r.Worst == Severity.Critical);
        var warning = report.Results.Count(r => r.Worst == Severity.Warning);
        var info = report.Results.Count(r => r.Worst == Severity.Info);
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

        var errored = report.Results.Count(r => r.Status == CheckStatus.Errored);
        if (errored > 0)
        {
            parts.Add($"{errored} errored");
        }

        var skipped = report.Results.Where(r => r.Status == CheckStatus.Skipped).ToList();
        if (skipped.Count > 0)
        {
            parts.Add($"{skipped.Count} skipped ({string.Join(", ", skipped.Select(r => $"{r.Check.Id}: {r.Reason}"))})");
        }

        return string.Join(" · ", parts);
    }

    private const string ErroredColor = "1;35";

    private static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Critical => "1;31",
        Severity.Warning => "1;33",
        _ => "1;34",
    };
}
