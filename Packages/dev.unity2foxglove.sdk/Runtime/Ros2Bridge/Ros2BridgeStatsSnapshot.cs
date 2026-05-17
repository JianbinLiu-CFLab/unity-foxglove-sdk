// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable stats snapshot for the ROS2 Bridge background runtime.

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Thread-safe copy of ROS2 Bridge runtime state for Inspector and tests.</summary>
    public readonly struct Ros2BridgeStatsSnapshot
    {
        public Ros2BridgeStatsSnapshot(
            bool enabled,
            bool connected,
            bool connecting,
            int queuedFrames,
            long sentFrames,
            long droppedFrames,
            long failedFrames,
            string lastError,
            long lastConnectedUnixMs,
            long lastDisconnectedUnixMs)
        {
            Enabled = enabled;
            Connected = connected;
            Connecting = connecting;
            QueuedFrames = queuedFrames;
            SentFrames = sentFrames;
            DroppedFrames = droppedFrames;
            FailedFrames = failedFrames;
            LastError = lastError ?? string.Empty;
            LastConnectedUnixMs = lastConnectedUnixMs;
            LastDisconnectedUnixMs = lastDisconnectedUnixMs;
        }

        public static Ros2BridgeStatsSnapshot Disabled { get; } = new Ros2BridgeStatsSnapshot(
            enabled: false,
            connected: false,
            connecting: false,
            queuedFrames: 0,
            sentFrames: 0,
            droppedFrames: 0,
            failedFrames: 0,
            lastError: string.Empty,
            lastConnectedUnixMs: 0,
            lastDisconnectedUnixMs: 0);

        public bool Enabled { get; }
        public bool Connected { get; }
        public bool Connecting { get; }
        public int QueuedFrames { get; }
        public long SentFrames { get; }
        public long DroppedFrames { get; }
        public long FailedFrames { get; }
        public string LastError { get; }
        public long LastConnectedUnixMs { get; }
        public long LastDisconnectedUnixMs { get; }
    }
}
