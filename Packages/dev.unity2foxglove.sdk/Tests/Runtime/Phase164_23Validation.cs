using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_23Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-23 Tests ---");
            _passed = 0;

            VerifyRoslynCandidateScanIsSinglePass();
            VerifyEditorFallbackUsesCombinedScan();
            VerifyPayloadExprAvoidsJsonNameList();
            VerifySourceGeneratorFreshnessUsesHashCompare();
            VerifyRegistry();

            Console.WriteLine("Phase 164-23: " + _passed + " checks passed.\n");
        }

        private static void VerifyRoslynCandidateScanIsSinglePass()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var isCandidate = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool IsCandidate");
            var hasFoxRunAttr = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool HasFoxRunAttr");

            Check(isCandidate.Contains("return HasFoxRunAttr(f.AttributeLists);", StringComparison.Ordinal)
                  && isCandidate.Contains("return HasFoxRunAttr(p.AttributeLists);", StringComparison.Ordinal)
                  && !source.Contains("private static bool HasFoxRunFieldAttr", StringComparison.Ordinal),
                "164-23A-1: Roslyn candidate predicate scans FoxRun/FoxRunField attributes once");
            Check(hasFoxRunAttr.Contains("AttrAttributeName", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("AttrQualifiedNameSuffix", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("FieldAttrAttributeName", StringComparison.Ordinal)
                  && hasFoxRunAttr.Contains("FieldAttrQualifiedNameSuffix", StringComparison.Ordinal),
                "164-23A-2: merged candidate scan recognizes FoxRun and FoxRunField spellings");
        }

        private static void VerifyEditorFallbackUsesCombinedScan()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var scanner = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var generate = PhaseValidationSourceHelpers.SourceMethod(source, "out List<(string AsmName, string Ns, string ClassName)> foxRunTypes)");
            var combined = PhaseValidationSourceHelpers.SourceMethod(scanner, "private static FoxRunAndServiceScanResult ScanFoxRunMembersAndServices");

            Check(generate.Contains("var editorScan = ScanFoxRunMembersAndServices(ignoreReflectionTypeLoadExceptions: true);", StringComparison.Ordinal)
                  && generate.Contains("var scan = editorScan.FoxRun;", StringComparison.Ordinal)
                  && generate.Contains("var serviceScan = editorScan.Services;", StringComparison.Ordinal)
                  && generate.Contains("foxRunTypes = editorScan.FoxRunTypes;", StringComparison.Ordinal)
                  && !generate.Contains("ScanFoxRunMembers(ignoreReflectionTypeLoadExceptions: true);", StringComparison.Ordinal)
                  && !generate.Contains("ScanFoxServiceMethods(ignoreReflectionTypeLoadExceptions: true);", StringComparison.Ordinal),
                "164-23B-1: source-file generation uses one combined FoxRun/FoxService reflection scan");
            Check(Count(combined, "AppDomain.CurrentDomain.GetAssemblies()") == 1
                  && combined.Contains("var members = ScanType(type);", StringComparison.Ordinal)
                  && combined.Contains("var methods = ScanServiceType(type);", StringComparison.Ordinal)
                  && combined.Contains("BuildFoxServiceScanResult(serviceEntries)", StringComparison.Ordinal),
                "164-23B-2: combined scan collects members and services inside one assembly/type traversal");
        }

        private static void VerifyPayloadExprAvoidsJsonNameList()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var payloadExpr = PhaseValidationSourceHelpers.SourceMethod(source, "private static string PayloadExpr");

            Check(payloadExpr.Contains("fields[j].JsonFieldName", StringComparison.Ordinal)
                  && !payloadExpr.Contains("fields.Select", StringComparison.Ordinal)
                  && !payloadExpr.Contains("jsonNames", StringComparison.Ordinal)
                  && !payloadExpr.Contains(".ToList()", StringComparison.Ordinal),
                "164-23C-1: payload expression emission avoids intermediate JSON-name list allocation");
        }

        private static void VerifySourceGeneratorFreshnessUsesHashCompare()
        {
            var validator = Read("Scripts/package/validate_source_generator_dll.py");

            Check(validator.Contains("built_hash = sha256(built_dll)", StringComparison.Ordinal)
                  && validator.Contains("checked_hash = sha256(CHECKED_IN_DLL)", StringComparison.Ordinal)
                  && validator.Contains("if built_hash != checked_hash:", StringComparison.Ordinal)
                  && !validator.Contains("read_bytes() !=", StringComparison.Ordinal),
                "164-23D-1: source generator freshness validator avoids redundant full-DLL byte reads");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-23\"", StringComparison.Ordinal), "164-23E-1: validation registry exposes Phase164-23");
            Check(project.Contains("Phase164_23Validation.cs", StringComparison.Ordinal), "164-23E-2: runtime validation project compiles Phase164-23");
        }

        private static int Count(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
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
