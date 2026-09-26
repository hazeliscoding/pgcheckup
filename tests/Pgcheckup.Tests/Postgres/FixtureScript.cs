using System.Text.RegularExpressions;
using Pgcheckup.Checks;
using Pgcheckup.Checks.Generator;
using RuntimeThresholdKind = Pgcheckup.Checks.ThresholdKind;

namespace Pgcheckup.Tests.Postgres;

/// <summary>One statement of a fixture.</summary>
/// <param name="Sql">The statement, with any comments before it.</param>
/// <param name="MayFail">Whether an <c>-- expect error</c> line comes before it, so it must fail.</param>
internal sealed record FixtureStatement(string Sql, bool MayFail);

/// <summary>
/// A check's fixture: setup statements, plus directives. <c>-- threshold name = value</c> lowers a
/// threshold when the real condition can't be reproduced at full scale. <c>-- server name = value</c>
/// starts Postgres with a setting that needs a restart. <c>-- expect error</c> means the next
/// statement must fail, as a failed <c>CREATE INDEX CONCURRENTLY</c> does.
/// </summary>
internal sealed partial class FixtureScript
{
    private FixtureScript(
        IReadOnlyList<FixtureStatement> statements,
        IReadOnlyDictionary<string, string> thresholds,
        IReadOnlyDictionary<string, string> serverSettings)
    {
        Statements = statements;
        Thresholds = thresholds;
        ServerSettings = serverSettings;
    }

    public IReadOnlyList<FixtureStatement> Statements { get; }

    public IReadOnlyDictionary<string, string> Thresholds { get; }

    public IReadOnlyDictionary<string, string> ServerSettings { get; }

    public static FixtureScript Load(string checkId, string fixture) =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "checks", checkId, "fixtures", fixture + ".sql")));

    public static FixtureScript Parse(string text)
    {
        var thresholds = ThresholdLine().Matches(text).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var serverSettings = ServerLine().Matches(text).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());

        var errors = new List<SourceError>();
        var tokens = SqlTokenizer.Tokenize(text, errors);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"The fixture doesn't parse: {string.Join("; ", errors)}");
        }

        var statements = new List<FixtureStatement>();
        var start = 0;
        foreach (var end in tokens.Where(t => t.Kind == TokenKind.Semicolon).Select(t => t.Start).Append(text.Length))
        {
            if (tokens.Any(t => t.Start >= start && t.Start < end && t.Kind != TokenKind.Semicolon))
            {
                var sql = text[start..end].Trim();
                statements.Add(new FixtureStatement(sql, ExpectErrorLine().IsMatch(sql)));
            }

            start = end + 1;
        }

        return new FixtureScript(statements, thresholds, serverSettings);
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

    [GeneratedRegex(@"^--\s*server\s+([a-z][a-z0-9_.]*)\s*=\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex ServerLine();

    [GeneratedRegex(@"^--\s*expect error\s*$", RegexOptions.Multiline)]
    private static partial Regex ExpectErrorLine();
}
