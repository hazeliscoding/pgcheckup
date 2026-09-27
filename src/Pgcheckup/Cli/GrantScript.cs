using System.Text;
using System.Text.RegularExpressions;

namespace Pgcheckup.Cli;

/// <summary>The SQL that <c>pgcheckup grant</c> prints for a least-privilege checkup role.</summary>
public static partial class GrantScript
{
    // Reserved words can't be role or database names without quotes (PostgreSQL docs, appendix C).
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric", "authorization",
        "binary", "both", "case", "cast", "check", "collate", "collation", "column", "concurrently",
        "constraint", "create", "cross", "current_catalog", "current_date", "current_role",
        "current_schema", "current_time", "current_timestamp", "current_user", "default", "deferrable",
        "desc", "distinct", "do", "else", "end", "except", "false", "fetch", "for", "foreign", "freeze",
        "from", "full", "grant", "group", "having", "ilike", "in", "initially", "inner", "intersect",
        "into", "is", "isnull", "join", "lateral", "leading", "left", "like", "limit", "localtime",
        "localtimestamp", "natural", "not", "notnull", "null", "offset", "on", "only", "or", "order",
        "outer", "overlaps", "placing", "primary", "references", "returning", "right", "select",
        "session_user", "similar", "some", "symmetric", "system_user", "table", "tablesample", "then",
        "to", "trailing", "true", "union", "unique", "user", "using", "variadic", "verbose", "when",
        "where", "window", "with",
    };

    /// <summary>Builds the SQL. It is printed for a person to review and run, never run by pgcheckup.</summary>
    /// <param name="role">The role to create, quoted when the name needs it.</param>
    /// <param name="database">The database the role may connect to, quoted when the name needs it.</param>
    /// <param name="owner">
    /// The role that owns the application's tables. Default privileges only cover sequences that
    /// this role creates later.
    /// </param>
    /// <returns>
    /// SQL that creates the role with <c>pg_monitor</c>, <c>CONNECT</c> and a read-only default, then
    /// the sequence grants that only <c>integer-exhaustion</c> needs.
    /// </returns>
    /// <exception cref="ArgumentException">A name contains a control character.</exception>
    public static string Build(string role, string database, string owner)
    {
        var r = Identifier(role);
        var d = Identifier(database);
        var o = Identifier(owner);
        var sql = new StringBuilder();
        sql.Append("-- A least-privilege role for pgcheckup. Review it, then run it as a superuser\n");
        sql.Append("-- (rds_superuser on Amazon RDS, cloudsqlsuperuser on Cloud SQL).\n");
        sql.Append($"CREATE ROLE {r} LOGIN;\n");
        sql.Append($"-- Set its password with \\password {r}, or use your provider's IAM login.\n");
        sql.Append($"GRANT pg_monitor TO {r};\n");
        sql.Append($"GRANT CONNECT ON DATABASE {d} TO {r};\n");
        sql.Append("-- Every session of this role is read-only, even outside pgcheckup.\n");
        sql.Append($"ALTER ROLE {r} SET default_transaction_read_only = on;\n");
        sql.Append('\n');
        sql.Append("-- Only integer-exhaustion needs these. They show sequence counters, never table rows.\n");
        sql.Append($"-- Repeat them for each schema with sequences. The second covers sequences that {o},\n");
        sql.Append("-- the role that owns your tables, creates later; name another with --owner.\n");
        sql.Append($"GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO {r};\n");
        sql.Append($"ALTER DEFAULT PRIVILEGES FOR ROLE {o} IN SCHEMA public GRANT SELECT ON SEQUENCES TO {r};\n");
        return sql.ToString();
    }

    // A line break in a name would end the -- comment it appears in, and run the rest as SQL.
    private static string Identifier(string name) =>
        name.Any(char.IsControl) ? throw new ArgumentException("A role or database name can't contain control characters.")
        : PlainIdentifier().IsMatch(name) && !Reserved.Contains(name) ? name
        : $"\"{name.Replace("\"", "\"\"")}\"";

    [GeneratedRegex("^[a-z_][a-z0-9_$]*$")]
    private static partial Regex PlainIdentifier();
}
