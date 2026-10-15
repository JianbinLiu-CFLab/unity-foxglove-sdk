// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-27 validation for R2FU runtime selection and play-mode guards.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_27Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-27: R2FU Runtime Selection and Guards ===");
            _passed = 0;

            RuntimeSelectionKeepsManifestWritesStable();
            RuntimeSelectionDetectsZenohAcrossPluginLayouts();
            RuntimeSelectorInspectorAvoidsFalseSelectionAndExtraManifestReads();
            PlayModeGuardClearsCompilationState();
            ProviderTeardownIsObservableAndSessionDisposeIsTerminal();
            ProviderSelectorDependencyMatchesAsmdef();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-27: {_passed} checks passed.");
        }

        private static void RuntimeSelectionKeepsManifestWritesStable()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(selection.Contains("WriteManifestAtomically", StringComparison.Ordinal)
                  && selection.Contains("ReadManifestJson(manifest", StringComparison.Ordinal)
                  && selection.Contains("new JProperty(packageName", StringComparison.Ordinal),
                "163-27A-1: runtime package switching keeps atomic manifest writes and edits dependencies as JSON");
            Check(selection.Contains("DetectLineEnding(manifest)", StringComparison.Ordinal)
                  && selection.Contains("SerializeManifest", StringComparison.Ordinal)
                  && !selection.Contains("string.Join(Environment.NewLine, lines)", StringComparison.Ordinal),
                "163-27A-2: runtime package switching preserves manifest line endings after JSON serialization");
            var resolveIndex = selection.IndexOf("resolve: () => Client.Resolve()", StringComparison.Ordinal);
            var invalidateIndex = selection.IndexOf("InvalidateStatusCache();", resolveIndex, StringComparison.Ordinal);
            var environmentIndex = selection.IndexOf(
                "ApplyCommunicationModeEnvironment(projectDirectory);",
                invalidateIndex,
                StringComparison.Ordinal);
            Check(resolveIndex >= 0
                  && invalidateIndex > resolveIndex
                  && environmentIndex > invalidateIndex,
                "163-27A-3: runtime package switching resolves the transaction, invalidates cached selection, then refreshes RMW_IMPLEMENTATION");
        }

        private static void RuntimeSelectionDetectsZenohAcrossPluginLayouts()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(selection.Contains("EnumerateDirectories(pluginsRoot, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal)
                  && selection.Contains("HasNativeLibrary(pluginRoot, \"rmw_zenoh_cpp\")", StringComparison.Ordinal)
                  && selection.Contains("libraryName + \".dll\"", StringComparison.Ordinal)
                  && selection.Contains("\"lib\" + libraryName + \".so\"", StringComparison.Ordinal)
                  && selection.Contains("\"lib\" + libraryName + \".dylib\"", StringComparison.Ordinal),
                "163-27B-1: Zenoh payload discovery scans plugin layouts and native library suffixes instead of hardcoding Windows x86_64");
        }

        private static void RuntimeSelectorInspectorAvoidsFalseSelectionAndExtraManifestReads()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var drawRestartStatus = ExtractMethod(inspector, "DrawRestartStatus");
            var switchAndResolve = ExtractMethod(inspector, "SwitchAndResolve");

            Check(inspector.Contains("No active runtime", StringComparison.Ordinal)
                  && inspector.Contains("runtimeIndex = popupLabels.Length == installed.Length ? changedIndex : changedIndex - 1", StringComparison.Ordinal),
                "163-27C-1: runtime popup shows a neutral placeholder instead of visually selecting the first candidate");
            Check(drawRestartStatus.Contains("GetRuntimePackageRequiringEditorRestart(status)", StringComparison.Ordinal)
                  && drawRestartStatus.Contains("GetCommunicationModeRequiringEditorRestart(status)", StringComparison.Ordinal)
                  && !drawRestartStatus.Contains("GetStatus(projectDirectory)", StringComparison.Ordinal),
                "163-27C-2: restart status reuses the Inspector status snapshot instead of rereading manifest.json");
            Check(switchAndResolve.IndexOf("_pendingResolveMessage =", StringComparison.Ordinal)
                     < switchAndResolve.IndexOf("SwitchActiveRuntimePackage", StringComparison.Ordinal)
                  && !switchAndResolve.Contains("EditorGUILayout.HelpBox", StringComparison.Ordinal),
                "163-27C-3: runtime resolve feedback is set before Client.Resolve can trigger package refresh");
        }

        private static void PlayModeGuardClearsCompilationState()
        {
            var guard = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");

            Check(guard.Contains("CompilationPipeline.compilationFinished += OnCompilationFinished", StringComparison.Ordinal)
                  && guard.Contains("SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, false);", StringComparison.Ordinal),
                "163-27D-1: play-mode guard clears stale compilation SessionState when compilation finishes without reload");
            Check(guard.Contains("!compilationStartedWhilePlaying && !EditorApplication.isPlaying", StringComparison.Ordinal)
                  && guard.Contains("? \"script compilation assembly reload\"", StringComparison.Ordinal)
                  && guard.Contains(": \"assembly reload\"", StringComparison.Ordinal),
                "163-27D-2: play-mode guard handles active Play Mode assembly reloads that bypass compilationStarted");
            Check(guard.Contains("ScheduleReloadAssembliesUnlock", StringComparison.Ordinal)
                  && guard.Contains("EditorApplication.UnlockReloadAssemblies()", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.ExitingPlayMode", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.EnteredEditMode", StringComparison.Ordinal),
                "163-27D-3: play-mode guard releases editor reload lock after R2FU Play Mode exits");
        }

        private static void ProviderTeardownIsObservableAndSessionDisposeIsTerminal()
        {
            var publisherHub = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherHub.cs");
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");

            Check(publisherHub.Contains("Custom native ROS2 publisher teardown failed: ", StringComparison.Ordinal)
                  && publisherHub.Contains("exception.GetType().Name", StringComparison.Ordinal)
                  && publisherHub.Contains("Diagnostics must not interrupt the remaining teardown.", StringComparison.Ordinal),
                "163-27E-1: the typed R2FU Provider hub reports recoverable teardown failures without interrupting remaining cleanup");
            Check(provider.Contains("private sealed class Session : IFoxRunTransportSession", StringComparison.Ordinal)
                  && provider.Contains("var owner = Interlocked.Exchange(ref _owner, null);", StringComparison.Ordinal)
                  && provider.Contains("owner?.Release(Generation);", StringComparison.Ordinal)
                  && provider.Contains("manager.UnregisterFoxRunTransportProvider(this);", StringComparison.Ordinal),
                "163-27E-2: R2FU Provider session Dispose is terminal and component detach remains safely unregisterable");
        }

        private static void ProviderSelectorDependencyMatchesAsmdef()
        {
            var setupDrawer = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityManagerSetupDrawer.cs");
            var providerDrawer = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs");
            var asmdef = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef");
            var manager = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");

            Check(setupDrawer.Contains(
                      "IFoxRunManagerSetupDrawer",
                      StringComparison.Ordinal)
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCount(
                          setupDrawer,
                          "FoxRunManagerSetupDrawerRegistry",
                          "Register") == 1
                  && PhaseValidationSourceHelpers
                      .InvocationCountInMethod(
                          setupDrawer,
                          "Draw",
                          "DrawActiveRuntimeSelector") == 1
                  && PhaseValidationSourceHelpers
                      .InvocationCount(
                          setupDrawer,
                          "DrawActiveRuntimeSelector") == 1
                  && PhaseValidationSourceHelpers
                      .InvocationCount(
                          providerDrawer,
                          "DrawActiveRuntimeSelector") == 0
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCountInMethod(
                          manager,
                          "DrawFoxRunTransportProviderExtensions",
                          "FoxRunManagerSetupDrawerRegistry",
                          "Capture") == 1
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCountInMethod(
                          manager,
                          "DrawFoxRunTransportProviderExtensions",
                          "setupDrawer",
                          "Draw") == 1
                  && !setupDrawer.Contains(
                      "Type.GetType",
                      StringComparison.Ordinal),
                "163-27F-1: one unconstrained R2FU setup drawer invokes the runtime selector exactly once without core reflection or native-drawer duplication");
            Check(asmdef.Contains(
                      "\"name\": \"Unity2Foxglove.Ros2ForUnity.Editor\"",
                      StringComparison.Ordinal)
                  && asmdef.Contains(
                      "\"Unity.FoxgloveSDK.Editor\"",
                      StringComparison.Ordinal)
                  && asmdef.Contains(
                      "\"defineConstraints\": []",
                      StringComparison.Ordinal),
                "163-27F-2: the unconstrained R2FU Editor assembly declares its generic Manager-editor dependency and remains available before first runtime selection");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_27Validation.cs", StringComparison.Ordinal),
                "163-27G-1: runtime test project compiles Phase163_27Validation");
            Check(registry.Contains("--phase163-27", StringComparison.Ordinal)
                  && registry.Contains("Phase163_27Validation.Validate", StringComparison.Ordinal),
                "163-27G-2: validation registry exposes --phase163-27");
        }

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = source.IndexOf("private static", StringComparison.Ordinal);
            while (signature >= 0)
            {
                var nameIndex = source.IndexOf(methodName, signature, StringComparison.Ordinal);
                if (nameIndex >= 0)
                {
                    var openParen = source.IndexOf('(', nameIndex);
                    var nextBrace = source.IndexOf('{', signature);
                    if (openParen >= 0 && nextBrace >= 0 && openParen < nextBrace)
                        break;
                }

                signature = source.IndexOf("private static", signature + 1, StringComparison.Ordinal);
            }

            if (signature < 0)
                signature = source.IndexOf("public static", StringComparison.Ordinal);
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
