// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-31 validation for Jazzy runtime refresh and package path hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_31Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-31: Jazzy Runtime Refresh Package Path ===");
            _passed = 0;

            RuntimeSelectionKeepsJazzyFastDdsOnly();
            RuntimeSelectionUsesPlatformSensitiveEmbeddedPackageComparison();
            JazzyRuntimeCapturesSourcedDistroBeforeStandalonePatch();
            JazzyRuntimeStopsComponentExecutorsBeforeSharedShutdown();
            JazzyComponentDoesNotRestartDeadRuntimeAfterSharedShutdown();
            JazzyBuilderRegeneratesStandaloneIntegrityPatch();
            JazzyValidatorChecksLocalPackageManagerGuardAndLifecyclePatch();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-31: {_passed} checks passed.");
        }

        private static void RuntimeSelectionKeepsJazzyFastDdsOnly()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(source.Contains("IsZenohCapableDistro(rosDistro) && string.IsNullOrWhiteSpace(zenohPayloadDiagnostic)", StringComparison.Ordinal)
                  && source.Contains("GetZenohPayloadDiagnostic(packageDirectory, rosDistro)", StringComparison.Ordinal),
                "163-31A-1: runtime selection requires both distro capability and complete Zenoh payload");
            Check(source.Contains("string.Equals(rosDistro, \"lyrical\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && !ExtractMethod(source, "IsZenohCapableDistro").Contains("\"jazzy\"", StringComparison.Ordinal),
                "163-31A-2: runtime selection does not mark Jazzy Zenoh-capable by stray DLL presence");
        }

        private static void RuntimeSelectionUsesPlatformSensitiveEmbeddedPackageComparison()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(source.Contains("Application.platform == RuntimePlatform.WindowsEditor", StringComparison.Ordinal)
                  && source.Contains("StringComparison.OrdinalIgnoreCase", StringComparison.Ordinal)
                  && source.Contains(": StringComparison.Ordinal;", StringComparison.Ordinal),
                "163-31B: embedded package detection only uses case-insensitive paths on Windows Editor");
        }

        private static void JazzyRuntimeCapturesSourcedDistroBeforeStandalonePatch()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var constructor = ExtractMethod(source, "ROS2ForUnity");
            var checkIntegrity = ExtractMethod(source, "CheckIntegrity");

            Check(source.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && checkIntegrity.Contains("CheckIntegrity(GetROSVersionSourced());", StringComparison.Ordinal)
                  && source.Contains("private static void FailIntegrity", StringComparison.Ordinal),
                "163-31C-1: Jazzy runtime keeps public CheckIntegrity and fails closed on integrity mismatches");
            Check(constructor.Contains("string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();", StringComparison.Ordinal)
                  && constructor.IndexOf("sourcedRosDistroBeforeStandalonePatch", StringComparison.Ordinal)
                     < constructor.IndexOf("SetStandalonePrefixPath();", StringComparison.Ordinal)
                  && constructor.IndexOf("SetStandalonePrefixPath();", StringComparison.Ordinal)
                     < constructor.IndexOf("CheckROSSupport(currentRos2Version);", StringComparison.Ordinal)
                  && constructor.Contains("CheckIntegrity(sourcedRosDistroBeforeStandalonePatch);", StringComparison.Ordinal),
                "163-31C-2: Jazzy standalone startup snapshots external ROS_DISTRO before patching native env");
        }

        private static void JazzyRuntimeStopsComponentExecutorsBeforeSharedShutdown()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var destroy = ExtractMethod(source, "DestroyROS2ForUnity");

            Check(destroy.Contains("ROS2UnityComponent.StopAllExecutorsForRosShutdown();", StringComparison.Ordinal)
                  && destroy.IndexOf("ROS2UnityComponent.StopAllExecutorsForRosShutdown();", StringComparison.Ordinal)
                     < destroy.IndexOf("Ros2cs.Shutdown();", StringComparison.Ordinal),
                "163-31D: Jazzy runtime stops component executors before shutting down shared ROS2 context");
        }

        private static void JazzyComponentDoesNotRestartDeadRuntimeAfterSharedShutdown()
        {
            var component = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var lazyConstruct = ExtractMethod(component, "LazyConstruct");
            var stopAll = ExtractMethod(component, "StopAllExecutorsForRosShutdown");
            var startExecutor = ExtractMethod(component, "StartExecutor");
            var markRuntimeShutdown = ExtractMethod(component, "MarkRuntimeShutdown");

            Check(component.Contains("private static readonly HashSet<ROS2UnityComponent> instances", StringComparison.Ordinal)
                  && stopAll.Contains("component.MarkRuntimeShutdown();", StringComparison.Ordinal)
                  && component.Contains("instances.Remove(this);", StringComparison.Ordinal),
                "163-31E-1: Jazzy components register for shared runtime shutdown and unregister on detach");
            Check(component.Contains("private bool runtimeShutdownRequested", StringComparison.Ordinal)
                  && lazyConstruct.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && lazyConstruct.Contains("throw new ObjectDisposedException", StringComparison.Ordinal)
                  && startExecutor.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && startExecutor.Contains("ros2forUnity == null", StringComparison.Ordinal)
                  && markRuntimeShutdown.Contains("ros2forUnity = null;", StringComparison.Ordinal),
                "163-31E-2: Jazzy executor does not restart against a dead ROS2ForUnity instance");
        }

        private static void JazzyBuilderRegeneratesStandaloneIntegrityPatch()
        {
            var builder = ReadRepoText("Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py");
            var patch = ExtractPythonFunction(builder, "patch_standalone_environment_bootstrap");

            Check(patch.Contains("old_check_signature", StringComparison.Ordinal)
                  && patch.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && patch.Contains("sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced()", StringComparison.Ordinal)
                  && patch.Contains("CheckIntegrity(sourcedRosDistroBeforeStandalonePatch)", StringComparison.Ordinal)
                  && patch.Contains("FailIntegrity", StringComparison.Ordinal),
                "163-31F: Jazzy runtime builder regenerates the standalone integrity and env-order patch");
        }

        private static void JazzyValidatorChecksLocalPackageManagerGuardAndLifecyclePatch()
        {
            var validator = ReadRepoText("Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py");
            var required = ExtractPythonFunction(validator, "check_required_files");
            var packagePath = ExtractPythonFunction(validator, "check_package_path_patch");
            var sourcePatch = ExtractPythonFunction(validator, "check_runtime_source_patches");

            Check(required.Contains("RUNTIME_ROOT / \"Scripts\" / \"ROS2UnityCore.cs\"", StringComparison.Ordinal),
                "163-31G-1: Jazzy validator reports ROS2UnityCore.cs as an explicit required file");
            Check(packagePath.Contains("re.search(", StringComparison.Ordinal)
                  && packagePath.Contains("UnityEditor\\.PackageManager\\.PackageInfo", StringComparison.Ordinal)
                  && !packagePath.Contains("text.index(\"#if UNITY_EDITOR\")", StringComparison.Ordinal),
                "163-31G-2: Jazzy validator checks the PackageManager guard around the lookup itself");
            Check(sourcePatch.Contains("sourcedRosDistroBeforeStandalonePatch", StringComparison.Ordinal)
                  && sourcePatch.Contains("FailIntegrity", StringComparison.Ordinal)
                  && sourcePatch.Contains("ROS2UnityComponent.StopAllExecutorsForRosShutdown()", StringComparison.Ordinal)
                  && sourcePatch.Contains("runtimeShutdownRequested", StringComparison.Ordinal),
                "163-31G-3: Jazzy validator requires runtime integrity and shared-shutdown lifecycle patches");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_31Validation.cs", StringComparison.Ordinal),
                "163-31H-1: runtime test project compiles Phase163_31Validation");
            Check(registry.Contains("--phase163-31", StringComparison.Ordinal)
                  && registry.Contains("Phase163_31Validation.Validate", StringComparison.Ordinal),
                "163-31H-2: validation registry exposes --phase163-31");
        }

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = -1;
            foreach (var prefix in new[]
                     {
                         "public void ",
                         "private void ",
                         "internal void ",
                         "internal ",
                         "private static void ",
                         "public static void ",
                         "public ",
                         "private ",
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
