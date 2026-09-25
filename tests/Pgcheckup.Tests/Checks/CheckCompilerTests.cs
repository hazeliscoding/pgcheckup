using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

public class CheckCompilerTests
{
    private const string ValidFrontmatter = """
        id: sample-check
        title: Sample check
        category: wal
        severity: warning
        min_version: 14
        privileges: [pg_monitor]
        thresholds:
          min_size: 1GB
          min_age: 1h
        message: >-
          Thing {subject} is [{age} ]old
          and big.
        fix: |
          Do this:
          SELECT 1;
        """;

    private const string ValidBody = """
        ## What breaks

        Things.

        ## Fix

        Fix it.

        ## Seen in

        - [Replication slots](https://www.postgresql.org/docs/current/warm-standby.html)
        """;

    private const string ValidSql = "SELECT 'x' AS subject WHERE 1 >= @min_age AND 2 >= @min_size";

    private static readonly string[] BothFixtures = ["fixtures/fires.sql", "fixtures/healthy.sql"];

    private static string Markdown(string frontmatter = ValidFrontmatter, string body = ValidBody) =>
        $"---\n{frontmatter}\n---\n\n{body}\n";

    private static CheckCompilation Compile(
        string? markdown = null, string? sql = ValidSql, string[]? otherFiles = null, string id = "sample-check") =>
        CheckCompiler.Compile(new CheckFiles(id, markdown ?? Markdown(), sql, otherFiles ?? BothFixtures));

    private static CheckError SingleError(CheckCompilation compilation)
    {
        Assert.Null(compilation.Check);
        return Assert.Single(compilation.Errors);
    }

    [Fact]
    public void Compiles_a_valid_check()
    {
        var compilation = Compile();

        Assert.Empty(compilation.Errors);
        var check = Assert.IsType<CompiledCheck>(compilation.Check);
        Assert.Equal("sample-check", check.Id);
        Assert.Equal("Sample check", check.Title);
        Assert.Equal("wal", check.Category);
        Assert.Equal("warning", check.Severity);
        Assert.Equal(14, check.MinVersion);
        Assert.Equal(["pg_monitor"], check.Privileges);
        Assert.Empty(check.SkipOn);
        Assert.Equal("SELECT 'x' AS subject WHERE 1 >= $1 AND 2 >= $2", check.Sql);
        Assert.Equal("'Thing '<subject>' is '[<age>' ']'old and big.'", TemplateText.Describe(check.Message));
        Assert.Equal("'Do this:\nSELECT 1;'", TemplateText.Describe(check.Fix));
        Assert.StartsWith("## What breaks", check.Note);
    }

    [Fact]
    public void Orders_thresholds_by_their_parameter_position()
    {
        var check = Compile().Check!;

        Assert.Collection(
            check.Thresholds,
            t =>
            {
                Assert.Equal("min_age", t.Name);
                Assert.Equal(ThresholdKind.Duration, t.Value.Kind);
                Assert.Equal(3_600_000_000m, t.Value.Value);
            },
            t =>
            {
                Assert.Equal("min_size", t.Name);
                Assert.Equal(ThresholdKind.Bytes, t.Value.Kind);
                Assert.Equal(1_073_741_824m, t.Value.Value);
            });
    }

    [Fact]
    public void Reads_quoted_scalars_and_ignores_comments()
    {
        var frontmatter = ValidFrontmatter
            .Replace("title: Sample check", "# A comment line\ntitle: \"Sample: \\\"check\\\"\"")
            .Replace("category: wal", "category: 'wal' # trailing comment");

        var check = Compile(Markdown(frontmatter)).Check!;

        Assert.Equal("Sample: \"check\"", check.Title);
        Assert.Equal("wal", check.Category);
    }

    [Fact]
    public void Requires_frontmatter()
    {
        var error = SingleError(Compile(ValidBody));

        Assert.Equal("check.md", error.File);
        Assert.Equal(1, error.Line);
        Assert.Contains("---", error.Message);
    }

