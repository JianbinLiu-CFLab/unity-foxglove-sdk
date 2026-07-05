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
    }
}
