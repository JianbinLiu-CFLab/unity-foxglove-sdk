// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-29 validation for R2FU runtime package governance.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_29Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-29: R2FU Runtime Package Governance ===");
            _passed = 0;

            AdapterPackageDeclaresSdkDependency();
            NativeAsmdefReferencesOnlyExistingSdkAssemblies();
            RuntimeSelectionValidatesManifestJson();
            RuntimeSelectionReadsManifestDependenciesAsJson();
            RuntimeSelectionKeepsGenericCommunicationModeKey();
            RuntimeSelectionKeepsDynamicPackageReferences();
            RuntimeDocsAndInventoryAreConsistent();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-29: {_passed} checks passed.");
        }

        private static void AdapterPackageDeclaresSdkDependency()
        {
            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/package.json");
            Check(packageJson.Contains("\"dev.unity2foxglove.sdk\": \"1.9.5\"", StringComparison.Ordinal),
                "163-29A: R2FU adapter package declares its SDK package dependency");
        }

        private static void NativeAsmdefReferencesOnlyExistingSdkAssemblies()
        {
            var nativeAsmdef = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            var editorAsmdef = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef");
            var sdkAsmdef = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");

            Check(sdkAsmdef.Contains("\"name\": \"Unity.FoxgloveSDK\"", StringComparison.Ordinal)
                  && nativeAsmdef.Contains("\"Unity.FoxgloveSDK\"", StringComparison.Ordinal)
                  && !nativeAsmdef.Contains("Unity.FoxgloveSDK.Runtime", StringComparison.Ordinal),
                "163-29B-1: native R2FU asmdef does not reference a non-existent Unity.FoxgloveSDK.Runtime assembly");
            Check(editorAsmdef.Contains("\"Newtonsoft.Json\"", StringComparison.Ordinal),
                "163-29B-2: R2FU editor assembly explicitly references Newtonsoft.Json for manifest parsing");
        }

        private static void RuntimeSelectionValidatesManifestJson()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var switchMethod = ExtractMethod(selection, "SwitchActiveRuntimePackage");

            Check(selection.Contains("JObject.Parse", StringComparison.Ordinal)
                  && selection.Contains("ReadManifestJson(", StringComparison.Ordinal)
                  && selection.Contains("ValidateManifestJson(", StringComparison.Ordinal),
                "163-29C-1: runtime selector has JSON parsing and validation helpers for manifest governance");
            Check(Count(switchMethod, "ValidateManifestJson(manifest, manifestPath);") >= 2
                  && switchMethod.IndexOf("ValidateManifestJson(manifest, manifestPath);", StringComparison.Ordinal)
                     < switchMethod.IndexOf("RemoveRuntimePackageDependencies(manifest)", StringComparison.Ordinal)
                  && switchMethod.LastIndexOf("ValidateManifestJson(manifest, manifestPath);", StringComparison.Ordinal)
                     < switchMethod.IndexOf("WriteManifestAtomically(manifestPath, manifest)", StringComparison.Ordinal),
                "163-29C-2: runtime package switching validates manifest JSON before and after modification");
        }

        private static void RuntimeSelectionReadsManifestDependenciesAsJson()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var readMethod = ExtractMethod(selection, "ReadManifestRuntimePackages");

            Check(readMethod.Contains("ReadManifestDependencies", StringComparison.Ordinal)
                  && readMethod.Contains(".Properties()", StringComparison.Ordinal)
                  && readMethod.Contains("StartsWith(RuntimePackagePrefix", StringComparison.Ordinal)
                  && !readMethod.Contains("Regex.Matches", StringComparison.Ordinal),
                "163-29D: active runtime package discovery reads manifest dependencies as JSON properties instead of regex scanning");
        }

        private static void RuntimeSelectionKeepsGenericCommunicationModeKey()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(selection.Contains("\"Unity2Foxglove.R2FU.CommunicationMode\"", StringComparison.Ordinal)
                  && !selection.Contains("LyricalCommunicationMode", StringComparison.Ordinal),
                "163-29E: saved RMW communication mode key is runtime-neutral rather than Lyrical-specific");
        }

        private static void RuntimeSelectionKeepsDynamicPackageReferences()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(selection.Contains("BuildRuntimePackageReference(projectDirectory, packageName)", StringComparison.Ordinal)
                  && selection.Contains("GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)", StringComparison.Ordinal)
                  && !selection.Contains("file:../../Packages/", StringComparison.Ordinal),
                "163-29F-1: runtime package manifest entries use project-relative paths instead of a hard-coded repository depth");
            Check(selection.Contains("DetectLineEnding(manifest)", StringComparison.Ordinal)
                  && !selection.Contains("string.Join(Environment.NewLine, lines)", StringComparison.Ordinal),
                "163-29F-2: runtime package manifest edits preserve existing line endings");
        }

        private static void RuntimeDocsAndInventoryAreConsistent()
        {
            var humbleInventory = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/RuntimeSupport/r2fu-humble-win64-runtime-inventory.json");
            var jazzyReadme = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/README.md");
            var lyricalReadme = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/README.md");

            Check(humbleInventory.Contains("fmt.dll is not part of the current Humble critical runtime closure", StringComparison.Ordinal)
                  && !humbleInventory.Contains("spdlog.dll, and fmt.dll or Unity may fail", StringComparison.Ordinal),
                "163-29G-1: Humble inventory caveat matches the current critical DLL closure");
            Check(jazzyReadme.Contains("Runtime Identity", StringComparison.Ordinal)
                  && jazzyReadme.Contains("One Runtime Policy", StringComparison.Ordinal)
                  && lyricalReadme.Contains("Supported RMW implementations", StringComparison.Ordinal)
                  && lyricalReadme.Contains("Zenoh mode is Lyrical-only", StringComparison.Ordinal),
                "163-29G-2: Jazzy and Lyrical runtime packages include user-facing package governance README content");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_29Validation.cs", StringComparison.Ordinal),
                "163-29H-1: runtime test project compiles Phase163_29Validation");
            Check(registry.Contains("--phase163-29", StringComparison.Ordinal)
                  && registry.Contains("Phase163_29Validation.Validate", StringComparison.Ordinal),
                "163-29H-2: validation registry exposes --phase163-29");
        }

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = -1;
            foreach (var prefix in new[] { "public static void ", "public static IReadOnlyList<string> ", "private static string " })
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

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = source.IndexOf(value, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
            }

            return count;
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
