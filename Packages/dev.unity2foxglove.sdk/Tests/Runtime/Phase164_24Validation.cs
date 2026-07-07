using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_24Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-24 Tests ---");
            _passed = 0;

            VerifyPlayModeRefreshUsesChangedSignal();
            VerifyBuildPreprocessReusesGeneratedTypeList();
            VerifyManifestWritersDeferReportsAndStreamCompare();
            VerifySchemaInfoWriterAvoidsHotGenerationAllocations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-24: " + _passed + " checks passed.\n");
        }

        private static void VerifyPlayModeRefreshUsesChangedSignal()
        {
            var hook = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");

            Check(hook.Contains("GenerateManifestFilesOnlyWithResult()", StringComparison.Ordinal)
                  && hook.Contains("if (refresh.SchemaInfoChanged)", StringComparison.Ordinal)
                  && !hook.Contains("ReadExistingText", StringComparison.Ordinal)
                  && !hook.Contains("File.ReadAllText", StringComparison.Ordinal),
                "164-24A-1: Play Mode refresh uses schema-info changed signal instead of rereading generated source");
        }

        private static void VerifyBuildPreprocessReusesGeneratedTypeList()
        {
            var build = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var scanner = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var preprocess = PhaseValidationSourceHelpers.SourceMethod(build, "public void OnPreprocessBuild");
            var ensureWithTypes = PhaseValidationSourceHelpers.SourceMethod(build, "List<(string AsmName, string Ns, string ClassName)> types)");
            var generate = PhaseValidationSourceHelpers.SourceMethod(codegen, "out List<(string AsmName, string Ns, string ClassName)> foxRunTypes)");
            var combined = PhaseValidationSourceHelpers.SourceMethod(scanner, "private static FoxRunAndServiceScanResult ScanFoxRunMembersAndServices");

            Check(preprocess.Contains("GenerateSourceFiles(out manifest, out foxRunTypes)", StringComparison.Ordinal)
                  && preprocess.Contains("EnsureFoxRunLinkXml(linkPath, foxRunTypes)", StringComparison.Ordinal)
                  && !preprocess.Contains("CollectFoxRunTypes()", StringComparison.Ordinal),
                "164-24B-1: build preprocess passes generated FoxRun type list into link.xml writer");
            Check(!build.Contains("static void EnsureFoxRunLinkXml(string linkPath)", StringComparison.Ordinal)
                  && ensureWithTypes.Contains("types = types ?? new List", StringComparison.Ordinal)
                  && ensureWithTypes.Contains("FoxrunCodeGenerator.EmitLinkXml(types)", StringComparison.Ordinal),
                "164-24B-2: link.xml writer has one cached-type path and no duplicate standalone scan");
            Check(generate.Contains("foxRunTypes = editorScan.FoxRunTypes;", StringComparison.Ordinal)
                  && combined.Contains("foxRunTypes.Add((asm.GetName().Name, ns, type.Name));", StringComparison.Ordinal),
                "164-24B-3: combined generator scan records FoxRun types for IL2CPP preservation");
        }

        private static void VerifyManifestWritersDeferReportsAndStreamCompare()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs"
            })
            {
                var source = Read(path);
                var write = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool WriteIfChanged");

                Check(source.Contains("if (manifestChanged || hashChanged || !File.Exists(reportPath))", StringComparison.Ordinal)
                      && source.IndexOf("WriteReport", StringComparison.Ordinal) >
                      source.IndexOf("if (manifestChanged || hashChanged || !File.Exists(reportPath))", StringComparison.Ordinal),
                    "164-24C-1: manifest report serialization is deferred until a write is needed: " + path);
                Check(write.Contains("var existing = new FileInfo(path);", StringComparison.Ordinal)
                      && write.Contains("existing.Length == bytes.Length", StringComparison.Ordinal)
                      && write.Contains("FileContentEquals(path, bytes)", StringComparison.Ordinal)
                      && !write.Contains("ReadAllBytes", StringComparison.Ordinal),
                    "164-24C-2: manifest writer uses length-first streaming equality: " + path);
            }

            var build = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var writeText = PhaseValidationSourceHelpers.SourceMethod(build, "private static void WriteTextIfChanged");
            Check(writeText.Contains("var existing = new FileInfo(path);", StringComparison.Ordinal)
                  && writeText.Contains("existing.Length == bytes.Length", StringComparison.Ordinal)
                  && writeText.Contains("FileContentEquals(path, bytes)", StringComparison.Ordinal)
                  && !writeText.Contains("ReadAllBytes", StringComparison.Ordinal),
                "164-24C-3: build link.xml writer uses length-first streaming equality");
        }

        private static void VerifySchemaInfoWriterAvoidsHotGenerationAllocations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var write = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool WriteIfChanged");
            var appendLiteral = PhaseValidationSourceHelpers.SourceMethod(source, "private static void AppendStringLiteral");
            var indent = PhaseValidationSourceHelpers.SourceMethod(source, "private static string Indent");

            Check(source.Contains("WriteGeneratedInfoFilesWithResult", StringComparison.Ordinal)
                  && source.Contains("public bool SourceChanged", StringComparison.Ordinal)
                  && source.Contains("public bool AnyChanged", StringComparison.Ordinal),
                "164-24D-1: schema-info writer exposes changed result without breaking existing verification API");
            Check(write.Contains("var existing = new FileInfo(path);", StringComparison.Ordinal)
                  && write.Contains("existing.Length == bytes.Length", StringComparison.Ordinal)
                  && write.Contains("FileContentEquals(path, bytes)", StringComparison.Ordinal)
                  && !write.Contains("ReadAllBytes", StringComparison.Ordinal),
                "164-24D-2: schema-info writer uses length-first streaming equality");
            Check(source.Contains("private static readonly string[] Indents", StringComparison.Ordinal)
                  && indent.Contains("Indents[level]", StringComparison.Ordinal)
                  && indent.Contains("new string(' ', level * 4)", StringComparison.Ordinal),
                "164-24D-3: schema-info indentation uses a static lookup for common levels with fallback");
            Check(source.Contains("AppendIndentedStringLiteralLine", StringComparison.Ordinal)
                  && appendLiteral.Contains("sb.Append('\"');", StringComparison.Ordinal)
                  && !appendLiteral.Contains("new StringBuilder", StringComparison.Ordinal),
                "164-24D-4: schema-info string literal emission appends into the active builder");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-24\"", StringComparison.Ordinal), "164-24E-1: validation registry exposes Phase164-24");
            Check(project.Contains("Phase164_24Validation.cs", StringComparison.Ordinal), "164-24E-2: runtime validation project compiles Phase164-24");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
