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
    }
}
