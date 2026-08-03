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
            VerifyRemoteMcapTokenAndCleanupAreCached();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 164-2: " + _passed + " checks passed.\n");
        }

        private static void VerifyNativeBridgeSceneGateIsSharedAndCached()
        {
            var gate = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityNativeBridgeLifecycleGate.cs");
            var transform = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
            var imu = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs");
            var pointCloud = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            var camera = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");

            Check(gate.Contains("internal static class Ros2ForUnityNativeBridgeLifecycleGate", StringComparison.Ordinal)
                  && gate.Contains("_lastRefreshedActiveSceneHandle", StringComparison.Ordinal)
                  && gate.Contains("_unsafeSceneHandles", StringComparison.Ordinal)
                  && gate.Contains("IsActiveSceneCacheCurrent", StringComparison.Ordinal)
                  && gate.Contains("BuildUnsafeSceneHandles", StringComparison.Ordinal)
                  && gate.Contains("ResetForSubsystemRegistration()", StringComparison.Ordinal),
                "164-2A-1: R2FU native bridges share a cached lifecycle scene shutdown gate");
            Check(UsesSceneGate(transform)
                  && UsesSceneGate(imu)
                  && UsesSceneGate(pointCloud)
                  && UsesSceneGate(camera),
                "164-2A-2: all R2FU native bridges use the shared scene gate from IsShuttingDown and bootstrap");
        }

        private static bool UsesSceneGate(string source)
            => source.Contains("Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene)", StringComparison.Ordinal)
               && source.Contains("Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge", StringComparison.Ordinal)
               && source.Contains("Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene)", StringComparison.Ordinal);

        private static void VerifyNativeBridgeEditorWarmupStopsPollingTime()
        {
            var gate = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityNativeBridgeLifecycleGate.cs");
            Check(gate.Contains("EditorPlayModeStableDelaySeconds", StringComparison.Ordinal)
                  && gate.Contains("_editorEnteredPlayModeAt = EditorApplication.timeSinceStartup", StringComparison.Ordinal)
                  && gate.Contains("EditorApplication.update += OnEditorUpdateUntilPlayModeStable", StringComparison.Ordinal)
                  && gate.Contains("EditorApplication.update -= OnEditorUpdateUntilPlayModeStable", StringComparison.Ordinal)
                  && gate.Contains("if (EditorApplication.timeSinceStartup - _editorEnteredPlayModeAt < EditorPlayModeStableDelaySeconds)", StringComparison.Ordinal),
                "164-2B-1: shared lifecycle gate polls EditorApplication time only until Play Mode is stable");

            foreach (var relativePath in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs"
            })
            {
                var source = ReadRepoText(relativePath);
                Check(!source.Contains("EditorApplication.timeSinceStartup", StringComparison.Ordinal)
                      && source.Contains("Ros2ForUnityNativeBridgeLifecycleGate", StringComparison.Ordinal),
                    "164-2B-2: " + Path.GetFileName(relativePath) + " delegates Editor warmup timing to the shared lifecycle gate");
            }
        }

        private static void VerifyManagerAvoidsDisabledDiagnosticsWrites()
        {
            var diagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var method = SourceMethod(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()");

            Check(method.Contains("if (_statisticsState.FrameStallDiagnosticsWasEnabled)", StringComparison.Ordinal)
                  && method.Contains("_statisticsState.ResetFrameStallDiagnostics();", StringComparison.Ordinal)
                  && method.Contains("_statisticsState.FrameStallDiagnosticsWasEnabled = false;", StringComparison.Ordinal)
                  && method.IndexOf("if (_statisticsState.FrameStallDiagnosticsWasEnabled)", StringComparison.Ordinal)
                     < method.IndexOf("_statisticsState.ResetFrameStallDiagnostics();", StringComparison.Ordinal),
                "164-2C: disabled frame-stall diagnostics reset state once on transition");
        }

        private static void VerifyManagerCachesRemoteReplayPath()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var replayState = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/ReplayRuntimeState.cs");
            var serverResources = PhaseValidationSourceHelpers.ReadFoxgloveManagerServerSources();
            var refresh = SourceMethod(serverResources, "private void RefreshRemoteMcapFileServerIfNeeded()");

            Check(replayState.Contains("internal string CachedResolvedReplayFilePath;", StringComparison.Ordinal)
                  && replayState.Contains("internal string CachedReplayFilePathInput;", StringComparison.Ordinal)
                  && manager.Contains("private readonly ReplayRuntimeState _replayState = new ReplayRuntimeState();", StringComparison.Ordinal)
                  && manager.Contains("InvalidateResolvedReplayFilePathCache();", StringComparison.Ordinal),
                "164-2D-1: FoxgloveManager owns a replay-file resolution cache invalidated from validation");
            Check(serverResources.Contains("private string ResolveReplayFilePathCached()", StringComparison.Ordinal)
                  && refresh.Contains("ResolveReplayFilePathCached()", StringComparison.Ordinal)
                  && !refresh.Contains("ResolveProjectPath(_replayFilePath)", StringComparison.Ordinal),
                "164-2D-2: remote MCAP refresh reuses cached replay-file resolution");
        }

        private static void VerifyRemoteMcapTokenAndCleanupAreCached()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var serverLifecycle = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var serverResources = PhaseValidationSourceHelpers.ReadFoxgloveManagerServerSources();
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var mcapEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var stopServer = SourceMethod(serverLifecycle, "private void StopServer(bool restoreLivePublishers)");
            var refresh = SourceMethod(serverResources, "private void RefreshRemoteMcapFileServerIfNeeded()");

            Check(manager.Contains("public const string RemoteMcapFileServerTokenEnvironmentVariable = \"FOXGLOVE_REMOTE_MCAP_TOKEN\";", StringComparison.Ordinal)
                  && manager.Contains("[SerializeField, HideInInspector] private string _remoteMcapFileServerToken = \"\";", StringComparison.Ordinal)
                  && editor.Contains("_remoteMcapFileServerTokenProperty", StringComparison.Ordinal)
                  && mcapEditor.Contains("DrawPasswordProperty(\"_remoteMcapFileServerToken\", \"Bearer Token\")", StringComparison.Ordinal),
                "164-2D2-1: Remote MCAP bearer token is env-first and drawn as a password field");
            Check(serverResources.Contains("RequiredBearerToken = ResolveRemoteMcapFileServerToken()", StringComparison.Ordinal)
                  && refresh.Contains("var token = ResolveRemoteMcapFileServerToken();", StringComparison.Ordinal)
                  && refresh.Contains("string.Equals(_remoteMcapFileServerKnownToken, token, System.StringComparison.Ordinal)", StringComparison.Ordinal)
                  && serverResources.Contains("_remoteMcapFileServerKnownToken = ResolveRemoteMcapFileServerToken();", StringComparison.Ordinal),
                "164-2D2-2: resolved Remote MCAP bearer token participates in server options and refresh cache");
            Check(stopServer.Contains("DetachRuntimeForwarders(_runtime?.Session);", StringComparison.Ordinal)
                  && stopServer.IndexOf("DetachRuntimeForwarders(_runtime?.Session);", StringComparison.Ordinal)
                     < stopServer.IndexOf("if (_runtime?.Session == null)", StringComparison.Ordinal),
                "164-2D2-3: StopServer detaches runtime forwarders before the not-running early return");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-2\", \"Phase 164-2: phase164-2 optimization regression coverage for runtime lifecycle hot paths\", Phase164_2Validation.Validate", StringComparison.Ordinal),
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
