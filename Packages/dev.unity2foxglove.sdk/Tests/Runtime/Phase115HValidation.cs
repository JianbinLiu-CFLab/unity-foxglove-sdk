// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates the post-Phase105 comment governance refresh boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115HValidation
    {
        private const string EvidencePath = "Developer/100 Phase115H Post105 Comment Governance Review.md";
        private const string RuntimeScripts =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115H: Post-105 Comment Governance Refresh ===");
            _passed = 0;

            VerifyEvidenceInventory();
            VerifyValidationWiring();
            VerifyVendoredLocalPatchGovernance();
            VerifyFoxRunSchemaEvidenceComments();
            VerifyReplayCommentGovernance();
            VerifyR2FUCommentGovernance();

            Console.WriteLine($"Phase 115H: {_passed} checks passed.");
        }

        private static void VerifyEvidenceInventory()
        {
            Check(File.Exists(RepoPath(EvidencePath)),
                "115H-A1: Developer evidence document exists");

            var evidence = ReadRepoText(EvidencePath);
            CheckContainsAll(
                evidence,
                "115H-A2: evidence records the git-derived baseline and current branch",
                "Baseline range: `1fc37db..HEAD`",
                "Execution HEAD:",
                "Branch: `feature/phase115h-post105-comment-governance-refresh`");
            CheckContainsAll(
                evidence,
                "115H-A3: evidence records comment-governance history and stash quarantine",
                "Last completed large-scale comment governance pass",
                "PR #100",
                "1fc37db60896e650f0f0c65b98fc8842f9447734",
                "0fb74ffc7cafd82356068563f4623199a6aadffc",
                "refs/stash");
            CheckContainsAll(
                evidence,
                "115H-A4: evidence records deliberate source exclusions",
                "Generated/golden exclusions",
                "FoxRunGenerationModelFixture_FoxRun.golden.cs",
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs",
                "generated protobuf",
                "binary/native artifacts");
            CheckContainsAll(
                evidence,
                "115H-A5: evidence records post-105 audit buckets",
                "Self-owned source inventory",
                "Modified third-party/vendored candidates",
                "Phase 111F/138B",
                "Phase 112-115G");
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("--phase115h", StringComparison.Ordinal)
                  && program.Contains("RunPhase115HOnly", StringComparison.Ordinal)
                  && program.Contains("Phase115HValidation.Validate()", StringComparison.Ordinal),
                "115H-B1: Program.cs wires --phase115h");
            Check(project.Contains("Phase115HValidation.cs", StringComparison.Ordinal),
                "115H-B2: runtime test project compiles Phase115HValidation");
        }

        private static void VerifyVendoredLocalPatchGovernance()
        {
            foreach (var relative in PatchedVendorFiles())
            {
                var source = ReadRepoText(RuntimeScripts + "/" + relative);
                Check(source.Contains("Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.", StringComparison.Ordinal),
                    "115H-C1: modified vendored file carries local modifications copyright: " + relative);
            }

            foreach (var relative in KnownLocalPatchMarkerFiles())
            {
                var source = ReadRepoText(RuntimeScripts + "/" + relative);
                Check(source.Contains("U2F-LOCAL-PATCH", StringComparison.Ordinal),
                    "115H-C2: meaningful vendored lifecycle patch carries U2F-LOCAL-PATCH: " + relative);
            }

            foreach (var relative in UnmodifiedVendoredExamples())
            {
                var source = ReadRepoText(RuntimeScripts + "/" + relative);
                Check(!source.Contains("Modifications Copyright", StringComparison.Ordinal)
                      && !source.Contains("U2F-LOCAL-PATCH", StringComparison.Ordinal),
                    "115H-C3: copied upstream-only vendored file is not marked as locally modified: " + relative);
            }

            var generator = ReadRepoText("Scripts/release/build_r2fu_runtime_package.py");
            var validator = ReadRepoText("Scripts/release/validate_r2fu_runtime_package.py");
            CheckContainsAll(
                generator,
                "115H-C4: runtime generator preserves local patch overlays",
                "collect_local_patch_overlays",
                "apply_local_patch_overlays",
                "U2F-LOCAL-PATCH",
                "Modifications Copyright");
            CheckContainsAll(
                validator,
                "115H-C5: runtime package validator knows local patch governance tokens",
                "check_runtime_source_patches",
                "U2F-LOCAL-PATCH",
                "Modifications Copyright");
        }

        private static void VerifyFoxRunSchemaEvidenceComments()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs",
                "public sealed class FoxRunGenerationModel",
                "115H-D1: FoxRun generation model summary documents semantic source shared by emission and descriptor evidence",
                "semantic", "emission", "descriptor");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs",
                "public readonly string RawObservedTypeName;",
                "115H-D2: raw observed type name comment keeps provenance separate from generated C#",
                "provenance", "debug");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs",
                "public readonly string EmissionTypeName;",
                "115H-D3: emission type name comment documents generated C# boundary",
                "generated C#");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs",
                "public readonly string CanonicalType;",
                "115H-D4: canonical type comment documents schema identity boundary",
                "schema", "identity");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs",
                "internal sealed class Unity2FoxgloveSchemaEvidenceSettings",
                "115H-D5: schema evidence settings comment documents Project Settings and Manager sync ownership",
                "Project Settings", "Manager");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/SchemaEvidenceSidecarWriter.cs",
                "public static SchemaEvidenceSidecarResult StageSidecar",
                "115H-D6: sidecar staging comment documents delayed publish lifecycle",
                "staged", "recording startup");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaMcapMetadata.cs",
                "public static class FoxRunSchemaMcapMetadata",
                "115H-D7: FoxRun MCAP schema metadata comment avoids ordinary topic inventory overclaim",
                "FoxRun", "contract", "not", "topic inventory");
        }

        private static void VerifyReplayCommentGovernance()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs",
                "public void Enable(",
                "115H-E1: replay enable comments distinguish Strict/Warn/Off behavior",
                "Strict", "Warn", "Off");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs",
                "internal sealed class McapReplayPreflightDrawer",
                "115H-E2: replay preflight drawer summary documents advisory pre-Play-Mode boundary",
                "advisory", "Play Mode");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPoseOwnershipArbiter.cs",
                "public sealed class ReplayPoseOwnershipArbiter",
                "115H-E3: replay pose arbiter summary documents behavior-based per-Transform ownership",
                "behavior", "Transform");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPoseOwnershipArbiter.cs",
                "public ReplayPoseOwnershipDecision OfferPose(",
                "115H-E4: replay pose offer comment avoids topic-name priority as ownership mechanism",
                "behavior", "channel", "not topic");
        }

        private static void VerifyR2FUCommentGovernance()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Context.cs",
                "public interface IUnity2FoxgloveRos2Context",
                "115H-F1: ROS2 facade context summary documents optional package boundary",
                "optional", "package");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2ContextFactory.cs",
                "public static class Unity2FoxgloveRos2ContextFactory",
                "115H-F2: ROS2 context factory summary avoids implying bundled ROS2 runtime",
                "facade", "not bundle");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2UnavailableContext.cs",
                "public sealed class Unity2FoxgloveRos2UnavailableContext",
                "115H-F3: unavailable context summary documents no bundled runtime behavior",
                "not bundled", "facade");
            CheckContainsAll(
                ReadRepoText("Scripts/smoke/phase138b_r2fu_jazzy_windows_build.py"),
                "115H-F4: Phase138B smoke script documents Python orchestrator and upstream PowerShell boundary",
                "Python orchestrator",
                "not a project-owned PowerShell",
                "upstream build entry points");
        }

        private static IEnumerable<string> PatchedVendorFiles()
        {
            return new[]
            {
                "ROS2ForUnity.cs",
                "ROS2Node.cs",
                "ROS2UnityComponent.cs",
                "ROS2UnityCore.cs",
                "Sensor.cs",
                "Time/DotnetTimeSource.cs",
                "Time/ROS2Clock.cs",
                "Time/ROS2ScalableTimeSource.cs",
                "Time/ROS2TimeSource.cs",
                "Time/TimeUtils.cs"
            };
        }

        private static IEnumerable<string> KnownLocalPatchMarkerFiles()
        {
            return new[]
            {
                "ROS2ForUnity.cs",
                "ROS2Node.cs",
                "Sensor.cs",
                "Time/ROS2Clock.cs",
                "Time/ROS2ScalableTimeSource.cs",
                "Time/ROS2TimeSource.cs",
                "Time/TimeUtils.cs"
            };
        }

        private static IEnumerable<string> UnmodifiedVendoredExamples()
        {
            return new[]
            {
                "Time/ITimeSource.cs",
                "Time/UnityTimeSource.cs",
                "Transformations.cs"
            };
        }

        private static void CheckContainsAll(string text, string message, params string[] requiredTerms)
        {
            Check(requiredTerms.All(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0), message);
        }

        private static void CheckSummaryBefore(string relativePath, string declaration, string message, params string[] requiredTerms)
        {
            var text = ReadRepoText(relativePath);
            var window = WindowBefore(text, declaration, 18);
            var ok = window.Contains("/// <summary>", StringComparison.Ordinal)
                     && requiredTerms.All(term => window.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(ok, message);
        }

        private static string WindowBefore(string text, string declaration, int lookbackLines)
        {
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var index = Array.FindIndex(lines, line => line.Contains(declaration, StringComparison.Ordinal));
            if (index < 0)
                throw new InvalidOperationException("Phase115H could not find declaration: " + declaration);

            var start = Math.Max(0, index - lookbackLines);
            return string.Join("\n", lines.Skip(start).Take(index - start));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase115H file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase115H validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
