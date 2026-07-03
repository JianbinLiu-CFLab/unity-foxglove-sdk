// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-32 validation for Lyrical runtime selection and Zenoh package hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_32Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-32: Lyrical Runtime Selection Hardening ===");
            _passed = 0;

            RuntimeSelectorEditsManifestThroughJsonAst();
            RuntimeSelectorScopesCommunicationModePerRuntime();
            RuntimeSelectorSurfacesMissingZenohPayload();
            LyricalRuntimeCapturesSourcedDistroBeforeStandalonePatch();
            LyricalRuntimeDoesNotRestartAfterSharedShutdown();
            LyricalBuilderAndValidatorRegenerateRuntimePatches();
            LyricalValidationUsesRepoRootDiscovery();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-32: {_passed} checks passed.");
        }

        private static void RuntimeSelectorEditsManifestThroughJsonAst()
        {
            var selector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var remove = ExtractCSharpMethod(selector, "RemoveRuntimePackageDependencies");
            var add = ExtractCSharpMethod(selector, "AddRuntimePackageDependency");

            Check(!selector.Contains("using System.Text.RegularExpressions;", StringComparison.Ordinal)
                  && remove.Contains("ReadManifestJson", StringComparison.Ordinal)
                  && remove.Contains("dependencies.Properties()", StringComparison.Ordinal)
                  && remove.Contains("property.Remove();", StringComparison.Ordinal),
                "163-32A-1: runtime selector removes manifest runtime dependencies through parsed JSON properties");
            Check(add.Contains("new JProperty(packageName", StringComparison.Ordinal)
                  && add.Contains("anchor.AddAfterSelf", StringComparison.Ordinal)
                  && add.Contains("dependencies object is empty", StringComparison.Ordinal)
                  && !add.Contains("lines.Add", StringComparison.Ordinal),
                "163-32A-2: runtime selector inserts the active runtime with an explicit JSON anchor");
            Check(selector.Contains("SerializeManifest", StringComparison.Ordinal)
                  && selector.Contains("DetectLineEnding(manifest)", StringComparison.Ordinal)
                  && selector.Contains("Newtonsoft.Json.Formatting.Indented", StringComparison.Ordinal),
                "163-32A-3: runtime selector serializes valid manifest JSON while preserving line ending style");
        }

        private static void RuntimeSelectorScopesCommunicationModePerRuntime()
        {
            var selector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var getter = ExtractCSharpMethod(selector, "GetCommunicationModeForRuntime");
            var setter = ExtractCSharpMethod(selector, "SetCommunicationMode");

            Check(selector.Contains("GetCommunicationModeSettingsKey", StringComparison.Ordinal)
                  && getter.Contains("GetCommunicationModeSettingsKey(runtime)", StringComparison.Ordinal)
                  && getter.Contains("CommunicationModeEditorUserSettingsKey", StringComparison.Ordinal)
                  && setter.Contains("GetCommunicationModeSettingsKey(runtime)", StringComparison.Ordinal),
                "163-32B: communication-mode preference is scoped to the selected runtime package with legacy fallback");
        }

        private static void RuntimeSelectorSurfacesMissingZenohPayload()
        {
            var selector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var descriptorCtor = ExtractCSharpMethod(selector, "Ros2ForUnityRuntimeDescriptor");
            var diagnostic = ExtractCSharpMethod(selector, "ComputeZenohPayloadDiagnostic");

            Check(selector.Contains("public string ZenohPayloadDiagnostic", StringComparison.Ordinal)
                  && descriptorCtor.Contains("ZenohPayloadDiagnostic = zenohPayloadDiagnostic", StringComparison.Ordinal)
                  && diagnostic.Contains("rmw_zenoh_cpp native library", StringComparison.Ordinal)
                  && diagnostic.Contains("StreamingAssets Zenoh router config", StringComparison.Ordinal)
                  && diagnostic.Contains("Rebuild or re-import the Lyrical runtime ZIP", StringComparison.Ordinal),
                "163-32C-1: runtime descriptor records why Zenoh is unavailable when payload files are missing");
            Check(selector.Contains("IsZenohCapableDistro(rosDistro) && string.IsNullOrWhiteSpace(zenohPayloadDiagnostic)", StringComparison.Ordinal)
                  && inspector.Contains("SelectedRuntime.ZenohPayloadDiagnostic", StringComparison.Ordinal)
                  && inspector.Contains("MessageType.Warning", StringComparison.Ordinal),
                "163-32C-2: Inspector shows a warning instead of silently hiding an incomplete Lyrical Zenoh payload");
        }

        private static void LyricalRuntimeCapturesSourcedDistroBeforeStandalonePatch()
        {
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var constructor = ExtractCSharpMethod(runtime, "ROS2ForUnity");
            var checkIntegrity = ExtractCSharpMethod(runtime, "CheckIntegrity");

            Check(runtime.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && checkIntegrity.Contains("CheckIntegrity(GetROSVersionSourced());", StringComparison.Ordinal)
                  && runtime.Contains("private static void FailIntegrity", StringComparison.Ordinal),
                "163-32D-1: Lyrical runtime keeps public CheckIntegrity and fails closed on integrity mismatches");
            Check(constructor.Contains("string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();", StringComparison.Ordinal)
                  && constructor.IndexOf("sourcedRosDistroBeforeStandalonePatch", StringComparison.Ordinal)
                     < constructor.IndexOf("SetStandaloneRosDistro(packagedRos2Version);", StringComparison.Ordinal)
                  && constructor.Contains("WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);", StringComparison.Ordinal)
                  && constructor.Contains("CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);", StringComparison.Ordinal)
                  && !runtime.Contains("ROS2 version in standalone process environment does not match this runtime package", StringComparison.Ordinal),
                "163-32D-2: Lyrical standalone startup warns then ignores external ROS_DISTRO before integrity checks");
        }

        private static void LyricalRuntimeDoesNotRestartAfterSharedShutdown()
        {
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var component = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var completeShutdown = ExtractCSharpMethod(runtime, "CompleteShutdownShared");
            var lazyConstruct = ExtractCSharpMethod(component, "LazyConstruct");
            var stopAll = ExtractCSharpMethod(component, "StopAllExecutorsForRosShutdown");
            var startExecutor = ExtractCSharpMethod(component, "StartExecutor");
            var markRuntimeShutdown = ExtractCSharpMethod(component, "MarkRuntimeShutdown");

            Check(completeShutdown.Contains("ROS2UnityComponent.StopAllExecutorsForRosShutdown();", StringComparison.Ordinal)
                  && completeShutdown.IndexOf("ROS2UnityComponent.StopAllExecutorsForRosShutdown();", StringComparison.Ordinal)
                     < completeShutdown.IndexOf("Ros2cs.Shutdown();", StringComparison.Ordinal),
                "163-32E-1: Lyrical runtime stops component executors before shared ROS2 shutdown");
            Check(component.Contains("private bool runtimeShutdownRequested", StringComparison.Ordinal)
                  && stopAll.Contains("component.MarkRuntimeShutdown();", StringComparison.Ordinal)
                  && lazyConstruct.Contains("throw new ObjectDisposedException(nameof(ROS2UnityComponent))", StringComparison.Ordinal)
                  && startExecutor.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && startExecutor.Contains("ros2forUnity == null", StringComparison.Ordinal)
                  && markRuntimeShutdown.Contains("ros2forUnity = null;", StringComparison.Ordinal),
                "163-32E-2: Lyrical component does not lazy-construct or start an executor after shared shutdown begins");
        }

        private static void LyricalBuilderAndValidatorRegenerateRuntimePatches()
        {
            var builder = ReadRepoText("Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py");
            var validator = ReadRepoText("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py");
            var patch = ExtractPythonFunction(builder, "patch_standalone_environment_isolation");
            var packagePath = ExtractPythonFunction(validator, "check_package_path_patch");
            var sourcePatch = ExtractPythonFunction(validator, "check_runtime_source_patches");

            Check(patch.Contains("old_check_signature", StringComparison.Ordinal)
                  && patch.Contains("private void CheckIntegrity(string ros2SourcedCodename)", StringComparison.Ordinal)
                  && patch.Contains("sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced()", StringComparison.Ordinal)
                  && patch.Contains("WarnIfStandaloneRosDistroOverride", StringComparison.Ordinal)
                  && patch.Contains("CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)", StringComparison.Ordinal)
                  && !patch.Contains("ROS2 version in standalone process environment does not match this runtime package", StringComparison.Ordinal),
                "163-32F-1: Lyrical builder regenerates standalone ROS_DISTRO override isolation");
            Check(packagePath.Contains("re.search(", StringComparison.Ordinal)
                  && packagePath.Contains("UnityEditor\\.PackageManager\\.PackageInfo", StringComparison.Ordinal)
                  && !packagePath.Contains("text.index(\"#if UNITY_EDITOR\")", StringComparison.Ordinal),
                "163-32F-2: Lyrical validator checks PackageManager lookup guard around the lookup itself");
            Check(sourcePatch.Contains("sourcedRosDistroBeforeStandalonePatch", StringComparison.Ordinal)
                  && sourcePatch.Contains("WarnIfStandaloneRosDistroOverride", StringComparison.Ordinal)
                  && !sourcePatch.Contains("CheckIntegrity(sourcedRosDistroBeforeStandalonePatch)", StringComparison.Ordinal)
                  && sourcePatch.Contains("runtimeShutdownRequested", StringComparison.Ordinal)
                  && sourcePatch.Contains("MarkRuntimeShutdown()", StringComparison.Ordinal),
                "163-32F-3: Lyrical validator requires standalone env isolation and no-reinit shutdown patches");
        }

        private static void LyricalValidationUsesRepoRootDiscovery()
        {
            var validation = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuLyricalRuntimePackageValidation.cs");

            Check(validation.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && !validation.Contains("AppContext.BaseDirectory, \"..\", \"..\", \"..\", \"..\"", StringComparison.Ordinal),
                "163-32G: Lyrical validation resolves paths from the repository root helper");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_32Validation.cs", StringComparison.Ordinal),
                "163-32H-1: runtime test project compiles Phase163_32Validation");
            Check(registry.Contains("--phase163-32", StringComparison.Ordinal)
                  && registry.Contains("Phase163_32Validation.Validate", StringComparison.Ordinal),
                "163-32H-2: validation registry exposes --phase163-32");
        }

        private static string ExtractPythonFunction(string source, string functionName)
        {
            var signature = source.IndexOf("def " + functionName + "(", StringComparison.Ordinal);
            if (signature < 0)
                return string.Empty;

            var next = source.IndexOf("\ndef ", signature + 1, StringComparison.Ordinal);
            return next < 0 ? source.Substring(signature) : source.Substring(signature, next - signature);
        }

        private static string ExtractCSharpMethod(string source, string methodName)
        {
            var signature = -1;
            foreach (var prefix in new[]
                     {
                         "public void ",
                         "private void ",
                         "internal void ",
                         "internal ",
                         "private static string ",
                         "private static void ",
                         "public static string ",
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
