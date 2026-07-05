// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Runtime-only connection counters and watcher state for FoxgloveManager.

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class ConnectionRuntimeState
    {
        internal ConnectionRuntimeState(int firstAutoChannelId)
        {
            NextChannelId = firstAutoChannelId;
        }

        internal string Ros2BridgeSetupError = string.Empty;
        internal ulong Ros2BridgeSequence;
        internal bool LastFoxgloveOutputEnabled;
        internal bool LastRos2BridgeEnabled;
        internal bool OutputModeWatchInitialized;
        internal int NextChannelId;
        internal ulong ChannelSessionGeneration;

        internal void ResetChannelIds(int firstAutoChannelId)
        {
            NextChannelId = firstAutoChannelId;
        }

        internal void AdvanceChannelSessionGeneration()
        {
            unchecked
            {
                ChannelSessionGeneration++;
                if (ChannelSessionGeneration == 0)
                    ChannelSessionGeneration = 1;
            }
        }

        internal ulong NextRos2BridgeSequence()
        {
            unchecked
            {
                Ros2BridgeSequence++;
                return Ros2BridgeSequence;
            }
        }
    }
}
