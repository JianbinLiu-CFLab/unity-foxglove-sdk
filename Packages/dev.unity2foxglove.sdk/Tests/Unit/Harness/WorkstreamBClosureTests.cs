// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep Workstream B Editor transaction, cache, cleanup, and platform contracts executable.

using System;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "Workstream-B")]
    [Trait("Domain", "Editor closure")]
    public sealed class WorkstreamBClosureTests
    {
        [Fact]
        public void InteractivePackageSelectionWaitsForRegistrationAndFencesCompletion()
        {
            var transaction = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs");
            var coordinator = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityInteractiveSelectionCoordinator.cs");
            var selection = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Assert.Contains("return false;", transaction, StringComparison.Ordinal);
            Assert.Contains("ResolvePending", transaction, StringComparison.Ordinal);
            Assert.Contains("Events.registeredPackages", coordinator, StringComparison.Ordinal);
            Assert.Contains("SessionState.SetString", coordinator, StringComparison.Ordinal);
            Assert.Contains("EvaluateActive", coordinator, StringComparison.Ordinal);
            Assert.Contains("Generation", coordinator, StringComparison.Ordinal);
            Assert.Contains("TryRestore", coordinator, StringComparison.Ordinal);
            Assert.Contains("IsExpired", coordinator, StringComparison.Ordinal);
            Assert.Contains("TryFail", coordinator, StringComparison.Ordinal);
            Assert.Contains("RestoreManifest", coordinator, StringComparison.Ordinal);
            Assert.Contains("CompleteInteractiveSelection", selection, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();",
                TestSources.ExtractMethod(
                    selection,
                    "public static void SwitchActiveRuntimePackage"),
                StringComparison.Ordinal);
        }

        [Fact]
        public void InteractiveSelectionStatePersistsAcrossReloadAndRejectsStaleCompletion()
        {
            var start = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            var state = Ros2ForUnityInteractiveSelectionState.Begin(
                7,
                "project",
                "runtime",
                "addon",
                "manifest",
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                start,
                TimeSpan.FromMinutes(5));
            var snapshot = state.Capture();

            Assert.True(
                Ros2ForUnityInteractiveSelectionState.TryRestore(
                    snapshot,
                    start.AddSeconds(1),
                    out var restored));
            Assert.Equal(7, restored.Generation);
            Assert.True(restored.IsPending);
            Assert.False(restored.TryComplete(6));
            Assert.True(restored.TryComplete(7));
            Assert.False(restored.IsPending);
            Assert.False(restored.TryComplete(7));

            var expiredSnapshot = state.Capture();
            expiredSnapshot.DeadlineUtc = start.AddSeconds(1);
            Assert.True(
                Ros2ForUnityInteractiveSelectionState.TryRestore(
                    expiredSnapshot,
                    start.AddSeconds(2),
                    out var expired));
            Assert.True(expired.IsExpired(start.AddSeconds(2)));
            Assert.True(expired.TryFail(7));
        }

        [Fact]
        public void SelectorDiagnosticsPreserveExceptionInSinkWithoutLeakingItToUiMessage()
        {
            var exception = new InvalidOperationException("manifest-secret");
            Exception observed = null;
            var previous = Ros2ForUnityEditorDiagnostics.ExceptionSink;
            try
            {
                Ros2ForUnityEditorDiagnostics.ExceptionSink = value => observed = value;
                Ros2ForUnityEditorDiagnostics.ReportSelectorFailure(
                    exception,
                    _ => throw new InvalidOperationException("fallback should not run"));
            }
            finally
            {
                Ros2ForUnityEditorDiagnostics.ExceptionSink = previous;
            }

            Assert.Same(exception, observed);
            Assert.DoesNotContain(
                exception.Message,
                Ros2ForUnityEditorDiagnostics.SelectorFailureMessage,
                StringComparison.Ordinal);
        }

        [Fact]
        public void FrozenSessionContractIsVisibleAtTheInspectorBoundary()
        {
            var publish = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var subscribe = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");

            Assert.Contains("active session retains its captured Provider", publish, StringComparison.Ordinal);
            Assert.Contains("active session retains its captured Provider", subscribe, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerAndTypesupportInspectorsRetainOriginalSelectorDiagnostics()
        {
            var manager = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityManagerSetupDrawer.cs");
            var custom = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/FoxRunRos2CustomTypesupportInspector.cs");

            Assert.Contains("ReportSelectorFailure", manager, StringComparison.Ordinal);
            Assert.Contains("ReportSelectorFailure", custom, StringComparison.Ordinal);
            Assert.Contains("ReportSelectorFailure", custom, StringComparison.Ordinal);
            Assert.Contains("PendingMessage", custom, StringComparison.Ordinal);
            Assert.Contains("HasPending", custom, StringComparison.Ordinal);
            Assert.DoesNotContain("+ exception.Message", custom, StringComparison.Ordinal);
        }

        [Fact]
        public void ReflectionDiscoveryAndCanonicalArtifactsRemainFailClosed()
        {
            var scanner = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var generator = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");

            Assert.Contains("ModuleVersionId", scanner, StringComparison.Ordinal);
            Assert.Contains("if (ignoreReflectionTypeLoadExceptions && complete)", scanner, StringComparison.Ordinal);
            Assert.Contains("_cachedReflectionGenerationModel", scanner, StringComparison.Ordinal);
            Assert.Contains("EnsureCanonicalArtifactGenerationAllowed(scan.IsComplete)", generator, StringComparison.Ordinal);
            Assert.Contains("FOXRUN901", generator, StringComparison.Ordinal);
            Assert.Contains("GenerateCanonicalArtifactsIfComplete", generator, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildOnlyLinkAndOpenH264PlatformBoundariesAreExplicit()
        {
            var build = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var installer = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            var camera = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Assert.Contains("GeneratedLinkMarker", build, StringComparison.Ordinal);
            Assert.Contains("GeneratedLinkMarker + Environment.NewLine", build, StringComparison.Ordinal);
            Assert.Contains("MonitorBuildLifecycle", build, StringComparison.Ordinal);
            Assert.Contains("IsOwnedGeneratedLinkXml", build, StringComparison.Ordinal);
            Assert.Contains("BuildPipeline.isBuildingPlayer", build, StringComparison.Ordinal);
            Assert.Contains("IsAutomaticInstallSupported", installer, StringComparison.Ordinal);
            Assert.Contains("DisabledScope(!OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported)", camera, StringComparison.Ordinal);
        }
    }
}
