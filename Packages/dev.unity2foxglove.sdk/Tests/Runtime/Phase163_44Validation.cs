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
            Check(method.Contains("Other();", StringComparison.Ordinal),
                "163-44B-1: SourceMethod extracts the requested method body");
            Check(!method.Contains("Dangerous();", StringComparison.Ordinal),
                "163-44B-2: SourceMethod stops at the requested method boundary");

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
            Check(!PhaseValidationSourceHelpers.SourceMethodContains(unbalanced, "Target", "Dangerous();"),
                "163-44B-3: SourceMethodContains fails closed on unbalanced method braces");
        }

        private static void VerifyProgramLifecycle(string repoRoot)
        {
            var program = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            Check(program.Contains("private static int MainCore(string[] args)", StringComparison.Ordinal)
                  && program.Contains("finally\n        {\n            TempMcapHelper.Cleanup();", StringComparison.Ordinal),
                "163-44C-1: Main cleans TempMcapHelper for every CLI entrypoint");
            Check(program.Contains("--demo and --demo3d require --serve.", StringComparison.Ordinal),
                "163-44C-2: demo-only flags report the required --serve parent mode");
            Check(program.Contains("Interlocked.Exchange(ref stopping, 1)", StringComparison.Ordinal)
                  && program.Contains("DisposeTimerAndWait(heartbeat)", StringComparison.Ordinal)
                  && program.Contains("timer.Dispose(disposed)", StringComparison.Ordinal),
                "163-44C-3: manual server drains timer callbacks before runtime disposal");
        }

        private static void VerifyUnitCoverage(string repoRoot)
        {
            var tests = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeHarnessTests.cs");
            Check(tests.Contains("MainCleansTempMcapHelperInFinallyForAllEntrypoints", StringComparison.Ordinal)
                  && tests.Contains("RuntimeGitLsFilesDrainsPipesBeforeTimedWait", StringComparison.Ordinal)
                  && tests.Contains("DemoFlagsRequireServe", StringComparison.Ordinal),
                "163-44D-1: xUnit runtime harness tests cover the Phase163-44 lifecycle contracts");
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }
    }
}
