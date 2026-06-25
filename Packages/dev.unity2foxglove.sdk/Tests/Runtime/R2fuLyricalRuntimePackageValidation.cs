// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 146B validation for the R2FU Lyrical Win64 runtime package.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    public static class R2fuLyricalRuntimePackageValidation
    {
        private const string RuntimePackage =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64";
        private const string AdapterPackage =
            "Packages/dev.unity2foxglove.ros2forunity";
        private const string LyricalScripts =
            "Scripts/ros2forunity/windows/lyrical";
        private const string UnityManifestPath =
            "Unity2Foxglove/Packages/manifest.json";
        private const string UnityLockPath =
            "Unity2Foxglove/Packages/packages-lock.json";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";
        private const string ProjectPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj";
        private const string HumbleRuntimePackage =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64";
        private const string JazzyRuntimePackage =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string Phase162SmokeHelper =
            "Scripts/smoke/ros2/phase162_lyrical_zenoh_player_smoke.py";
        private const string Phase162ArtifactSha =
            "ea1e1c6179cf75e11ad01045dc3e7112363cc00d2052fc264ab79437ffdda608";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 146B: R2FU Lyrical Win64 Runtime Package ===");
            _passed = 0;

            RuntimePackageShapeIsPresent();
            LyricalScriptsAreDistroSpecific();
            AdapterManifestRecordsLyricalRuntime();
            SelectorDiscoversLyricalCandidateRuntime();
            UnityProjectResolvesOnlyOneRuntime();
            RuntimeCandidatesAreNotEmbedded();
            ValidationRegistryWiresPhase146B();

            Console.WriteLine($"Phase 146B: {_passed} checks passed.");
        }

        public static void ValidatePhase162()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 162: R2FU Lyrical Zenoh Runtime Selection ===");
            _passed = 0;

            Phase162RuntimePackageAdvertisesSelectableRmw();
            Phase162PayloadIncludesZenohAndBaselineAssets();
            Phase162SelectorScopesCommunicationModeToZenohCapability();
            Phase162InspectorAndGuardApplyRmwBeforePlayMode();
            _passed += R2fuJazzyRuntimeRefreshValidation.ValidateNativeBridgeLifecycleGuards("162-G");
            Phase162SmokeHelperCapturesZenohRouterFlow();
            Phase162LyricalRuntimeStopsExecutorsBeforeZenohShutdown();
            Phase162PointCloud2UsesSensorDataQos();
            Phase162ValidationRegistryIsWired();

            Console.WriteLine($"Phase 162: {_passed} checks passed.");
        }

        private static void RuntimePackageShapeIsPresent()
        {
            Check(RepoFileExists(RuntimePackage + "/package.json"),
                "146B-A1: Lyrical runtime package.json is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/runtime-manifest.json"),
                "146B-A2: Lyrical runtime manifest is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json"),
                "146B-A3: Lyrical runtime inventory is present");
            Check(RepoFileExists(RuntimePackage + "/THIRD_PARTY_NOTICES.md"),
                "146B-A4: Lyrical runtime notices are present");

            var packageJson = ReadRepoText(RuntimePackage + "/package.json");
            Check(packageJson.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && packageJson.Contains("Lyrical Win64", StringComparison.Ordinal),
                "146B-A5: package metadata names the Lyrical Win64 runtime package");

            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(manifest.Contains("\"runtimeId\": \"r2fu-lyrical-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rosDistro\": \"lyrical\"", StringComparison.Ordinal)
                  && manifest.Contains("\"supportLevel\": \"Supported\"", StringComparison.Ordinal),
                "146B-A6: runtime manifest records Lyrical identity and supported status");

            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            Check(runtimeSource.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && runtimeSource.Contains("ValidateRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal),
                "146B-A7: ROS2ForUnity.cs has package path support and RMW guard");

            var asmdef = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef");
            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"WindowsStandalone64\"", StringComparison.Ordinal)
                  && !asmdef.Contains("defineConstraints", StringComparison.Ordinal),
                "146B-A8: Lyrical runtime asmdef is Windows runtime scoped and not define-gated");
        }

        private static void LyricalScriptsAreDistroSpecific()
        {
            foreach (var script in new[]
            {
                "inspect_r2fu_runtime_artifact.py",
                "build_r2fu_runtime_package.py",
                "sync_r2fu_artifact_to_unity2foxglove.py",
                "validate_r2fu_runtime_package.py",
                "validate_ros2forunity_package.py",
                "phase146b_r2fu_lyrical_windows_build.py",
            })
            {
                Check(RepoFileExists(LyricalScripts + "/" + script),
                    "146B-B-file: " + script + " exists");
            }

            var build = ReadRepoText(LyricalScripts + "/build_r2fu_runtime_package.py");
            Check(build.Contains("Ros2ForUnity_lyrical_standalone_windows_x86_64.zip", StringComparison.Ordinal)
                  && build.Contains("r2fu-lyrical-win64", StringComparison.Ordinal)
                  && build.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal),
                "146B-B1: Lyrical builder uses Lyrical artifact, runtime id, and package name");
            Check(build.Contains("patch_rmw_guard", StringComparison.Ordinal)
                  && build.Contains("ValidateRmwImplementation", StringComparison.Ordinal),
                "146B-B2: Lyrical builder regenerates the RMW guard");

            var orchestrator = ReadRepoText(LyricalScripts + "/phase146b_r2fu_lyrical_windows_build.py");
            Check(orchestrator.Contains("r2fu-lyrical-win64", StringComparison.Ordinal)
                  && orchestrator.Contains("default_ros2_root(\"lyrical\")", StringComparison.Ordinal)
                  && orchestrator.Contains("phase146b", StringComparison.Ordinal),
                "146B-B3: Lyrical build orchestrator has distro-specific defaults");
        }

        private static void AdapterManifestRecordsLyricalRuntime()
        {
            var manifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");
            Check(manifest.Contains("\"supportedRuntimePackages\"", StringComparison.Ordinal)
                  && manifest.Contains("\"packageName\": \"dev.unity2foxglove.ros2forunity.runtime.lyrical.win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"runtimeId\": \"r2fu-lyrical-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSha256\": \"" + Phase162ArtifactSha + "\"", StringComparison.Ordinal),
                "146B-C1: adapter manifest records the Lyrical supported runtime package");
            Check(RepoFileExists(AdapterPackage + "/Compliance/r2fu-lyrical-win64-runtime-inventory.json")
                  && RepoFileExists(AdapterPackage + "/Compliance/r2fu-lyrical-win64-runtime-notices.md"),
                "146B-C2: adapter compliance has Lyrical inventory and notices");

            var readme = ReadRepoText(AdapterPackage + "/README.md");
            Check(readme.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && readme.Contains("active runtime dropdown", StringComparison.Ordinal)
                  && readme.Contains("After an Editor session has loaded one ROS2 runtime", StringComparison.Ordinal),
                "146B-C3: adapter README documents Lyrical package selection");

            var sampleReadme = ReadRepoText(AdapterPackage + "/Samples~/ROS2 For Unity External Adapter/README.md");
            Check(sampleReadme.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && sampleReadme.Contains("active runtime dropdown", StringComparison.Ordinal)
                  && sampleReadme.Contains("If this Editor session already entered Play Mode", StringComparison.Ordinal),
                "146B-C4: external adapter sample documents runtime selection");
        }

        private static void SelectorDiscoversLyricalCandidateRuntime()
        {
            var selector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs");
            var nativeAsmdef = ReadRepoText(AdapterPackage + "/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            Check(selector.Contains("DiscoverCandidateRuntimes", StringComparison.Ordinal)
                  && selector.Contains("RuntimePackagePrefix", StringComparison.Ordinal)
                  && !selector.Contains("\"Lyrical Win64\"", StringComparison.Ordinal),
                "146B-D1: active runtime selector discovers Lyrical by package naming convention");
            Check(nativeAsmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && nativeAsmdef.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY\"", StringComparison.Ordinal)
                  && !nativeAsmdef.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE\"", StringComparison.Ordinal)
                  && !nativeAsmdef.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY_LYRICAL_WIN64_PACKAGE\"", StringComparison.Ordinal),
                "146B-D2: native bridge references the stable active runtime assembly without distro pinning");
            var guard = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            Check(selector.Contains("SessionState", StringComparison.Ordinal)
                  && selector.Contains("GetRuntimePackageRequiringEditorRestart", StringComparison.Ordinal)
                  && selector.Contains("EditorApplication.OpenProject(projectDirectory)", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal)
                  && guard.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal),
                "146B-D3: runtime switching uses a per-session Play Mode guard and one-click restart");
        }

        private static void UnityProjectResolvesOnlyOneRuntime()
        {
            var manifest = ReadRepoText(UnityManifestPath);
            var lockFile = ReadRepoText(UnityLockPath);
            var manifestRuntimes = RuntimePackageKeys(manifest);
            var lockRuntimes = RuntimePackageKeys(lockFile);

            Check(manifestRuntimes.Length == 1,
                "146B-E1: Unity sample project manifest resolves exactly one R2FU runtime package");
            Check(lockRuntimes.Length == 1,
                "146B-E2: Unity sample project lock resolves exactly one R2FU runtime package");
            Check(manifest.Contains("file:../../Packages/" + manifestRuntimes[0], StringComparison.Ordinal),
                "146B-E3: active runtime is referenced from the repository Packages candidate directory");
        }

        private static void RuntimeCandidatesAreNotEmbedded()
        {
            var embeddedRoot = RepoPath("Unity2Foxglove/Packages");
            var embeddedCandidates = Directory.Exists(embeddedRoot)
                ? Directory.GetDirectories(embeddedRoot, "dev.unity2foxglove.ros2forunity.runtime.*", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            Check(embeddedCandidates.Length == 0,
                "146B-E4: runtime candidates are not embedded under Unity2Foxglove/Packages");
        }

        private static void ValidationRegistryWiresPhase146B()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase146b\", \"Phase 146B\", R2fuLyricalRuntimePackageValidation.Validate", StringComparison.Ordinal),
                "146B-F1: validation registry wires --phase146b");
            Check(project.Contains("R2fuLyricalRuntimePackageValidation.cs", StringComparison.Ordinal),
                "146B-F2: runtime validation project compiles the 146B validation");
        }

        private static void Phase162RuntimePackageAdvertisesSelectableRmw()
        {
            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(manifest.Contains("\"artifactSha256\": \"" + Phase162ArtifactSha + "\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSize\": 25179851", StringComparison.Ordinal)
                  && manifest.Contains("\"inventoryFileCount\": 1227", StringComparison.Ordinal),
                "162-A1: Lyrical manifest pins the refreshed artifact identity");
            Check(manifest.Contains("\"defaultRmwImplementation\": \"rmw_fastrtps_cpp\"", StringComparison.Ordinal)
                  && manifest.Contains("\"supportedRmwImplementations\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rmw_zenoh_cpp\"", StringComparison.Ordinal)
                  && manifest.Contains("\"FastDDS (default)\"", StringComparison.Ordinal)
                  && manifest.Contains("\"Zenoh\"", StringComparison.Ordinal),
                "162-A2: Lyrical manifest records FastDDS default and Zenoh selectable mode");

            var inventory = ReadRepoText(RuntimePackage + "/RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json");
            Check(inventory.Contains("\"sha256\": \"" + Phase162ArtifactSha + "\"", StringComparison.Ordinal)
                  && inventory.Contains("\"defaultRmwImplementation\": \"rmw_fastrtps_cpp\"", StringComparison.Ordinal)
                  && inventory.Contains("\"rmw_zenoh_cpp\"", StringComparison.Ordinal),
                "162-A3: Lyrical inventory records refreshed hash and supported RMW implementations");

            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            Check(runtimeSource.Contains("defaultRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("zenohRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("IsSupportedRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("selectedRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("SetProcessEnvironmentVariable(\"RMW_IMPLEMENTATION\", selectedRmwImplementation)", StringComparison.Ordinal)
                  && runtimeSource.Contains("_wputenv_s", StringComparison.Ordinal)
                  && runtimeSource.Contains("Failed to set Windows CRT environment variable", StringComparison.Ordinal)
                  && !runtimeSource.Contains("expectedRmwImplementation", StringComparison.Ordinal),
                "162-A4: Lyrical ROS2ForUnity.cs selects default FastDDS or explicit Zenoh before native-visible init");
        }

        private static void Phase162PayloadIncludesZenohAndBaselineAssets()
        {
            foreach (var relative in new[]
            {
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/fastdds-3.6.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rosidl_buffer_backend_registry.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rosidl_dynamic_typesupport_fastrtps.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rmw_zenoh_cpp.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/zenohc.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                "/Runtime/Ros2ForUnity/StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                "/Runtime/Ros2ForUnity/StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                "/Runtime/Ros2ForUnity/Plugins/rosgraph_msgs_assembly.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll",
            })
            {
                Check(RepoFileExists(RuntimePackage + relative), "162-B-file: " + relative + " exists");
            }

            Check(!RepoFileExists(HumbleRuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rmw_zenoh_cpp.dll")
                  && !RepoFileExists(JazzyRuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/rmw_zenoh_cpp.dll"),
                "162-B1: Humble and Jazzy runtime packages remain FastDDS-only");
        }

        private static void Phase162SelectorScopesCommunicationModeToZenohCapability()
        {
            var selector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs");
            Check(selector.Contains("SupportsZenoh", StringComparison.Ordinal)
                  && selector.Contains("HasZenohPayload", StringComparison.Ordinal)
                  && selector.Contains("rmw_zenoh_cpp.dll", StringComparison.Ordinal)
                  && selector.Contains("DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5", StringComparison.Ordinal),
                "162-C1: runtime descriptor uses Zenoh payload capability detection");
            Check(selector.Contains("FastDdsCommunicationMode", StringComparison.Ordinal)
                  && selector.Contains("ZenohCommunicationMode", StringComparison.Ordinal)
                  && selector.Contains("EditorUserSettings", StringComparison.Ordinal)
                  && selector.Contains("GetCommunicationModeForRuntime", StringComparison.Ordinal),
                "162-C2: selector stores a Lyrical communication mode with FastDDS default");
        }

        private static void Phase162InspectorAndGuardApplyRmwBeforePlayMode()
        {
            var inspector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var selector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs");
            Check(inspector.Contains("Communication Mode", StringComparison.Ordinal)
                  && inspector.Contains("GetCommunicationModeDisplayName", StringComparison.Ordinal)
                  && inspector.Contains("status.SelectedRuntime.SupportsZenoh", StringComparison.Ordinal)
                  && selector.Contains("FastDDS (default)", StringComparison.Ordinal)
                  && selector.Contains("Zenoh (rmw_zenoh_cpp)", StringComparison.Ordinal),
                "162-D1: Inspector shows Lyrical Zenoh communication mode only for capable runtimes");
            Check(inspector.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && inspector.Contains("GetCommunicationModeRequiringEditorRestart", StringComparison.Ordinal)
                  && inspector.Contains("RMW DLLs", StringComparison.Ordinal),
                "162-D2: Inspector blocks unsafe communication-mode hot switching");

            var guard = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            Check(selector.Contains("Environment.SetEnvironmentVariable(\"RMW_IMPLEMENTATION\"", StringComparison.Ordinal)
                  && selector.Contains("GetRmwImplementationForCommunicationMode", StringComparison.Ordinal)
                  && selector.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal)
                  && guard.Contains("GetCommunicationModeRequiringEditorRestart", StringComparison.Ordinal)
                  && guard.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal),
                "162-D3: Play Mode guard applies selected RMW before R2FU initialization");
            Check(guard.Contains("CompilationPipeline.compilationStarted", StringComparison.Ordinal)
                  && guard.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal)
                  && guard.Contains("CompilationStartedWhileR2fuPlayModeKey", StringComparison.Ordinal)
                  && guard.Contains("native ROS2/RMW DLLs cannot be safely unloaded during Play Mode", StringComparison.Ordinal),
                "162-D4: Play Mode guard exits for script-compilation reloads without blocking Lyrical Play Mode startup");
        }

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
        }

        private static void Phase162LyricalRuntimeStopsExecutorsBeforeZenohShutdown()
        {
            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var componentSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");

            Check(runtimeSource.Contains("shutdownInProgress", StringComparison.Ordinal)
                  && runtimeSource.Contains("TryBeginShutdownLocked()", StringComparison.Ordinal)
                  && runtimeSource.Contains("CompleteShutdownShared()", StringComparison.Ordinal),
                "162-E5: Lyrical ROS2 context shutdown is serialized across editor reload/play-mode exits");
            Check(runtimeSource.Contains("ROS2UnityComponent.StopAllExecutorsForRosShutdown()", StringComparison.Ordinal)
                  && runtimeSource.IndexOf("ROS2UnityComponent.StopAllExecutorsForRosShutdown()", StringComparison.Ordinal)
                     < runtimeSource.IndexOf("Ros2cs.Shutdown()", StringComparison.Ordinal),
                "162-E6: Lyrical runtime stops ROS2 executor threads before unloading Zenoh/RMW through Ros2cs.Shutdown");
            Check(componentSource.Contains("private static readonly HashSet<ROS2UnityComponent> instances", StringComparison.Ordinal)
                  && componentSource.Contains("instances.Add(this)", StringComparison.Ordinal)
                  && componentSource.Contains("instances.Remove(this)", StringComparison.Ordinal)
                  && componentSource.Contains("public static void StopAllExecutorsForRosShutdown()", StringComparison.Ordinal),
                "162-E7: Lyrical ROS2UnityComponent tracks active components for cooperative native shutdown");
            Check(runtimeSource.Contains("if (!isInitialized || shutdownInProgress)", StringComparison.Ordinal),
                "162-E8: Lyrical runtime reports not-ready while native shutdown is in progress");
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

            var bridge = ReadRepoText(AdapterPackage + "/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            Check(bridge.Contains("CreateSensorPublisher<sensor_msgs.msg.PointCloud2>(topic)", StringComparison.Ordinal),
                "162-F2: native PointCloud2 bridge publishes with sensor-data QoS");

            var rviz = ReadRepoText("Scripts/smoke/ros2/launch_phase138u_lidar_deskew_rviz2.py");
            Check(rviz.Contains("Reliability Policy: Best Effort", StringComparison.Ordinal)
                  && rviz.Contains("Depth: 1", StringComparison.Ordinal),
                "162-F3: RViz2 PointCloud2 displays use non-queued sensor-data QoS");

            Check(bridge.Contains("ResolveDynamicTfAnchor", StringComparison.Ordinal)
                  && bridge.Contains("CoordinateConverter.UnityToFoxglovePosition(_source.transform.position)", StringComparison.Ordinal)
                  && bridge.Contains("CoordinateConverter.UnityToFoxgloveRotation(_source.transform.rotation)", StringComparison.Ordinal),
                "162-F4: PointCloud2 TF anchor follows the source transform instead of publishing a stale static pose");

            var scene = ReadRepoText("Unity2Foxglove/Assets/Scenes/Phase138_Foxglove_MCAP_Smoke.unity");
            Check(scene.Contains("_publishPointCloud2NativeTfAnchor: 1", StringComparison.Ordinal)
                  && scene.Contains("_pointCloud2NativeTfParentFrame: map", StringComparison.Ordinal)
                  && scene.Contains("_frameId: os_lidar", StringComparison.Ordinal),
                "162-F5: Phase138 smoke scene enables map-to-lidar TF for RViz fixed-frame acceptance");

            var localPlaySetup = ReadRepoText("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs");
            Check(localPlaySetup.Contains("SetField(publisher, \"_publishPointCloud2NativeTfAnchor\", true)", StringComparison.Ordinal)
                  && localPlaySetup.Contains("SetField(publisher, \"_frameId\", \"os_lidar\")", StringComparison.Ordinal),
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

        private static void Phase162ValidationRegistryIsWired()
        {
            var registry = ReadRepoText(RegistryPath);
            var adapterManifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");
            Check(registry.Contains("Ci(\"--phase162\", \"Phase 162\", R2fuLyricalRuntimePackageValidation.ValidatePhase162", StringComparison.Ordinal),
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
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath);

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

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
