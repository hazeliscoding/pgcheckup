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
    /// Writes the header, then each finding with its fix, then a summary that counts each check
    /// once at its worst severity.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the scan found.</param>
    /// <param name="color">Whether to color the severity words. The words are always there.</param>
    /// <remarks>Findings are ordered by severity, most severe first, then by check id.</remarks>
    public static void Write(TextWriter output, ScanReport report, bool color)
    {
        var server = report.Server;
        output.WriteLine($"pgcheckup · {server.Database} on {server.Host} · PostgreSQL {server.Version}");
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
            var label = finding.Severity.ToString().ToUpperInvariant();
            output.WriteLine($"{Paint(label, finding.Severity, color)}{new string(' ', Indent - label.Length)}{finding.CheckId}");
            WriteIndented(output, finding.Message, new string(' ', Indent), new string(' ', Indent));
            WriteIndented(output, finding.Fix, new string(' ', Indent) + FixLabel, new string(' ', Indent + FixLabel.Length));
            output.WriteLine();
        }

        output.WriteLine(Summary(report));
    }

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
        var parts = new List<string> { $"{report.Results.Count(r => r.Worst == null)} passed" };
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

        return string.Join(" · ", parts);
    }

    private static string Paint(string label, Severity severity, bool color)
    {
        if (!color)
        {
            return label;
        }

        var code = severity switch
        {
            Severity.Critical => "1;31",
            Severity.Warning => "1;33",
            _ => "1;34",
        };
        return $"\u001b[{code}m{label}\u001b[0m";
    }
}
