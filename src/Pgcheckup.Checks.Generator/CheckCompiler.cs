using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pgcheckup.Checks.Generator;

public sealed class CheckFiles(string id, string? checkMd, string? checkSql, IReadOnlyCollection<string> otherFiles)
{
    public string Id { get; } = id;

    public string? CheckMd { get; } = checkMd;

    public string? CheckSql { get; } = checkSql;

    // Paths relative to the check's folder, with forward slashes.
    public IReadOnlyCollection<string> OtherFiles { get; } = otherFiles;
}

public sealed class CheckError(string file, int line, string message)
{
    public string File { get; } = file;

    public int Line { get; } = line;

    public string Message { get; } = message;

    public override string ToString() => $"{File}({Line}): {Message}";
}

public sealed class CompiledThreshold(string name, ThresholdValue value)
{
    public string Name { get; } = name;

    public ThresholdValue Value { get; } = value;
}

public sealed class CompiledCheck
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Category { get; set; } = "";

    public string Severity { get; set; } = "";

    public int MinVersion { get; set; }

    public IReadOnlyList<string> Privileges { get; set; } = [];

    public IReadOnlyList<string> SkipOn { get; set; } = [];

    // In the order of their $n parameters.
    public IReadOnlyList<CompiledThreshold> Thresholds { get; set; } = [];

    public string Sql { get; set; } = "";

    public IReadOnlyList<TemplatePart> Message { get; set; } = [];

    public IReadOnlyList<TemplatePart> Fix { get; set; } = [];

    public string Note { get; set; } = "";
}

public sealed class CheckCompilation(CompiledCheck? check, IReadOnlyList<CheckError> errors)
{
    public CompiledCheck? Check { get; } = check;

    public IReadOnlyList<CheckError> Errors { get; } = errors;
}

public static class CheckCompiler
{
    public const string CheckMd = "check.md";
    public const string CheckSqlFile = "check.sql";
    public const string FiresFixture = "fixtures/fires.sql";
    public const string HealthyFixture = "fixtures/healthy.sql";

    public static readonly string[] Categories = ["ids", "cleanup", "wal", "capacity"];
    public static readonly string[] Severities = ["critical", "warning", "info"];
    public static readonly string[] Privileges = ["pg_monitor", "pg_read_all_settings", "pg_read_all_stats", "pg_stat_scan_tables"];
    public static readonly string[] Providers = ["rds", "aurora", "cloudsql", "azure", "supabase", "neon"];
    public static readonly string[] Sections = ["What breaks", "Fix", "Seen in"];

    private static readonly string[] RequiredKeys = ["id", "title", "category", "severity", "min_version", "privileges", "message", "fix"];
    private static readonly string[] OptionalKeys = ["skip_on", "thresholds"];

    private static readonly Regex KebabCase = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    public static CheckCompilation Compile(CheckFiles files)
    {
        var errors = new List<CheckError>();
        if (files.CheckMd == null)
        {
            errors.Add(new CheckError(CheckMd, 1, $"The check folder {files.Id} has no check.md."));
            return new CheckCompilation(null, errors);
        }

        var document = Frontmatter.Parse(files.CheckMd);
        errors.AddRange(document.Errors.Select(e => new CheckError(CheckMd, e.Line, e.Message)));
        if (document.Entries.Count == 0)
        {
            return new CheckCompilation(null, errors);
        }

        var check = new CompiledCheck { Note = document.Body };
        var entries = document.Entries.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in document.Entries.Where(e => !RequiredKeys.Contains(e.Key) && !OptionalKeys.Contains(e.Key)))
        {
            errors.Add(new CheckError(CheckMd, entry.Line, $"Unknown key {entry.Key}. Use {string.Join(", ", RequiredKeys.Concat(OptionalKeys))}."));
        }

        foreach (var missing in RequiredKeys.Where(k => !entries.ContainsKey(k)))
        {
            errors.Add(new CheckError(CheckMd, 1, $"The frontmatter has no {missing}."));
        }

        string? Scalar(string key)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (entry.Kind != EntryKind.Scalar || entry.Scalar.Length == 0)
            {
                errors.Add(new CheckError(CheckMd, entry.Line, $"{key} must be a single value."));
                return null;
            }

