// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.RemoteGateway.Native;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    internal static class RemoteGatewayCapabilityPolicy
    {
        // V1 is outbound visualization only. Keep ClientPublish, Services,
        // Parameters, Assets, and ConnectionGraph as separate future opt-ins.
        private const byte V1CapabilityFlags = 0;

        internal static RemoteGatewayNativeMethods.FoxgloveGatewayCapability CreateOutboundOnlyCapabilities()
            => new RemoteGatewayNativeMethods.FoxgloveGatewayCapability
            {
                Flags = V1CapabilityFlags
            };
    }
}
