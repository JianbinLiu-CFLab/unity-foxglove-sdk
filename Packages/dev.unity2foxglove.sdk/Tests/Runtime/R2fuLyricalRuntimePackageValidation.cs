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
                  && orchestrator.Contains("C:\\ros2_lyrical\\ros2-windows", StringComparison.Ordinal)
                  && orchestrator.Contains("phase146b", StringComparison.Ordinal),
                "146B-B3: Lyrical build orchestrator has distro-specific defaults");
        }

        private static void AdapterManifestRecordsLyricalRuntime()
        {
            var manifest = ReadRepoText(AdapterPackage + "/Compliance/ros2-for-unity-adoption-manifest.json");
            Check(manifest.Contains("\"supportedRuntimePackages\"", StringComparison.Ordinal)
                  && manifest.Contains("\"packageName\": \"dev.unity2foxglove.ros2forunity.runtime.lyrical.win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"runtimeId\": \"r2fu-lyrical-win64\"", StringComparison.Ordinal)
                  && manifest.Contains("\"artifactSha256\": \"58d4a3dbf5d354c8c90c30548a4a2712296b513f374685cdf9b395cba65c7fe5\"", StringComparison.Ordinal),
                "146B-C1: adapter manifest records the Lyrical supported runtime package");
            Check(RepoFileExists(AdapterPackage + "/Compliance/r2fu-lyrical-win64-runtime-inventory.json")
                  && RepoFileExists(AdapterPackage + "/Compliance/r2fu-lyrical-win64-runtime-notices.md"),
                "146B-C2: adapter compliance has Lyrical inventory and notices");

            var readme = ReadRepoText(AdapterPackage + "/README.md");
            Check(readme.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && readme.Contains("active runtime dropdown", StringComparison.Ordinal)
                  && readme.Contains("restart Unity", StringComparison.Ordinal),
                "146B-C3: adapter README documents Lyrical package selection");

            var sampleReadme = ReadRepoText(AdapterPackage + "/Samples~/ROS2 For Unity External Adapter/README.md");
            Check(sampleReadme.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal)
                  && sampleReadme.Contains("active runtime dropdown", StringComparison.Ordinal)
                  && sampleReadme.Contains("Restart Unity after switching runtime packages", StringComparison.Ordinal),
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
            Check(selector.Contains("MarkEditorRestartRequired", StringComparison.Ordinal)
                  && selector.Contains("GetPendingEditorRestartRuntimePackage", StringComparison.Ordinal)
                  && ReadRepoText(AdapterPackage + "/Editor/Ros2ForUnityRuntimePlayModeGuard.cs").Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal),
                "146B-D3: runtime switching requires an Editor restart before Play Mode");
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
