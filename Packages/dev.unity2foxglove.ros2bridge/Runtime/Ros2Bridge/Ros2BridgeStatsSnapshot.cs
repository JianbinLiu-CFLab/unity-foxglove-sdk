// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable stats snapshot for the ROS2 Bridge background runtime.

namespace Unity2Foxglove.Ros2Bridge
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
            : this(
                enabled,
                connected,
                connecting,
                queuedFrames,
                sentFrames,
                droppedFrames,
                failedFrames,
                lastError,
                lastConnectedUnixMs,
                lastDisconnectedUnixMs,
                acceptedFrames: 0,
                replacedFrames: 0,
                oversizeFrames: 0,
                backpressureRejectedFrames: 0,
                rejectedAfterStopFrames: 0,
                faultedFrames: 0,
                disposalFailures: 0,
                queuedBytes: 0,
                transientBytes: 0,
                inFlightBytes: 0)
        {
        }

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
            long lastDisconnectedUnixMs,
            long acceptedFrames,
            long replacedFrames,
            long oversizeFrames,
            long backpressureRejectedFrames,
            long rejectedAfterStopFrames,
            long faultedFrames,
            long disposalFailures,
            long queuedBytes,
            long transientBytes,
            long inFlightBytes)
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
            AcceptedFrames = acceptedFrames;
            ReplacedFrames = replacedFrames;
            OversizeFrames = oversizeFrames;
            BackpressureRejectedFrames = backpressureRejectedFrames;
            RejectedAfterStopFrames = rejectedAfterStopFrames;
            FaultedFrames = faultedFrames;
            DisposalFailures = disposalFailures;
            QueuedBytes = queuedBytes;
            TransientBytes = transientBytes;
            InFlightBytes = inFlightBytes;
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
        public long AcceptedFrames { get; }
        public long ReplacedFrames { get; }
        public long OversizeFrames { get; }
        public long BackpressureRejectedFrames { get; }
        public long RejectedAfterStopFrames { get; }
        public long FaultedFrames { get; }
        public long DisposalFailures { get; }
        public long QueuedBytes { get; }
        public long TransientBytes { get; }
        public long InFlightBytes { get; }
    }
}
