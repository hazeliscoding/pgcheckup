using System.CommandLine;
using System.Reflection;
using Npgsql;
using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>The <c>pgcheckup</c> command line: its commands, options and exit codes.</summary>
public static class PgcheckupCli
{
    /// <summary>Exit code 0: the scan ran and no finding reached <c>--fail-on</c>.</summary>
    public const int Passed = 0;

    /// <summary>Exit code 1: at least one finding reached <c>--fail-on</c>.</summary>
    public const int FindingsReachedFailOn = 1;

    /// <summary>Exit code 2: the scan couldn't run, whatever the reason.</summary>
    public const int CouldNotRun = 2;

    private static string Version =>
        typeof(PgcheckupCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Runs pgcheckup with every check compiled into the binary.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where the report and help go.</param>
    /// <param name="error">Where errors go.</param>
    /// <param name="environment">The process's environment variables, for PG* settings and NO_COLOR.</param>
    /// <param name="outputRedirected">Whether <paramref name="output"/> is a file or pipe, which turns color off.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>0, 1 or 2. See <see cref="Passed"/>, <see cref="FindingsReachedFailOn"/> and <see cref="CouldNotRun"/>.</returns>
    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        CancellationToken cancellationToken) =>
        RunAsync(args, CheckCatalog.All, output, error, environment, outputRedirected, cancellationToken);

    /// <summary>Runs pgcheckup with the given checks.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="checks">The checks that <c>list</c> shows and <c>scan</c> runs.</param>
    /// <param name="output">Where the report and help go.</param>
    /// <param name="error">Where errors go.</param>
    /// <param name="environment">The process's environment variables, for PG* settings and NO_COLOR.</param>
    /// <param name="outputRedirected">Whether <paramref name="output"/> is a file or pipe, which turns color off.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>
    /// 0, 1 or 2. Anything that stops the scan, including bad arguments and unexpected errors,
    /// returns 2, never 1.
    /// </returns>
    public static async Task<int> RunAsync(
        string[] args,
        IReadOnlyList<CheckDefinition> checks,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        CancellationToken cancellationToken)
    {
        var root = new RootCommand("Checks a PostgreSQL database for the problems that cause outages. Read-only, and safe to run on production.");

        var list = new Command("list", "List every check with its severity, category and minimum Postgres version.");
        list.SetAction(_ =>
        {
            CatalogText.WriteList(output, checks);
            return Passed;
        });
        root.Subcommands.Add(list);

        var checkId = new Argument<string>("check") { Description = "The id of a check, as pgcheckup list shows it." };
        var explain = new Command("explain", "Explain a check: what breaks, how to fix it, and where it has happened.");
        explain.Arguments.Add(checkId);
        explain.SetAction(result => Explain(checks, result.GetValue(checkId)!, output, error));
        root.Subcommands.Add(explain);

        var role = new Option<string>("--role")
        {
            Description = "The role to create.",
            DefaultValueFactory = _ => "checkup",
        };
        var database = new Option<string>("--database")
        {
            Description = "The database the role may connect to.",
            DefaultValueFactory = _ => "app",
        };
        var owner = new Option<string>("--owner")
        {
            Description = "The role that owns your tables, so the grant covers sequences it creates later.",
            DefaultValueFactory = _ => "app",
        };
        var grant = new Command("grant", "Print SQL for a least-privilege checkup role. pgcheckup never runs it.");
        grant.Options.Add(role);
        grant.Options.Add(database);
        grant.Options.Add(owner);
        grant.SetAction(result =>
        {
            try
            {
                output.Write(GrantScript.Build(result.GetValue(role)!, result.GetValue(database)!, result.GetValue(owner)!));
                return Passed;
            }
            catch (ArgumentException problem)
            {
                error.WriteLine($"pgcheckup: {problem.Message}");
                return CouldNotRun;
            }
        });
        root.Subcommands.Add(grant);

        var connection = new Argument<string?>("connection")
        {
            Description = "A postgres:// URL or a libpq key-value string. Without one, the PG* environment variables are used. Keep the password in PGPASSWORD or ~/.pgpass, not here.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var failOn = new Option<string>("--fail-on")
        {
            Description = "Exit with 1 when a finding reaches this severity: critical, warning or info.",
            DefaultValueFactory = _ => "critical",
        };
        failOn.AcceptOnlyFromAmong("critical", "warning", "info");
        var format = new Option<string>("--format")
        {
            Description = "How to write the report: terminal, json (see docs/json.md) or markdown.",
            DefaultValueFactory = _ => "terminal",
        };
        format.AcceptOnlyFromAmong("terminal", "json", "markdown");

        var scan = new Command("scan", "Scan a database and report what could take it down.");
        scan.Arguments.Add(connection);
        scan.Options.Add(failOn);
        scan.Options.Add(format);
        scan.SetAction((result, token) => ScanAsync(
            result.GetValue(connection), result.GetValue(failOn)!, result.GetValue(format)!, checks, output, error, environment, outputRedirected, token));
        root.Subcommands.Add(scan);

        var parsed = root.Parse(args);

        // Exit code 1 means findings, so argument errors get 2 like any other scan that couldn't run.
        if (parsed.Errors.Count > 0)
        {
            foreach (var parseError in parsed.Errors)
            {
                error.WriteLine($"pgcheckup: {parseError.Message}");
            }

            error.WriteLine("Run pgcheckup --help for usage.");
            return CouldNotRun;
        }

        // System.CommandLine's own handler would print a stack trace and exit 1, which means findings.
        try
        {
            return await parsed.InvokeAsync(
                new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false },
                cancellationToken);
        }
        catch (Exception problem)
        {
            error.WriteLine($"pgcheckup: the scan stopped: {problem.Message}");
            return CouldNotRun;
        }
    }

