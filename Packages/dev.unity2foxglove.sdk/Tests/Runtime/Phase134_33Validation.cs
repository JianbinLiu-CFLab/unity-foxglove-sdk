// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-33 regression coverage for early baseline validation hardening.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_33Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-33: Early Baseline Validation Hardening ===");
            _passed = 0;

            VerifyValidationWiring();
            VerifyPassCountersReset();
            VerifyDynamicIntegrationPorts();
            VerifyObservableConditionPolling();
            VerifyProtocolAssertions();
            VerifySourceInspectionAnchoring();
            VerifyPhase16Robustness();
            VerifyFakeTransportSnapshots();
            VerifyDeadCodeRemoval();

            Console.WriteLine($"Phase 134-33: {_passed} checks passed.");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("<Compile Include=\"Phase134_33Validation.cs\" />", StringComparison.Ordinal),
                "134-33A-1: Phase134_33Validation is compiled by the runtime test project");
            Check(registry.Contains("Ci(\"--phase134-33\", \"Phase 134-33\", Phase134_33Validation.Validate)", StringComparison.Ordinal),
                "134-33A-2: Phase134_33Validation is wired into the validation registry");
        }

        private static void VerifyPassCountersReset()
        {
            for (var phase = 1; phase <= 10; phase++)
            {
                var source = ReadRepoText($"Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase{phase}Validation.cs");
                Check(source.Contains("_passCount = 0;", StringComparison.Ordinal),
                    $"134-33B-{phase}: Phase {phase} resets its pass counter on each run");
            }
        }

        private static void VerifyDynamicIntegrationPorts()
        {
            var phase1 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase1Validation.cs");
            var phase2 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase2Validation.cs");
            var phase3 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase3Validation.cs");

            Check(phase1.Contains("GetEphemeralTcpPort()", StringComparison.Ordinal)
                  && !phase1.Contains("GetFreeTcpPort(int preferredPort)", StringComparison.Ordinal)
                  && !phase1.Contains("18767", StringComparison.Ordinal)
                  && !phase1.Contains("18768", StringComparison.Ordinal),
                "134-33C-1: Phase 1 websocket tests use ephemeral ports");
            Check(phase2.Contains("GetEphemeralTcpPort()", StringComparison.Ordinal)
                  && !phase2.Contains("18770", StringComparison.Ordinal)
                  && !phase2.Contains("18780", StringComparison.Ordinal)
                  && !phase2.Contains("18781", StringComparison.Ordinal),
                "134-33C-2: Phase 2 integration tests avoid fixed ports");
            Check(phase3.Contains("GetEphemeralTcpPort()", StringComparison.Ordinal)
                  && !phase3.Contains("18782", StringComparison.Ordinal)
                  && !phase3.Contains("18783", StringComparison.Ordinal),
                "134-33C-3: Phase 3 integration tests avoid fixed ports");
        }

        private static void VerifyObservableConditionPolling()
        {
            var phase2 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase2Validation.cs");
            var phase3 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase3Validation.cs");

            Check(phase2.Contains("SpinUntil(() => runtime.Session.HasChannelDemand(1)", StringComparison.Ordinal)
                  && phase2.Contains("SpinUntil(() => !runtime.Session.HasChannelDemand(1)", StringComparison.Ordinal),
                "134-33D-1: Phase 2 waits for observable subscribe/unsubscribe state");
            Check(phase3.Contains("SpinUntil(() => runtime.Session.HasChannelDemand(1)", StringComparison.Ordinal),
                "134-33D-2: Phase 3 waits for observable subscribe state");
            Check(!phase2.Contains("Task.Delay(100).Wait()", StringComparison.Ordinal)
                  && !phase3.Contains("Task.Delay(100).Wait()", StringComparison.Ordinal),
                "134-33D-3: early integration tests no longer depend on fixed delay sleeps");
        }

        private static void VerifyProtocolAssertions()
        {
            var phase2 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase2Validation.cs");
            var phase9 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase9Validation.cs");
            var phase13 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase13Validation.cs");

            Check(phase2.Contains("Unknown op leaves client usable", StringComparison.Ordinal)
                  && phase2.Contains("Malformed JSON leaves client usable", StringComparison.Ordinal)
                  && phase2.Contains("AssertClientCanStillSubscribeAndReceive", StringComparison.Ordinal),
                "134-33E-1: Phase 2 malformed/unknown-op tests assert client stability");
            Check(phase9.Contains("TryDecodeFetchAssetResponse", StringComparison.Ordinal)
                  && phase9.Contains("fetchAsset response frame decodes", StringComparison.Ordinal)
                  && !phase9.Contains("Assert(true, \"fetchAsset", StringComparison.Ordinal),
                "134-33E-2: Phase 9 validates fetchAsset binary routing instead of tautology");
            Check(phase13.Contains("TryDecodePlaybackState", StringComparison.Ordinal)
                  && phase13.Contains("QuaternionRoundtripEquivalent", StringComparison.Ordinal)
                  && !phase13.Contains("SentBinaryFrames(7)[0][14]", StringComparison.Ordinal),
                "134-33E-3: Phase 13 decodes PlaybackState and asserts quaternion equivalence");
        }

        private static void VerifySourceInspectionAnchoring()
        {
            var phase13 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase13Validation.cs");

            Check(phase13.Contains("ReadRepoText(\"Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs\")", StringComparison.Ordinal)
                  && phase13.Contains("ReadRepoText(\"Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs\")", StringComparison.Ordinal)
                  && !phase13.Contains("File.ReadAllText(\"Packages/dev.unity2foxglove.sdk", StringComparison.Ordinal),
                "134-33F-1: Phase 13 source inspections are anchored through repository-root helpers");
        }

        private static void VerifyPhase16Robustness()
        {
            var phase16 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");

            Check(phase16.Contains("TryRunGitLsFiles", StringComparison.Ordinal)
                  && phase16.Contains("git unavailable; skipping tracked private workspace boundary checks", StringComparison.Ordinal)
                  && !phase16.Contains("throw new Exception($\"git ls-files failed", StringComparison.Ordinal),
                "134-33G-1: Phase 16 private-boundary git checks warn/skip when git is unavailable");
            Check(phase16.Contains("CountPythonParensOutsideStrings", StringComparison.Ordinal)
                  && phase16.Contains("PythonLineEndsWithDeclarationColonOutsideStrings", StringComparison.Ordinal),
                "134-33G-2: Phase 16 Python declaration parser ignores string literals and comments");
        }

        private static void VerifyFakeTransportSnapshots()
        {
            var phase13 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase13Validation.cs");

            Check(phase13.Contains("private readonly object _gate", StringComparison.Ordinal)
                  && phase13.Contains("frames.ToArray()", StringComparison.Ordinal),
                "134-33H-1: Phase 13 fake transport snapshots binary frames under a lock");
        }

        private static void VerifyDeadCodeRemoval()
        {
            var phase6 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase6Validation.cs");

            Check(!phase6.Contains("Actually simpler", StringComparison.Ordinal)
                  && !phase6.Contains("new byte[1 + 4 + 4 + 4 + encBytes.Length]", StringComparison.Ordinal),
                "134-33I-1: Phase 6 service-call failure test no longer carries abandoned frame-building dead code");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
