namespace Pgcheckup.Tests.Postgres;

public class FixtureScriptTests
{
    [Fact]
    public void Splits_statements_on_semicolons_outside_literals()
    {
        var script = FixtureScript.Parse("""
            CREATE TABLE t (n int);
            DO $$ BEGIN PERFORM 1; END $$;
            -- a comment; with a semicolon
            SELECT 'a;b';
            """);

        Assert.Equal(
            ["CREATE TABLE t (n int)", "DO $$ BEGIN PERFORM 1; END $$", "-- a comment; with a semicolon\nSELECT 'a;b'"],
            script.Statements.Select(s => s.Sql));
    }

    [Fact]
    public void Reads_threshold_and_server_directives()
    {
        var script = FixtureScript.Parse("""
            -- threshold min_age = 0s
            -- server max_prepared_transactions = 5
            -- server archive_mode = on
            SELECT 1;
            """);

        Assert.Equal(new Dictionary<string, string> { ["min_age"] = "0s" }, script.Thresholds);
        Assert.Equal(
            new Dictionary<string, string> { ["max_prepared_transactions"] = "5", ["archive_mode"] = "on" },
            script.ServerSettings);
    }

    [Fact]
    public void Lets_only_the_statement_after_expect_error_fail()
    {
        var script = FixtureScript.Parse("""
            CREATE TABLE t (n int);
            -- expect error
            CREATE UNIQUE INDEX CONCURRENTLY t_n ON t (n);
            SELECT 1;
            """);

        Assert.Equal([false, true, false], script.Statements.Select(s => s.MayFail));
    }
}
