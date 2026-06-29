// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase164-2 optimization regression coverage for runtime lifecycle hot paths.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase164_2Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-2 Tests ---");
            _passed = 0;

            VerifyNativeBridgeSceneGateIsSharedAndCached();
            VerifyNativeBridgeEditorWarmupStopsPollingTime();
            VerifyManagerAvoidsDisabledDiagnosticsWrites();
            VerifyManagerCachesRemoteReplayPath();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 164-2: " + _passed + " checks passed.\n");
        }

        private static void VerifyNativeBridgeSceneGateIsSharedAndCached()
        {
            var transform = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
            var imu = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs");
            var pointCloud = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            var camera = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");

            Check(transform.Contains("internal static class Ros2ForUnityNativeBridgeSceneGate", StringComparison.Ordinal)
                  && transform.Contains("_cachedFrame = Time.frameCount", StringComparison.Ordinal)
                  && transform.Contains("if (_cachedFrame == frame", StringComparison.Ordinal)
                  && transform.Contains("Ros2ForUnityNativeBridgeSceneGate.Reset();", StringComparison.Ordinal),
                "164-2A-1: R2FU native bridges share a cached per-frame scene shutdown gate");
            Check(UsesSceneGate(transform)
                  && UsesSceneGate(imu)
                  && UsesSceneGate(pointCloud)
                  && UsesSceneGate(camera),
                "164-2A-2: all R2FU native bridges use the shared scene gate from IsShuttingDown and bootstrap");
        }

        private static bool UsesSceneGate(string source)
            => source.Contains("Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(IsEditorPlayModeTransition())", StringComparison.Ordinal)
               && source.Contains("Ros2ForUnityNativeBridgeSceneGate.IsBackupScene(gameObject.scene)", StringComparison.Ordinal)
               && source.Contains("Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(editorTransition: false)", StringComparison.Ordinal);

        private static void VerifyNativeBridgeEditorWarmupStopsPollingTime()
        {
            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs"
            })
            {
                var source = ReadRepoText(relativePath);
                Check(source.Contains("_editorPlayModeStable", StringComparison.Ordinal)
                      && source.Contains("if (_editorPlayModeStable)", StringComparison.Ordinal)
                      && source.Contains("_editorPlayModeStable = elapsed >= 3.0", StringComparison.Ordinal),
                    "164-2B: " + Path.GetFileName(relativePath) + " stops reading EditorApplication.timeSinceStartup after warmup");
            }
        }

        private static void VerifyManagerAvoidsDisabledDiagnosticsWrites()
        {
            var diagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var method = SourceMethod(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()");

            Check(method.Contains("if (_frameStallDiagnosticsWasEnabled)", StringComparison.Ordinal)
                  && method.Contains("_lastFrameStallDiagnosticsTime = 0d;", StringComparison.Ordinal)
                  && method.IndexOf("if (_frameStallDiagnosticsWasEnabled)", StringComparison.Ordinal)
                     < method.IndexOf("_lastFrameStallDiagnosticsTime = 0d;", StringComparison.Ordinal),
                "164-2C: disabled frame-stall diagnostics reset state once on transition");
        }

        private static void VerifyManagerCachesRemoteReplayPath()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var refresh = SourceMethod(server, "private void RefreshRemoteMcapFileServerIfNeeded()");

            Check(manager.Contains("private string _cachedResolvedReplayFilePath;", StringComparison.Ordinal)
                  && manager.Contains("private string _cachedReplayFilePathInput;", StringComparison.Ordinal)
                  && manager.Contains("InvalidateResolvedReplayFilePathCache();", StringComparison.Ordinal),
                "164-2D-1: FoxgloveManager owns a replay-file resolution cache invalidated from validation");
            Check(server.Contains("private string ResolveReplayFilePathCached()", StringComparison.Ordinal)
                  && refresh.Contains("ResolveReplayFilePathCached()", StringComparison.Ordinal)
                  && !refresh.Contains("ResolveProjectPath(_replayFilePath)", StringComparison.Ordinal),
                "164-2D-2: remote MCAP refresh reuses cached replay-file resolution");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-2\", \"Phase 164-2\", Phase164_2Validation.Validate", StringComparison.Ordinal),
                "164-2E-1: validation registry exposes Phase164-2");
            Check(project.Contains("<Compile Include=\"Phase164_2Validation.cs\" />", StringComparison.Ordinal),
                "164-2E-2: runtime validation project compiles Phase164-2");
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Could not slice method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not locate repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
