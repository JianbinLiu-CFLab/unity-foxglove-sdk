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
        private const string PlayModeGuardPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs";
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

            RuntimeSelectionDiscoversCandidatePackages();
            RuntimeSelectionUsesManifestAsTruth();
            DefineInstallerUsesOnlyBaseRuntimeSymbol();
            ManagerInspectorHostsOptionalSelector();
            RuntimeSelectorUsesOneDropdown();
            RuntimeSwitchRequiresEditorRestart();
            ReadmeDocumentsActiveRuntimeSelection();
            ValidationRegistryWiresPhase146A();

            Console.WriteLine($"Phase 146A: {_passed} checks passed.");
        }

        private static void RuntimeSelectionDiscoversCandidatePackages()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("RuntimePackagePrefix", StringComparison.Ordinal)
                  && source.Contains("DiscoverCandidateRuntimes", StringComparison.Ordinal),
                "146A-A1: runtime selector discovers runtime packages by package-id convention");
            Check(source.Contains("RepositoryPackagesDirectory", StringComparison.Ordinal)
                  && source.Contains("BuildRuntimePackageReference", StringComparison.Ordinal)
                  && source.Contains("GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)", StringComparison.Ordinal),
                "146A-A2: runtime selector derives manifest file references from the repository Packages directory");
            Check(!source.Contains("KnownRuntimes", StringComparison.Ordinal)
                  && !source.Contains("KnownRuntimeDescriptors", StringComparison.Ordinal),
                "146A-A3: runtime selector no longer hardcodes known runtime descriptors");
            Check(!source.Contains("JazzyWin64CompileSymbol", StringComparison.Ordinal)
                  && !source.Contains("LyricalWin64CompileSymbol", StringComparison.Ordinal),
                "146A-A4: runtime selector no longer carries per-distro compile gates");
        }

        private static void RuntimeSelectionUsesManifestAsTruth()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("ReadManifestRuntimePackages", StringComparison.Ordinal)
                  && source.Contains("ActiveRuntimePackage", StringComparison.Ordinal),
                "146A-B1: active runtime selection is derived from the Unity package manifest");
            Check(source.Contains("SwitchActiveRuntimePackage", StringComparison.Ordinal)
                  && source.Contains("Client.Resolve()", StringComparison.Ordinal),
                "146A-B2: runtime changes atomically rewrite manifest then ask Unity to resolve packages");
            Check(!source.Contains("Unity2FoxgloveRos2ForUnitySettings.json", StringComparison.Ordinal)
                  && !source.Contains("SaveActiveRuntimePackage", StringComparison.Ordinal),
                "146A-B3: selector no longer treats ProjectSettings JSON as source of truth");
            Check(source.Contains("RemoveRuntimePackageDependencies", StringComparison.Ordinal)
                  && source.Contains("AddRuntimePackageDependency", StringComparison.Ordinal),
                "146A-B4: manifest switching reaches the final single-runtime dependency state in one write");
            Check(source.Contains("SessionRuntimeKey", StringComparison.Ordinal)
                  && source.Contains("SessionState", StringComparison.Ordinal)
                  && !source.Contains("EditorPrefs", StringComparison.Ordinal),
                "146A-B5: runtime guard records per-Editor-session runtime state without persistent drift");
        }

        private static void DefineInstallerUsesOnlyBaseRuntimeSymbol()
        {
            var source = ReadRepoText(InstallerPath);

            Check(source.Contains("Ros2ForUnityRuntimeSelection.GetStatus()", StringComparison.Ordinal),
                "146A-C1: define installer reads the manifest-derived runtime selection status");
            Check(source.Contains("Ros2ForUnityRuntimeSelection.BaseCompileSymbol", StringComparison.Ordinal)
                  && source.Contains("EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", StringComparison.Ordinal),
                "146A-C2: define installer enables only the base optional R2FU symbol");
            Check(source.Contains("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", StringComparison.Ordinal),
                "146A-C3: define installer removes the base symbol when no active runtime is available");
            Check(!source.Contains("RuntimeCompileSymbols", StringComparison.Ordinal)
                  && !source.Contains("SelectedRuntime.CompileSymbol", StringComparison.Ordinal),
                "146A-C4: define installer does not synchronize per-runtime compile symbols");
        }

        private static void ManagerInspectorHostsOptionalSelector()
        {
            var source = ReadRepoText(ManagerInspectorPath);

            Check(source.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && source.Contains("DrawOptionalR2fuRuntimeSelector", StringComparison.Ordinal),
                "146A-D1: Foxglove Manager Inspector hosts the runtime selector under ROS2 Native");
            Check(source.Contains("\"Unity2Foxglove.\" + \"Ros2\" + \"For\" + \"Unity.Editor.\"", StringComparison.Ordinal)
                  && source.Contains("\"Ros2\" + \"For\" + \"UnityRuntimeSelectorInspector", StringComparison.Ordinal)
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
                "146A-E2: dropdown switches runtime only after a user-driven change");
            Check(source.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && source.Contains("SwitchAndResolve(projectDirectory, installed[runtimeIndex])", StringComparison.Ordinal)
                  && !source.Contains("GUILayout.Button(\"Use", StringComparison.Ordinal),
                "146A-E3: selector has no extra confirmation button and refuses unsafe Play Mode switching");
            Check(source.Contains("No active runtime", StringComparison.Ordinal),
                "146A-E4: runtime selector shows a neutral placeholder when no runtime is active");
        }

        private static void RuntimeSwitchRequiresEditorRestart()
        {
            var guard = ReadRepoText(PlayModeGuardPath);
            var inspector = ReadRepoText(InspectorPath);

            Check(guard.Contains("EditorApplication.playModeStateChanged", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal)
                  && guard.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal)
                  && guard.Contains("GetRuntimePackageRequiringEditorRestart", StringComparison.Ordinal),
                "146A-F1: Play Mode binds the first runtime used by this Editor session");
            Check(guard.Contains("EditorApplication.isPlaying = false", StringComparison.Ordinal)
                  && guard.Contains("Restart Unity before entering Play Mode", StringComparison.Ordinal),
                "146A-F2: Play Mode guard cancels unsafe mixed-runtime entry and explains the restart requirement");
            Check(guard.Contains("CompilationPipeline.compilationStarted", StringComparison.Ordinal)
                  && guard.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal)
                  && guard.Contains("CompilationStartedWhileR2fuPlayModeKey", StringComparison.Ordinal)
                  && guard.Contains("native ROS2/RMW DLLs cannot be safely unloaded during Play Mode", StringComparison.Ordinal),
                "146A-F3: Play Mode guard exits for script-compilation reloads without blocking normal Play Mode domain reload");
            Check(inspector.Contains("GetRuntimePackageRequiringEditorRestart", StringComparison.Ordinal)
                  && inspector.Contains("Restart Unity", StringComparison.Ordinal)
                  && inspector.Contains("RestartEditor(projectDirectory)", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("EditorApplication.OpenProject(projectDirectory)", StringComparison.Ordinal),
                "146A-F4: Inspector surfaces conditional restart state and offers one-click relaunch");
        }

        private static void ReadmeDocumentsActiveRuntimeSelection()
        {
            var source = ReadRepoText(ReadmePath);

            Check(source.Contains("candidate runtime packages", StringComparison.Ordinal)
                  && source.Contains("exactly one active runtime", StringComparison.Ordinal),
                "146A-F1: README documents candidate runtimes versus the active manifest runtime");
            Check(source.Contains("manifest.json", StringComparison.Ordinal)
                  && source.Contains("ROS2 For Unity Runtime", StringComparison.Ordinal)
                  && source.Contains("package reimport", StringComparison.Ordinal)
                  && source.Contains("After an Editor session has loaded one ROS2 runtime", StringComparison.Ordinal),
                "146A-G2: README documents manifest switching and conditional restart requirement");
        }

        private static void ValidationRegistryWiresPhase146A()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase146a\", \"Phase 146A\", R2fuActiveRuntimeSelectorValidation.Validate", StringComparison.Ordinal),
                "146A-H1: validation registry wires --phase146a to the runtime selector validation");
            Check(project.Contains("R2fuActiveRuntimeSelectorValidation.cs", StringComparison.Ordinal),
                "146A-H2: runtime validation project compiles the runtime selector validation");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"146A-file: {relativePath} exists");
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
