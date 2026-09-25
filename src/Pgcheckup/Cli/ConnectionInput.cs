using System.Globalization;
using System.Text;
using Npgsql;

namespace Pgcheckup.Cli;

// Errors say which part is wrong without repeating any of the input, which may hold a password.
public sealed class ConnectionInputException(string message) : Exception(message);

// Reads connections the way psql does: a postgres:// URL, a libpq key-value string, or nothing,
// with PGHOST, PGPORT, PGDATABASE and PGSSLMODE filling in what the input leaves out. Npgsql
// reads PGUSER, PGPASSWORD, PGPASSFILE and ~/.pgpass itself.
public static class ConnectionInput
{
    private static readonly (string Variable, string Key)[] Environment =
    [
        ("PGHOST", "host"),
        ("PGPORT", "port"),
        ("PGDATABASE", "dbname"),
        ("PGSSLMODE", "sslmode"),
    ];

    private static readonly Dictionary<string, SslMode> SslModes = new(StringComparer.Ordinal)
    {
        ["disable"] = SslMode.Disable,
        ["allow"] = SslMode.Allow,
        ["prefer"] = SslMode.Prefer,
        ["require"] = SslMode.Require,
        ["verify-ca"] = SslMode.VerifyCA,
        ["verify-full"] = SslMode.VerifyFull,
    };

    private static readonly Dictionary<string, Action<NpgsqlConnectionStringBuilder, string>> Keywords = new(StringComparer.Ordinal)
    {
        ["host"] = (b, v) => b.Host = v,
        ["port"] = (b, v) => b.Port = Number("port", v),
        ["dbname"] = (b, v) => b.Database = v,
        ["user"] = (b, v) => b.Username = v,
        ["password"] = (b, v) => b.Password = v,
        ["passfile"] = (b, v) => b.Passfile = v,
        ["sslmode"] = (b, v) => b.SslMode = SslModes.TryGetValue(v, out var mode)
            ? mode
            : throw new ConnectionInputException($"sslmode must be one of {string.Join(", ", SslModes.Keys)}."),
        ["sslrootcert"] = (b, v) => b.RootCertificate = v,
        ["sslcert"] = (b, v) => b.SslCertificate = v,
        ["sslkey"] = (b, v) => b.SslKey = v,
        ["sslpassword"] = (b, v) => b.SslPassword = v,
        ["connect_timeout"] = (b, v) => b.Timeout = Number("connect_timeout", v),
        ["target_session_attrs"] = (b, v) => b.TargetSessionAttributes = v,
        ["options"] = (b, v) => b.Options = v,

        // pgcheckup always connects as application_name=pgcheckup.
        ["application_name"] = (_, _) => { },
        ["fallback_application_name"] = (_, _) => { },
    };

    public static NpgsqlConnectionStringBuilder Parse(string? input, IReadOnlyDictionary<string, string?> environment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(input))
        {
            input = input.Trim();
            if (input.StartsWith("postgres://", StringComparison.Ordinal) || input.StartsWith("postgresql://", StringComparison.Ordinal))
            {
                ReadUrl(input, values);
            }
            else if (input.Contains("://", StringComparison.Ordinal))
            {
                throw new ConnectionInputException("A connection URL must start with postgres:// or postgresql://.");
            }
            else
            {
                ReadKeyValues(input, values);
            }
        }

        foreach (var (variable, key) in Environment)
        {
            if (!values.ContainsKey(key) && environment.TryGetValue(variable, out var value) && !string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }

        // libpq's fallback when there's no Unix socket. Npgsql has no default at all.
        values.TryAdd("host", "localhost");

        // An unknown key may be half of a password with a space in it, so it isn't named.
        if (values.Keys.Any(k => !Keywords.ContainsKey(k)))
        {
            throw new ConnectionInputException(
                $"The connection has a parameter pgcheckup doesn't read. It reads {string.Join(", ", Keywords.Keys)}.");
        }

        var settings = new NpgsqlConnectionStringBuilder();
        foreach (var (key, value) in values)
        {
            Keywords[key](settings, value);
        }

        return settings;
    }

    private static void ReadUrl(string url, Dictionary<string, string> values)
    {
        var rest = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];

