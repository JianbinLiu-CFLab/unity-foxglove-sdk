// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 161 validation for the R2FU Jazzy Win64 runtime refresh.

using System;
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
            NativeBridgeCallbacksDoNotLazyInitializeRos2DuringShutdown();

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

            Check(manifestRuntimes.Length == 1
                  && manifestRuntimes[0] == "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "161-E1: Unity sample project manifest resolves exactly the Jazzy R2FU runtime package");
            Check(lockRuntimes.Length == 1
                  && lockRuntimes[0] == "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "161-E2: Unity sample project lock resolves exactly the Jazzy R2FU runtime package");
            Check(manifest.Contains("file:../../Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && lockFile.Contains("\"source\": \"local\"", StringComparison.Ordinal),
                "161-E3: active Jazzy runtime is referenced from the repository Packages candidate directory");
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

        private static void NativeBridgeCallbacksDoNotLazyInitializeRos2DuringShutdown()
        {
            foreach (var bridge in new[]
            {
                "Ros2ForUnityTransformNativeBridge.cs",
                "Ros2ForUnityPointCloud2NativeBridge.cs",
                "Ros2ForUnityImuNativeBridge.cs",
                "Ros2ForUnityCameraNativeBridge.cs",
            })
            {
                var source = ReadRepoText(AdapterPackage + "/Runtime/Native/" + bridge);
                Check(source.Contains("using UnityEngine.SceneManagement;", StringComparison.Ordinal)
                      && source.Contains("IsBackupSceneActive()", StringComparison.Ordinal)
                      && source.Contains("Temp/__Backupscenes", StringComparison.Ordinal),
                    "161-G-backup-scene: " + bridge + " treats Unity backup scenes as R2FU shutdown windows");
                Check(source.Contains("EnsureRos2UnityReady()", StringComparison.Ordinal)
                      && source.Contains("TryGetExistingRos2Unity", StringComparison.Ordinal),
                    "161-G-prewarm: " + bridge + " prewarms ROS2 from stable bridge Update");
                Check(BridgeCallbackGetterAvoidsLazyInit(source),
                    "161-G-no-lazy-init: " + bridge + " callback getter never creates or first-initializes ROS2");
            }
        }

        private static bool BridgeCallbackGetterAvoidsLazyInit(string source)
        {
            var start = source.IndexOf(
                "private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)",
                StringComparison.Ordinal);
            if (start < 0)
                return false;

            var bodyStart = source.IndexOf('{', start);
            if (bodyStart < 0)
                return false;

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
                return false;

            var body = source.Substring(bodyStart, bodyEnd - bodyStart + 1);
            return !body.Contains("AddComponent<ROS2UnityComponent>", StringComparison.Ordinal)
                   && !body.Contains(".Ok()", StringComparison.Ordinal);
        }

        private static bool NativeDllExists(string fileName)
            => File.Exists(RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/" + fileName));

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"161-file: {relativePath} exists");
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath);

        private static string[] RuntimePackageKeys(string json)
        {
            var matches = Regex.Matches(
                json ?? string.Empty,
                "\"(dev\\.unity2foxglove\\.ros2forunity\\.runtime\\.[^\"]+)\"\\s*:");
            return matches.Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
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