    private static int Explain(IReadOnlyList<CheckDefinition> checks, string id, TextWriter output, TextWriter error)
    {
        if (checks.FirstOrDefault(c => c.Id == id) is not { } check)
        {
            error.WriteLine($"pgcheckup: there is no check named {id}. Run pgcheckup list to see them.");
            return CouldNotRun;
        }

        CatalogText.WriteExplanation(output, check);
        return Passed;
    }

    private static async Task<int> ScanAsync(
        string? input,
        string failOn,
        string format,
        IReadOnlyList<CheckDefinition> checks,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = ConnectionInput.Parse(input, environment);
        }
        catch (ConnectionInputException problem)
        {
            error.WriteLine($"pgcheckup: {problem.Message}");
            return CouldNotRun;
        }

        var host = settings.Host ?? "localhost";
        ReadOnlySession session;
        try
        {
            session = await ReadOnlySession.OpenAsync(settings, cancellationToken);
        }
        catch (Exception problem) when (problem is NpgsqlException or TimeoutException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine($"pgcheckup: couldn't connect to {host}: {problem.Message}");
            return CouldNotRun;
        }

        await using (session)
        {
            ScanReport report;
            try
            {
                report = await Scanner.ScanAsync(session, host, checks, cancellationToken);
            }
            catch (NpgsqlException problem)
            {
                error.WriteLine($"pgcheckup: couldn't read the server's version and privileges: {problem.Message}");
                return CouldNotRun;
            }

            switch (format)
            {
                case "json":
                    output.WriteLine(JsonReport.Write(report, Version));
                    break;
                case "markdown":
                    output.Write(MarkdownReport.Write(report));
                    break;
                default:
                    TerminalReport.Write(output, report, TerminalReport.UseColor(outputRedirected, environment));
                    break;
            }

            var threshold = failOn switch
            {
                "info" => Severity.Info,
                "warning" => Severity.Warning,
                _ => Severity.Critical,
            };
            // A finding outranks an error: it is the more useful signal, and an error still fails CI.
            if (report.Results.Any(r => r.Worst >= threshold))
            {
                return FindingsReachedFailOn;
            }

            return report.Results.Any(r => r.Status == CheckStatus.Errored) ? CouldNotRun : Passed;
        }
    }
}
