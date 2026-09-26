using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pgcheckup.Checks;
using Pgcheckup.Engine;

namespace Pgcheckup.Cli;

/// <summary>
/// The <c>--format json</c> report. Its shape is a contract, versioned by <c>schema</c> and
/// described in docs/json.md.
/// </summary>
public static class JsonReport
{
    /// <summary>The version of the JSON shape. Change it only through a decision in ROADMAP.md.</summary>
    public const int Schema = 1;

    /// <summary>Writes the report as indented JSON.</summary>
    /// <param name="report">What the scan found.</param>
    /// <param name="version">pgcheckup's own version, for the <c>pgcheckup</c> field.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(ScanReport report, string version)
    {
        var server = report.Server;
        var document = new JsonDocumentModel(
            Schema,
            version,
            new JsonServer(server.Database, server.Host, server.Version, server.Provider is { } p ? new JsonProvider(p.Id, p.Name) : null),
            new JsonSummary(
                report.Results.Count(r => r.Status == CheckStatus.Passed),
                report.Results.Count(r => r.Worst == Severity.Critical),
                report.Results.Count(r => r.Worst == Severity.Warning),
                report.Results.Count(r => r.Worst == Severity.Info),
                report.Results.Count(r => r.Status == CheckStatus.Errored),
                report.Results.Count(r => r.Status == CheckStatus.Skipped)),
            report.Results.Select(ToJson).ToList());
        return JsonSerializer.Serialize(document, JsonReportContext.Default.JsonDocumentModel);
    }

    private static JsonCheck ToJson(CheckResult result) => new(
        result.Check.Id,
        result.Check.Title,
        result.Check.Category,
        result.Status switch
        {
            CheckStatus.Found => Lower(result.Worst!.Value),
            _ => result.Status.ToString().ToLowerInvariant(),
        },
        result.Reason,
        result.Findings.Select(f => new JsonFinding(f.Subject, Lower(f.Severity), f.Message, f.Fix, Values(result.Check, f))).ToList());

    // Only the columns the message uses: the facts behind the finding, not helpers such as a
    // quoted name that exists for the fix.
    private static JsonObject Values(CheckDefinition check, Finding finding)
    {
        var values = new JsonObject();
        foreach (var name in check.Message.ValueNames.Where(finding.Values.ContainsKey))
        {
            values[name] = ToNode(finding.Values[name]);
        }

        return values;
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        short number => JsonValue.Create(number),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        TimeSpan span => JsonValue.Create(Math.Round((decimal)span.TotalSeconds, 3)),
        DateTime time => JsonValue.Create(time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static string Lower(Severity severity) => severity.ToString().ToLowerInvariant();
}

// The JSON shape; docs/json.md describes each field.
internal sealed record JsonDocumentModel(int Schema, string Pgcheckup, JsonServer Server, JsonSummary Summary, IReadOnlyList<JsonCheck> Checks);

internal sealed record JsonServer(string Database, string Host, string Version, JsonProvider? Provider);

internal sealed record JsonProvider(string Id, string Name);

internal sealed record JsonSummary(int Passed, int Critical, int Warning, int Info, int Errored, int Skipped);

internal sealed record JsonCheck(
    string Id,
    string Title,
    string Category,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    IReadOnlyList<JsonFinding> Findings);

internal sealed record JsonFinding(string Subject, string Severity, string Message, string Fix, JsonObject Values);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(JsonDocumentModel))]
internal sealed partial class JsonReportContext : JsonSerializerContext;
