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
    /// <remarks>Phase192 source sections: Phase162SmokeHelperCapturesZenohRouterFlow.cs</remarks>
    public static partial class R2fuLyricalRuntimePackageValidation
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
            "b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71";

        private static int _passed;
        private static readonly Dictionary<string, string> FileTextCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            Phase162ReadmesDocumentZenohPrerequisites();
            _passed += R2fuJazzyRuntimeRefreshValidation.ValidateNativeBridgeLifecycleGuards("162-G");
            Phase162SmokeHelperCapturesZenohRouterFlow();
            Phase162LyricalRuntimeStopsExecutorsBeforeZenohShutdown();
            Phase162LyricalRuntimeComponentLifecycleIsBounded();
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
                  && packageJson.Contains("Lyrical Win64", StringComparison.Ordinal)
                  && packageJson.Contains("\"unity2foxgloveRuntimeId\": \"r2fu-lyrical-win64\"", StringComparison.Ordinal)
                  && packageJson.Contains("\"dependencies\": {}", StringComparison.Ordinal),
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
                  && asmdef.Contains("\"autoReferenced\": false", StringComparison.Ordinal)
                  && !asmdef.Contains("defineConstraints", StringComparison.Ordinal),
                "146B-A8: Lyrical runtime asmdef is Windows runtime scoped, explicit-reference only, and not define-gated");
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
            var orchestratorImplementation = ReadRepoText(LyricalScripts + "/phase146b_r2fu_lyrical_windows_build/implementation.py");
            Check(orchestratorImplementation.Contains("r2fu-lyrical-win64", StringComparison.Ordinal)
                  && orchestratorImplementation.Contains("default_ros2_root(\"lyrical\")", StringComparison.Ordinal)
                  && orchestratorImplementation.Contains("phase146b", StringComparison.Ordinal)
                  && orchestrator.Contains("implementation.py", StringComparison.Ordinal),
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
                  && selector.Contains("RestartEditorInCleanProcess", StringComparison.Ordinal)
                  && selector.Contains("BuildCleanRestartPath", StringComparison.Ordinal)
                  && selector.Contains("ProcessStartInfo", StringComparison.Ordinal)
                  && selector.Contains("UseShellExecute = false", StringComparison.Ordinal)
                  && selector.Contains("EditorApplication.Exit(0)", StringComparison.Ordinal)
                  && !selector.Contains("EditorApplication.OpenProject(projectDirectory)", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal)
                  && guard.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal),
                "146B-D3: runtime switching uses a per-session Play Mode guard and a clean-process restart");
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

            Check(registry.Contains("Ci(\"--phase146b\", \"Phase 146B: validation for the R2FU Lyrical Win64 runtime package\", R2fuLyricalRuntimePackageValidation.Validate", StringComparison.Ordinal),
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

            foreach (var relative in new[]
            {
                "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                "/Runtime/Ros2ForUnity/StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
            })
            {
                var config = ReadRepoText(RuntimePackage + relative);
                var adminspace = config.Substring(config.IndexOf("adminspace:", StringComparison.Ordinal));
                Check(config.Contains("exit_on_failure: false", StringComparison.Ordinal)
                      && config.Contains("max_message_size: 134217728", StringComparison.Ordinal)
                      && !config.Contains("max_message_size: 1073741824", StringComparison.Ordinal)
                      && adminspace.Contains("enabled: false", StringComparison.Ordinal)
                      && adminspace.Contains("read: false", StringComparison.Ordinal),
                    "162-B2: Lyrical Zenoh session config is Unity-safe by default: " + relative);
            }

            VerifyInventoriedZenohConfigHash(
                "Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                "162-B3a");
            VerifyInventoriedZenohConfigHash(
                "Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                "162-B3b");
            VerifyInventoriedZenohConfigHash(
                "StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                "162-B3c");
            VerifyInventoriedZenohConfigHash(
                "StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                "162-B3d");
        }

        private static void Phase162SelectorScopesCommunicationModeToZenohCapability()
        {
            var selector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs");
            Check(selector.Contains("SupportsZenoh", StringComparison.Ordinal)
                  && selector.Contains("GetZenohPayloadDiagnostic", StringComparison.Ordinal)
                  && selector.Contains("HasNativeLibrary(pluginRoot, \"rmw_zenoh_cpp\")", StringComparison.Ordinal)
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
            var capabilities = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeCapabilityModel.cs");
            Check(inspector.Contains("Communication Mode", StringComparison.Ordinal)
                  && inspector.Contains("GetCommunicationModeIds(selectedRuntime)", StringComparison.Ordinal)
                  && inspector.Contains("GetCommunicationModeLabels(selectedRuntime)", StringComparison.Ordinal)
                  && inspector.Contains("status.SelectedRuntime.CommunicationModes.Count > 1", StringComparison.Ordinal)
                  && capabilities.Contains("FastDDS (default)", StringComparison.Ordinal)
                  && capabilities.Contains("Zenoh (rmw_zenoh_cpp)", StringComparison.Ordinal),
                "162-D1: Inspector shows manifest-declared communication modes only for capable runtimes");
            Check(inspector.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && inspector.Contains("GetCommunicationModeRequiringEditorRestart", StringComparison.Ordinal)
                  && inspector.Contains("RMW DLLs", StringComparison.Ordinal),
                "162-D2: Inspector blocks unsafe communication-mode hot switching");

            var guard = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            Check(selector.Contains("Ros2ForUnityEditorEnvironmentLease.Set(\"RMW_IMPLEMENTATION\"", StringComparison.Ordinal)
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
    }
}
