using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>What <c>pgcheckup list</c> and <c>pgcheckup explain</c> print.</summary>
public static class CatalogText
{
    private const int LabelWidth = 13;

    /// <summary>Writes one line per check, under a header: id, severity, category, minimum Postgres version and title.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="checks">The checks, in the order to list them.</param>
    public static void WriteList(TextWriter output, IReadOnlyList<CheckDefinition> checks)
    {
        var idWidth = Math.Max("check".Length, checks.Count == 0 ? 0 : checks.Max(c => c.Id.Length)) + 2;
        output.WriteLine($"{"check".PadRight(idWidth)}{"severity",-10}{"category",-10}{"postgres",-10}title");
        foreach (var check in checks)
        {
            output.WriteLine($"{check.Id.PadRight(idWidth)}{Lower(check.Severity),-10}{check.Category,-10}{check.MinVersion + "+",-10}{check.Title}");
        }
    }

    /// <summary>Writes a check's details, then its note: what breaks, how to fix it and where it was seen.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="check">The check to explain.</param>
    /// <remarks>Lines for providers and thresholds appear only when the check has some.</remarks>
    public static void WriteExplanation(TextWriter output, CheckDefinition check)
    {
        output.WriteLine($"{check.Id} · {check.Title}");
        output.WriteLine();
        WriteDetail(output, "Severity:", Lower(check.Severity));
        WriteDetail(output, "Category:", check.Category);
        WriteDetail(output, "Postgres:", $"{check.MinVersion} or later");
        WriteDetail(output, "Needs:", check.Privileges.Count == 0 ? "no extra privileges" : string.Join(" and ", check.Privileges.Select(PrivilegeName)));
        if (check.SkipOn.Count > 0)
        {
            WriteDetail(output, "Skipped on:", string.Join(", ", check.SkipOn.Select(ProviderName)));
        }

        if (check.Thresholds.Count > 0)
        {
            WriteDetail(output, "Thresholds:", string.Join(", ", check.Thresholds.Select(t => $"{t.Name} = {t.Text}")));
        }

        output.WriteLine();
        output.WriteLine(check.Note);
    }

    private static void WriteDetail(TextWriter output, string label, string value) =>
        output.WriteLine($"{label.PadRight(LabelWidth)}{value}");

    private static string Lower(Severity severity) => severity.ToString().ToLowerInvariant();

    private static string PrivilegeName(string privilege) =>
        privilege == Applicability.SelectOnSequences ? "SELECT on sequences" : privilege;

    private static string ProviderName(string id) => Provider.Known.FirstOrDefault(p => p.Id == id)?.Name ?? id;
}
