// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Routing
// Purpose: Optional live-data mirror sink contract for add-on packages.

using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Optional sink that mirrors locally registered channels and live publish
    /// payloads. Add-on packages implement this without becoming transports.
    /// </summary>
    public interface IFoxgloveMirrorSink
    {
        /// <summary>Whether this sink currently needs payloads for the channel.</summary>
        bool HasChannelDemand(AdvertiseChannel channel);

        /// <summary>Mirror a local channel registration.</summary>
        void RegisterChannel(AdvertiseChannel channel);

        /// <summary>Mirror a local channel removal.</summary>
        void UnregisterChannel(uint channelId);

        /// <summary>Mirror a live payload for a previously registered channel.</summary>
        void Publish(AdvertiseChannel channel, ulong logTimeNs, byte[] payload);
    }
}
