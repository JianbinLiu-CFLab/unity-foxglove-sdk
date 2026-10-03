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
            Assert.Contains("MarkTimedOut", coordinator, StringComparison.Ordinal);
            Assert.Contains("PendingStatusKey", coordinator, StringComparison.Ordinal);
            Assert.Contains("Retry", coordinator, StringComparison.Ordinal);
            Assert.Contains("Cancel", coordinator, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityInteractiveSelectionCoordinatorCore", coordinator, StringComparison.Ordinal);
            Assert.Contains("internal static void Complete()", coordinator, StringComparison.Ordinal);
            Assert.DoesNotContain("rollbackManifest", coordinator, StringComparison.Ordinal);
            var cancelMethod = TestSources.ExtractMethod(
                coordinator,
                "internal static void Cancel()");
            var persistedCancellation = cancelMethod.IndexOf("PersistPending()", StringComparison.Ordinal);
            var refreshAfterCancellation = cancelMethod.IndexOf("Backend.Resolve()", StringComparison.Ordinal);
            Assert.True(
                persistedCancellation >= 0 && persistedCancellation < refreshAfterCancellation,
                "Cancel must persist its terminal status before Package Manager refresh can reload the domain.");
            Assert.Contains("CompleteInteractiveSelection", selection, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();",
                TestSources.ExtractMethod(
                    selection,
                    "public static void SwitchActiveRuntimePackage"),
                StringComparison.Ordinal);
        }

        [Fact]
        public void SelectionLifecycleRetainsOwnershipAfterTimeoutAndSupportsRetryAndCancel()
        {
            var start = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            var state = Ros2ForUnityInteractiveSelectionState.Begin(
                1,
                "project",
                "runtime",
                "addon",
                "manifest",
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                start,
                TimeSpan.FromSeconds(5));
            var lifecycle = new Ros2ForUnitySelectionLifecycle();
            lifecycle.Begin(state);

            Assert.False(
                lifecycle.Tick(
                    start.AddSeconds(1),
                    _ => false,
                    _ => true,
                    _ => throw new InvalidOperationException("commit must not run before registration")));
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Pending, lifecycle.Status);

            Assert.True(lifecycle.MarkTimedOut(start.AddSeconds(5)));
            var expiredCommitCount = 0;
            Assert.False(
                lifecycle.Tick(
                    start.AddSeconds(5),
                    _ => true,
                    _ => true,
                    _ => expiredCommitCount++));
            Assert.Equal(0, expiredCommitCount);
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.TimedOut, lifecycle.Status);
            Assert.True(lifecycle.HasOwner);
            var resolveCount = 0;
            Assert.True(
                lifecycle.Retry(
                    start.AddSeconds(6),
                    TimeSpan.FromSeconds(5),
                    () => resolveCount++));
            Assert.Equal(1, resolveCount);
            Assert.Equal(2, lifecycle.State.Generation);
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Pending, lifecycle.Status);

            var commitCount = 0;
            Assert.True(
                lifecycle.Tick(
                    start.AddSeconds(7),
                    _ => true,
                    _ => true,
                    _ => commitCount++));
            Assert.Equal(1, commitCount);
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Completed, lifecycle.Status);
            Assert.False(lifecycle.HasOwner);

            var cancelState = Ros2ForUnityInteractiveSelectionState.Begin(
                4,
                "project",
                "runtime",
                string.Empty,
                "manifest",
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                start,
                TimeSpan.FromMinutes(5));
            var cancelLifecycle = new Ros2ForUnitySelectionLifecycle();
            cancelLifecycle.Begin(cancelState);
            var rollbackCount = 0;
            Assert.True(cancelLifecycle.Cancel(() => rollbackCount++));
            Assert.Equal(1, rollbackCount);
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Cancelled, cancelLifecycle.Status);
            Assert.False(cancelLifecycle.Cancel(() => rollbackCount++));
            Assert.Equal(1, rollbackCount);

            var restored = new Ros2ForUnitySelectionLifecycle();
            restored.Restore(
                Ros2ForUnityInteractiveSelectionState.Begin(
                    8,
                    "project",
                    "runtime",
                    string.Empty,
                    "manifest",
                    Ros2ForUnityInteractiveSelectionKind.Runtime,
                    start,
                    TimeSpan.FromSeconds(1)),
                start.AddSeconds(2));
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.TimedOut, restored.Status);
            Assert.True(restored.HasOwner);

            var invalidState = Ros2ForUnityInteractiveSelectionState.Begin(
                12,
                "project",
                "runtime",
                string.Empty,
                "manifest",
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                start,
                TimeSpan.FromMinutes(5));
            var invalidLifecycle = new Ros2ForUnitySelectionLifecycle();
            invalidLifecycle.Begin(invalidState);
            Assert.False(
                invalidLifecycle.Tick(
                    start.AddSeconds(1),
                    _ => true,
                    _ => false,
                    _ => throw new InvalidOperationException("invalid identity must not commit")));
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Failed, invalidLifecycle.Status);
            Assert.True(invalidLifecycle.HasOwner);
        }

        [Fact]
        public void InteractiveSelectionCoordinatorCoreExercisesRegistrationRetryCommitAndRollback()
        {
            var start = new DateTime(2026, 10, 3, 13, 0, 0, DateTimeKind.Utc);
            var state = Ros2ForUnityInteractiveSelectionState.Begin(
                21,
                "project",
                "runtime",
                "addon",
                "manifest",
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                start,
                TimeSpan.FromSeconds(5));
            var backend = new SelectionBackend();
            var coordinator = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
            coordinator.Begin(state);

            Assert.False(coordinator.Tick(start.AddSeconds(1), backend, _ => { }));
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Pending, coordinator.Status);
            Assert.True(coordinator.MarkTimedOut(start.AddSeconds(5)));
            Assert.False(coordinator.MarkTimedOut(start.AddSeconds(6)));
            Assert.True(coordinator.HasOwner);

            var stale = coordinator.State;
            Assert.True(
                coordinator.Retry(
                    start.AddSeconds(7),
                    TimeSpan.FromSeconds(5),
                    backend,
                    _ => { }));
            Assert.False(stale.IsPending);
            Assert.Equal(22, coordinator.State.Generation);
            backend.Registered = true;
            Assert.True(coordinator.Tick(start.AddSeconds(8), backend, _ => { }));
            Assert.Equal(1, backend.CommitCount);
            Assert.False(coordinator.HasOwner);

            var failed = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
            failed.Begin(
                Ros2ForUnityInteractiveSelectionState.Begin(
                    30,
                    "project",
                    "runtime",
                    string.Empty,
                    "manifest",
                    Ros2ForUnityInteractiveSelectionKind.Runtime,
                    start,
                    TimeSpan.FromMinutes(5)));
            backend = new SelectionBackend { Registered = true, ThrowValidation = true };
            var failureCount = 0;
            Assert.False(failed.Tick(start.AddSeconds(1), backend, _ => failureCount++));
            Assert.Equal(Ros2ForUnitySelectionLifecycleStatus.Failed, failed.Status);
            Assert.True(failed.HasOwner);
            Assert.Equal(1, failureCount);
            backend.ThrowValidation = false;
            Assert.True(
                failed.Retry(
                    start.AddSeconds(2),
                    TimeSpan.FromMinutes(5),
                    backend,
                    _ => { }));
            Assert.True(failed.Cancel(backend, _ => { }));
            Assert.Equal(1, backend.RollbackCount);
            Assert.Equal(1, backend.ResolveCount);
            Assert.False(failed.HasOwner);
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
            var coordinator = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityInteractiveSelectionCoordinator.cs");

            Assert.Contains("ReportSelectorFailure", manager, StringComparison.Ordinal);
            Assert.Contains("ReportSelectorFailure", custom, StringComparison.Ordinal);
            Assert.Contains("DrawPendingResolveControls", custom, StringComparison.Ordinal);
            Assert.Contains("DrawPendingResolveControls", coordinator, StringComparison.Ordinal);
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
            var batch = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Phase181Ros2RuntimeBatchSelection.cs");

            Assert.Contains("GeneratedLinkMarker", build, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GeneratedLinkMarker + Environment.NewLine + linkXml",
                build,
                StringComparison.Ordinal);
            Assert.Contains("[InitializeOnLoad]", build, StringComparison.Ordinal);
            Assert.Contains("MonitorBuildLifecycle", build, StringComparison.Ordinal);
            Assert.Contains("IsOwnedGeneratedLinkXml", build, StringComparison.Ordinal);
            Assert.Contains("BuildPipeline.isBuildingPlayer", build, StringComparison.Ordinal);
            var preprocess = TestSources.ExtractMethod(build, "public void OnPreprocessBuild");
            var activeIndex = preprocess.IndexOf("_buildWasActive = true;", StringComparison.Ordinal);
            var cleanupIndex = preprocess.IndexOf("RemoveStaleGeneratedLinkXmlAtStartup();", StringComparison.Ordinal);
            Assert.True(activeIndex >= 0 && activeIndex < cleanupIndex);
            Assert.Contains("IsAutomaticInstallSupported", installer, StringComparison.Ordinal);
            Assert.Contains("DisabledScope(!OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported)", camera, StringComparison.Ordinal);
            Assert.Contains("ResolvePending", batch, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityInteractiveSelectionCoordinatorCore", batch, StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnitySelectionLifecycleDefaults.ResolveTimeout", batch, StringComparison.Ordinal);
            Assert.Contains("RestoreOriginalManifestBeforeFailure", batch, StringComparison.Ordinal);
            Assert.Contains("RestoreManifest(", batch, StringComparison.Ordinal);
            Assert.Contains("selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending", batch, StringComparison.Ordinal);
        }

        private sealed class SelectionBackend : IRos2ForUnitySelectionBackend
        {
            public bool Registered;
            public bool ThrowValidation;
            public int CommitCount;
            public int ResolveCount;
            public int RollbackCount;

            public bool IsRegistered(Ros2ForUnityInteractiveSelectionState state)
                => Registered;

            public bool IsValid(Ros2ForUnityInteractiveSelectionState state)
            {
                if (ThrowValidation)
                    throw new InvalidOperationException("validation failed");
                return true;
            }

            public void Commit(Ros2ForUnityInteractiveSelectionState state)
                => CommitCount++;

            public void Resolve()
                => ResolveCount++;

            public void Rollback(Ros2ForUnityInteractiveSelectionState state)
                => RollbackCount++;
        }
    }
}
