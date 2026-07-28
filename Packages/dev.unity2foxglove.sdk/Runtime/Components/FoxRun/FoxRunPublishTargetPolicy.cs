// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves inherited FoxRun publish destinations without duplicating Manager output policy.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Resolves the Manager FoxRun publish profile from the ordinary Publish
    /// Destinations.
    /// </summary>
    public static class FoxRunPublishTargetPolicy
    {
        public static FoxRunEndpoint FromPublishDestinations(
            bool foxgloveEnabled,
            bool ros2NativeEnabled,
            bool ros2BridgeEnabled)
        {
            var targets = (FoxRunEndpoint)0;
            if (foxgloveEnabled)
                targets |= FoxRunEndpoint.Foxglove;
            if (ros2NativeEnabled)
                targets |= FoxRunEndpoint.Ros2Native;
            if (ros2BridgeEnabled)
                targets |= FoxRunEndpoint.Ros2Bridge;

            // A profile must remain non-empty even while every transport master
            // switch is disabled. Disabled transports still prevent delivery.
            return targets == 0 ? FoxRunEndpoint.Foxglove : targets;
        }
    }
}
