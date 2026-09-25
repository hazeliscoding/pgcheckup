using System.Collections;
using System.Text;
using Pgcheckup.Cli;

// The report's separators and any non-ASCII object names need UTF-8, whatever the console's code page.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var environment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
{
    environment[(string)variable.Key] = (string?)variable.Value;
}

return await PgcheckupCli.RunAsync(args, Console.Out, Console.Error, environment, Console.IsOutputRedirected, CancellationToken.None);
