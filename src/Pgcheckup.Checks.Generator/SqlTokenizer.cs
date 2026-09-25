using System.Collections.Generic;

namespace Pgcheckup.Checks.Generator;

public sealed class SourceError
{
    public SourceError(int line, string message)
    {
        Line = line;
        Message = message;
    }

    public int Line { get; }

    public string Message { get; }

    public override string ToString() => $"line {Line}: {Message}";
}

public enum TokenKind
{
    Word,
    QuotedIdentifier,
    Parameter,
    PositionalParameter,
    Semicolon,
    Other,
}

public readonly struct Token
{
    public Token(TokenKind kind, int start, int length, string text)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Text = text;
    }

    public TokenKind Kind { get; }

    public int Start { get; }

    public int Length { get; }

    public string Text { get; }
}

// Just enough of Postgres's lexer to tell code from comments, string literals and quoted
// identifiers, so that checks on statements and function names can't be fooled by either.
public static class SqlTokenizer
{
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
