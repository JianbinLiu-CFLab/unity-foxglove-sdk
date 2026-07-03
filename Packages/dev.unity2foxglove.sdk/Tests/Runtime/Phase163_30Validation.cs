// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-30 validation for Humble runtime import and FastRTPS package path hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_30Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-30: Humble Runtime Import and FastRTPS Package Path ===");
            _passed = 0;

            HumbleRuntimeCapturesSourcedDistroBeforeStandalonePatch();
            HumbleRuntimeLifecycleLogsAvoidEditorStackTraceExtraction();
            HumbleExecutorDoesNotRestartDeadRuntimeAfterSharedShutdown();
            HumbleSyncWritesSemverLockVersion();
            Ros2WindowsEnvDoesNotInferUnknownPathsAsJazzy();
            HumbleValidatorUsesLocalPackageManagerGuardAndRequiresCore();
            HumbleBuilderRegeneratesStandaloneDistroCapture();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-30: {_passed} checks passed.");
        }

        private static void HumbleRuntimeCapturesSourcedDistroBeforeStandalonePatch()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var constructor = ExtractMethod(source, "ROS2ForUnity");
            var checkIntegrity = ExtractMethod(source, "CheckIntegrity");

            Check(source.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && checkIntegrity.Contains("CheckIntegrity(GetROSVersionSourced());", StringComparison.Ordinal),
                "163-30A-1: Humble runtime keeps public CheckIntegrity while allowing a pre-patch ROS_DISTRO snapshot");
            Check(constructor.Contains("string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();", StringComparison.Ordinal)
                  && constructor.IndexOf("sourcedRosDistroBeforeStandalonePatch", StringComparison.Ordinal)
                     < constructor.IndexOf("SetStandaloneRosDistro(packagedRos2Version)", StringComparison.Ordinal)
                  && constructor.Contains("WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);", StringComparison.Ordinal)
                  && constructor.Contains("CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);", StringComparison.Ordinal)
                  && !source.Contains("ROS2 version in standalone process environment does not match this runtime package", StringComparison.Ordinal),
                "163-30A-2: Humble standalone startup warns then ignores external ROS_DISTRO before integrity checks");
        }

        private static void HumbleRuntimeLifecycleLogsAvoidEditorStackTraceExtraction()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var constructor = ExtractMethod(source, "ROS2ForUnity");
            var shutdown = ExtractMethod(source, "CompleteShutdownShared");

            Check(source.Contains("private static void LogRuntimeInfoWithoutStackTrace", StringComparison.Ordinal)
                  && constructor.Contains("LogRuntimeInfoWithoutStackTrace(\"ROS2 version: \"", StringComparison.Ordinal)
                  && shutdown.Contains("LogRuntimeInfoWithoutStackTrace(\"Shutting down Ros2 For Unity\")", StringComparison.Ordinal),
                "163-30A-3: Humble runtime lifecycle logs avoid Editor stack trace extraction");
        }

        private static void HumbleExecutorDoesNotRestartDeadRuntimeAfterSharedShutdown()
        {
            var component = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var stopAll = ExtractMethod(component, "StopAllExecutorsForRosShutdown");
            var startExecutor = ExtractMethod(component, "StartExecutor");
            var markRuntimeShutdown = ExtractMethod(component, "MarkRuntimeShutdown");

            Check(component.Contains("private bool runtimeShutdownRequested", StringComparison.Ordinal)
                  && stopAll.Contains("component.MarkRuntimeShutdown();", StringComparison.Ordinal),
                "163-30B-1: shared Humble shutdown marks components whose ROS2 context was torn down");
            Check(startExecutor.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && startExecutor.Contains("ros2forUnity == null", StringComparison.Ordinal)
                  && markRuntimeShutdown.Contains("ros2forUnity = null;", StringComparison.Ordinal),
                "163-30B-2: Humble executor does not restart against a dead ROS2ForUnity instance");
        }

        private static void HumbleSyncWritesSemverLockVersion()
        {
            var sync = ReadRepoText("Scripts/ros2forunity/windows/humble/sync_r2fu_artifact_to_unity2foxglove.py");

            Check(sync.Contains("\"version\": PACKAGE_VERSION", StringComparison.Ordinal)
                  && !sync.Contains("\"version\": runtime_ref", StringComparison.Ordinal),
                "163-30C: Humble project sync writes package semver into packages-lock.json");
        }

        private static void Ros2WindowsEnvDoesNotInferUnknownPathsAsJazzy()
        {
            var env = ReadRepoText("Scripts/smoke/ros2/_ros2_windows_env.py");
            var infer = ExtractPythonFunction(env, "infer_ros_distro");

            Check(env.Contains("DEFAULT_JAZZY_ROS2_ROOT = default_ros2_root(\"jazzy\")", StringComparison.Ordinal)
                  && env.Contains("DEFAULT_HUMBLE_ROS2_ROOT = default_ros2_root(\"humble\")", StringComparison.Ordinal)
                  && env.Contains("DEFAULT_LYRICAL_ROS2_ROOT = default_ros2_root(\"lyrical\")", StringComparison.Ordinal)
                  && env.Contains("DEFAULT_ROS2_ROOT = DEFAULT_JAZZY_ROS2_ROOT", StringComparison.Ordinal),
                "163-30D-1: shared ROS2 smoke helper exposes explicit distro defaults while preserving the legacy Jazzy alias");
            Check(infer.Contains("raise ValueError", StringComparison.Ordinal)
                  && infer.TrimEnd().EndsWith("raise ValueError(f\"Cannot infer ROS_DISTRO from ROS2 root path: {ros2_root}\")", StringComparison.Ordinal),
                "163-30D-2: infer_ros_distro fails closed for unknown ROS2 root paths");
        }

        private static void HumbleValidatorUsesLocalPackageManagerGuardAndRequiresCore()
        {
            var validator = ReadRepoText("Scripts/ros2forunity/windows/humble/validate_r2fu_runtime_package.py");
            var required = ExtractPythonFunction(validator, "check_required_files");
            var packagePath = ExtractPythonFunction(validator, "check_package_path_patch");

            Check(required.Contains("RUNTIME_ROOT / \"Scripts\" / \"ROS2UnityCore.cs\"", StringComparison.Ordinal),
                "163-30E-1: Humble validator reports ROS2UnityCore.cs as an explicit required file");
            Check(packagePath.Contains("re.search(", StringComparison.Ordinal)
                  && packagePath.Contains("UnityEditor\\.PackageManager\\.PackageInfo", StringComparison.Ordinal)
                  && !packagePath.Contains("text.index(\"#if UNITY_EDITOR\")", StringComparison.Ordinal),
                "163-30E-2: Humble validator checks the PackageManager guard around the lookup itself");
        }

        private static void HumbleBuilderRegeneratesStandaloneDistroCapture()
        {
            var builder = ReadRepoText("Scripts/ros2forunity/windows/humble/build_r2fu_runtime_package.py");
            var patch = ExtractPythonFunction(builder, "patch_standalone_environment_isolation");

            Check(patch.Contains("old_check_signature", StringComparison.Ordinal)
                  && patch.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && patch.Contains("sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced()", StringComparison.Ordinal)
                  && patch.Contains("WarnIfStandaloneRosDistroOverride", StringComparison.Ordinal)
                  && patch.Contains("CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)", StringComparison.Ordinal)
                  && !patch.Contains("ROS2 version in standalone process environment does not match this runtime package", StringComparison.Ordinal),
                "163-30F: Humble runtime builder regenerates standalone ROS_DISTRO override isolation");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_30Validation.cs", StringComparison.Ordinal),
                "163-30G-1: runtime test project compiles Phase163_30Validation");
            Check(registry.Contains("--phase163-30", StringComparison.Ordinal)
                  && registry.Contains("Phase163_30Validation.Validate", StringComparison.Ordinal),
                "163-30G-2: validation registry exposes --phase163-30");
        }

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = -1;
            foreach (var prefix in new[]
                     {
                         "public void ",
                         "private void ",
                         "internal ",
                         "private static void ",
                         "public static void "
                     })
            {
                signature = source.IndexOf(prefix + methodName + "(", StringComparison.Ordinal);
                if (signature >= 0)
                    break;
            }

            if (signature < 0)
                return string.Empty;

            var bodyStart = source.IndexOf('{', signature);
            if (bodyStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            return source.Substring(bodyStart);
        }

        private static string ExtractPythonFunction(string source, string functionName)
        {
            var signature = source.IndexOf("def " + functionName + "(", StringComparison.Ordinal);
            if (signature < 0)
                return string.Empty;

            var next = source.IndexOf("\ndef ", signature + 1, StringComparison.Ordinal);
            return next < 0 ? source.Substring(signature) : source.Substring(signature, next - signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
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
