using System.Collections.Generic;

namespace Pgcheckup.Checks.Generator;

/// <summary>A problem found in one file of a check, with the line it is on.</summary>
public sealed class SourceError
{
    /// <summary>Creates an error for a line of the file being read.</summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="message">What is wrong, in a sentence a check author can act on.</param>
    public SourceError(int line, string message)
    {
        Line = line;
        Message = message;
    }

    /// <summary>The 1-based line number the error is on.</summary>
    public int Line { get; }

    /// <summary>What is wrong, in a sentence a check author can act on.</summary>
    public string Message { get; }

    /// <inheritdoc/>
    public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>The kinds of token <see cref="SqlTokenizer"/> tells apart.</summary>
public enum TokenKind
{
    /// <summary>A keyword or unquoted identifier, such as <c>SELECT</c> or <c>pg_stat_activity</c>.</summary>
    Word,

    /// <summary>A double-quoted identifier. <see cref="Token.Text"/> holds the name without quotes.</summary>
    QuotedIdentifier,

    /// <summary>A threshold reference such as <c>@min_age</c>. <see cref="Token.Text"/> holds the name without <c>@</c>.</summary>
    Parameter,

    /// <summary>A positional parameter such as <c>$1</c>, which check.sql must not use.</summary>
    PositionalParameter,

    /// <summary>A <c>;</c> outside any literal or comment.</summary>
    Semicolon,

    /// <summary>Any other single character: operators, punctuation and digits.</summary>
    Other,
}

/// <summary>One token of a SQL text, with its position in that text.</summary>
public readonly struct Token
{
    /// <summary>Creates a token.</summary>
    /// <param name="kind">What kind of token it is.</param>
    /// <param name="start">The 0-based offset of its first character in the SQL text.</param>
    /// <param name="length">How many characters of the SQL text it covers.</param>
    /// <param name="text">Its text, unquoted for identifiers and without <c>@</c> for parameters.</param>
    public Token(TokenKind kind, int start, int length, string text)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Text = text;
    }

    /// <summary>What kind of token it is.</summary>
    public TokenKind Kind { get; }

    /// <summary>The 0-based offset of its first character in the SQL text.</summary>
    public int Start { get; }

    /// <summary>How many characters of the SQL text it covers, including quotes or <c>@</c>.</summary>
    public int Length { get; }

    /// <summary>Its text, unquoted for identifiers and without <c>@</c> for parameters.</summary>
    public string Text { get; }
}

/// <summary>
/// Just enough of Postgres's lexer to tell code from comments, string literals and quoted
/// identifiers, so that checks on statements and function names can't be fooled by either.
/// </summary>
public static class SqlTokenizer
{
    /// <summary>Splits SQL into tokens, skipping whitespace, comments and string literals.</summary>
    /// <param name="sql">The SQL text, such as a check.sql or a fixture.</param>
    /// <param name="errors">Receives a <see cref="SourceError"/> for each comment, string or identifier that is never closed.</param>
    /// <returns>
    /// The tokens in order. String literals (standard, <c>E''</c> and dollar-quoted) and comments
    /// produce no tokens.
    /// </returns>
    public static List<Token> Tokenize(string sql, List<SourceError> errors)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            var start = i;

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && next == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && next == '*')
            {
                i = SkipBlockComment(sql, i, errors);
            }
            else if (c == '\'')
            {
                var escapes = tokens.Count > 0 && IsEscapeStringPrefix(sql, tokens[tokens.Count - 1]);
                i = SkipQuoted(sql, i, '\'', escapes, errors, "string literal");
            }
            else if (c == '"')
            {
                i = SkipQuoted(sql, i, '"', false, errors, "quoted identifier");
                var content = sql.Substring(start + 1, System.Math.Max(0, i - start - 2)).Replace("\"\"", "\"");
                tokens.Add(new Token(TokenKind.QuotedIdentifier, start, i - start, content));
            }
            else if (c == '$' && char.IsDigit(next))
            {
                i++;
                while (i < sql.Length && char.IsDigit(sql[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.PositionalParameter, start, i - start, sql.Substring(start, i - start)));
            }
            else if (c == '$' && TryReadDollarTag(sql, i, out var tag))
            {
                var end = sql.IndexOf(tag, i + tag.Length, System.StringComparison.Ordinal);
                if (end < 0)
                {
                    errors.Add(new SourceError(LineOf(sql, start), $"The {tag} string is never closed."));
                    i = sql.Length;
                }
                else
                {
                    i = end + tag.Length;
                }
            }
            else if (IsIdentifierStart(c))
            {
                while (i < sql.Length && IsIdentifierPart(sql[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Word, start, i - start, sql.Substring(start, i - start)));
            }
            else if (c == '@' && IsIdentifierStart(next))
            {
                i++;
                while (i < sql.Length && IsIdentifierPart(sql[i]) && sql[i] != '$')
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Parameter, start, i - start, sql.Substring(start + 1, i - start - 1)));
            }
            else if (c == ';')
            {
                i++;
                tokens.Add(new Token(TokenKind.Semicolon, start, 1, ";"));
            }
            else
            {
                i++;
                tokens.Add(new Token(TokenKind.Other, start, 1, c.ToString()));
            }
        }

        return tokens;
    }

    private static bool IsEscapeStringPrefix(string sql, Token previous) =>
        previous.Kind == TokenKind.Word
        && previous.Length == 1
        && (previous.Text == "E" || previous.Text == "e")
        && previous.Start + 1 < sql.Length
        && sql[previous.Start + 1] == '\'';

    private static int SkipBlockComment(string sql, int i, List<SourceError> errors)
    {
        var start = i;
        var depth = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                depth++;
                i += 2;
            }
            else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                {
                    return i;
                }
            }
            else
            {
                i++;
            }
        }

        errors.Add(new SourceError(LineOf(sql, start), "A /* comment is never closed."));
        return i;
    }

    private static int SkipQuoted(string sql, int i, char quote, bool backslashEscapes, List<SourceError> errors, string what)
    {
        var start = i;
        i++;
        while (i < sql.Length)
        {
            if (backslashEscapes && sql[i] == '\\')
            {
                i += 2;
            }
            else if (sql[i] == quote && i + 1 < sql.Length && sql[i + 1] == quote)
            {
                i += 2;
            }
            else if (sql[i] == quote)
            {
                return i + 1;
            }
            else
            {
                i++;
            }
        }

        errors.Add(new SourceError(LineOf(sql, start), $"A {what} is never closed."));
        return sql.Length;
    }

    private static bool TryReadDollarTag(string sql, int i, out string tag)
    {
        var j = i + 1;
        if (j < sql.Length && IsIdentifierStart(sql[j]))
        {
            while (j < sql.Length && IsIdentifierPart(sql[j]) && sql[j] != '$')
            {
                j++;
            }
        }

        if (j < sql.Length && sql[j] == '$')
        {
            tag = sql.Substring(i, j - i + 1);
            return true;
        }

        tag = "";
        return false;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c > 127;

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsDigit(c) || c == '$';

    /// <summary>The 1-based line that a 0-based offset falls on.</summary>
    internal static int LineOf(string text, int position)
    {
        var line = 1;
        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
