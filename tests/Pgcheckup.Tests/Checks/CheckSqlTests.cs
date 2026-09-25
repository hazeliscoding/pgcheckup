using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

public class CheckSqlTests
{
    private static SqlResult Compile(string sql, params string[] thresholds) => CheckSql.Compile(sql, thresholds);

    [Fact]
    public void Rewrites_named_thresholds_to_positional_parameters_in_order_of_first_use()
    {
        var result = Compile("SELECT 1 WHERE a >= @min_x AND b < @max_y OR c = @min_x", "max_y", "min_x");

        Assert.Empty(result.Errors);
        Assert.Equal("SELECT 1 WHERE a >= $1 AND b < $2 OR c = $1", result.Sql);
        Assert.Equal(["min_x", "max_y"], result.Parameters);
    }

    [Fact]
    public void Leaves_at_signs_alone_inside_literals_comments_and_operators()
    {
        const string sql = """
            SELECT '@x', E'\'@x', $q$ @x $q$, "@x", a @> b -- @x
            /* @x /* nested @x */ @x */
            WHERE n >= @x
            """;

        var result = Compile(sql, "x");

        Assert.Empty(result.Errors);
        Assert.Equal(sql.Replace("n >= @x", "n >= $1"), result.Sql);
    }

    [Fact]
    public void Reports_an_unknown_threshold_with_its_line()
    {
        var result = Compile("SELECT 1\nWHERE a > @nope", "x");

        Assert.Contains(result.Errors, e => e.Line == 2 && e.Message.Contains("@nope"));
    }

    [Fact]
    public void Reports_a_threshold_the_sql_never_reads()
    {
        var result = Compile("SELECT 1", "unused_limit");

        Assert.Contains(result.Errors, e => e.Message.Contains("unused_limit"));
    }

    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select 1")]
    [InlineData("-- leading comment\nWITH x AS (SELECT 1) SELECT * FROM x")]
    [InlineData("/* c */ SELECT 1;")]
    public void Accepts_one_select_or_with_statement(string sql)
    {
        Assert.Empty(Compile(sql).Errors);
    }

    [Fact]
    public void Drops_a_trailing_semicolon()
    {
        Assert.Equal("SELECT 1\n", Compile("SELECT 1;\n").Sql);
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("SET statement_timeout = 0")]
    [InlineData("(SELECT 1)")]
    [InlineData("")]
    [InlineData("-- only a comment")]
    public void Rejects_anything_but_a_select_or_with_statement(string sql)
    {
        Assert.Contains(Compile(sql).Errors, e => e.Message.Contains("SELECT or WITH"));
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1; -- trailing\nDELETE FROM t")]
    public void Rejects_more_than_one_statement(string sql)
    {
        Assert.Contains(Compile(sql).Errors, e => e.Message.Contains("one statement"));
    }

    [Theory]
    [InlineData("SELECT pg_terminate_backend(pid) FROM pg_stat_activity")]
    [InlineData("SELECT PG_CANCEL_BACKEND(1)")]
    [InlineData("SELECT pg_catalog.pg_reload_conf()")]
    [InlineData("SELECT \"pg_terminate_backend\"(1)")]
    [InlineData("SELECT nextval('s')")]
    [InlineData("SELECT set_config('x', 'y', true)")]
    [InlineData("SELECT txid_current()")]
    [InlineData("SELECT pg_current_xact_id()")]
    [InlineData("SELECT pg_advisory_lock(1)")]
    [InlineData("SELECT pg_try_advisory_xact_lock(1)")]
    [InlineData("SELECT pg_stat_reset_shared('wal')")]
    [InlineData("SELECT pg_create_physical_replication_slot('s')")]
    [InlineData("SELECT pg_drop_replication_slot('s')")]
    [InlineData("SELECT pg_logical_slot_get_changes('s', NULL, NULL)")]
    [InlineData("SELECT dblink('x', 'y')")]
    [InlineData("SELECT lo_import('/etc/passwd')")]
    [InlineData("SELECT pg_read_file('postgresql.conf')")]
    [InlineData("SELECT pg_switch_wal()")]
    [InlineData("SELECT pg_notify('c', 'x')")]
    [InlineData("SELECT pg_logical_emit_message(false, 'x', 'y')")]
    [InlineData("SELECT public.pg_stat_statements_reset()")]
    [InlineData("SELECT loread(0, 1)")]
    [InlineData("SELECT query_to_xml('SELECT pg_terminate_backend(1)', false, false, '')")]
    [InlineData("SELECT query_to_xmlschema('SELECT 1', false, false, '')")]
    [InlineData("SELECT table_to_xml('orders', false, false, '')")]
    [InlineData("SELECT cursor_to_xml('c', 1, false, false, '')")]
    [InlineData("SELECT schema_to_xml('public', false, false, '')")]
    [InlineData("SELECT database_to_xml(false, false, '')")]
    [InlineData("SELECT ts_stat('SELECT 1')")]
    [InlineData("SELECT ts_rewrite('a'::tsquery, 'SELECT 1')")]
    public void Rejects_functions_with_side_effects(string sql)
    {
        Assert.Contains(Compile(sql).Errors, e => e.Line == 1 && e.Message.Contains("side effects"));
    }

    [Theory]
    [InlineData("SELECT U&\"\0070g_terminate_backend\"(1)")]
    [InlineData("SELECT u&\"x\" FROM t")]
    public void Rejects_unicode_escaped_identifiers(string sql)
    {
        Assert.Contains(Compile(sql).Errors, e => e.Message.Contains("U&"));
    }

    [Theory]
    [InlineData("SELECT 'pg_terminate_backend'")]
    [InlineData("SELECT 1 -- pg_terminate_backend(pid)")]
    [InlineData("SELECT pg_logical_slot_peek_changes('s', NULL, NULL)")]
    [InlineData("SELECT txid_current_if_assigned()")]
    [InlineData("SELECT pg_current_xact_id_if_assigned()")]
    [InlineData("SELECT pg_last_wal_replay_lsn()")]
    [InlineData("SELECT pg_ls_waldir()")]
    public void Allows_read_only_functions_and_mentions_in_literals(string sql)
    {
        Assert.Empty(Compile(sql).Errors);
    }
}
