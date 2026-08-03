// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bridge-owned generated CDR publish seam.

using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>
    /// Implemented only by the Bridge physical emitter. Core generation and
    /// runtime routing remain unaware of ROS identities and CDR.
    /// </summary>
    public interface IFoxRunBridgeGeneratedPublishSource
    {
        bool FoxRunBridge_TryBuildPublish(
            int topicIndex,
            ulong nowNs,
            out FoxRunTransportPublishRoute route,
            out string reason);
    }
}
