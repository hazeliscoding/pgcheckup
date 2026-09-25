using System.CommandLine;
using Npgsql;
using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

public static class PgcheckupCli
{
    public const int Passed = 0;
    public const int FindingsReachedFailOn = 1;
    public const int CouldNotRun = 2;

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        CancellationToken cancellationToken)
    {
        var root = new RootCommand("Checks a PostgreSQL database for the problems that cause outages. Read-only, and safe to run on production.");

        var list = new Command("list", "List every check.");
        list.SetAction(_ => List(output));
        root.Subcommands.Add(list);

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

        var scan = new Command("scan", "Scan a database and report what could take it down.");
        scan.Arguments.Add(connection);
        scan.Options.Add(failOn);
        scan.SetAction((result, token) => ScanAsync(
            result.GetValue(connection), result.GetValue(failOn)!, output, error, environment, outputRedirected, token));
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

        return await parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
    }

    private static int List(TextWriter output)
    {
        var width = CheckCatalog.All.Max(c => c.Id.Length) + 2;
        foreach (var check in CheckCatalog.All)
        {
            output.WriteLine($"{check.Id.PadRight(width)}{check.Severity.ToString().ToLowerInvariant(),-10}{check.Title}");
        }

        return Passed;
    }

    private static async Task<int> ScanAsync(
        string? input,
        string failOn,
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
                report = await Scanner.ScanAsync(session, host, CheckCatalog.All, cancellationToken);
            }
            catch (Exception problem) when (problem is CheckFailedException or NpgsqlException)
            {
                error.WriteLine($"pgcheckup: {problem.Message}");
                return CouldNotRun;
            }

            TerminalReport.Write(output, report, TerminalReport.UseColor(outputRedirected, environment));

            var threshold = failOn switch
            {
                "info" => Severity.Info,
                "warning" => Severity.Warning,
                _ => Severity.Critical,
            };
            return report.Results.Any(r => r.Worst >= threshold) ? FindingsReachedFailOn : Passed;
        }
    }
}
