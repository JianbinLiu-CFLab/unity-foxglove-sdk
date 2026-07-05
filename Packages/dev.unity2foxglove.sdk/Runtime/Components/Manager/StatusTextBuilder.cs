// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure status and diagnostic text formatting helpers for
// FoxgloveManager.

namespace Unity.FoxgloveSDK.Components
{
    internal static class StatusTextBuilder
    {
        internal static string CreateReplayFallbackWarning(string resolvedReplayPath, string failure)
        {
            if (string.IsNullOrWhiteSpace(failure))
            {
                failure = "No replay failure details were reported.";
            }

            return "[Foxglove] Replay was requested but did not enable; restoring live publishers. "
                   + "Replay file: "
                   + (string.IsNullOrWhiteSpace(resolvedReplayPath) ? "<empty>" : resolvedReplayPath)
                   + ". Cause: "
                   + failure;
        }

        internal static string CreateServerStartedMessage(string connectionUrl)
            => "[Foxglove] Server started on " + connectionUrl;

        internal static string CreateRos2BridgeDisabledWarning(string reason)
            => "[Foxglove] ROS2 Bridge disabled: " + reason;
    }
}
