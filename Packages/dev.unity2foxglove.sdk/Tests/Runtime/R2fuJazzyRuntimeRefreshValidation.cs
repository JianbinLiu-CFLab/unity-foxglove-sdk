// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 161 validation for the R2FU Jazzy Win64 runtime refresh.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    public static class R2fuJazzyRuntimeRefreshValidation
    {
        private const string RuntimePackage =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string AdapterPackage =
            "Packages/dev.unity2foxglove.ros2forunity";
        private const string JazzyScripts =
            "Scripts/ros2forunity/windows/jazzy";
        private const string UnityManifestPath =
            "Unity2Foxglove/Packages/manifest.json";
        private const string UnityLockPath =
            "Unity2Foxglove/Packages/packages-lock.json";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";
        private const string ProjectPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj";
        private const string ExpectedSha =
            "df4806b750435b3a1252f39b46dd2e4e60ddc0eb6ac57989bcf00adb23fe29f3";

        private static int _passed;
        private static readonly Dictionary<string, string> FileTextCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> NativeBridgeLifecycleSourceCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 161: R2FU Jazzy Win64 Runtime Refresh ===");
            _passed = 0;

            RuntimePackageShapeIsPresent();
            RuntimePackageContainsJazzyDependencyFloor();
            RuntimePackageRecordsInventoryDelta();
            AdapterAdoptionManifestRecordsRefreshedJazzyRuntime();
            JazzyScriptsArePinnedToHandoffArtifact();
            UnityProjectResolvesOnlyJazzyRuntime();
            ValidationRegistryWiresPhase161();
            _passed += ValidateNativeBridgeLifecycleGuards("161-G");

            Console.WriteLine($"Phase 161: {_passed} checks passed.");
        }

        private static void RuntimePackageShapeIsPresent()
        {
            Check(RepoFileExists(RuntimePackage + "/package.json"),
                "161-A1: Jazzy runtime package.json is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/runtime-manifest.json"),
                "161-A2: Jazzy runtime manifest is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json"),
                "161-A3: Jazzy runtime inventory is present");
            Check(RepoFileExists(RuntimePackage + "/THIRD_PARTY_NOTICES.md"),
                "161-A4: Jazzy runtime notices are present");

            var packageJson = ReadRepoText(RuntimePackage + "/package.json");
            Check(packageJson.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && packageJson.Contains("Jazzy Win64", StringComparison.Ordinal),
                "161-A5: package metadata names the Jazzy Win64 runtime package");

            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(manifest.Contains("\"runtimeId\": \"r2fu-jazzy-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rosDistro\": \"jazzy\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rmwImplementation\": \"rmw_fastrtps_cpp\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSha256\": \"" + ExpectedSha + "\"", StringComparison.Ordinal),
                "161-A6: runtime manifest records Jazzy identity, FastRTPS RMW, and pinned artifact hash");

            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            Check(runtimeSource.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && runtimeSource.Contains("ValidateRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal),
                "161-A7: ROS2ForUnity.cs has package path support and FastRTPS RMW guard");
            Check(runtimeSource.Contains("SetProcessEnvironmentVariable", StringComparison.Ordinal)
                  && runtimeSource.Contains("_wputenv_s", StringComparison.Ordinal)
                  && runtimeSource.Contains("SetStandalonePrefixPath", StringComparison.Ordinal)
                  && runtimeSource.Contains("AMENT_PREFIX_PATH", StringComparison.Ordinal)
                  && runtimeSource.Contains("SetStandaloneRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("RMW_IMPLEMENTATION", StringComparison.Ordinal)
                  && runtimeSource.IndexOf("SetStandalonePrefixPath();", StringComparison.Ordinal)
                     < runtimeSource.IndexOf("Ros2cs.Init();", StringComparison.Ordinal)
                  && runtimeSource.IndexOf("SetStandaloneRmwImplementation();", StringComparison.Ordinal)
                     < runtimeSource.IndexOf("Ros2cs.Init();", StringComparison.Ordinal)
                  && runtimeSource.IndexOf("SetEnvPathVariable();", StringComparison.Ordinal)
                     < runtimeSource.IndexOf("Ros2cs.Init();", StringComparison.Ordinal),
                "161-A7b: ROS2ForUnity.cs configures native-visible standalone environment before Ros2cs.Init");

            var asmdef = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef");
            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"WindowsStandalone64\"", StringComparison.Ordinal)
                  && !asmdef.Contains("defineConstraints", StringComparison.Ordinal),
                "161-A8: Jazzy runtime asmdef is Windows runtime scoped and not define-gated");
        }

        private static void RuntimePackageContainsJazzyDependencyFloor()
        {
            foreach (var assembly in new[]
            {
                "builtin_interfaces_assembly.dll",
                "std_msgs_assembly.dll",
                "sensor_msgs_assembly.dll",
                "tf2_msgs_assembly.dll",
                "rosgraph_msgs_assembly.dll",
            })
            {
                Check(RepoFileExists(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/" + assembly),
                    "161-B-baseline-managed: " + assembly + " exists");
            }

            foreach (var family in new[]
            {
                "actionlib_msgs",
                "statistics_msgs",
                "stereo_msgs",
                "type_description_interfaces",
            })
            {
                Check(RepoFileExists(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/" + family + "_assembly.dll"),
                    "161-B-handoff-managed: " + family + " managed assembly exists");
            }

            foreach (var dll in new[]
            {
                "tf2.dll",
                "tf2_ros.dll",
                "static_transform_broadcaster_node.dll",
                "rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
                "rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll",
                "rosidl_dynamic_typesupport_fastrtps.dll",
                "stereo_msgs__rosidl_typesupport_fastrtps_c.dll",
                "actionlib_msgs__rosidl_typesupport_fastrtps_c.dll",
            })
            {
                Check(NativeDllExists(dll), "161-B-native: " + dll + " exists");
            }

            Check(!NativeDllExists("rmw_zenoh_cpp.dll"),
                "161-B-zenoh: Jazzy runtime remains FastRTPS-only");
        }

        private static void RuntimePackageRecordsInventoryDelta()
        {
            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            var inventory = ReadRepoText(RuntimePackage + "/RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json");

            foreach (var token in new[]
            {
                "\"handoffInventoryDelta\"",
                "\"addedDlls\"",
                "\"allowedRemovedStaleBackupDlls\"",
                "\"assetCriticalBaseline\"",
                "Ros2ForUnity/Plugins/Windows/x86_64/tf2.dll",
                "Ros2ForUnity/Plugins/Windows/x86_64/tf2_ros.dll",
                "Ros2ForUnity/Plugins/Windows/x86_64/static_transform_broadcaster_node.dll",
                "Ros2ForUnity/Plugins/actionlib_msgs_assembly.dll",
                "Ros2ForUnity/Plugins/stereo_msgs_assembly.dll",
                "Ros2ForUnity/Plugins/rosgraph_msgs_assembly.dll",
            })
            {
                Check(manifest.Contains(token, StringComparison.Ordinal),
                    "161-C-manifest-token: " + token + " is recorded");
            }

            foreach (var stalePath in new[]
            {
                "geometry_msgs_velocity_with_covariance_stamped__rosidl_typesupport_c_native.dll",
                "test_msgs_complex_nested_key__rosidl_typesupport_c_native.dll",
                "test_msgs_keyed_long__rosidl_typesupport_c_native.dll",
                "test_msgs_keyed_string__rosidl_typesupport_c_native.dll",
                "test_msgs_non_keyed_with_nested_key__rosidl_typesupport_c_native.dll",
            })
            {
                Check(manifest.Contains(stalePath, StringComparison.Ordinal),
                    "161-C-allowed-stale: " + stalePath + " is named in the allowed removed set");
                Check(!inventory.Contains(stalePath, StringComparison.Ordinal),
                    "161-C-absent-stale: " + stalePath + " is not present in current Jazzy inventory");
            }
        }

        private static void JazzyScriptsArePinnedToHandoffArtifact()
        {
            var sync = ReadRepoText(JazzyScripts + "/sync_r2fu_artifact_to_unity2foxglove.py");
            Check(sync.Contains("EXPECTED_ARTIFACT_SHA256", StringComparison.Ordinal)
                  && sync.Contains(ExpectedSha, StringComparison.Ordinal)
                  && sync.Contains("r2fu-runtime-artifacts", StringComparison.Ordinal),
                "161-D1: Jazzy sync script fail-closes on the pinned artifact under r2fu-runtime-artifacts");

            var build = ReadRepoText(JazzyScripts + "/build_r2fu_runtime_package.py");
            Check(build.Contains("EXPECTED_ARTIFACT_SHA256", StringComparison.Ordinal)
                  && build.Contains(ExpectedSha, StringComparison.Ordinal)
                  && build.Contains("PHASE161_ADDED_DLLS", StringComparison.Ordinal)
                  && build.Contains("PHASE161_ALLOWED_STALE_REMOVED_DLLS", StringComparison.Ordinal)
                  && build.Contains("PHASE161_ASSET_CRITICAL_BASELINE", StringComparison.Ordinal)
                  && build.Contains("patch_standalone_environment_bootstrap", StringComparison.Ordinal)
                  && build.Contains("AMENT_PREFIX_PATH", StringComparison.Ordinal)
                  && build.Contains("_wputenv_s", StringComparison.Ordinal),
                "161-D2: Jazzy builder pins the artifact and records named inventory delta/env bootstrap sets");

            var validator = ReadRepoText(JazzyScripts + "/validate_r2fu_runtime_package.py");
            Check(validator.Contains(ExpectedSha, StringComparison.Ordinal)
                  && validator.Contains("Phase161 added DLL paths are present", StringComparison.Ordinal)
                  && validator.Contains("Phase161 stale old-backup DLL paths are absent", StringComparison.Ordinal)
                  && validator.Contains("Phase161 asset-critical baseline paths are present", StringComparison.Ordinal),
                "161-D3: Jazzy validator enforces Phase161 delta and baseline rules");

            var inspect = ReadRepoText(JazzyScripts + "/inspect_r2fu_runtime_artifact.py");
            Check(inspect.Contains("r2fu-runtime-artifacts", StringComparison.Ordinal)
                  && inspect.Contains("Ros2ForUnity_jazzy_standalone_windows_x86_64.zip", StringComparison.Ordinal),
                "161-D4: Jazzy artifact inspection defaults to the repo-local runtime artifact entrypoint");
        }

        private static void AdapterAdoptionManifestRecordsRefreshedJazzyRuntime()
        {
            var manifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");

            Check(manifest.Contains(ExpectedSha, StringComparison.Ordinal)
                  && !manifest.Contains("709c7c5ecb693402ab0d3dbb3ec0268e1b7a6db0e18cb694e922278e10cbcb7a", StringComparison.Ordinal),
                "161-C-adoption-sha: adapter adoption manifest records the refreshed Jazzy artifact hash");
            Check(manifest.Contains("\"artifactSize\": 17677300", StringComparison.Ordinal)
                  && manifest.Contains("\"inventoryFileCount\": 1198", StringComparison.Ordinal),
                "161-C-adoption-inventory: adapter adoption manifest records refreshed Jazzy size and file count");
            Check(Regex.Matches(
                    manifest,
                    "\"packageName\": \"dev\\.unity2foxglove\\.ros2forunity\\.runtime\\.jazzy\\.win64\"").Count == 2,
                "161-C-adoption-package: current and supported runtime entries both name the Jazzy package");
            Check(manifest.Contains("rosgraph_msgs_assembly.dll", StringComparison.Ordinal)
                  && manifest.Contains("rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll", StringComparison.Ordinal)
                  && manifest.Contains("rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll", StringComparison.Ordinal),
                "161-C-adoption-baseline: adapter adoption manifest preserves rosgraph asset-critical files");
        }

        private static void UnityProjectResolvesOnlyJazzyRuntime()
        {
            var manifest = ReadRepoText(UnityManifestPath);
            var lockFile = ReadRepoText(UnityLockPath);
            var manifestRuntimes = RuntimePackageKeys(manifest);
            var lockRuntimes = RuntimePackageKeys(lockFile);

            Check(manifestRuntimes.Length == 1,
                "161-E1: Unity sample project manifest resolves exactly one R2FU runtime package");
            Check(lockRuntimes.Length == 1 && manifestRuntimes.Length == 1 && lockRuntimes[0] == manifestRuntimes[0],
                "161-E2: Unity sample project lock resolves the same single R2FU runtime package");
            if (manifestRuntimes.Length != 1)
                return;

            var activeRuntimePackage = manifestRuntimes[0];
            Check(manifest.Contains("file:../../Packages/" + activeRuntimePackage, StringComparison.Ordinal)
                  && lockFile.Contains("\"source\": \"local\"", StringComparison.Ordinal),
                "161-E3: active runtime is referenced from the repository Packages candidate directory");

            var activeRuntimeManifest = ReadRepoText("Packages/" + activeRuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(RuntimeId(activeRuntimeManifest) == ExpectedRuntimeId(activeRuntimePackage),
                "161-E4: active runtime package identity matches its runtime manifest");
        }

        private static void ValidationRegistryWiresPhase161()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase161\", \"Phase 161\", R2fuJazzyRuntimeRefreshValidation.Validate", StringComparison.Ordinal),
                "161-F1: validation registry wires --phase161");
            Check(project.Contains("R2fuJazzyRuntimeRefreshValidation.cs", StringComparison.Ordinal),
                "161-F2: runtime validation project compiles the 161 validation");
        }

        public static int ValidateNativeBridgeLifecycleGuards(string labelPrefix)
        {
            var passed = 0;

            foreach (var bridge in new[]
            {
                "Ros2ForUnityTransformNativeBridge.cs",
                "Ros2ForUnityPointCloud2NativeBridge.cs",
                "Ros2ForUnityImuNativeBridge.cs",
                "Ros2ForUnityCameraNativeBridge.cs",
            })
            {
                var relativePath = AdapterPackage + "/Runtime/Native/" + bridge;
                var path = RepoPath(relativePath);
                CheckLifecycle(File.Exists(path), labelPrefix + "-file: " + relativePath + " exists");
                var source = ReadLifecycleSource(path);
                var lifecycleSource = source;
                var sharedGatePath = RepoPath(AdapterPackage + "/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
                if (!string.Equals(path, sharedGatePath, StringComparison.OrdinalIgnoreCase))
                    lifecycleSource += "\n" + ReadLifecycleSource(sharedGatePath);

                CheckLifecycle(lifecycleSource.Contains("using UnityEngine.SceneManagement;", StringComparison.Ordinal)
                      && lifecycleSource.Contains("IsBackupSceneActive()", StringComparison.Ordinal)
                      && lifecycleSource.Contains("Temp/__Backupscenes", StringComparison.Ordinal)
                      && lifecycleSource.Contains("scene.name", StringComparison.Ordinal)
                      && lifecycleSource.Contains("EndsWith(\".backup\"", StringComparison.Ordinal),
                    labelPrefix + "-backup-scene: " + bridge + " treats Unity backup scenes as R2FU shutdown windows");
                CheckLifecycle(source.Contains("gameObject.scene", StringComparison.Ordinal)
                      && source.Contains("IsBackupScene(gameObject.scene)", StringComparison.Ordinal),
                    labelPrefix + "-owner-backup-scene: " + bridge + " blocks ROS2 prewarm when the bridge object lives in Unity backup scenes");
                CheckLifecycle(lifecycleSource.Contains("IsAnyBackupSceneLoaded()", StringComparison.Ordinal)
                      && lifecycleSource.Contains("SceneManager.sceneCount", StringComparison.Ordinal)
                      && lifecycleSource.Contains("SceneManager.GetSceneAt", StringComparison.Ordinal),
                    labelPrefix + "-loaded-backup-scene: " + bridge + " blocks ROS2 prewarm while any Unity backup scene is loaded");
                CheckLifecycle(source.Contains("_playModeSceneLoaded", StringComparison.Ordinal)
                      && source.Contains("RuntimeInitializeLoadType.SubsystemRegistration", StringComparison.Ordinal)
                      && source.Contains("RuntimeInitializeLoadType.AfterSceneLoad", StringComparison.Ordinal),
                    labelPrefix + "-after-scene-load-gate: " + bridge + " blocks ROS2 prewarm during Unity Play Mode backup/restore transitions");
                CheckLifecycle(lifecycleSource.Contains("IsStableUserSceneLoaded()", StringComparison.Ordinal)
                      && lifecycleSource.Contains("StartsWith(\"Assets/\"", StringComparison.Ordinal)
                      && lifecycleSource.Contains("StartsWith(\"Packages/\"", StringComparison.Ordinal)
                      && (source.Contains("!IsStableUserSceneLoaded()", StringComparison.Ordinal)
                          || source.Contains("Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(IsEditorPlayModeTransition())", StringComparison.Ordinal)),
                    labelPrefix + "-stable-user-scene-gate: " + bridge + " prewarms ROS2 only from stable project scenes");
                CheckLifecycle(source.Contains("InitializeEditorPlayModeGate", StringComparison.Ordinal)
                      && source.Contains("EditorApplication.playModeStateChanged", StringComparison.Ordinal)
                      && source.Contains("PlayModeStateChange.EnteredPlayMode", StringComparison.Ordinal)
                      && source.Contains("PlayModeStateChange.ExitingPlayMode", StringComparison.Ordinal)
                      && source.Contains("PlayModeStateChange.EnteredEditMode", StringComparison.Ordinal)
                      && source.Contains("EditorApplication.quitting", StringComparison.Ordinal)
                      && source.Contains("EditorApplication.isCompiling", StringComparison.Ordinal)
                      && source.Contains("EditorApplication.isUpdating", StringComparison.Ordinal)
                      && source.Contains("EditorApplication.timeSinceStartup", StringComparison.Ordinal)
                      && source.Contains("IsEditorPlayModeTransition()", StringComparison.Ordinal),
                    labelPrefix + "-editor-play-mode-gate: " + bridge + " blocks ROS2 prewarm until Unity reports stable Play Mode and no editor update/quitting transition");
                var oldBootstrapGate = source.Contains("if (!IsStableUserSceneLoaded() || IsBackupSceneActive() || IsAnyBackupSceneLoaded())", StringComparison.Ordinal)
                    && source.IndexOf("if (!IsStableUserSceneLoaded() || IsBackupSceneActive() || IsAnyBackupSceneLoaded())", StringComparison.Ordinal)
                       < source.IndexOf("_playModeSceneLoaded = true", StringComparison.Ordinal);
                var cachedBootstrapGate = source.Contains("if (Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(editorTransition: false))", StringComparison.Ordinal)
                    && source.IndexOf("if (Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(editorTransition: false))", StringComparison.Ordinal)
                       < source.IndexOf("_playModeSceneLoaded = true", StringComparison.Ordinal);
                CheckLifecycle((oldBootstrapGate || cachedBootstrapGate)
                      && source.Contains("_runtimeShuttingDown = true", StringComparison.Ordinal)
                      && source.Contains("_playModeSceneLoaded = false", StringComparison.Ordinal)
                      && source.Contains("return;", StringComparison.Ordinal),
                    labelPrefix + "-bootstrap-backup-gate: " + bridge + " does not bootstrap native bridges from Unity backup scenes");
                CheckLifecycle(BridgeUpdatePrewarmsRos2FromGuardedPlayMode(source),
                    labelPrefix + "-update-prewarm: " + bridge + " first-initializes ROS2 only from guarded bridge Update");
                CheckLifecycle(BridgeCallbackGetterUsesReadyRuntimeOnly(source),
                    labelPrefix + "-no-callback-lazy-init: " + bridge + " data callbacks never first-initialize ROS2");
            }

            return passed;

            void CheckLifecycle(bool condition, string message)
            {
                if (!condition)
                    throw new Exception("[FAIL] " + message);
                passed++;
                Console.WriteLine("[PASS] " + message);
            }
        }

        private static string ReadLifecycleSource(string path)
        {
            if (!NativeBridgeLifecycleSourceCache.TryGetValue(path, out var source))
            {
                source = File.ReadAllText(path);
                NativeBridgeLifecycleSourceCache[path] = source;
            }

            return source;
        }

        private static bool BridgeUpdatePrewarmsRos2FromGuardedPlayMode(string source)
        {
            var body = MethodBody(source, "private void Update()");
            if (body.Length == 0)
                return false;

            var shutdownGate = body.IndexOf("if (IsShuttingDown", StringComparison.Ordinal);
            var ensure = body.IndexOf("EnsureRos2UnityReady()", StringComparison.Ordinal);
            var refresh = body.IndexOf("RefreshBindings();", StringComparison.Ordinal);
            return shutdownGate >= 0
                   && ensure > shutdownGate
                   && refresh > ensure
                   && body.Contains("!_ros2RuntimeWasReady", StringComparison.Ordinal)
                   && !body.Contains("ROS2UnityComponent", StringComparison.Ordinal);
        }

        private static bool BridgeCallbackGetterUsesReadyRuntimeOnly(string source)
        {
            var body = MethodBody(source, "private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)");
            var shutdownGate = body.IndexOf("if (IsShuttingDown)", StringComparison.Ordinal);
            var readyGate = body.IndexOf("if (!_ros2RuntimeWasReady)", StringComparison.Ordinal);
            var existing = body.IndexOf("TryGetExistingRos2Unity", StringComparison.Ordinal);
            return shutdownGate >= 0
                   && readyGate > shutdownGate
                   && existing > readyGate
                   && !body.Contains("EnsureRos2UnityReady()", StringComparison.Ordinal);
        }

        private static string MethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var bodyStart = source.IndexOf('{', start);
            if (bodyStart < 0)
                return string.Empty;

            var depth = 0;
            var bodyEnd = -1;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        bodyEnd = i;
                        break;
                    }
                }
            }

            if (bodyEnd <= bodyStart)
                return string.Empty;

            return source.Substring(bodyStart, bodyEnd - bodyStart + 1);
        }

        private static bool NativeDllExists(string fileName)
            => File.Exists(RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/" + fileName));

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"161-file: {relativePath} exists");
            if (FileTextCache.TryGetValue(path, out var cached))
                return cached;

            var text = File.ReadAllText(path);
            FileTextCache[path] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string[] RuntimePackageKeys(string json)
        {
            var matches = Regex.Matches(
                json ?? string.Empty,
                "\"(dev\\.unity2foxglove\\.ros2forunity\\.runtime\\.[^\"]+)\"\\s*:");
            return matches.Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
        }

        private static string RuntimeId(string runtimeManifest)
        {
            var match = Regex.Match(
                runtimeManifest ?? string.Empty,
                "\"runtimeId\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExpectedRuntimeId(string runtimePackage)
        {
            const string prefix = "dev.unity2foxglove.ros2forunity.runtime.";
            var suffix = runtimePackage.StartsWith(prefix, StringComparison.Ordinal)
                ? runtimePackage.Substring(prefix.Length)
                : runtimePackage;
            return "r2fu-" + suffix.Replace('.', '-');
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