    [Fact]
    public void Reports_an_unknown_key_on_its_line()
    {
        var error = SingleError(Compile(Markdown(ValidFrontmatter + "\ncolour: blue")));

        Assert.Equal(17, error.Line);
        Assert.Contains("colour", error.Message);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("category")]
    [InlineData("severity")]
    [InlineData("min_version")]
    [InlineData("privileges")]
    public void Reports_a_missing_required_key(string key)
    {
        var frontmatter = string.Join("\n", ValidFrontmatter.Split('\n').Where(l => !l.StartsWith(key + ":", StringComparison.Ordinal)));

        Assert.Contains(key, SingleError(Compile(Markdown(frontmatter))).Message);
    }

    [Fact]
    public void Reports_a_duplicate_key()
    {
        Assert.Contains("title", SingleError(Compile(Markdown(ValidFrontmatter + "\ntitle: Again"))).Message);
    }

    [Fact]
    public void Requires_the_id_to_match_the_folder()
    {
        Assert.Contains("other-check", SingleError(Compile(id: "other-check")).Message);
    }

    [Theory]
    [InlineData("id: sample-check", "id: Sample_Check", "Sample_Check")]
    [InlineData("category: wal", "category: disks", "disks")]
    [InlineData("severity: warning", "severity: high", "high")]
    [InlineData("min_version: 14", "min_version: 9.6", "9.6")]
    [InlineData("min_version: 14", "min_version: 9", "9")]
    [InlineData("privileges: [pg_monitor]", "privileges: [superuser]", "superuser")]
    [InlineData("privileges: [pg_monitor]", "privileges: [pg_monitor]\nskip_on: [heroku]", "heroku")]
    [InlineData("min_size: 1GB", "min_size: 1 gigabyte", "1 gigabyte")]
    [InlineData("min_size: 1GB", "Min_Size: 1GB", "Min_Size")]
    [InlineData("title: Sample check", "title: [a, b]", "title")]
    public void Rejects_invalid_values(string valid, string invalid, string named)
    {
        var markdown = Markdown(ValidFrontmatter.Replace(valid, invalid));

        Assert.Contains(Compile(markdown).Errors, e => e.File == "check.md" && e.Message.Contains(named));
    }

    [Fact]
    public void Reports_a_bad_threshold_on_its_line()
    {
        var error = Assert.Single(Compile(Markdown(ValidFrontmatter.Replace("min_age: 1h", "min_age: 1 hour"))).Errors);

        Assert.Equal(10, error.Line);
    }

    [Fact]
    public void Reports_a_threshold_named_twice()
    {
        var error = SingleError(Compile(Markdown(ValidFrontmatter.Replace("  min_age: 1h", "  min_age: 1h\n  min_age: 2h"))));

        Assert.Equal(11, error.Line);
        Assert.Contains("min_age", error.Message);
    }

    [Fact]
    public void Reports_template_errors_on_the_template_line()
    {
        var error = SingleError(Compile(Markdown(ValidFrontmatter.Replace("{subject}", "{subject:gb}"))));

        Assert.Equal(12, error.Line);
        Assert.Contains("gb", error.Message);
    }

    [Theory]
    [InlineData("## What breaks", "What breaks")]
    [InlineData("## Fix", "Fix")]
    [InlineData("## Seen in", "Seen in")]
    public void Requires_every_section(string heading, string named)
    {
        var body = ValidBody.Replace(heading, "## Something else");

        Assert.Contains(named, SingleError(Compile(Markdown(body: body))).Message);
    }

    [Fact]
    public void Requires_a_link_under_seen_in()
    {
        var body = ValidBody.Replace("- [Replication slots](https://www.postgresql.org/docs/current/warm-standby.html)", "- A talk I remember");

        Assert.Contains("link", SingleError(Compile(Markdown(body: body))).Message);
    }

    [Theory]
    [InlineData("fixtures/fires.sql")]
    [InlineData("fixtures/healthy.sql")]
    public void Requires_both_fixtures(string fixture)
    {
        var error = SingleError(Compile(otherFiles: BothFixtures.Where(f => f != fixture).ToArray()));

        Assert.Equal(fixture, error.File);
    }

    [Fact]
    public void Requires_check_sql()
    {
        Assert.Equal("check.sql", SingleError(Compile(sql: null)).File);
    }

    [Fact]
    public void Reports_sql_errors_against_check_sql()
    {
        var error = SingleError(Compile(sql: ValidSql + "\n AND pg_terminate_backend(1) IS NULL"));

        Assert.Equal("check.sql", error.File);
        Assert.Equal(2, error.Line);
    }
}
