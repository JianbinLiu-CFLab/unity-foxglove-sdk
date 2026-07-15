// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 165 validation for R2FU native bridge hot-path lifecycle guards.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for R2FU native bridge lifecycle hot paths.</summary>
    internal static class R2fuNativeBridgeHotPathLifecycleValidation
    {
        private const string AdapterPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string NativeDir = AdapterPackage + "/Runtime/Native";
        private const string LifecycleGateFile = NativeDir + "/Ros2ForUnityNativeBridgeLifecycleGate.cs";

        private static readonly string[] BridgeFiles =
        {
            "Ros2ForUnityTransformNativeBridge.cs",
            "Ros2ForUnityPointCloud2NativeBridge.cs",
            "Ros2ForUnityImuNativeBridge.cs",
            "Ros2ForUnityCameraNativeBridge.cs",
        };

        private static readonly string[] CameraBindingFiles =
        {
            "Ros2ForUnityCameraCompressedImageBinding.cs",
            "Ros2ForUnityCameraRawImageBinding.cs",
            "Ros2ForUnityCameraInfoBinding.cs",
        };

        private static int _passed;

        /// <summary>Runs the Phase 165 hot-path lifecycle validation.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- R2FU Native Bridge Hot Path Lifecycle Validation ---");
            _passed = 0;

            VerifySharedLifecycleGateShape();
            VerifyBridgeHotPathsUseCheapLifecycleReads();
            VerifyPointCloud2PublishersPrewarmOutsideFrameCallback();
            VerifyReadyLogsAvoidEditorStackTraceExtraction();
            VerifyCameraBindingHotPathsRemainSceneFree();
            VerifyPhase161LifecycleValidationRecognizesSharedGate();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"R2FU native bridge hot path performance: {_passed} checks passed.");
        }

        private static void VerifySharedLifecycleGateShape()
        {
            var source = ReadRepoText(LifecycleGateFile);

            Check(source.Contains("internal static class Ros2ForUnityNativeBridgeLifecycleGate", StringComparison.Ordinal),
                "165-A1: standalone native bridge lifecycle gate exists");
            Check(source.Contains("RuntimeInitializeLoadType.SubsystemRegistration", StringComparison.Ordinal)
                  && source.Contains("RuntimeInitializeLoadType.AfterSceneLoad", StringComparison.Ordinal),
                "165-A2: lifecycle gate resets and refreshes around Unity load phases");
            Check(source.Contains("SceneManager.sceneLoaded", StringComparison.Ordinal)
                  && source.Contains("SceneManager.activeSceneChanged", StringComparison.Ordinal),
                "165-A3: lifecycle gate refreshes scene state from scene lifecycle events");
            Check(source.Contains("Application.quitting", StringComparison.Ordinal)
                  && source.Contains("EditorApplication.playModeStateChanged", StringComparison.Ordinal)
                  && source.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal),
                "165-A4: lifecycle gate owns application, editor play-mode, and assembly reload shutdown windows");
            Check(source.Contains("Temp/__Backupscenes", StringComparison.Ordinal)
                  && source.Contains("SceneManager.sceneCount", StringComparison.Ordinal)
                  && source.Contains("SceneManager.GetSceneAt", StringComparison.Ordinal)
                  && source.Contains("scene.path", StringComparison.Ordinal)
                  && source.Contains("scene.name", StringComparison.Ordinal)
                  && source.Contains("EndsWith(\".backup\"", StringComparison.Ordinal),
                "165-A5: lifecycle gate preserves backup-scene detection coverage");
            Check(source.Contains("IsShuttingDownForBridge", StringComparison.Ordinal)
                  && source.Contains("IsBridgeSceneUnsafe", StringComparison.Ordinal)
                  && source.Contains("CanInitializeNativeRuntimeForBridge", StringComparison.Ordinal)
                  && source.Contains("IsStablePlayModeScene", StringComparison.Ordinal),
                "165-A6: lifecycle gate exposes cheap bridge-facing lifecycle state");
            Check(!source.Contains("Time.frameCount", StringComparison.Ordinal),
                "165-A7: lifecycle gate does not rely on per-frame scene-query memoization");
            Check(source.Contains("EditorPlayModeStableDelaySeconds = 3.0", StringComparison.Ordinal)
                  && source.Contains("early Editor Play Mode", StringComparison.Ordinal),
                "165-A8: lifecycle gate documents the intentional early Play Mode native bootstrap delay");
            CheckHotPathFreeOfSceneQueries(RequiredMethod(source, "internal static bool IsShuttingDownForBridge", "Ros2ForUnityNativeBridgeLifecycleGate.cs")
                                          + "\n" + RequiredMethod(source, "internal static bool IsBridgeSceneUnsafe", "Ros2ForUnityNativeBridgeLifecycleGate.cs"),
                "165-A9: lifecycle gate bridge-facing methods stay allocation-free scene-handle reads");
            Check(source.Contains("EditorApplication.hierarchyChanged", StringComparison.Ordinal)
                  && source.Contains("EditorSceneManager.sceneOpened", StringComparison.Ordinal)
                  && source.Contains("EditorSceneManager.sceneClosed", StringComparison.Ordinal),
                "165-A10: lifecycle gate refreshes cached scene state from editor hierarchy and scene restore events");

            var hierarchyHandler = RequiredMethod(source, "private static void OnEditorHierarchyChanged", "Ros2ForUnityNativeBridgeLifecycleGate.cs");
            Check(hierarchyHandler.Contains("_isStablePlayModeScene = false", StringComparison.Ordinal)
                  && hierarchyHandler.Contains("_sceneStateDirty = true", StringComparison.Ordinal)
                  && !hierarchyHandler.Contains("RefreshSceneState();", StringComparison.Ordinal),
                "165-A11: editor hierarchy refresh conservatively closes native bootstrap and defers scene rebuild");
            Check(source.Contains("_lastRefreshedActiveSceneHandle", StringComparison.Ordinal)
                  && source.Contains("_lastRefreshedActiveSceneHandle = activeScene.handle", StringComparison.Ordinal)
                  && source.Contains("SceneManager.GetActiveScene().handle == _lastRefreshedActiveSceneHandle", StringComparison.Ordinal)
                  && source.Contains("CanBootstrapBridge", StringComparison.Ordinal)
                  && source.Contains("IsActiveSceneCacheCurrent", StringComparison.Ordinal)
                  && source.Contains("!IsActiveSceneCacheCurrent || IsBridgeSceneUnsafe(ownerScene)", StringComparison.Ordinal),
                "165-A12: lifecycle gate fail-closes bridge bootstrap when active scene changes before event refresh");
            var nativeInitGate = RequiredMethod(source, "internal static bool CanInitializeNativeRuntimeForBridge", "Ros2ForUnityNativeBridgeLifecycleGate.cs");
            Check(nativeInitGate.Contains("RefreshSceneStateIfNeeded();", StringComparison.Ordinal)
                  && nativeInitGate.Contains("!_nativeReloadWindow", StringComparison.Ordinal)
                  && nativeInitGate.Contains("_isStablePlayModeScene", StringComparison.Ordinal)
                  && nativeInitGate.Contains("!IsBridgeSceneUnsafe(ownerScene)", StringComparison.Ordinal),
                "165-A13: lifecycle gate refreshes dirty scene state before cold native runtime initialization");
            Check(source.Contains("private static volatile bool _sceneStateDirty = true", StringComparison.Ordinal)
                  && source.Contains("private static volatile int _unsafeSceneHandleCount", StringComparison.Ordinal)
                  && source.Contains("EnsureUnsafeSceneHandleCapacity", StringComparison.Ordinal)
                  && !source.Contains("new int[Math.Max(SceneManager.sceneCount, 1)]", StringComparison.Ordinal),
                "173-055-E1: lifecycle gate reuses unsafe-scene handle storage and avoids getter-time array churn");
        }

        private static void VerifyBridgeHotPathsUseCheapLifecycleReads()
        {
            foreach (var bridge in BridgeFiles)
            {
                var source = ReadRepoText(NativeDir + "/" + bridge);
                var updateBody = RequiredMethod(source, "private void Update()", bridge);
                var tryGetBody = RequiredMethod(source, "private bool TryGetRos2Unity", bridge);
                var ensureBody = RequiredMethod(source, "private bool EnsureRos2UnityReady()", bridge);
                var hotSource = updateBody + "\n" + tryGetBody + "\n" + ensureBody;
                var tryEnsureBody = PhaseValidationSourceHelpers.SourceMethod(source, "private bool TryEnsurePublisher");
                if (tryEnsureBody.Length > 0)
                    hotSource += "\n" + tryEnsureBody;

                if (bridge.Contains("Transform", StringComparison.Ordinal))
                    hotSource += "\n" + RequiredMethod(source, "private void OnFrameTransformReady", bridge);
                else if (bridge.Contains("PointCloud2", StringComparison.Ordinal))
                    hotSource += "\n" + RequiredMethod(source, "private void OnPointCloud2NativeFrameReady", bridge);
                else if (bridge.Contains("Imu", StringComparison.Ordinal))
                    hotSource += "\n" + RequiredMethod(source, "private void OnFrameReady", bridge);

                Check(source.Contains("Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge", StringComparison.Ordinal),
                    "165-B1: " + bridge + " delegates shutdown checks to the shared lifecycle gate");
                Check(ensureBody.Contains("Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene)", StringComparison.Ordinal)
                      && ensureBody.IndexOf("CanInitializeNativeRuntimeForBridge", StringComparison.Ordinal)
                         < ensureBody.IndexOf("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                      && ensureBody.IndexOf("CanInitializeNativeRuntimeForBridge", StringComparison.Ordinal)
                         < ensureBody.IndexOf("_ros2Unity.Ok()", StringComparison.Ordinal),
                    "165-B1b: " + bridge + " refreshes lifecycle state before first-initializing R2FU native runtime");
                Check(!source.Contains("Ros2ForUnityNativeBridgeSceneGate", StringComparison.Ordinal)
                      && !source.Contains("InitializeEditorPlayModeGate", StringComparison.Ordinal)
                      && !source.Contains("IsEditorPlayModeTransition()", StringComparison.Ordinal),
                    "165-B2: " + bridge + " no longer owns duplicate scene/editor lifecycle helpers");
                CheckHotPathFreeOfSceneQueries(hotSource, "165-B3: " + bridge + " hot paths avoid scene queries and path/name backup checks");
                Check(!updateBody.Contains("FindObjectsByType", StringComparison.Ordinal)
                      && updateBody.IndexOf("Time.unscaledTime", StringComparison.Ordinal) < updateBody.IndexOf("RefreshBindings();", StringComparison.Ordinal),
                    "165-B4: " + bridge + " object scans remain behind the scan interval gate");
            }
        }

        private static void VerifyPointCloud2PublishersPrewarmOutsideFrameCallback()
        {
            var source = ReadRepoText(NativeDir + "/Ros2ForUnityPointCloud2NativeBridge.cs");
            var refreshBody = RequiredMethod(source, "private void RefreshBindings()", "Ros2ForUnityPointCloud2NativeBridge.cs");
            var registerBody = RequiredMethod(source, "private void RegisterPublisherBinding", "Ros2ForUnityPointCloud2NativeBridge.cs");
            var callbackBody = RequiredMethod(source, "private void OnPointCloud2NativeFrameReady", "Ros2ForUnityPointCloud2NativeBridge.cs");
            var prewarmBody = RequiredMethod(source, "public void PrewarmPublishers", "Ros2ForUnityPointCloud2NativeBridge.cs");
            var deskewPrewarmBody = RequiredMethod(source, "private string ResolvePrewarmDeskewedTopic", "Ros2ForUnityPointCloud2NativeBridge.cs");

            Check(refreshBody.Contains("RegisterPublisherBinding(publisher)", StringComparison.Ordinal)
                  && registerBody.Contains("PrewarmPublishers(_ros2Unity)", StringComparison.Ordinal),
                "165-PC1: PointCloud2 bridge prewarms DDS publishers while scan refresh registers bindings");
            Check(prewarmBody.Contains("TryEnsurePublisher(ros2Unity, Topic", StringComparison.Ordinal)
                  && prewarmBody.Contains("ResolvePrewarmDeskewedTopic()", StringComparison.Ordinal)
                  && prewarmBody.Contains("TryEnsurePublisher(ros2Unity, deskewedTopic", StringComparison.Ordinal),
                "165-PC2: PointCloud2 bridge prewarms both raw and deskewed DDS publishers");
            Check(prewarmBody.Contains("PrewarmTfAnchorPublisher()", StringComparison.Ordinal),
                "165-PC2b: PointCloud2 bridge prewarms the optional TF anchor publisher outside frame callbacks");
            Check(deskewPrewarmBody.Contains("PointCloudMotionCompensationOutputPolicy.RawOnly", StringComparison.Ordinal)
                  && deskewPrewarmBody.Contains("PointCloudMotionCompensationOutputPolicy.ReplaceOutput", StringComparison.Ordinal)
                  && deskewPrewarmBody.Contains("MotionCompensatedPointCloud2Topic", StringComparison.Ordinal),
                "165-PC3: PointCloud2 bridge prewarm respects motion-compensation output policy");
            Check(callbackBody.Contains("TryEnsurePublisher(ros2Unity, frameTopic", StringComparison.Ordinal),
                "165-PC4: PointCloud2 frame callback keeps lazy publisher creation as a configuration-change fallback");
        }

        private static void VerifyReadyLogsAvoidEditorStackTraceExtraction()
        {
            foreach (var bridge in new[]
                     {
                         "Ros2ForUnityTransformNativeBridge.cs",
                         "Ros2ForUnityPointCloud2NativeBridge.cs",
                         "Ros2ForUnityImuNativeBridge.cs",
                     })
            {
                var source = ReadRepoText(NativeDir + "/" + bridge);
                var readyBody = bridge.Contains("PointCloud2", StringComparison.Ordinal)
                    ? RequiredMethod(source, "private void LogReady", bridge)
                    : RequiredMethod(source, "private void LogReadyOnce", bridge);

                Check(readyBody.Contains("LogOption.NoStacktrace", StringComparison.Ordinal),
                    "165-PC5: " + bridge + " ready logs avoid Editor stack trace extraction");
            }
        }

        private static void VerifyCameraBindingHotPathsRemainSceneFree()
        {
            foreach (var binding in CameraBindingFiles)
            {
                var source = ReadRepoText(NativeDir + "/" + binding);
                var hotSource = RequiredMethod(source, "private void OnFrameReady", binding)
                    + "\n" + RequiredMethod(source, "private bool TryEnsurePublisher", binding);

                CheckHotPathFreeOfSceneQueries(hotSource, "165-C1: " + binding + " callback hot paths avoid scene queries and path/name backup checks");
            }
        }

        private static void VerifyPhase161LifecycleValidationRecognizesSharedGate()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs");

            Check(source.Contains("Ros2ForUnityNativeBridgeLifecycleGate.cs", StringComparison.Ordinal)
                  && source.Contains("IsShuttingDownForBridge", StringComparison.Ordinal),
                "165-D1: Phase161/162 lifecycle validation recognizes the shared lifecycle gate");
            Check(!source.Contains("source.Contains(\"IsBackupScene(gameObject.scene)\"", StringComparison.Ordinal)
                  && !source.Contains("source.Contains(\"InitializeEditorPlayModeGate\"", StringComparison.Ordinal),
                "165-D2: Phase161/162 lifecycle validation no longer requires duplicated bridge lifecycle helpers");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var entry = PhaseValidationRegistry.Find(new[] { "--phase165" });

            Check(entry != null
                  && entry.Name == "R2FU native bridge hot path performance"
                  && entry.Category == ValidationCategory.CiSafe
                  && entry.Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Performance)
                  && !entry.IncludeInDefault
                  && entry.Run == (Action)Validate,
                "165-E1: validation registry exposes descriptive Phase165 hot-path guard");
            Check(project.Contains("R2fuNativeBridgeHotPathLifecycleValidation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase165Validation.cs", StringComparison.Ordinal),
                "165-E2: runtime validation project compiles descriptive Phase165 validation file");
        }

        private static void CheckHotPathFreeOfSceneQueries(string source, string label)
        {
            Check(!source.Contains("SceneManager.", StringComparison.Ordinal)
                  && !source.Contains("scene.path", StringComparison.Ordinal)
                  && !source.Contains("scene.name", StringComparison.Ordinal)
                  && !source.Contains("gameObject.name", StringComparison.Ordinal)
                  && !source.Contains("IsBackupScene(", StringComparison.Ordinal)
                  && !source.Contains("IsSceneUnsafe", StringComparison.Ordinal),
                label);
        }

        private static string RequiredMethod(string source, string signature, string fileName)
        {
            var body = PhaseValidationSourceHelpers.SourceMethod(source, signature);
            if (body.Length == 0)
                throw new InvalidOperationException("[FAIL] missing method in " + fileName + ": " + signature);
            return body;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
