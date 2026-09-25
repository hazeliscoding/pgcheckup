using System.Text.RegularExpressions;
using Pgcheckup.Checks;
using Pgcheckup.Checks.Generator;
using RuntimeThresholdKind = Pgcheckup.Checks.ThresholdKind;

namespace Pgcheckup.Tests.Postgres;

// A check's fixture: setup statements, plus `-- threshold name = value` lines that lower a
// threshold when the real condition can't be reproduced at full scale.
internal sealed partial class FixtureScript
{
    private FixtureScript(IReadOnlyList<string> statements, IReadOnlyDictionary<string, string> thresholds)
    {
        Statements = statements;
        Thresholds = thresholds;
    }

    public IReadOnlyList<string> Statements { get; }

    public IReadOnlyDictionary<string, string> Thresholds { get; }

    public static FixtureScript Load(string checkId, string fixture) =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "checks", checkId, "fixtures", fixture + ".sql")));

    public static FixtureScript Parse(string text)
    {
        var thresholds = ThresholdLine().Matches(text).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());

        var errors = new List<SourceError>();
        var tokens = SqlTokenizer.Tokenize(text, errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"The fixture doesn't parse: {string.Join("; ", errors)}");
        }

        var statements = new List<string>();
        var start = 0;
        foreach (var end in tokens.Where(t => t.Kind == TokenKind.Semicolon).Select(t => t.Start).Append(text.Length))
        {
            if (tokens.Any(t => t.Start >= start && t.Start < end && t.Kind != TokenKind.Semicolon))
            {
                statements.Add(text[start..end].Trim());
            }

            start = end + 1;
        }

        return new FixtureScript(statements, thresholds);
    }

    public CheckDefinition Apply(CheckDefinition check)
    {
        var unknown = Thresholds.Keys.Except(check.Thresholds.Select(t => t.Name)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"The fixture lowers {string.Join(", ", unknown)}, which {check.Id} doesn't have.");
        }

        return check with
        {
            Thresholds = check.Thresholds.Select(t =>
            {
                if (!Thresholds.TryGetValue(t.Name, out var text))
                {
                    return t;
                }

                if (!ThresholdValue.TryParse(text, out var value, out var error) || value.Kind.ToString() != t.Kind.ToString())
                {
                    throw new InvalidOperationException($"The fixture sets {t.Name} to {text}, which isn't a {t.Kind} value. {error}");
                }

                return t with { Value = value.Value, Text = text };
            }).ToList(),
        };
    }

    [GeneratedRegex(@"^--\s*threshold\s+([a-z][a-z0-9_]*)\s*=\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex ThresholdLine();
}
