// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "170D")]
    [Trait("Domain", "Manager")]
    public sealed class FoxgloveManagerStateDecompositionTests
    {
        [Fact]
        public void ServerResourcePartialsKeepResourceOwnershipSeparateFromLifecycle()
        {
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var remoteMcap = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.RemoteMcap.cs");
            var replayCursor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.ReplayCursor.cs");
            var secrets = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.Secrets.cs");

            Assert.Contains("public void StartServer()", server, StringComparison.Ordinal);
            Assert.Contains("private void StopServer(bool restoreLivePublishers)", server, StringComparison.Ordinal);
            Assert.Contains("private IFoxgloveTransport CreateTransport", server, StringComparison.Ordinal);
            Assert.Contains("private bool ValidateTransportConfiguration()", server, StringComparison.Ordinal);
            Assert.DoesNotContain("private void StartRemoteMcapFileServerIfNeeded()", server, StringComparison.Ordinal);
            Assert.DoesNotContain("private void StartReplayCursorEndpointIfNeeded()", server, StringComparison.Ordinal);
            Assert.DoesNotContain("private string ResolveSharedToken()", server, StringComparison.Ordinal);

            Assert.Contains("private RemoteMcapHttpServer _remoteMcapFileServer", remoteMcap, StringComparison.Ordinal);
            Assert.Contains("private void RefreshRemoteMcapFileServerIfNeeded()", remoteMcap, StringComparison.Ordinal);
            Assert.Contains("private RemoteMcapHttpOptions BuildRemoteMcapFileServerOptions", remoteMcap, StringComparison.Ordinal);
            Assert.Contains("private void StopRemoteMcapFileServer()", remoteMcap, StringComparison.Ordinal);
            Assert.DoesNotContain("StartServer", remoteMcap, StringComparison.Ordinal);
            Assert.DoesNotContain("StopServer", remoteMcap, StringComparison.Ordinal);

            Assert.Contains("private void StartReplayCursorEndpointIfNeeded()", replayCursor, StringComparison.Ordinal);
            Assert.Contains("private bool ShouldRunReplayCursorEndpoint()", replayCursor, StringComparison.Ordinal);
            Assert.Contains("private UnityReplayCursorEndpointQueueResult QueueExternalReplayCursor", replayCursor, StringComparison.Ordinal);
            Assert.Contains("private void StopReplayCursorEndpoint()", replayCursor, StringComparison.Ordinal);
            Assert.DoesNotContain("StartServer", replayCursor, StringComparison.Ordinal);
            Assert.DoesNotContain("StopServer", replayCursor, StringComparison.Ordinal);

            Assert.Contains("private string ResolveSharedToken()", secrets, StringComparison.Ordinal);
            Assert.Contains("private string ResolveCertificatePassword()", secrets, StringComparison.Ordinal);
            Assert.Contains("private string ResolveRemoteMcapFileServerToken()", secrets, StringComparison.Ordinal);
            Assert.Contains("private string ResolveReplayCursorBridgeToken()", secrets, StringComparison.Ordinal);
            Assert.Contains("private static string ResolveSecretValue", secrets, StringComparison.Ordinal);
            Assert.DoesNotContain("StartServer", secrets, StringComparison.Ordinal);
            Assert.DoesNotContain("StopServer", secrets, StringComparison.Ordinal);
        }

        [Fact]
        public void RecordingRuntimeStateOwnsPendingSidecarWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var setup = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var state = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/RecordingRuntimeState.cs");
            var stateMeta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/RecordingRuntimeState.cs.meta");

            Assert.Contains("[SerializeField] private bool _enableRecording;", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private string _recordingPrefix", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private string _recordingDirectory", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private int _recordingChunkSizeKB", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private McapCompressionMode _recordingCompression", manager, StringComparison.Ordinal);
            Assert.Contains("private readonly RecordingRuntimeState _recordingState = new RecordingRuntimeState();", manager, StringComparison.Ordinal);

            Assert.DoesNotContain("SchemaEvidenceSidecarResult _pendingRecordingSidecar", setup, StringComparison.Ordinal);
            Assert.Contains("_recordingState.PendingSidecar = pendingSidecar", setup, StringComparison.Ordinal);
            Assert.Contains("_recordingState.TakePendingSidecar()", setup, StringComparison.Ordinal);
            Assert.Contains("_recordingState.Clear()", setup, StringComparison.Ordinal);

            Assert.Contains("internal sealed class RecordingRuntimeState", state, StringComparison.Ordinal);
            Assert.Contains("internal SchemaEvidenceSidecarResult PendingSidecar", state, StringComparison.Ordinal);
            Assert.Contains("internal bool HasPendingSidecar => PendingSidecar != null;", state, StringComparison.Ordinal);
            Assert.Contains("internal SchemaEvidenceSidecarResult TakePendingSidecar()", state, StringComparison.Ordinal);
            Assert.DoesNotContain("[SerializeField]", state, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", stateMeta, StringComparison.Ordinal);
        }

        [Fact]
        public void RecordingRuntimeStateCanClearAndTakePendingSidecar()
        {
            var sidecar = new SchemaEvidenceSidecarResult(
                success: true,
                complete: true,
                sidecarDirectory: "sidecar",
                warnings: Array.Empty<string>(),
                temporaryDirectory: "temp");
            var state = new RecordingRuntimeState();

            Assert.False(state.HasPendingSidecar);

            state.PendingSidecar = sidecar;
            Assert.True(state.HasPendingSidecar);
            Assert.Same(sidecar, state.TakePendingSidecar());
            Assert.False(state.HasPendingSidecar);

            state.PendingSidecar = sidecar;
            state.Clear();
            Assert.False(state.HasPendingSidecar);
        }





        [Fact]
        public void ReplayRuntimeStateOwnsReplayCachesWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var setup = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var remoteMcap = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.RemoteMcap.cs");
            var state = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/ReplayRuntimeState.cs");
            var stateMeta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/ReplayRuntimeState.cs.meta");

            Assert.Contains("[SerializeField] private bool _enableReplay;", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private string _replayFilePath", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _replayAutoPlay;", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _disableLivePublishers;", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _cachedReplayFilePathInput", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _cachedResolvedReplayFilePath", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _livePublishersDisabled", manager, StringComparison.Ordinal);
            Assert.Contains("private readonly System.Collections.Generic.List<MonoBehaviour> _disabledPublishers = new();", manager, StringComparison.Ordinal);
            Assert.Contains("private readonly ReplayRuntimeState _replayState = new ReplayRuntimeState();", manager, StringComparison.Ordinal);

            Assert.Contains("_replayState.LivePublishersDisabled", setup, StringComparison.Ordinal);
            Assert.Contains("_disabledPublishers", setup, StringComparison.Ordinal);
            Assert.Contains("_replayState.CachedReplayFilePathInput", remoteMcap, StringComparison.Ordinal);
            Assert.Contains("_replayState.CachedResolvedReplayFilePath", remoteMcap, StringComparison.Ordinal);

            Assert.Contains("internal sealed class ReplayRuntimeState", state, StringComparison.Ordinal);
            Assert.Contains("internal string CachedReplayFilePathInput;", state, StringComparison.Ordinal);
            Assert.Contains("internal string CachedResolvedReplayFilePath;", state, StringComparison.Ordinal);
            Assert.Contains("internal bool LivePublishersDisabled;", state, StringComparison.Ordinal);
            Assert.Contains("internal void InvalidateResolvedReplayFilePathCache()", state, StringComparison.Ordinal);
            Assert.DoesNotContain("[SerializeField]", state, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", stateMeta, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayRuntimeStateCanInvalidateResolvedReplayPathCache()
        {
            var state = new ReplayRuntimeState
            {
                CachedReplayFilePathInput = "input.mcap",
                CachedResolvedReplayFilePath = "C:/project/input.mcap",
                LivePublishersDisabled = true
            };

            state.InvalidateResolvedReplayFilePathCache();

            Assert.Null(state.CachedReplayFilePathInput);
            Assert.Null(state.CachedResolvedReplayFilePath);
            Assert.True(state.LivePublishersDisabled);
        }

        [Fact]
        public void StatisticsRuntimeStateOwnsDiagnosticsStateWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var diagnostics = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var state = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/StatisticsRuntimeState.cs");
            var stateMeta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/StatisticsRuntimeState.cs.meta");

            Assert.Contains("[SerializeField] private bool _publishCadenceDiagnosticsEnabled;", diagnostics, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(0.5f)] private float _publishCadenceDiagnosticsSummaryIntervalSeconds", diagnostics, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _frameStallDiagnosticsEnabled;", diagnostics, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(10f)] private float _frameStallDiagnosticsThresholdMs", diagnostics, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _frameStallStageTimingDiagnosticsEnabled;", diagnostics, StringComparison.Ordinal);
            Assert.Contains("private readonly StatisticsRuntimeState _statisticsState = new StatisticsRuntimeState();", manager, StringComparison.Ordinal);

            Assert.DoesNotContain("private readonly PublishCadenceDiagnostics _publishCadenceDiagnostics = new();", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("private double _nextPublishCadenceDiagnosticsSummaryTime", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("private double _lastFrameStallDiagnosticsTime", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("private double _frameStallStageRuntimeTickMs", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_statisticsState.PublishCadenceDiagnostics", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_statisticsState.NextPublishCadenceDiagnosticsSummaryTime", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_statisticsState.LastFrameStallDiagnosticsTime", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_statisticsState.FrameStallStageRuntimeTickMs", diagnostics, StringComparison.Ordinal);

            Assert.Contains("internal sealed class StatisticsRuntimeState", state, StringComparison.Ordinal);
            Assert.Contains("internal readonly PublishCadenceDiagnostics PublishCadenceDiagnostics = new();", state, StringComparison.Ordinal);
            Assert.Contains("internal sealed class PublishCadenceDiagnostics", state, StringComparison.Ordinal);
            Assert.Contains("internal double NextPublishCadenceDiagnosticsSummaryTime;", state, StringComparison.Ordinal);
            Assert.Contains("internal double LastFrameStallDiagnosticsTime;", state, StringComparison.Ordinal);
            Assert.Contains("internal bool PublishCadenceDiagnosticsWasEnabled;", state, StringComparison.Ordinal);
            Assert.Contains("internal bool FrameStallDiagnosticsWasEnabled;", state, StringComparison.Ordinal);
            Assert.Contains("internal void ResetFrameStallDiagnostics()", state, StringComparison.Ordinal);
            Assert.Contains("internal void ResetFrameStallStageTimingValues()", state, StringComparison.Ordinal);
            Assert.DoesNotContain("[SerializeField]", state, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", stateMeta, StringComparison.Ordinal);
        }

        [Fact]
        public void StatisticsRuntimeStateCanResetFrameStallDiagnostics()
        {
            var state = new StatisticsRuntimeState
            {
                NextPublishCadenceDiagnosticsSummaryTime = 1d,
                LastFrameStallDiagnosticsTime = 2d,
                LastFrameStallGcBytes = 3,
                LastFrameStallMonoUsedBytes = 4,
                LastFrameStallTotalAllocatedBytes = 5,
                LastFrameStallTransportDroppedDataFrames = 6,
                LastFrameStallGcCount0 = 7,
                LastFrameStallGcCount1 = 8,
                LastFrameStallGcCount2 = 9,
                FrameStallStageRuntimeTickMs = 10d,
                FrameStallStageClientLifecycleDrainMs = 11d,
                FrameStallStageClientMessageDrainMs = 12d,
                FrameStallStagePublishCadenceDiagnosticsMs = 13d,
                FrameStallStageLiveOutputModeWatchersMs = 14d,
                FrameStallStageRemoteMcapRefreshMs = 15d,
                FrameStallStageReplayCursorEndpointRefreshMs = 16d,
                FrameStallStageTotalMs = 17d,
                PublishCadenceDiagnosticsWasEnabled = true,
                FrameStallDiagnosticsWasEnabled = true
            };

            state.ResetFrameStallDiagnostics();

            Assert.Equal(1d, state.NextPublishCadenceDiagnosticsSummaryTime);
            Assert.Equal(0d, state.LastFrameStallDiagnosticsTime);
            Assert.Equal(0, state.LastFrameStallGcBytes);
            Assert.Equal(0, state.LastFrameStallMonoUsedBytes);
            Assert.Equal(0, state.LastFrameStallTotalAllocatedBytes);
            Assert.Equal(0, state.LastFrameStallTransportDroppedDataFrames);
            Assert.Equal(0, state.LastFrameStallGcCount0);
            Assert.Equal(0, state.LastFrameStallGcCount1);
            Assert.Equal(0, state.LastFrameStallGcCount2);
            Assert.Equal(0d, state.FrameStallStageRuntimeTickMs);
            Assert.Equal(0d, state.FrameStallStageClientLifecycleDrainMs);
            Assert.Equal(0d, state.FrameStallStageClientMessageDrainMs);
            Assert.Equal(0d, state.FrameStallStagePublishCadenceDiagnosticsMs);
            Assert.Equal(0d, state.FrameStallStageLiveOutputModeWatchersMs);
            Assert.Equal(0d, state.FrameStallStageRemoteMcapRefreshMs);
            Assert.Equal(0d, state.FrameStallStageReplayCursorEndpointRefreshMs);
            Assert.Equal(0d, state.FrameStallStageTotalMs);
            Assert.True(state.PublishCadenceDiagnosticsWasEnabled);
            Assert.True(state.FrameStallDiagnosticsWasEnabled);
            Assert.NotNull(state.PublishCadenceDiagnostics);
        }

        [Fact]
        public void PublishCadenceTopicSummaryIsIdempotent()
        {
            var diagnostics = new PublishCadenceDiagnostics();
            diagnostics.Record("/demo", "json", 1.0, 7);
            diagnostics.Record("/demo", "json", 1.01, 7);

            var topicsField = typeof(PublishCadenceDiagnostics).GetField("_topics", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(topicsField);
            var topics = topicsField.GetValue(diagnostics);
            var values = (IEnumerable)topics.GetType().GetProperty("Values").GetValue(topics);
            object topicStats = null;
            foreach (var value in values)
            {
                topicStats = value;
                break;
            }

            Assert.NotNull(topicStats);
            var buildSummary = topicStats.GetType().GetMethod("BuildSummary", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(buildSummary);

            var first = (string)buildSummary.Invoke(topicStats, Array.Empty<object>());
            var second = (string)buildSummary.Invoke(topicStats, Array.Empty<object>());

            Assert.Equal(first, second);
            Assert.Contains("burstFrames=1", first, StringComparison.Ordinal);
        }




    }
}
