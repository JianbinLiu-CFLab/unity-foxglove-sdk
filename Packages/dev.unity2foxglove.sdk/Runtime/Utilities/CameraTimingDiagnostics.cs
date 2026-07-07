// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Shared camera timing snapshot bridge for frame-stall diagnostics.

using System;

namespace Unity.FoxgloveSDK.Components
{
    internal readonly struct CameraTimingSnapshot
    {
        public static readonly CameraTimingSnapshot NoFrame = new CameraTimingSnapshot(
            hasFrame: false,
            recordedRealtimeSeconds: -1d,
            renderMs: -1d,
            readbackLatencyMs: -1d,
            readbackCopyMs: -1d,
            jpegEncodeMs: -1d,
            serializeMs: -1d,
            completedJpegDrainMs: -1d,
            jpegBytes: -1,
            pendingReadbacksBefore: -1,
            pendingReadbacksAfter: -1,
            encodeQueueDepth: -1,
            completedQueueDepth: -1);

        public CameraTimingSnapshot(
            bool hasFrame,
            double recordedRealtimeSeconds,
            double renderMs,
            double readbackLatencyMs,
            double readbackCopyMs,
            double jpegEncodeMs,
            double serializeMs,
            double completedJpegDrainMs,
            int jpegBytes,
            int pendingReadbacksBefore,
            int pendingReadbacksAfter,
            int encodeQueueDepth,
            int completedQueueDepth)
        {
            HasFrame = hasFrame;
            RecordedRealtimeSeconds = recordedRealtimeSeconds;
            RenderMs = renderMs;
            ReadbackLatencyMs = readbackLatencyMs;
            ReadbackCopyMs = readbackCopyMs;
            JpegEncodeMs = jpegEncodeMs;
            SerializeMs = serializeMs;
            CompletedJpegDrainMs = completedJpegDrainMs;
            JpegBytes = jpegBytes;
            PendingReadbacksBefore = pendingReadbacksBefore;
            PendingReadbacksAfter = pendingReadbacksAfter;
            EncodeQueueDepth = encodeQueueDepth;
            CompletedQueueDepth = completedQueueDepth;
        }

        public bool HasFrame { get; }
        public double RecordedRealtimeSeconds { get; }
        public double RenderMs { get; }
        public double ReadbackLatencyMs { get; }
        public double ReadbackCopyMs { get; }
        public double JpegEncodeMs { get; }
        public double SerializeMs { get; }
        public double CompletedJpegDrainMs { get; }
        public int JpegBytes { get; }
        public int PendingReadbacksBefore { get; }
        public int PendingReadbacksAfter { get; }
        public int EncodeQueueDepth { get; }
        public int CompletedQueueDepth { get; }

        public double AgeMs(double nowRealtimeSeconds)
            => HasFrame ? Math.Max(0d, nowRealtimeSeconds - RecordedRealtimeSeconds) * 1000d : -1d;
    }

    internal static class CameraTimingDiagnostics
    {
        // Main-thread diagnostics bridge: camera publishers publish from Unity callbacks and
        // FoxgloveManager reads during its Update/stall logging path. Do not write this from
        // background workers; CameraTimingSnapshot is intentionally a large immutable struct.
        private static CameraTimingSnapshot s_lastSnapshot = CameraTimingSnapshot.NoFrame;

        public static CameraTimingSnapshot LastSnapshotOrDefault
            => s_lastSnapshot;

        public static void Publish(CameraTimingSnapshot snapshot)
            => s_lastSnapshot = snapshot;

        public static void Reset()
            => s_lastSnapshot = CameraTimingSnapshot.NoFrame;
    }
}
