// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
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
        public void WarningDebounceStateOwnsWarningFieldsWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var channels = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");
            var clientEvents = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            var publishing = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var status = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Status.cs");
            var state = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/WarningDebounceState.cs");
            var stateMeta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/WarningDebounceState.cs.meta");

            Assert.DoesNotContain("private bool _warnedNotRunning", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _lastInvalidPublishTopicWarningKey", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _lastInvalidRos2SchemaWarningKey", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _lastRos2BridgePublishWarningKey", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private long _lastRos2BridgePublishWarningTicks", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private readonly object _ros2BridgePublishWarningGate", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private long _lastClientEventOverflowWarningTicks", clientEvents, StringComparison.Ordinal);
            Assert.Contains("private readonly WarningDebounceState _warningDebounceState = new WarningDebounceState();", manager, StringComparison.Ordinal);

            Assert.Contains("_warningDebounceState.WarnedNotRunning", channels, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.WarnedNotRunning", publishing, StringComparison.Ordinal);
            Assert.Contains("ref _warningDebounceState.LastClientEventOverflowWarningTicks", clientEvents, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.LastInvalidPublishTopicWarningKey", publishing, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.LastInvalidRos2SchemaWarningKey", publishing, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.Ros2BridgePublishWarningGate", publishing, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.ResetNotRunning()", server, StringComparison.Ordinal);
            Assert.Contains("_warningDebounceState.WarnedNotRunning", status, StringComparison.Ordinal);

            Assert.Contains("internal sealed class WarningDebounceState", state, StringComparison.Ordinal);
            Assert.Contains("internal readonly object Ros2BridgePublishWarningGate", state, StringComparison.Ordinal);
            Assert.Contains("internal long LastClientEventOverflowWarningTicks", state, StringComparison.Ordinal);
            Assert.DoesNotContain("[SerializeField]", state, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", stateMeta, StringComparison.Ordinal);
        }

        [Fact]
        public void WarningDebounceStateCanResetNotRunningState()
        {
            var state = new WarningDebounceState
            {
                WarnedNotRunning = true,
                LastInvalidPublishTopicWarningKey = "topic",
                LastInvalidRos2SchemaWarningKey = "schema",
                LastRos2BridgePublishWarningKey = "bridge",
                LastRos2BridgePublishWarningTicks = 42,
                LastClientEventOverflowWarningTicks = 24
            };

            state.ResetNotRunning();

            Assert.False(state.WarnedNotRunning);
            Assert.Equal("topic", state.LastInvalidPublishTopicWarningKey);
            Assert.Equal("schema", state.LastInvalidRos2SchemaWarningKey);
            Assert.Equal("bridge", state.LastRos2BridgePublishWarningKey);
            Assert.Equal(42, state.LastRos2BridgePublishWarningTicks);
            Assert.Equal(24, state.LastClientEventOverflowWarningTicks);
            Assert.NotNull(state.Ros2BridgePublishWarningGate);
        }

        [Fact]
        public void ReplayRuntimeStateOwnsReplayCachesWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var setup = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
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
            Assert.Contains("_replayState.CachedReplayFilePathInput", server, StringComparison.Ordinal);
            Assert.Contains("_replayState.CachedResolvedReplayFilePath", server, StringComparison.Ordinal);

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
                FrameStallStageManagerUpdateMs = 17d,
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
            Assert.Equal(0d, state.FrameStallStageManagerUpdateMs);
            Assert.True(state.PublishCadenceDiagnosticsWasEnabled);
            Assert.True(state.FrameStallDiagnosticsWasEnabled);
            Assert.NotNull(state.PublishCadenceDiagnostics);
        }

        [Fact]
        public void ConnectionRuntimeStateOwnsConnectionCountersWithoutMovingSerializedFields()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var channels = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");
            var publishing = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var state = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/ConnectionRuntimeState.cs");
            var stateMeta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/ConnectionRuntimeState.cs.meta");

            Assert.Contains("[SerializeField] private FoxgloveTransportMode _transportMode", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _foxgloveOutputEnabled", manager, StringComparison.Ordinal);
            Assert.Contains("[SerializeField] private bool _ros2BridgeEnabled", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _ros2BridgeSetupError", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private ulong _ros2BridgeSequence", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _lastFoxgloveOutputEnabled", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _lastRos2BridgeEnabled", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _outputModeWatchInitialized", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private int _nextChannelId", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("private ulong _channelSessionGeneration", channels, StringComparison.Ordinal);
            Assert.Contains("private readonly ConnectionRuntimeState _connectionState = new ConnectionRuntimeState(FirstAutoChannelId);", manager, StringComparison.Ordinal);

            Assert.Contains("_connectionState.ChannelSessionGeneration", channels, StringComparison.Ordinal);
            Assert.Contains("_connectionState.AdvanceChannelSessionGeneration();", channels, StringComparison.Ordinal);
            Assert.Contains("_connectionState.NextChannelId", publishing, StringComparison.Ordinal);
            Assert.Contains("_connectionState.NextRos2BridgeSequence()", publishing, StringComparison.Ordinal);
            Assert.Contains("_connectionState.ResetChannelIds(FirstAutoChannelId);", server, StringComparison.Ordinal);
            Assert.Contains("_connectionState.OutputModeWatchInitialized", manager, StringComparison.Ordinal);

            Assert.Contains("internal sealed class ConnectionRuntimeState", state, StringComparison.Ordinal);
            Assert.Contains("internal ConnectionRuntimeState(int firstAutoChannelId)", state, StringComparison.Ordinal);
            Assert.Contains("internal string Ros2BridgeSetupError = string.Empty;", state, StringComparison.Ordinal);
            Assert.Contains("internal ulong Ros2BridgeSequence;", state, StringComparison.Ordinal);
            Assert.Contains("internal int NextChannelId;", state, StringComparison.Ordinal);
            Assert.Contains("internal ulong ChannelSessionGeneration;", state, StringComparison.Ordinal);
            Assert.Contains("internal void ResetChannelIds(int firstAutoChannelId)", state, StringComparison.Ordinal);
            Assert.Contains("internal ulong NextRos2BridgeSequence()", state, StringComparison.Ordinal);
            Assert.DoesNotContain("[SerializeField]", state, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", stateMeta, StringComparison.Ordinal);
        }

        [Fact]
        public void ConnectionRuntimeStateCanAdvanceAndResetChannelIds()
        {
            var state = new ConnectionRuntimeState(7);

            Assert.Equal(7, state.NextChannelId);
            Assert.Equal(0UL, state.ChannelSessionGeneration);

            state.NextChannelId = 12;
            state.ChannelSessionGeneration = ulong.MaxValue;
            state.AdvanceChannelSessionGeneration();

            Assert.Equal(1UL, state.ChannelSessionGeneration);

            state.Ros2BridgeSetupError = "bridge failed";
            Assert.Equal(1UL, state.NextRos2BridgeSequence());
            Assert.Equal(2UL, state.NextRos2BridgeSequence());

            state.ResetChannelIds(7);

            Assert.Equal(7, state.NextChannelId);
            Assert.Equal(1UL, state.ChannelSessionGeneration);
            Assert.Equal("bridge failed", state.Ros2BridgeSetupError);
            Assert.Equal(2UL, state.Ros2BridgeSequence);
        }
    }
}
