// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 146A validation for the project-level R2FU active runtime selector.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class R2fuActiveRuntimeSelectorValidation
    {
        private const string SelectionPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs";
        private const string InstallerPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs";
        private const string InspectorPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs";
        private const string ReadmePath =
            "Packages/dev.unity2foxglove.ros2forunity/README.md";
        private const string ManagerInspectorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";
        private const string ProjectPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 146A: R2FU Active Runtime Selector ===");
            _passed = 0;

            RuntimeSelectionKnowsSupportedPackages();
            RuntimeSelectionUsesProjectSettings();
            DefineInstallerUsesOnlyTheActiveRuntime();
            ManagerInspectorHostsOptionalSelector();
            RuntimeSelectorUsesOneDropdown();
            ReadmeDocumentsActiveRuntimeSelection();
            ValidationRegistryWiresPhase146A();

            Console.WriteLine($"Phase 146A: {_passed} checks passed.");
        }

        private static void RuntimeSelectionKnowsSupportedPackages()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal),
                "146A-A1: runtime selector knows the Jazzy Win64 runtime package");
            Check(source.Contains("dev.unity2foxglove.ros2forunity.runtime.lyrical.win64", StringComparison.Ordinal),
                "146A-A2: runtime selector reserves the Lyrical Win64 runtime package id");
            Check(source.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE", StringComparison.Ordinal)
                  && source.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY_LYRICAL_WIN64_PACKAGE", StringComparison.Ordinal),
                "146A-A3: runtime selector maps each runtime to a runtime-specific compile symbol");
            Check(source.Contains("KnownRuntimeDescriptors", StringComparison.Ordinal)
                  && source.Contains("RuntimeCompileSymbols", StringComparison.Ordinal),
                "146A-A4: runtime selector exposes known runtimes and runtime symbol set to editor tooling");
        }

        private static void RuntimeSelectionUsesProjectSettings()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json", StringComparison.Ordinal),
                "146A-B1: active runtime selection persists in ProjectSettings");
            Check(source.Contains("activeRuntimePackage", StringComparison.Ordinal)
                  && source.Contains("SaveActiveRuntimePackage", StringComparison.Ordinal),
                "146A-B2: settings file stores the explicit active runtime package");
            Check(source.Contains("installed.Length > 0", StringComparison.Ordinal)
                  && source.Contains("installed[0]", StringComparison.Ordinal),
                "146A-B3: missing settings fall back to the first installed runtime");
        }

        private static void DefineInstallerUsesOnlyTheActiveRuntime()
        {
            var source = ReadRepoText(InstallerPath);

            Check(source.Contains("Ros2ForUnityRuntimeSelection.GetStatus()", StringComparison.Ordinal),
                "146A-C1: define installer reads the project runtime selection status");
            Check(source.Contains("Ros2ForUnityRuntimeSelection.RuntimeCompileSymbols", StringComparison.Ordinal)
                  && source.Contains("RemoveSymbol(parts, symbol)", StringComparison.Ordinal),
                "146A-C2: define installer clears stale runtime-specific symbols");
            Check(source.Contains("Ros2ForUnityRuntimeSelection.BaseCompileSymbol", StringComparison.Ordinal)
                  && source.Contains("status.SelectedRuntime.CompileSymbol", StringComparison.Ordinal),
                "146A-C3: define installer enables the base symbol and only the selected runtime symbol");
            Check(source.Contains("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", StringComparison.Ordinal),
                "146A-C4: define installer removes the base symbol when no active runtime is available");
        }

        private static void ManagerInspectorHostsOptionalSelector()
        {
            var source = ReadRepoText(ManagerInspectorPath);

            Check(source.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && source.Contains("DrawOptionalRos2ForUnityRuntimeSelector", StringComparison.Ordinal),
                "146A-D1: Foxglove Manager Inspector hosts the runtime selector under ROS2 Native");
            Check(source.Contains("Unity2Foxglove.Ros2ForUnity.Editor.Ros2ForUnityRuntimeSelectorInspector", StringComparison.Ordinal)
                  && source.Contains("GetMethod", StringComparison.Ordinal),
                "146A-D2: core SDK discovers the optional selector by reflection");
            Check(source.Contains("TargetInvocationException", StringComparison.Ordinal)
                  && source.Contains("ROS2 For Unity runtime selector failed", StringComparison.Ordinal),
                "146A-D3: optional selector failures are contained inside the Inspector UI");
        }

        private static void RuntimeSelectorUsesOneDropdown()
        {
            var source = ReadRepoText(InspectorPath);

            Check(source.Contains("EditorGUILayout.Popup(\"Active Runtime\"", StringComparison.Ordinal),
                "146A-E1: runtime selection is a single Active Runtime dropdown");
            Check(source.Contains("EditorGUI.BeginChangeCheck()", StringComparison.Ordinal)
                  && source.Contains("EditorGUI.EndChangeCheck()", StringComparison.Ordinal),
                "146A-E2: dropdown writes settings only after a user-driven change");
            Check(source.Contains("installed.Length > 0", StringComparison.Ordinal)
                  && source.Contains("SaveAndReconcile(projectDirectory, status.SelectedRuntime)", StringComparison.Ordinal)
                  && !source.Contains("GUILayout.Button(\"Use", StringComparison.Ordinal),
                "146A-E3: default runtime selection persists without an extra confirmation button");
            Check(!source.Contains("Select active runtime...", StringComparison.Ordinal),
                "146A-E4: runtime selector does not add a placeholder confirmation step");
        }

        private static void ReadmeDocumentsActiveRuntimeSelection()
        {
            var source = ReadRepoText(ReadmePath);

            Check(source.Contains("Multiple runtime packages may be installed", StringComparison.Ordinal)
                  && source.Contains("exactly one active runtime", StringComparison.Ordinal),
                "146A-F1: README documents installed runtimes versus active runtime");
            Check(source.Contains("ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json", StringComparison.Ordinal)
                  && source.Contains("ROS2 For Unity Runtime", StringComparison.Ordinal)
                  && source.Contains("changing the dropdown selects a different active runtime", StringComparison.Ordinal),
                "146A-F2: README documents the active runtime setting and Inspector selector");
        }

        private static void ValidationRegistryWiresPhase146A()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase146a\", \"Phase 146A\", R2fuActiveRuntimeSelectorValidation.Validate", StringComparison.Ordinal),
                "146A-G1: validation registry wires --phase146a to the runtime selector validation");
            Check(project.Contains("R2fuActiveRuntimeSelectorValidation.cs", StringComparison.Ordinal),
                "146A-G2: runtime validation project compiles the runtime selector validation");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"146A-file: {relativePath} exists");
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
