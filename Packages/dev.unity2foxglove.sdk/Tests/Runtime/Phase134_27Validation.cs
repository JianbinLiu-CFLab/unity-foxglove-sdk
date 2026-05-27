// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-27 regression coverage for ROS2 For Unity sample sync and smoke hardening.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_27Validation
    {
        private const string Phase130BuilderPath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 MarkerArray Acceptance/Phase130MarkerArrayMessageBuilder.cs";
        private const string Phase132SmokePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 Standard Message Expansion/Phase132StandardMessagesSmoke.cs";
        private const string Phase132CameraPath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 Standard Message Expansion/Phase132StandardCameraSource.cs";
        private const string SyncScriptPath = "Scripts/samples/sync_ros2_samples.py";

        private const string PackageSamplesRoot = "Packages/dev.unity2foxglove.ros2forunity/Samples~";
        private const string ImportedSamplesRoot =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1";

        private static readonly HashSet<string> AllowedImportedDrift = new(StringComparer.Ordinal)
        {
            "ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs"
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-27: R2FU Adapter Samples II ===");
            _passed = 0;

            VerifyPhase132TimestampCarry();
            VerifyPhase132CameraAndCleanup();
            VerifyPhase132RuntimeRootEvidence();
            VerifyMarkerArrayDeleteAllAndFrameOverride();
            VerifySyncScript();
            VerifyImportedSamplesAreSynchronized();

            Console.WriteLine($"Phase 134-27: {_passed} checks passed.");
        }

        private static void VerifyPhase132TimestampCarry()
        {
            var source = ReadRepoText(Phase132SmokePath);
            Check(source.Contains("var nanos = (uint)Math.Max(0d, Math.Round(fractional * 1000000000d));", StringComparison.Ordinal)
                  && source.Contains("if (nanos >= 1000000000u)", StringComparison.Ordinal)
                  && source.Contains("sec = checked(sec + 1);", StringComparison.Ordinal)
                  && source.Contains("nanos -= 1000000000u;", StringComparison.Ordinal)
                  && source.Contains("nanosec = nanos;", StringComparison.Ordinal),
                "134-27A-1: Phase132 timestamp rounding carries one-billion nanoseconds into seconds");
            Check(!source.Contains("Math.Min(999999999d", StringComparison.Ordinal)
                  && !source.Contains("nanosec == 1000000000u", StringComparison.Ordinal),
                "134-27A-2: Phase132 timestamp carry branch is no longer made unreachable by clamping");
        }

        private static void VerifyPhase132CameraAndCleanup()
        {
            var smoke = ReadRepoText(Phase132SmokePath);
            var camera = ReadRepoText(Phase132CameraPath);
            var cleanupStart = smoke.IndexOf("private void CleanupRuntime()", StringComparison.Ordinal);
            var cleanupBody = cleanupStart >= 0 ? smoke.Substring(cleanupStart) : string.Empty;
            Check(cleanupBody.Contains("_warnedMissingStartExecutor = false;", StringComparison.Ordinal)
                  && cleanupBody.IndexOf("_executorStarted = false;", StringComparison.Ordinal)
                     < cleanupBody.IndexOf("_warnedMissingStartExecutor = false;", StringComparison.Ordinal),
                "134-27B-1: Phase132 cleanup resets the missing StartExecutor one-shot warning");
            Check(!camera.Contains("data.Length != expectedLength", StringComparison.Ordinal),
                "134-27B-2: Phase132 camera source no longer contains the dead post-allocation length check");
        }

        private static void VerifyPhase132RuntimeRootEvidence()
        {
            var source = ReadRepoText(Phase132SmokePath);
            Check(source.Contains("GetRos2ForUnityPath", StringComparison.Ordinal)
                  && source.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && source.Contains("assembly.GetType(\"ROS2.\" + \"ROS2ForUnity\")", StringComparison.Ordinal),
                "134-27C-1: Phase132 runtime-root evidence uses the ROS2 For Unity assembly-reported root");
            Check(!source.Contains("GetDirectories(packagesRoot", StringComparison.Ordinal)
                  && !source.Contains("dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal),
                "134-27C-2: Phase132 runtime-root evidence no longer scans package runtime directories");
        }

        private static void VerifyMarkerArrayDeleteAllAndFrameOverride()
        {
            var source = ReadRepoText(Phase130BuilderPath);
            Check(source.Contains("BuildDeleteAll(int sec, uint nanosec)", StringComparison.Ordinal)
                  && source.Contains("CreateDeleteAllMarker(sec, nanosec)", StringComparison.Ordinal)
                  && source.Contains("Ns = string.Empty", StringComparison.Ordinal)
                  && source.Contains("Id = 0", StringComparison.Ordinal)
                  && !source.Contains("CreateBaseMarker(\"delete_all\"", StringComparison.Ordinal),
                "134-27D-1: MarkerArray DELETEALL uses ignored namespace/id values explicitly");
            Check(source.Contains("string frameId = DefaultFrameId", StringComparison.Ordinal)
                  && source.Contains("CreateHeader(sec, nanosec, frameId)", StringComparison.Ordinal)
                  && source.Contains("Frame_id = string.IsNullOrWhiteSpace(frameId) ? DefaultFrameId : frameId.Trim()", StringComparison.Ordinal),
                "134-27D-2: MarkerArray builder accepts optional frame overrides while keeping map as default");
        }

        private static void VerifySyncScript()
        {
            var source = ReadRepoText(SyncScriptPath);
            Check(source.Contains("DEFAULT_PACKAGE_ROOT", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_IMPORTED_ROOT", StringComparison.Ordinal)
                  && source.Contains("--apply", StringComparison.Ordinal)
                  && source.Contains("--dry-run", StringComparison.Ordinal),
                "134-27E-1: ROS2 sample sync script has package/imported defaults and apply/dry-run modes");
            Check(source.Contains("Phase110Ros2ForUnityStringSmoke.cs", StringComparison.Ordinal)
                  && source.Contains("IGNORED_SUFFIXES", StringComparison.Ordinal)
                  && source.Contains(".meta", StringComparison.Ordinal),
                "134-27E-2: ROS2 sample sync script preserves imported Phase110 batch harness drift and ignores Unity meta files");
        }

        private static void VerifyImportedSamplesAreSynchronized()
        {
            var packageRoot = RepoPath(PackageSamplesRoot);
            var importedRoot = RepoPath(ImportedSamplesRoot);
            foreach (var packageFile in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(packageRoot, packageFile).Replace('\\', '/');
                if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || AllowedImportedDrift.Contains(relative))
                    continue;

                var importedFile = Path.Combine(importedRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Check(File.Exists(importedFile), "134-27F-1: imported sample file exists for " + relative);
                Check(File.ReadAllBytes(packageFile).AsSpan().SequenceEqual(File.ReadAllBytes(importedFile)),
                    "134-27F-2: imported sample file matches package source for " + relative);
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = RepoPath(relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

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
