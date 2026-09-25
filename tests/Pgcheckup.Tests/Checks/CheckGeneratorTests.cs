using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Pgcheckup.Checks.Generator;

namespace Pgcheckup.Tests.Checks;

public class CheckGeneratorTests
{
    private const string Root = "/repo/checks/";

    private const string ValidCheckMd = """
        ---
        id: good-check
        title: Good check
        category: wal
        severity: warning
        min_version: 14
        privileges: [pg_monitor]
        thresholds:
          min_size: 1GB
        message: Slot {subject}[ for {age}] holds {size:bytes} and "quotes".
        fix: |
          SELECT pg_drop_replication_slot({slot_literal});
          SELECT ARRAY[[1]];
        ---

        ## What breaks

        Disks fill.

        ## Fix

        Drop it.

        ## Seen in

        - [Docs](https://www.postgresql.org/docs/current/warm-standby.html)
        """;

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create(
            "Generated",
            references: TrustedPlatformReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            [new CheckGenerator().AsSourceGenerator()],
            files.Select(f => (AdditionalText)new InMemoryText(f.Path, f.Text)),
            optionsProvider: new BuildProperties(new() { ["build_property.PgcheckupChecksDir"] = Root }));

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        // The generated catalog must compile against the real runtime types.
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Pgcheckup.Checks.CheckDefinition).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => MetadataReference.CreateFromFile(p));

    private static (string, string)[] GoodCheck(string folder = "good-check") =>
    [
        ($"{Root}{folder}/check.md", ValidCheckMd),
        ($"{Root}{folder}/check.sql", "SELECT 'x' AS subject WHERE 1 > @min_size"),
        ($"{Root}{folder}/fixtures/fires.sql", ""),
        ($"{Root}{folder}/fixtures/healthy.sql", ""),
    ];

    [Fact]
    public void Emits_a_catalog_that_compiles_for_valid_checks()
    {
        var result = Run(GoodCheck());

        Assert.Empty(result.Diagnostics);
        var source = Assert.Single(result.GeneratedTrees).ToString();
        Assert.Contains("\"good-check\"", source);
    }

    [Fact]
    public void Reports_an_invalid_check_as_an_error_on_its_file_and_line()
    {
        var files = GoodCheck();
        files[0].Item2 = ValidCheckMd.Replace("title: Good check", "title: Good check\ncolour: blue");

        var diagnostic = Assert.Single(Run(files).Diagnostics);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("PGC001", diagnostic.Id);
        Assert.Contains("colour", diagnostic.GetMessage());
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal($"{Root}good-check/check.md", span.Path);
        Assert.Equal(4, span.StartLinePosition.Line + 1);
    }

    [Fact]
    public void Reports_a_check_folder_without_check_md()
    {
        var diagnostic = Assert.Single(Run(($"{Root}lost-check/check.sql", "SELECT 1")).Diagnostics);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("lost-check", diagnostic.GetMessage());
    }

    [Fact]
    public void Ignores_files_outside_check_folders()
    {
        var result = Run(("/repo/docs/check.md", "not a check"), ($"{Root}README.md", "About checks"));

        Assert.Empty(result.Diagnostics);
    }

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class BuildProperties(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(global);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options([]);

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
