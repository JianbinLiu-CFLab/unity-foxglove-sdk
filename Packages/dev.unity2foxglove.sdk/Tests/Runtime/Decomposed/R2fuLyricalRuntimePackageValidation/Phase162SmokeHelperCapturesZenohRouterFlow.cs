// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 146B validation for the R2FU Lyrical Win64 runtime package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class R2fuLyricalRuntimePackageValidation
    {
        private static void Phase162SmokeHelperCapturesZenohRouterFlow()
        {
            var helper = ReadRepoText(Phase162SmokeHelper);
            Check(helper.Contains("--rmw-implementation", StringComparison.Ordinal)
                  && helper.Contains("rmw_zenoh_cpp", StringComparison.Ordinal)
                  && helper.Contains("ZENOH_ROUTER_CHECK_ATTEMPTS", StringComparison.Ordinal)
                  && helper.Contains("--echo-output", StringComparison.Ordinal),
                "162-E1: Zenoh smoke helper exposes explicit RMW and echo-output controls");
            Check(helper.Contains("--zenoh-router", StringComparison.Ordinal)
                  && helper.Contains("router-ready-marker", StringComparison.Ordinal)
                  && helper.Contains("wait_for_marker", StringComparison.Ordinal)
                  && helper.Contains("Started", StringComparison.Ordinal),
                "162-E2: Zenoh smoke helper gates on router readiness from logs");
            Check(helper.Contains("kill_process_tree", StringComparison.Ordinal)
                  && helper.Contains("taskkill", StringComparison.Ordinal)
                  && helper.Contains("--scripting-backend", StringComparison.Ordinal)
                  && helper.Contains("il2cpp", StringComparison.Ordinal)
                  && helper.Contains("topic\",", StringComparison.Ordinal)
                  && helper.Contains("echo\",", StringComparison.Ordinal),
                "162-E3: Zenoh smoke helper has timeout cleanup and IL2CPP-compatible player path");
            Check(helper.Contains("import phase138u_lidar_deskew_rviz2_acceptance as phase138u", StringComparison.Ordinal)
                  && helper.Contains("build_rviz_acceptance_args", StringComparison.Ordinal)
                  && helper.Contains("PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_PASS", StringComparison.Ordinal)
                  && helper.Contains("--echo-only", StringComparison.Ordinal)
                  && helper.Contains("--no-rviz", StringComparison.Ordinal),
                "162-E4: bare Phase162 runs Zenoh RViz2 PointCloud2 acceptance while echo is explicitly opt-in");
            Check(helper.Contains("run_phase138u_with_rviz_capture", StringComparison.Ordinal)
                  && helper.Contains("captured_rviz_processes", StringComparison.Ordinal)
                  && helper.Contains("wait_for_rviz_shutdown_before_router_cleanup", StringComparison.Ordinal)
                  && helper.Contains("--release-router-on-exit", StringComparison.Ordinal),
                "162-E4b: Zenoh smoke helper keeps its auto-started router alive while launched RViz2 remains open");
        }

        private static void Phase162ReadmesDocumentZenohPrerequisites()
        {
            var adapterReadme = ReadRepoText(AdapterPackage + "/README.md");
            var sampleReadme = ReadRepoText(AdapterPackage + "/Samples~/ROS2 For Unity External Adapter/README.md");
            var importedSampleReadme = ReadRepoText("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 For Unity External Adapter/README.md");

            foreach (var pair in new[]
            {
                ("adapter README", adapterReadme, "162-D5"),
                ("external adapter sample README", sampleReadme, "162-D6"),
                ("imported external adapter sample README", importedSampleReadme, "162-D7"),
            })
            {
                Check(pair.Item2.Contains("FastDDS and Zenoh are separate ROS2 communication modes", StringComparison.Ordinal)
                      && pair.Item2.Contains("They do not discover or exchange topics with each other", StringComparison.Ordinal)
                      && pair.Item2.Contains("RMW_IMPLEMENTATION=rmw_zenoh_cpp", StringComparison.Ordinal)
                      && pair.Item2.Contains("RMW_IMPLEMENTATION=rmw_fastrtps_cpp", StringComparison.Ordinal)
                      && pair.Item2.Contains("rmw_zenohd", StringComparison.Ordinal)
                      && pair.Item2.Contains("does not bridge FastDDS traffic", StringComparison.Ordinal),
                    pair.Item3 + ": " + pair.Item1 + " documents Zenoh router and same-RMW prerequisites");
            }
        }

        private static void Phase162LyricalRuntimeStopsExecutorsBeforeZenohShutdown()
        {
            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var componentSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");

            Check(runtimeSource.Contains("shutdownInProgress", StringComparison.Ordinal)
                  && runtimeSource.Contains("private static volatile bool isInitialized = false;", StringComparison.Ordinal)
                  && runtimeSource.Contains("TryBeginShutdownLocked()", StringComparison.Ordinal)
                  && runtimeSource.Contains("CompleteShutdownShared()", StringComparison.Ordinal),
                "162-E5: Lyrical ROS2 context shutdown is serialized across editor reload/play-mode exits");
            var completeShutdown = ExtractMethod(runtimeSource, "private static void CompleteShutdownShared()");
            var finishShutdown = ExtractMethod(runtimeSource, "private static void FinishShutdownShared()");
            Check(completeShutdown.Contains("ROS2UnityComponent.StopAllExecutorsForRosShutdown()", StringComparison.Ordinal)
                  && completeShutdown.Contains("FinishShutdownShared();", StringComparison.Ordinal)
                  && finishShutdown.Contains("Ros2cs.Shutdown()", StringComparison.Ordinal),
                "162-E6: Lyrical runtime stops ROS2 executor threads before unloading Zenoh/RMW through Ros2cs.Shutdown");
            var constructor = ExtractMethod(runtimeSource, "internal ROS2ForUnity()");
            var windowsBlockStart = constructor.IndexOf("if (GetOS() == Platform.Windows)", StringComparison.Ordinal);
            var windowsBlockEnd = constructor.IndexOf("} else {", windowsBlockStart, StringComparison.Ordinal);
            var windowsBlock = windowsBlockStart >= 0 && windowsBlockEnd > windowsBlockStart
                ? constructor.Substring(windowsBlockStart, windowsBlockEnd - windowsBlockStart)
                : string.Empty;
            Check(runtimeSource.Contains("#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN", StringComparison.Ordinal)
                  && runtimeSource.Contains("PlatformNotSupportedException(\"Windows CRT environment updates require a Windows Unity build target.\")", StringComparison.Ordinal)
                  && !windowsBlock.Contains("SetStandaloneRosDistro(currentRos2Version)", StringComparison.Ordinal)
                  && !windowsBlock.Contains("SetStandalonePrefixPath();", StringComparison.Ordinal)
                  && !windowsBlock.Contains("SetStandaloneRmwImplementation();", StringComparison.Ordinal)
                  && !windowsBlock.Contains("SetStandaloneRcutilsConsoleMode();", StringComparison.Ordinal),
                "162-E6b: Lyrical Windows environment setup is symbol-guarded and not repeated after standalone setup");
            Check(componentSource.Contains("private static readonly HashSet<ROS2UnityComponent> instances", StringComparison.Ordinal)
                  && componentSource.Contains("instances.Add(this)", StringComparison.Ordinal)
                  && componentSource.Contains("instances.Remove(this)", StringComparison.Ordinal)
                  && componentSource.Contains("public static bool StopAllExecutorsForRosShutdown()", StringComparison.Ordinal),
                "162-E7: Lyrical ROS2UnityComponent tracks active components for cooperative native shutdown");
            Check(componentSource.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && componentSource.Contains("MarkRuntimeShutdown()", StringComparison.Ordinal)
                  && componentSource.Contains("component.MarkRuntimeShutdown();", StringComparison.Ordinal)
                  && componentSource.Contains("throw new ObjectDisposedException(nameof(ROS2UnityComponent))", StringComparison.Ordinal)
                  && componentSource.Contains("ros2forUnity == null", StringComparison.Ordinal),
                "162-E8: Lyrical ROS2UnityComponent does not reinitialize after shared runtime shutdown starts");
            Check(componentSource.Contains("private void Awake()", StringComparison.Ordinal)
                  && componentSource.Contains("ROS2ForUnity.PrewarmUnityPaths();", StringComparison.Ordinal)
                  && !componentSource.Contains("            runtimeShutdownRequested = false;", StringComparison.Ordinal),
                "162-E8b: Lyrical ROS2UnityComponent prewarms Unity path Lazy values and has no dead shutdown reset");
            Check(runtimeSource.Contains("if (!isInitialized || shutdownInProgress)", StringComparison.Ordinal),
                "162-E9: Lyrical runtime reports not-ready while native shutdown is in progress");
        }

        private static void Phase162PointCloud2UsesSensorDataQos()
        {
            var helper = ReadRepoText(Phase162SmokeHelper);
            Check(helper.Contains("\"sensor_msgs/msg/PointCloud2\"", StringComparison.Ordinal)
                  && helper.Contains("--qos-reliability", StringComparison.Ordinal)
                  && helper.Contains("best_effort", StringComparison.Ordinal)
                  && helper.Contains("--qos-depth", StringComparison.Ordinal)
                  && helper.Contains("\"1\"", StringComparison.Ordinal),
                "162-F1: Zenoh PointCloud2 echo subscribes with sensor-data QoS");

            var bridge = ReadRepoText(AdapterPackage + "/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            Check(bridge.Contains("CreateSensorPublisher<sensor_msgs.msg.PointCloud2>(topic)", StringComparison.Ordinal),
                "162-F2: native PointCloud2 bridge publishes with sensor-data QoS");
            Check(bridge.Contains("ZenohBackpressurePublishSlowThresholdMs", StringComparison.Ordinal)
                  && bridge.Contains("ZenohBackpressureCooldownSeconds", StringComparison.Ordinal)
                  && bridge.Contains("IsZenohRmwActive()", StringComparison.Ordinal)
                  && bridge.Contains("Environment.GetEnvironmentVariable(\"RMW_IMPLEMENTATION\")", StringComparison.Ordinal)
                  && bridge.Contains("ShouldSkipZenohBackpressureFrame", StringComparison.Ordinal)
                  && bridge.Contains("UpdateZenohBackpressure(publishMs)", StringComparison.Ordinal)
                  && bridge.Contains("zenohBackpressureSkip", StringComparison.Ordinal),
                "162-F2b: Zenoh PointCloud2 native publish applies a short backpressure cooldown after slow RMW publishes");

            var rviz = ReadRepoText("Scripts/smoke/ros2/launch_phase138u_lidar_deskew_rviz2.py");
            Check(rviz.Contains("Reliability Policy: Best Effort", StringComparison.Ordinal)
                  && rviz.Contains("Depth: 1", StringComparison.Ordinal),
                "162-F3: RViz2 PointCloud2 displays use non-queued sensor-data QoS");

            Check(bridge.Contains("ResolveDynamicTfAnchor", StringComparison.Ordinal)
                  && bridge.Contains("PackedPointCloudTfAnchorResolver.Resolve", StringComparison.Ordinal)
                  && bridge.Contains("_source.transform.position.x", StringComparison.Ordinal)
                  && bridge.Contains("_source.transform.rotation.w", StringComparison.Ordinal),
                "162-F4: PointCloud2 TF anchor follows the source transform instead of publishing a stale static pose");

            var scene = ReadRepoText("Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity");
            Check(scene.Contains("_publishPackedPointCloudTfAnchor: 1", StringComparison.Ordinal)
                  && scene.Contains("_packedPointCloudTfParentFrame: map", StringComparison.Ordinal)
                  && scene.Contains("_frameId: os_lidar", StringComparison.Ordinal),
                "162-F5: Phase138 smoke scene enables map-to-lidar TF for RViz fixed-frame acceptance");

            var localPlaySetup = ReadRepoText("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs");
            Check(localPlaySetup.Contains("SetField(publisher, \"_publishPackedPointCloudTfAnchor\", true", StringComparison.Ordinal)
                  && localPlaySetup.Contains("SetField(publisher, \"_frameId\", \"os_lidar\"", StringComparison.Ordinal),
                "162-F6: local Lyrical Zenoh play setup enables TF anchor for moving RViz acceptance");
            Check(localPlaySetup.Contains("PlayRequestedKey", StringComparison.Ordinal)
                  && localPlaySetup.Contains("SessionState.GetBool(PlayRequestedKey", StringComparison.Ordinal)
                  && localPlaySetup.Contains("SessionState.SetBool(PlayRequestedKey, true)", StringComparison.Ordinal)
                  && localPlaySetup.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal),
                "162-F7: local Lyrical Zenoh play setup is one-shot across domain reloads");

            foreach (var package in new[] { HumbleRuntimePackage, JazzyRuntimePackage, RuntimePackage })
            {
                var node = ReadRepoText(package + "/Runtime/Ros2ForUnity/Scripts/ROS2Node.cs");
                Check(node.Contains("SetPolicies(", StringComparison.Ordinal)
                      && node.Contains("QOS_POLICY_HISTORY_KEEP_LAST", StringComparison.Ordinal)
                      && node.Contains("QOS_POLICY_RELIABILITY_BEST_EFFORT", StringComparison.Ordinal)
                      && node.Contains("QOS_POLICY_DURABILITY_VOLATILE", StringComparison.Ordinal),
                    "162-F-runtime: " + package + " CreateSensorPublisher explicitly maps sensor QoS");
            }
        }

        private static void Phase162LyricalRuntimeComponentLifecycleIsBounded()
        {
            var componentSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var okMethod = ExtractMethod(componentSource, "public bool Ok()");
            var startMethod = ExtractMethod(componentSource, "void Start()");
            var startExecutorMethod = ExtractMethod(componentSource, "private void StartExecutor()");
            var stopExecutorMethod = ExtractMethod(componentSource, "private void StopExecutor()");

            Check(componentSource.Contains("[DisallowMultipleComponent]", StringComparison.Ordinal),
                "162-G1: Lyrical ROS2UnityComponent prevents accidental duplicate components");
            Check(okMethod.Contains("needsConstruct = ros2forUnity == null;", StringComparison.Ordinal)
                  && okMethod.Contains("if (needsConstruct)", StringComparison.Ordinal)
                  && okMethod.IndexOf("if (needsConstruct)", StringComparison.Ordinal)
                     < okMethod.LastIndexOf("lock (mutex)", StringComparison.Ordinal),
                "162-G2: Lyrical Ok() releases its first mutex section before lazy ROS2 construction");
            Check(startMethod.Contains("LazyConstruct();", StringComparison.Ordinal)
                  && startMethod.Contains("StartExecutor();", StringComparison.Ordinal),
                "162-G3: Lyrical executor starts during Start() instead of waiting only for FixedUpdate");
            Check(startExecutorMethod.Contains("executorThread.Name = \"ROS2UnityComponent.Executor\";", StringComparison.Ordinal),
                "162-G4: Lyrical executor thread has a profiler/crash-dump name");
            Check(stopExecutorMethod.Contains("Idempotent by design", StringComparison.Ordinal)
                  && stopExecutorMethod.Contains("threadToJoin != null && threadToJoin != Thread.CurrentThread", StringComparison.Ordinal),
                "162-G5: Lyrical StopExecutor documents and preserves idempotent bounded join semantics");
        }

        private static void Phase162ValidationRegistryIsWired()
        {
            var registry = ReadRepoText(RegistryPath);
            var adapterManifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");
            Check(registry.Contains("Ci(\"--phase162\", \"Phase 162: phase 146B validation for the R2FU Lyrical Win64 runtime package\", R2fuLyricalRuntimePackageValidation.ValidatePhase162", StringComparison.Ordinal),
                "162-H1: validation registry wires --phase162");
            Check(adapterManifest.Contains("\"artifactSha256\": \"" + Phase162ArtifactSha + "\"", StringComparison.Ordinal)
                  && adapterManifest.Contains("\"supportedRmwImplementations\"", StringComparison.Ordinal)
                  && adapterManifest.Contains("\"rmw_zenoh_cpp\"", StringComparison.Ordinal)
                  && adapterManifest.Contains("\"rosgraph_msgs_assembly.dll\"", StringComparison.Ordinal),
                "162-H2: adapter compliance manifest records refreshed Lyrical Zenoh runtime");
        }

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"146B-file: {relativePath} exists");
            if (FileTextCache.TryGetValue(path, out var cached))
                return cached;

            var normalized = relativePath.Replace('\\', '/');
            var text = normalized.StartsWith("Scripts/", StringComparison.Ordinal)
                ? PhaseValidationSourceHelpers.ReadSplitPythonSource(normalized)
                : File.ReadAllText(path);
            FileTextCache[path] = text;
            return text;
        }

        private static void VerifyInventoriedZenohConfigHash(string inventoryRelativePath, string checkPrefix)
        {
            var inventory = ReadRepoText(RuntimePackage + "/RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json");
            var expected = ExtractInventorySha256(inventory, inventoryRelativePath);
            var diskRelative = inventoryRelativePath.StartsWith("StreamingAssets/", StringComparison.Ordinal)
                ? "Runtime/Ros2ForUnity/" + inventoryRelativePath
                : "Runtime/" + inventoryRelativePath;
            var actual = Sha256Hex(RepoPath(RuntimePackage + "/" + diskRelative));
            Check(string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
                checkPrefix + ": inventoried Zenoh config hash matches disk for " + inventoryRelativePath);
        }

        private static string ExtractInventorySha256(string inventory, string inventoryRelativePath)
        {
            var pattern = "\\{[^{}]*\"path\"\\s*:\\s*\"" + Regex.Escape(inventoryRelativePath)
                + "\"[^{}]*\"sha256\"\\s*:\\s*\"([0-9a-fA-F]{64})\"";
            var match = Regex.Match(inventory, pattern, RegexOptions.Singleline);
            if (!match.Success)
                throw new Exception("[FAIL] inventory entry missing for " + inventoryRelativePath);
            return match.Groups[1].Value;
        }

        private static string Sha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string[] RuntimePackageKeys(string json)
        {
            var matches = Regex.Matches(
                json ?? string.Empty,
                "\"(dev\\.unity2foxglove\\.ros2forunity\\.runtime\\.[^\"]+)\"\\s*:");
            var values = new string[matches.Count];
            for (var index = 0; index < matches.Count; index++)
                values[index] = matches[index].Groups[1].Value;
            return values;
        }

        private static string ExtractMethod(string source, string methodName)
            => PhaseValidationSourceHelpers.SourceMethod(source, methodName);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
