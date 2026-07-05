// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Runtime-only MCAP recording state for FoxgloveManager.

using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class RecordingRuntimeState
    {
        internal SchemaEvidenceSidecarResult PendingSidecar { get; set; }

        internal bool HasPendingSidecar => PendingSidecar != null;

        internal SchemaEvidenceSidecarResult TakePendingSidecar()
        {
            var pending = PendingSidecar;
            PendingSidecar = null;
            return pending;
        }

        internal void Clear()
        {
            PendingSidecar = null;
        }
    }
}
