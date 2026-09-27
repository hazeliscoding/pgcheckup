using System.Text;
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
    /// <param name="width">
    /// The terminal's width, to wrap messages at, or <see langword="null"/> not to wrap. Fixes are
    /// never wrapped: they are SQL to copy, and a line break inside a quoted name would change it.
    /// </param>
    /// <remarks>Findings are ordered by severity, most severe first, then by check id.</remarks>
    public static void Write(TextWriter output, ScanReport report, bool color, int? width = null)
    {
        var server = report.Server;
        var provider = server.Provider is { } managed ? $" · {managed.Name}" : "";
        output.WriteLine($"pgcheckup · {ValueText.Printable(server.Database)} on {ValueText.Printable(server.Host)} · PostgreSQL {server.Version}{provider}");
        output.WriteLine();

        foreach (var finding in ReportText.OrderedFindings(report))
        {
            WriteLabel(output, finding.Severity.ToString().ToUpperInvariant(), SeverityColor(finding.Severity), finding.CheckId, color);
            WriteIndented(output, finding.Message, new string(' ', Indent), new string(' ', Indent), width);
            WriteIndented(output, finding.Fix, new string(' ', Indent) + FixLabel, new string(' ', Indent + FixLabel.Length), null);
            output.WriteLine();
        }

        foreach (var errored in ReportText.Errored(report))
        {
            WriteLabel(output, "ERRORED", ErroredColor, errored.Check.Id, color);
            WriteIndented(output, ReportText.Sentence(errored.Reason), new string(' ', Indent), new string(' ', Indent), width);
            output.WriteLine();
        }

        output.WriteLine(ReportText.Summary(report));
    }

    private static void WriteLabel(TextWriter output, string label, string colorCode, string checkId, bool color)
    {
        var painted = color ? $"\u001b[{colorCode}m{label}\u001b[0m" : label;
        output.WriteLine($"{painted}{new string(' ', Indent - label.Length)}{checkId}");
    }

    private static void WriteIndented(TextWriter output, string text, string first, string rest, int? width)
    {
        var lines = text.Split('\n').SelectMany(line => width is { } w ? Wrap(line, w - rest.Length) : [line]).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            output.WriteLine((i == 0 ? first : rest) + lines[i]);
        }
    }

    // Breaks at spaces. A word longer than the line stays whole, and a very narrow terminal gets
    // no wrapping rather than a column of single words.
    private static IEnumerable<string> Wrap(string line, int available)
    {
        if (available < 20 || line.Length <= available)
        {
            yield return line;
            yield break;
        }

        var current = new StringBuilder();
        foreach (var word in line.Split(' '))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > available)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        yield return current.ToString();
    }

    private const string ErroredColor = "1;35";

    private static string SeverityColor(Severity severity) => severity switch
    {
        Severity.Critical => "1;31",
        Severity.Warning => "1;33",
        _ => "1;34",
    };
}