        // As in libpq, credentials run to the first @ that comes before any /, so a password
        // may hold ? or : but a / in it must be percent-encoded.
        var credentialsEnd = rest.IndexOfAny(['@', '/']);
        if (credentialsEnd >= 0 && rest[credentialsEnd] == '@')
        {
            var userInfo = rest[..credentialsEnd];
            rest = rest[(credentialsEnd + 1)..];
            var colon = userInfo.IndexOf(':');
            values["user"] = Uri.UnescapeDataString(colon >= 0 ? userInfo[..colon] : userInfo);
            if (colon >= 0)
            {
                values["password"] = Uri.UnescapeDataString(userInfo[(colon + 1)..]);
            }
        }

        var query = "";
        var questionMark = rest.IndexOf('?');
        if (questionMark >= 0)
        {
            query = rest[(questionMark + 1)..];
            rest = rest[..questionMark];
        }

        var slash = rest.IndexOf('/');
        var authority = slash >= 0 ? rest[..slash] : rest;
        var database = slash >= 0 ? rest[(slash + 1)..] : "";
        if (database.Length > 0)
        {
            values["dbname"] = Uri.UnescapeDataString(database);
        }

        if (authority.Length > 0)
        {
            var hosts = authority.Split(',').Select(SplitHostPort).ToList();
            if (hosts.Count == 1)
            {
                values["host"] = hosts[0].Host;
                if (hosts[0].Port != null)
                {
                    values["port"] = hosts[0].Port!;
                }
            }
            else
            {
                values["host"] = string.Join(",", hosts.Select(h => h.Port == null ? h.Host : $"{h.Host}:{h.Port}"));
            }
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(equals >= 0 ? pair[..equals] : pair);
            values[key] = Uri.UnescapeDataString(equals >= 0 ? pair[(equals + 1)..] : "");
        }
    }

    private static (string Host, string? Port) SplitHostPort(string hostPort)
    {
        string host;
        string? port = null;
        if (hostPort.StartsWith('['))
        {
            var close = hostPort.IndexOf(']');
            if (close < 0)
            {
                throw new ConnectionInputException("An IPv6 host in the URL is missing its closing ].");
            }

            host = hostPort[1..close];
            if (hostPort.Length > close + 1 && hostPort[close + 1] == ':')
            {
                port = hostPort[(close + 2)..];
            }
        }
        else
        {
            var colon = hostPort.LastIndexOf(':');
            host = colon >= 0 ? hostPort[..colon] : hostPort;
            port = colon >= 0 ? hostPort[(colon + 1)..] : null;
        }

        if (port != null)
        {
            Number("port", port);
        }

        return (Uri.UnescapeDataString(host), port);
    }

    // libpq's format: key=value pairs separated by spaces, with values in single quotes when
    // they hold spaces, and backslash escapes inside.
    private static void ReadKeyValues(string input, Dictionary<string, string> values)
    {
        var i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            var keyStart = i;
            while (i < input.Length && input[i] != '=' && !char.IsWhiteSpace(input[i]))
            {
                i++;
            }

            var key = input[keyStart..i];
            while (i < input.Length && char.IsWhiteSpace(input[i]))
            {
                i++;
            }

            if (i >= input.Length || input[i] != '=')
            {
                throw new ConnectionInputException("Part of the connection string isn't key=value. Put values that contain spaces in single quotes.");
            }

            i++;
            while (i < input.Length && char.IsWhiteSpace(input[i]))
            {
                i++;
            }

            var value = new StringBuilder();
            if (i < input.Length && input[i] == '\'')
            {
                i++;
                var closed = false;
                while (i < input.Length)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        value.Append(input[i + 1]);
                        i += 2;
                    }
                    else if (input[i] == '\'')
                    {
                        closed = true;
                        i++;
                        break;
                    }
                    else
                    {
                        value.Append(input[i++]);
                    }
                }

                if (!closed)
                {
                    throw new ConnectionInputException("A value in the connection string opens a quote that never closes.");
                }
            }
            else
            {
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        i++;
                    }

                    value.Append(input[i++]);
                }
            }

            values[key] = value.ToString();
        }
    }

    private static int Number(string key, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ConnectionInputException($"The {key} in the connection isn't a number.");
}
