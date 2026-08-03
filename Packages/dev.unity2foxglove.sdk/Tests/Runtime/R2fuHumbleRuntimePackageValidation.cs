// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 160 validation for the R2FU Humble Win64 runtime package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class R2fuHumbleRuntimePackageValidation
    {
        private const string RuntimePackage =
            "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64";
        private const string AdapterPackage =
            "Packages/dev.unity2foxglove.ros2forunity";
        private const string HumbleScripts =
            "Scripts/ros2forunity/windows/humble";
        private const string Ros2SmokeScripts =
            "Scripts/smoke/ros2";
        private const string UnityManifestPath =
            "Unity2Foxglove/Packages/manifest.json";
        private const string UnityLockPath =
            "Unity2Foxglove/Packages/packages-lock.json";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";
        private const string ProjectPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj";
        private const string ExpectedSha =
            "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0";

        private static readonly Dictionary<string, string> FileTextCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<string> Failures = new List<string>();

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 160: R2FU Humble Win64 Runtime Package ===");
            _passed = 0;
            FileTextCache.Clear();
            Failures.Clear();

            RunCheckGroup("runtime package shape", RuntimePackageShapeIsPresent);
            RunCheckGroup("runtime dependency floor", RuntimePackageContainsHumbleDependencyFloor);
            RunCheckGroup("plugin import health", RuntimePackageRecordsPluginImportAndManifestHealth);
            RunCheckGroup("humble scripts", HumbleScriptsAreDistroSpecific);
            RunCheckGroup("adapter manifest", AdapterManifestRecordsHumbleRuntime);
            RunCheckGroup("runtime selector", SelectorDiscoversHumbleByConvention);
            RunCheckGroup("unity project runtime", UnityProjectResolvesOnlyHumbleRuntime);
            RunCheckGroup("embedded runtime candidates", RuntimeCandidatesAreNotEmbedded);
            RunCheckGroup("validation registry", ValidationRegistryWiresPhase160);

            if (Failures.Count > 0)
                throw new Exception("Phase 160 failed:\n" + string.Join("\n", Failures.Select(failure => "  - " + failure)));

            Console.WriteLine($"Phase 160: {_passed} checks passed.");
        }

        private static void RuntimePackageShapeIsPresent()
        {
            Check(RepoFileExists(RuntimePackage + "/package.json"),
                "160-A1: Humble runtime package.json is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/runtime-manifest.json"),
                "160-A2: Humble runtime manifest is present");
            Check(RepoFileExists(RuntimePackage + "/RuntimeSupport/r2fu-humble-win64-runtime-inventory.json"),
                "160-A3: Humble runtime inventory is present");
            Check(RepoFileExists(RuntimePackage + "/THIRD_PARTY_NOTICES.md"),
                "160-A4: Humble runtime notices are present");

            var packageJson = ReadRepoText(RuntimePackage + "/package.json");
            Check(packageJson.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal)
                  && packageJson.Contains("Humble Win64", StringComparison.Ordinal),
                "160-A5: package metadata names the Humble Win64 runtime package");

            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(manifest.Contains("\"runtimeId\": \"r2fu-humble-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rosDistro\": \"humble\"", StringComparison.Ordinal)
                  && manifest.Contains("\"rmwImplementation\": \"rmw_fastrtps_cpp\"", StringComparison.Ordinal)
                  && manifest.Contains("\"supportLevel\": \"Recommended\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSha256\": \"" + ExpectedSha + "\"", StringComparison.Ordinal),
                "160-A6: runtime manifest records Humble identity, FastRTPS RMW, and pinned artifact hash");

            var pluginMetadata = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/metadata_ros2cs.xml");
            var nativeMetadata = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/metadata_ros2cs.xml");
            Check(pluginMetadata.Contains("<ros2>humble</ros2>", StringComparison.Ordinal)
                  && pluginMetadata.Contains("<desc>v0.9.0</desc>", StringComparison.Ordinal)
                  && nativeMetadata.Contains("<ros2>humble</ros2>", StringComparison.Ordinal)
                  && nativeMetadata.Contains("<desc>v0.9.0</desc>", StringComparison.Ordinal),
                "160-A6b: Humble ros2cs metadata distro and description agree");
            Check(pluginMetadata.Contains("<plugins root=\".\"", StringComparison.Ordinal)
                  && nativeMetadata.Contains("<plugins root=\".\"", StringComparison.Ordinal)
                  && !pluginMetadata.Contains("D:\\", StringComparison.Ordinal)
                  && !nativeMetadata.Contains("D:\\", StringComparison.Ordinal),
                "160-A6c: Humble ros2cs metadata does not ship build-machine plugin roots");
            Check(NativeDllExists("dds_security_auth.dll")
                  && RepoFileExists(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/dds_security_auth.dll.meta"),
                "160-A6d: Humble DDS security auth plugin has Unity meta coverage");

            var runtimeSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var componentSource = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            Check(runtimeSource.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal)
                  && runtimeSource.Contains("ValidateRmwImplementation", StringComparison.Ordinal)
                  && runtimeSource.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal),
                "160-A7: ROS2ForUnity.cs has package path support and FastRTPS RMW guard");
            Check(runtimeSource.Contains("StopAllExecutorsForRosShutdown", StringComparison.Ordinal)
                  && componentSource.Contains("StopAllExecutorsForRosShutdown", StringComparison.Ordinal),
                "160-A7b: ROS2ForUnity shutdown hook is implemented by ROS2UnityComponent");
            var constructor = ExtractMethodBody(runtimeSource, "ROS2ForUnity");
            Check(constructor.Contains("string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();", StringComparison.Ordinal)
                  && CountSubstring(constructor, "SetStandalonePrefixPath();") == 1
                  && CountSubstring(constructor, "SetStandaloneRmwImplementation();") == 1
                  && CountSubstring(constructor, "SetStandaloneRcutilsConsoleMode();") == 1
                  && CountSubstring(constructor, "SetStandaloneRosDistro(currentRos2Version);") == 1
                  && !constructor.Contains("packagedRos2Version = GetMetadataValue", StringComparison.Ordinal)
                  && runtimeSource.Contains("AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared", StringComparison.Ordinal)
                  && runtimeSource.Contains("AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared", StringComparison.Ordinal)
                  && !runtimeSource.Contains("ThrowIfUninitialized", StringComparison.Ordinal)
                  && runtimeSource.Contains("LoadMetadata() must complete before metadata-backed properties are read.", StringComparison.Ordinal),
                "160-A7c: Humble ROS2ForUnity avoids duplicate standalone env setup and stale reload handlers");
            var dispose = ExtractMethodBody(runtimeSource, "Dispose");
            Check(runtimeSource.Contains("internal class ROS2ForUnity : IDisposable", StringComparison.Ordinal)
                  && dispose.Contains("DestroyROS2ForUnity();", StringComparison.Ordinal)
                  && dispose.Contains("GC.SuppressFinalize(this);", StringComparison.Ordinal)
                  && !runtimeSource.Contains("~ROS2ForUnity", StringComparison.Ordinal),
                "160-A7d: Humble ROS2ForUnity implements deterministic Dispose without a native finalizer");

            var asmdef = ReadRepoText(RuntimePackage + "/Runtime/Ros2ForUnity/Scripts/Unity2Foxglove.Ros2ForUnity.Runtime.HumbleWin64.asmdef");
            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"WindowsStandalone64\"", StringComparison.Ordinal)
                  && !asmdef.Contains("defineConstraints", StringComparison.Ordinal),
                "160-A8: Humble runtime asmdef is Windows runtime scoped and not define-gated");
        }

        private static void RuntimePackageContainsHumbleDependencyFloor()
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
                    "160-B-baseline-managed: " + assembly + " exists");
            }

            foreach (var family in new[]
            {
                "actionlib_msgs",
                "composition_interfaces",
                "lifecycle_msgs",
                "statistics_msgs",
                "stereo_msgs",
            })
            {
                Check(RepoFileExists(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/" + family + "_assembly.dll"),
                    "160-B-handoff-managed: " + family + " managed assembly exists");
                Check(NativeDllExists(family + "__rosidl_typesupport_fastrtps_c.dll"),
                    "160-B-handoff-native: " + family + " FastRTPS native support exists");
            }

            foreach (var dll in new[]
            {
                "tf2.dll",
                "tf2_ros.dll",
                "static_transform_broadcaster_node.dll",
                "rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
            })
            {
                Check(NativeDllExists(dll), "160-B-native: " + dll + " exists");
            }

            Check(!NativeDllExists("rmw_zenoh_cpp.dll"),
                "160-B-zenoh: Humble runtime remains FastRTPS-only");
        }

        private static void RuntimePackageRecordsPluginImportAndManifestHealth()
        {
            var nativePluginRoot = RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64");
            var dllMetas = Directory.GetFiles(nativePluginRoot, "*.dll.meta", SearchOption.TopDirectoryOnly);
            Check(dllMetas.Length > 900,
                "160-B-plugin-count: Humble Win64 native DLL metas are present");

            var invalidImporterMetas = dllMetas
                .Where(path =>
                {
                    var meta = File.ReadAllText(path);
                    return !meta.Contains("PluginImporter:", StringComparison.Ordinal)
                           || meta.Contains("TextScriptImporter:", StringComparison.Ordinal)
                           || !meta.Contains("CPU: x86_64", StringComparison.Ordinal)
                           || !meta.Contains("OS: Windows", StringComparison.Ordinal)
                           || !meta.Contains("Standalone: Windows", StringComparison.Ordinal);
                })
                .Select(Path.GetFileName)
                .ToArray();
            Check(invalidImporterMetas.Length == 0,
                "160-B-plugin-importers: Humble Win64 native DLL metas import as Windows x86_64 plugins");

            var xmlWithAbsoluteWindowsPaths = Directory.GetFiles(RepoPath(RuntimePackage), "*.xml", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("D:\\", StringComparison.Ordinal))
                .Select(path => path.Substring(RepoPath(RuntimePackage).Length + 1))
                .ToArray();
            Check(xmlWithAbsoluteWindowsPaths.Length == 0,
                "160-B-metadata-paths: Humble runtime XML metadata does not expose build-machine Windows paths");
        }

        private static void HumbleScriptsAreDistroSpecific()
        {
            foreach (var script in new[]
            {
                "inspect_r2fu_runtime_artifact.py",
                "build_r2fu_runtime_package.py",
                "sync_r2fu_artifact_to_unity2foxglove.py",
                "validate_r2fu_runtime_package.py",
                "validate_ros2forunity_package.py",
                "phase160_r2fu_humble_windows_build.py",
            })
            {
                Check(RepoFileExists(HumbleScripts + "/" + script),
                    "160-C-file: " + script + " exists");
            }

            var sync = ReadRepoText(HumbleScripts + "/sync_r2fu_artifact_to_unity2foxglove.py");
            Check(sync.Contains("EXPECTED_ARTIFACT_SHA256", StringComparison.Ordinal)
                  && sync.Contains(ExpectedSha, StringComparison.Ordinal)
                  && sync.Contains("RUNTIME_PACKAGE_PREFIX", StringComparison.Ordinal),
                "160-C1: Humble sync script fail-closes on the pinned artifact hash and single runtime prefix");

            var build = ReadRepoText(HumbleScripts + "/build_r2fu_runtime_package.py");
            Check(build.Contains("Ros2ForUnity_humble_standalone_windows_x86_64.zip", StringComparison.Ordinal)
                  && build.Contains("r2fu-humble-win64", StringComparison.Ordinal)
                  && build.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal),
                "160-C2: Humble builder uses Humble artifact, runtime id, and package name");

            var validator = ReadRepoText(HumbleScripts + "/validate_r2fu_runtime_package.py");
            Check(validator.Contains("rosgraph_msgs", StringComparison.Ordinal)
                  && validator.Contains("HUMBLE_HANDOFF_FAMILIES", StringComparison.Ordinal)
                  && validator.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal)
                  && validator.Contains("avoids duplicate standalone environment setup", StringComparison.Ordinal)
                  && validator.Contains("before assembly reload", StringComparison.Ordinal),
                "160-C3: Humble validator locks the message dependency floor and FastRTPS-only boundary");

            var smoke = ReadRepoText(Ros2SmokeScripts + "/phase160_humble_lidar_deskew_acceptance.py");
            Check(smoke.Contains("default_ros2_root(\"humble\"", StringComparison.Ordinal)
                  && smoke.Contains("ros2-windows/ros2_humble", StringComparison.Ordinal)
                  && smoke.Contains("RESULT_MARKER = \"PHASE160_RESULT_JSON:\"", StringComparison.Ordinal)
                  && smoke.Contains("--probe", StringComparison.Ordinal)
                  && smoke.Contains("Manual pass criterion", StringComparison.Ordinal)
                  && smoke.Contains("phase160_humble_", StringComparison.Ordinal)
                  && smoke.Contains("launch_rviz", StringComparison.Ordinal)
                  && !smoke.Contains("phase138u_lidar_deskew_rviz2_acceptance", StringComparison.Ordinal),
                "160-C4: Humble ROS2 smoke launches Phase160 RViz2 from the repo-local Humble entrypoint and keeps direct probes explicit");

            var ros2Env = ReadRepoText(Ros2SmokeScripts + "/_ros2_windows_env.py");
            Check(ros2Env.Contains("\"humble\" in root_text", StringComparison.Ordinal)
                  && ros2Env.Contains("return \"humble\"", StringComparison.Ordinal),
                "160-C5: shared ROS2 smoke environment infers Humble from the repo-local entrypoint");
            Check(ros2Env.Contains("Library\" / \"plugins", StringComparison.Ordinal)
                  && ros2Env.Contains("QT_QPA_PLATFORM_PLUGIN_PATH", StringComparison.Ordinal),
                "160-C6: shared RViz launcher supports the Humble Qt plugin layout");
        }

        private static void AdapterManifestRecordsHumbleRuntime()
        {
            var manifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");
            Check(manifest.Contains("\"supportedRuntimePackages\"", StringComparison.Ordinal)
                  && manifest.Contains("\"packageName\": \"dev.unity2foxglove.ros2forunity.runtime.humble.win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"runtimeId\": \"r2fu-humble-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSha256\": \"" + ExpectedSha + "\"", StringComparison.Ordinal),
                "160-D1: adapter manifest records Humble as a supported runtime");
            var planned = PlannedRuntimeSection(manifest);
            Check(!planned.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal)
                  && !planned.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && !planned.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && planned.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.ubuntu2604.x64", StringComparison.Ordinal),
                "160-D1b: planned runtime packages contain only future candidates");

            Check(RepoFileExists(AdapterPackage + "/Compliance/r2fu-humble-win64-runtime-inventory.json"),
                "160-D2: adapter compliance has Humble runtime inventory");

            var readme = ReadRepoText(AdapterPackage + "/README.md");
            Check(readme.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal)
                  && readme.Contains("ros2-windows/ros2_humble", StringComparison.Ordinal)
                  && readme.Contains("Humble, Jazzy, and Lyrical", StringComparison.Ordinal),
                "160-D3: adapter README documents Humble runtime selection and local entrypoint");

            var sampleReadme = ReadRepoText(AdapterPackage + "/Samples~/ROS2 For Unity External Adapter/README.md");
            Check(sampleReadme.Contains("dev.unity2foxglove.ros2forunity.runtime.humble.win64", StringComparison.Ordinal)
                  && sampleReadme.Contains("active runtime dropdown", StringComparison.Ordinal),
                "160-D4: external adapter sample documents Humble as a runtime candidate");
        }

        private static void SelectorDiscoversHumbleByConvention()
        {
            var selector = ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs");
            Check(selector.Contains("RuntimePackagePrefix", StringComparison.Ordinal)
                  && selector.Contains("DiscoverCandidateRuntimes", StringComparison.Ordinal)
                  && selector.Contains("var packageRosDistro = parts[0]", StringComparison.Ordinal)
                  && selector.Contains("Ros2ForUnityRuntimeCapabilityParser.Parse", StringComparison.Ordinal)
                  && !selector.Contains("\"Humble Win64\"", StringComparison.Ordinal),
                "160-E1: active runtime selector discovers Humble by package naming convention and manifest capability identity");
            Check(selector.Contains("ReadManifestRuntimePackages", StringComparison.Ordinal)
                  && selector.Contains("Multiple ROS2 For Unity runtime packages", StringComparison.Ordinal),
                "160-E2: runtime selector keeps the single active runtime guard");
        }

        private static void UnityProjectResolvesOnlyHumbleRuntime()
        {
            var manifest = ReadRepoText(UnityManifestPath);
            var lockFile = ReadRepoText(UnityLockPath);
            var manifestRuntimes = RuntimePackageKeys(manifest);
            var lockRuntimes = RuntimePackageKeys(lockFile);

            Check(manifestRuntimes.Length == 1,
                "160-F1: Unity sample project manifest resolves exactly one R2FU runtime package");
            Check(lockRuntimes.Length == 1 && manifestRuntimes.Length == 1 && lockRuntimes[0] == manifestRuntimes[0],
                "160-F2: Unity sample project lock resolves the same single R2FU runtime package");
            if (manifestRuntimes.Length != 1)
                return;

            var activeRuntimePackage = manifestRuntimes[0];
            Check(manifest.Contains("file:../../Packages/" + activeRuntimePackage, StringComparison.Ordinal)
                  && lockFile.Contains("\"source\": \"local\"", StringComparison.Ordinal),
                "160-F3: active runtime is referenced from the repository Packages candidate directory");

            var activeRuntimeManifest = ReadRepoText("Packages/" + activeRuntimePackage + "/RuntimeSupport/runtime-manifest.json");
            Check(RuntimeId(activeRuntimeManifest) == ExpectedRuntimeId(activeRuntimePackage),
                "160-F4: active runtime package identity matches its runtime manifest");

            var runtimeSettings = ReadRepoText("Unity2Foxglove/ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json");
            Check(RuntimeSettingsActivePackage(runtimeSettings) == activeRuntimePackage,
                "160-F5: Unity runtime selection settings match the active project runtime package");
        }

        private static void RuntimeCandidatesAreNotEmbedded()
        {
            var embeddedRoot = RepoPath("Unity2Foxglove/Packages");
            var embeddedCandidates = Directory.Exists(embeddedRoot)
                ? Directory.GetDirectories(embeddedRoot, "dev.unity2foxglove.ros2forunity.runtime.*", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            Check(embeddedCandidates.Length == 0,
                "160-G1: runtime candidates are not embedded under Unity2Foxglove/Packages");
        }

        private static void ValidationRegistryWiresPhase160()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase160\"", StringComparison.Ordinal)
                  && registry.Contains("R2fuHumbleRuntimePackageValidation.Validate", StringComparison.Ordinal),
                "160-H1: validation registry wires --phase160");
            Check(project.Contains("R2fuHumbleRuntimePackageValidation.cs", StringComparison.Ordinal),
                "160-H2: runtime validation project compiles the 160 validation");
        }

        private static bool NativeDllExists(string fileName)
            => File.Exists(RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/" + fileName));

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"160-file: {relativePath} exists");
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
            const string runtimePackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
            var dependencies = JObject.Parse(json ?? string.Empty)["dependencies"] as JObject;
            return dependencies?.Properties()
                .Select(property => property.Name)
                .Where(name => name.StartsWith(runtimePackagePrefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>();
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

        private static string RuntimeSettingsActivePackage(string settingsJson)
        {
            var match = Regex.Match(
                settingsJson ?? string.Empty,
                "\"activeRuntimePackage\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string PlannedRuntimeSection(string json)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"plannedRuntimePackages\"\\s*:\\s*\\[(.*?)\\]",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var marker = methodName + "(";
            var signature = source.IndexOf(marker, StringComparison.Ordinal);
            if (signature < 0)
                return string.Empty;

            var openBrace = source.IndexOf('{', signature);
            if (openBrace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(openBrace, i - openBrace + 1);
                }
            }

            return string.Empty;
        }

        private static int CountSubstring(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void RunCheckGroup(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Failures.Add(name + ": " + ex.Message);
                Console.WriteLine("[FAIL] " + name + ": " + ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                Failures.Add(message);
                Console.WriteLine("[FAIL] " + message);
                return;
            }
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
