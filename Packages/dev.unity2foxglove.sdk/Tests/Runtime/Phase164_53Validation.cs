using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_53Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-53 Tests ---");
            _passed = 0;

            VerifyPhase115HCachesSourcesAndSyntaxTrees();
            VerifyPhase16UsesTargetedBuildOutputEnumeration();
            VerifyPhase105ScansLineWindowsWithoutJoining();
            VerifyPhase53CachesSourceReads();
            VerifyRegistry();

            Console.WriteLine("Phase 164-53: " + _passed + " checks passed.\n");
        }

        private static void VerifyPhase115HCachesSourcesAndSyntaxTrees()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115HValidation.cs");
            var validate = SourceMethod(source, "public static void Validate()");
            var readText = SourceMethod(source, "private static string ReadRepoText(string relativePath)");
            var readTree = SourceMethod(source, "private static SyntaxTree ReadRepoSyntaxTree(");
            var checkSummary = SourceMethod(source, "private static void CheckSummaryBefore(");

            Check(source.Contains("private static readonly Dictionary<string, string> SourceCache", StringComparison.Ordinal)
                  && source.Contains("private static readonly Dictionary<string, SyntaxTree> ParseCache", StringComparison.Ordinal),
                "164-53A-1: Phase115H owns source and syntax tree caches");
            Check(validate.Contains("SourceCache.Clear();", StringComparison.Ordinal)
                  && validate.Contains("ParseCache.Clear();", StringComparison.Ordinal),
                "164-53A-2: Phase115H clears validation caches on each run");
            Check(readText.Contains("SourceCache.TryGetValue", StringComparison.Ordinal)
                  && readText.Contains("SourceCache.Add(relativePath, text);", StringComparison.Ordinal),
                "164-53A-3: Phase115H reuses repository source reads");
            Check(readTree.Contains("ParseCache.TryGetValue", StringComparison.Ordinal)
                  && readTree.Contains("CSharpSyntaxTree.ParseText(text)", StringComparison.Ordinal)
                  && readTree.Contains("ParseCache.Add(relativePath, tree);", StringComparison.Ordinal),
                "164-53A-4: Phase115H parses each repository source once per validation run");
            Check(checkSummary.Contains("DocumentationContainsTerms(relativePath, declaration, requiredTerms)", StringComparison.Ordinal),
                "164-53A-5: Phase115H repository summary checks route through cached syntax trees");
        }

        private static void VerifyPhase16UsesTargetedBuildOutputEnumeration()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");
            var validateOutputs = SourceMethod(source, "static void ValidatePackageBuildOutputsAbsent(string repoRoot)");
            var enumerator = SourceMethod(source, "static IEnumerable<string> EnumeratePackageBuildOutputDirectories(");

            Check(validateOutputs.Contains("EnumeratePackageBuildOutputDirectories(packagesDir)", StringComparison.Ordinal),
                "164-53B-1: Phase16 routes build-output scans through the targeted enumerator");
            Check(enumerator.Contains("Directory.EnumerateDirectories(packagesDir, \"bin\"", StringComparison.Ordinal)
                  && enumerator.Contains("Directory.EnumerateDirectories(packagesDir, \"obj\"", StringComparison.Ordinal)
                  && !source.Contains("Directory.EnumerateDirectories(packagesDir, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal),
                "164-53B-2: Phase16 avoids wildcard directory scans under Packages");
        }

        private static void VerifyPhase105ScansLineWindowsWithoutJoining()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase105Validation.cs");
            var windowBefore = SourceMethod(source, "private static LineWindow WindowBefore(");
            var lineWindow = PhaseValidationSourceHelpers.SourceType(source, "private readonly struct LineWindow");
            var readText = SourceMethod(source, "private static string ReadRepoText(string relativePath)");

            Check(source.Contains("private readonly struct LineWindow", StringComparison.Ordinal)
                  && windowBefore.Contains("return new LineWindow(lines, start, index);", StringComparison.Ordinal),
                "164-53C-1: Phase105 window lookup returns a line-slice view");
            Check(lineWindow.Contains("for (var i = _start; i < _endExclusive; i++)", StringComparison.Ordinal)
                  && lineWindow.Contains("_lines[i].IndexOf(value, comparison)", StringComparison.Ordinal)
                  && !windowBefore.Contains("string.Join", StringComparison.Ordinal)
                  && !windowBefore.Contains("lines.Skip", StringComparison.Ordinal),
                "164-53C-2: Phase105 scans cached line slices without allocating joined windows");
            Check(readText.Contains("string.IsNullOrEmpty(root)", StringComparison.Ordinal)
                  && readText.Contains("Could not find repository root for Phase105 validation.", StringComparison.Ordinal),
                "164-53C-3: Phase105 fails clearly when the repository root cannot be resolved");
        }

        private static void VerifyPhase53CachesSourceReads()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase53Validation.cs");
            var validate = SourceMethod(source, "public static void Validate()");
            var readText = SourceMethod(source, "private static string ReadRepoText(string relativePath)");

            Check(source.Contains("private static readonly Dictionary<string, string> SourceCache", StringComparison.Ordinal)
                  && source.Contains("private static string CachedRepoRoot", StringComparison.Ordinal),
                "164-53D-1: Phase53 owns source and repository-root caches");
            Check(validate.Contains("SourceCache.Clear();", StringComparison.Ordinal)
                  && validate.Contains("CachedRepoRoot = null;", StringComparison.Ordinal),
                "164-53D-2: Phase53 clears source caches before each validation run");
            Check(readText.Contains("SourceCache.TryGetValue", StringComparison.Ordinal)
                  && readText.Contains("CachedRepoRoot", StringComparison.Ordinal)
                  && readText.Contains("SourceCache.Add(relativePath, text);", StringComparison.Ordinal),
                "164-53D-3: Phase53 reuses repository source reads");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-53\"", StringComparison.Ordinal), "164-53E-1: validation registry exposes Phase164-53");
            Check(project.Contains("Phase164_53Validation.cs", StringComparison.Ordinal), "164-53E-2: runtime validation project compiles Phase164-53");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

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
