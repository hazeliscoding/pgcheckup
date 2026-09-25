using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pgcheckup.Checks.Generator;

public sealed class SqlResult
{
    public SqlResult(string sql, IReadOnlyList<string> parameters, IReadOnlyList<SourceError> errors)
    {
        Sql = sql;
        Parameters = parameters;
        Errors = errors;
    }

    public string Sql { get; }

    public IReadOnlyList<string> Parameters { get; }

    public IReadOnlyList<SourceError> Errors { get; }
}

public static class CheckSql
{
    // A READ ONLY transaction blocks DDL, DML, nextval and row locks, but not these. They signal
    // or reconfigure the server, take locks, consume transaction IDs or read server files.
    private static readonly HashSet<string> DeniedFunctions = new(StringComparer.Ordinal)
    {
        "nextval", "setval", "set_config", "txid_current", "pg_current_xact_id",
        "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf", "pg_rotate_logfile",
        "pg_switch_wal", "pg_promote", "pg_notify", "pg_export_snapshot",
        "pg_logical_slot_get_changes", "pg_logical_slot_get_binary_changes", "pg_replication_slot_advance",
        "pg_sync_replication_slots", "pg_log_standby_snapshot", "pg_log_backend_memory_contexts",
        "pg_import_system_collations", "pg_create_restore_point",
        "pg_backup_start", "pg_backup_stop", "pg_start_backup", "pg_stop_backup",
        "pg_read_file", "pg_read_binary_file",
    };

    private static readonly string[] DeniedPrefixes =
    [
        "pg_stat_reset", "pg_create_", "pg_drop_", "pg_copy_", "pg_advisory_", "pg_try_advisory_",
        "pg_file_", "pg_wal_replay_", "pg_replication_origin_", "dblink", "lo_",
    ];

    public static SqlResult Compile(string sql, IReadOnlyCollection<string> thresholds)
    {
        var errors = new List<SourceError>();
        var tokens = SqlTokenizer.Tokenize(sql, errors);

        var first = tokens.FirstOrDefault();
        if (first.Kind != TokenKind.Word || !(IsKeyword(first, "select") || IsKeyword(first, "with")))
        {
            errors.Add(new SourceError(tokens.Count == 0 ? 1 : SqlTokenizer.LineOf(sql, first.Start), "check.sql must be one statement that starts with SELECT or WITH."));
        }

        var parameters = new List<string>();
        var output = new StringBuilder(sql.Length);
        var copied = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case TokenKind.Semicolon when i != tokens.Count - 1:
                    errors.Add(new SourceError(SqlTokenizer.LineOf(sql, token.Start), "check.sql must be one statement."));
                    break;

                case TokenKind.Semicolon:
                    output.Append(sql, copied, token.Start - copied);
                    copied = token.Start + 1;
                    break;

                case TokenKind.Word or TokenKind.QuotedIdentifier when i + 1 < tokens.Count && tokens[i + 1].Text == "(":
                    var name = token.Text.ToLowerInvariant();
                    if (DeniedFunctions.Contains(name) || DeniedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                    {
                        errors.Add(new SourceError(SqlTokenizer.LineOf(sql, token.Start), $"check.sql calls {name}(), which has side effects that a READ ONLY transaction doesn't stop."));
                    }

                    break;

                case TokenKind.PositionalParameter:
                    errors.Add(new SourceError(SqlTokenizer.LineOf(sql, token.Start), $"check.sql uses {token.Text}. Read thresholds as @name parameters."));
                    break;

                case TokenKind.Parameter:
                    if (!thresholds.Contains(token.Text))
                    {
                        errors.Add(new SourceError(SqlTokenizer.LineOf(sql, token.Start), $"check.sql reads @{token.Text}, which isn't a threshold in check.md."));
                        break;
                    }

                    if (!parameters.Contains(token.Text))
                    {
                        parameters.Add(token.Text);
                    }

                    output.Append(sql, copied, token.Start - copied);
                    output.Append('$').Append(parameters.IndexOf(token.Text) + 1);
                    copied = token.Start + token.Length;
                    break;
            }
        }

        output.Append(sql, copied, sql.Length - copied);

        foreach (var unused in thresholds.Where(t => !parameters.Contains(t)))
        {
            errors.Add(new SourceError(1, $"Threshold {unused} is never read by check.sql."));
        }

        return new SqlResult(output.ToString(), parameters, errors);
    }

    private static bool IsKeyword(Token token, string keyword) =>
        string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);
}