            return entry.Scalar;
        }

        void OneOf(string key, string? value, string[] allowed, System.Action<string> set)
        {
            if (value == null)
            {
                return;
            }

            if (allowed.Contains(value))
            {
                set(value);
            }
            else
            {
                errors.Add(new CheckError(CheckMd, entries[key].Line, $"{key} is {value}. Use {string.Join(", ", allowed)}."));
            }
        }

        IReadOnlyList<string> ListOf(string key, string[] allowed)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return [];
            }

            if (entry.Kind != EntryKind.List)
            {
                errors.Add(new CheckError(CheckMd, entry.Line, $"{key} must be a list, such as [{allowed[0]}], or []."));
                return [];
            }

            foreach (var unknown in entry.Items.Where(i => !allowed.Contains(i)))
            {
                errors.Add(new CheckError(CheckMd, entry.Line, $"{key} includes {unknown}. Use {string.Join(", ", allowed)}."));
            }

            return entry.Items;
        }

        IReadOnlyList<TemplatePart> Template(string key)
        {
            var text = Scalar(key);
            if (text == null)
            {
                return [];
            }

            var parts = TemplateParser.Parse(text, out var templateErrors);
            errors.AddRange(templateErrors.Select(e => new CheckError(CheckMd, entries[key].ValueLine, $"{key}: {e}")));
            return parts;
        }

        var id = Scalar("id");
        if (id != null && !KebabCase.IsMatch(id))
        {
            errors.Add(new CheckError(CheckMd, entries["id"].Line, $"The id {id} must be kebab-case, such as replication-slot-inactive."));
        }
        else if (id != null && id != files.Id)
        {
            errors.Add(new CheckError(CheckMd, entries["id"].Line, $"The id {id} must match the folder name, {files.Id}."));
        }
        else if (id != null)
        {
            check.Id = id;
        }

        check.Title = Scalar("title") ?? "";
        OneOf("category", Scalar("category"), Categories, v => check.Category = v);
        OneOf("severity", Scalar("severity"), Severities, v => check.Severity = v);

        var minVersion = Scalar("min_version");
        if (minVersion != null)
        {
            if (int.TryParse(minVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var major) && major >= 10)
            {
                check.MinVersion = major;
            }
            else
            {
                errors.Add(new CheckError(CheckMd, entries["min_version"].Line, $"min_version is {minVersion}. Use a Postgres major version, 10 or later, such as 14."));
            }
        }

        check.Privileges = ListOf("privileges", Privileges);
        check.SkipOn = ListOf("skip_on", Providers);
        check.Message = Template("message");
        check.Fix = Template("fix");

        var thresholds = new List<CompiledThreshold>();
        var thresholdNames = new List<string>();
        if (entries.TryGetValue("thresholds", out var thresholdEntry))
        {
            if (thresholdEntry.Kind != EntryKind.Map)
            {
                errors.Add(new CheckError(CheckMd, thresholdEntry.Line, "thresholds must be a list of `name: value` lines, indented under it."));
            }

            foreach (var (name, text, line) in thresholdEntry.Map)
            {
                if (thresholdNames.Contains(name))
                {
                    errors.Add(new CheckError(CheckMd, line, $"The threshold {name} appears more than once."));
                    continue;
                }

                thresholdNames.Add(name);
                if (!SnakeCase.IsMatch(name))
                {
                    errors.Add(new CheckError(CheckMd, line, $"The threshold name {name} must be snake_case, such as min_retained_wal."));
                }
                else if (!ThresholdValue.TryParse(text, out var value, out var error))
                {
                    errors.Add(new CheckError(CheckMd, line, $"{name}: {error}"));
                }
                else
                {
                    thresholds.Add(new CompiledThreshold(name, value));
                }
            }
        }

        CheckBody(document.Body, BodyStartLine(files.CheckMd), errors);

        if (files.CheckSql == null)
        {
            errors.Add(new CheckError(CheckSqlFile, 1, $"The check folder {files.Id} has no check.sql."));
        }
        else
        {
            // Invalid thresholds were reported above; naming them all here avoids a second error.
            var sql = CheckSql.Compile(files.CheckSql, thresholdNames);
            errors.AddRange(sql.Errors.Select(e => new CheckError(CheckSqlFile, e.Line, e.Message)));
            check.Sql = sql.Sql;
            check.Thresholds = sql.Parameters.SelectMany(p => thresholds.Where(t => t.Name == p)).ToList();
        }

        foreach (var fixture in new[] { FiresFixture, HealthyFixture }.Where(f => !files.OtherFiles.Contains(f)))
        {
            errors.Add(new CheckError(fixture, 1, $"The check folder {files.Id} has no {fixture}. Every check needs one that fires and one that stays quiet."));
        }

        return new CheckCompilation(errors.Count == 0 ? check : null, errors);
    }

    private static void CheckBody(string body, int bodyStartLine, List<CheckError> errors)
    {
        var lines = body.Split('\n');
        foreach (var section in Sections)
        {
            var heading = System.Array.FindIndex(lines, l => l.TrimEnd() == "## " + section);
            if (heading < 0)
            {
                errors.Add(new CheckError(CheckMd, bodyStartLine, $"check.md has no ## {section} section."));
                continue;
            }

            if (section == "Seen in")
            {
                var content = lines.Skip(heading + 1).TakeWhile(l => !l.StartsWith("## ", System.StringComparison.Ordinal));
                if (!content.Any(l => l.Contains("https://")))
                {
                    errors.Add(new CheckError(CheckMd, bodyStartLine + heading, "## Seen in needs at least one https:// link to a public incident or the Postgres docs."));
                }
            }
        }
    }

    private static int BodyStartLine(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var close = System.Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
        var first = System.Array.FindIndex(lines, close + 1, l => l.Trim().Length > 0);
        return first < 0 ? close + 1 : first + 1;
    }
}
