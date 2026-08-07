// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-44 runtime harness and shared helper review closure.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_44Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyGitLsFiles(repoRoot);
            VerifySourceMethodScanner();
            VerifyProgramLifecycle(repoRoot);
            VerifyLegacyToolListing(repoRoot);
            VerifyUnitCoverage(repoRoot);

            Console.WriteLine("Phase 163-44: runtime harness helper checks passed.");
        }

        private static void VerifyGitLsFiles(string repoRoot)
        {
            var files = PhaseRos2ForUnityValidationHelpers.GitLsFiles(repoRoot);
            Check(files.Contains("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs"),
                "163-44A-1: GitLsFiles returns tracked files from the repository");

            var helper = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseRos2ForUnityValidationHelpers.cs");
            Check(helper.Contains("ReadToEndAsync()", StringComparison.Ordinal)
                  && helper.Contains("GitLsFilesTimeoutMilliseconds", StringComparison.Ordinal)
                  && helper.Contains("process.Kill(entireProcessTree: true)", StringComparison.Ordinal),
                "163-44A-2: GitLsFiles drains redirected streams asynchronously and has a timeout kill path");
        }

        private static void VerifySourceMethodScanner()
        {
            const string source =
@"class Sample
{
    // Target is declared below.
    void Caller()
    {
        var methodName = ""Target"";
        Target();
    }

    void Target()
    {
        var text = ""{ not a block }"";
        var literal = @""still { not a block }"";
        // } comment close
        /* { comment open */
        Other();
    }

    void Other()
    {
        Dangerous();
    }
}";

            var method = PhaseValidationSourceHelpers.SourceMethod(source, "Target");
            Check(method.TrimStart().StartsWith("void Target()", StringComparison.Ordinal),
                "163-44B-1: SourceMethod anchors the requested declaration rather than earlier trivia or invocations");
            Check(method.Contains("Other();", StringComparison.Ordinal),
                "163-44B-2: SourceMethod extracts the requested method body");
            Check(!method.Contains("Dangerous();", StringComparison.Ordinal),
                "163-44B-3: SourceMethod stops at the requested method boundary");

            const string overloaded =
@"class Sample
{
    void Target(int value) { IntOnly(); }
    void Target(string value) { StringOnly(); }
}";
            Check(Throws<InvalidOperationException>(() =>
                    PhaseValidationSourceHelpers.SourceMethod(overloaded, "Target")),
                "163-44B-4: SourceMethod fails closed for an ambiguous bare method name");
            Check(string.IsNullOrEmpty(PhaseValidationSourceHelpers.TrySourceMethod(overloaded, "Target")),
                "163-44B-4a: TrySourceMethod preserves explicit optional lookup semantics");
            Check(Throws<InvalidOperationException>(() =>
                    PhaseValidationSourceHelpers.SourceMethod(overloaded, "Missing")),
                "163-44B-4b: SourceMethod fails closed when the declaration is missing");
            Check(PhaseValidationSourceHelpers.SourceMethod(overloaded, "void Target(int value)")
                    .Contains("IntOnly();", StringComparison.Ordinal),
                "163-44B-5: SourceMethod accepts a signature that selects one overload");

            const string prefixedNames =
@"class Sample
{
    void Target() { ExactName(); }
    void TargetExtended() { ExtendedName(); }
}";
            var exactPrefix = PhaseValidationSourceHelpers.SourceMethod(prefixedNames, "void Target");
            Check(exactPrefix.Contains("ExactName();", StringComparison.Ordinal)
                  && !exactPrefix.Contains("ExtendedName();", StringComparison.Ordinal),
                "163-44B-6: SourceMethod does not confuse an identifier with a longer prefixed sibling");

            const string unbalanced =
@"class Sample
{
    void Target()
    {
        var text = ""{"";

    void Other()
    {
        Dangerous();
    }";
            Check(Throws<InvalidOperationException>(() =>
                    PhaseValidationSourceHelpers.SourceMethodContains(
                        unbalanced,
                        "Target",
                        "Dangerous();")),
                "163-44B-7: SourceMethodContains rejects an unresolvable method instead of making a negative assertion pass");

            const string nestedType =
@"class Outer
{
    private sealed class Runner
    {
        private void Work() { NestedBody(); }
    }
}";
            var runner = PhaseValidationSourceHelpers.SourceType(nestedType, "private sealed class Runner");
            Check(PhaseValidationSourceHelpers.SourceMethod(runner, "private void Work()")
                    .Contains("NestedBody();", StringComparison.Ordinal),
                "163-44B-8: SourceType supports declaration-scoped method checks");

            const string properties =
@"class Sample
{
    int Target
    {
        get => _target;
        set { _target = value; InsideTarget(); }
    }

    int Other
    {
        set { Dangerous(); }
    }
}";
            var property = PhaseValidationSourceHelpers.SourceProperty(properties, "Target");
            Check(property.Contains("InsideTarget();", StringComparison.Ordinal)
                  && !property.Contains("Dangerous();", StringComparison.Ordinal),
                "163-44B-9: SourceProperty stops at the requested property boundary");
        }

        private static void VerifyProgramLifecycle(string repoRoot)
        {
            var program = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            Check(program.Contains("private static int MainCore(string[] args)", StringComparison.Ordinal)
                  && program.Contains("finally\n        {\n            TempMcapHelper.Cleanup();", StringComparison.Ordinal),
                "163-44C-1: Main cleans TempMcapHelper for every CLI entrypoint");
            var runTests = PhaseValidationSourceHelpers.SourceMethod(program, "RunTests");
            Check(runTests.TrimStart().StartsWith("static int RunTests(bool includeLocalEvidence)", StringComparison.Ordinal)
                  && !runTests.Contains("TempMcapHelper.Cleanup()", StringComparison.Ordinal),
                "163-44C-2: RunTests leaves TempMcapHelper cleanup ownership to Main");
            Check(program.Contains("--demo and --demo3d require --serve.", StringComparison.Ordinal),
                "163-44C-3: demo-only flags report the required --serve parent mode");
            Check(program.Contains("--demo and --demo3d cannot be used together.", StringComparison.Ordinal),
                "163-44C-4: manual demo modes are mutually exclusive to avoid duplicate channel ids");
            Check(program.Contains("Interlocked.Exchange(ref stopping, 1)", StringComparison.Ordinal)
                  && program.Contains("Interlocked.Increment(ref seq)", StringComparison.Ordinal)
                  && program.Contains("Interlocked.Increment(ref tfSeq)", StringComparison.Ordinal)
                  && program.Contains("DisposeTimerAndWait(heartbeat)", StringComparison.Ordinal)
                  && program.Contains("timer.Dispose(disposed)", StringComparison.Ordinal),
                "163-44C-5: manual server drains timer callbacks and uses interlocked timer counters");
            Check(program.Contains("RunPhase97Health(argList, argSet)", StringComparison.Ordinal)
                  && program.Contains("argSet.Contains(\"--phase97-live\")", StringComparison.Ordinal),
                "163-44C-6: Phase97 live flag lookup uses the precomputed argument set");
            var meta = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs.meta");
            Check(meta.Contains("MonoImporter:", StringComparison.Ordinal)
                  && meta.Contains("guid: b161da42b5a01a543b309c84c8e2dbca", StringComparison.Ordinal),
                "163-44C-7: Program.cs Unity meta keeps a valid MonoImporter block and stable GUID");
        }

        private static void VerifyLegacyToolListing(string repoRoot)
        {
            var program = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            foreach (var flag in new[]
            {
                "--phase97-health",
                "--phase98-sample-send-all",
                "--phase98-live",
                "--phase99-live",
                "--phase94-bridge-send",
                "--phase91-ros2-cdr-mcap",
                "--phase92-ros2-product-mcap",
                "--phase93-ros2-full-mcap",
                "--phase93-inspect-mcap",
                "--phase68-indexed-reader-smoke",
                "--phase44-all-schemas-mcap",
                "--phase139b-remote-data-loader-server",
            })
            {
                Check(program.Contains($"(\"{flag}\"", StringComparison.Ordinal),
                    "163-44D-tool: " + flag + " appears in --list-validations legacy tool output");
            }
        }

        private static void VerifyUnitCoverage(string repoRoot)
        {
            var tests = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeHarnessTests.cs");
            Check(tests.Contains("MainCleansTempMcapHelperInFinallyForAllEntrypoints", StringComparison.Ordinal)
                  && tests.Contains("RunTestsDoesNotOwnTempMcapHelperCleanup", StringComparison.Ordinal)
                  && tests.Contains("RuntimeGitLsFilesDrainsPipesBeforeTimedWait", StringComparison.Ordinal)
                  && tests.Contains("DemoFlagsRequireServe", StringComparison.Ordinal)
                  && tests.Contains("DemoAndDemo3dCannotRunTogether", StringComparison.Ordinal)
                  && tests.Contains("ListValidationsIncludesLegacyManualToolFlags", StringComparison.Ordinal),
                "163-44E-1: xUnit runtime harness tests cover the Phase163-44 lifecycle contracts");
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }
    }
}
