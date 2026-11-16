// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Shared camera timing snapshot bridge for frame-stall diagnostics.

using System;
using System.Collections.Generic;

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
        private static readonly Dictionary<int, CameraTimingSnapshot> s_snapshots =
            new Dictionary<int, CameraTimingSnapshot>();
        private static int s_nextPublisherId;

        public static CameraTimingSnapshot LastSnapshotOrDefault
            => AggregateSnapshots();

        internal static int RegisterPublisher()
            => ++s_nextPublisherId;

#if UNITY_2019_1_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
            => Reset();
#endif

        public static void Publish(int publisherId, CameraTimingSnapshot snapshot)
        {
            if (publisherId > 0)
                s_snapshots[publisherId] = snapshot;
        }

        public static void Reset()
            => s_snapshots.Clear();

        public static void Reset(int publisherId)
            => s_snapshots.Remove(publisherId);

        private static CameraTimingSnapshot AggregateSnapshots()
        {
            var aggregate = CameraTimingSnapshot.NoFrame;
            var latestPublisherId = 0;
            foreach (var pair in s_snapshots)
            {
                var snapshot = pair.Value;
                if (!snapshot.HasFrame)
                    continue;
                if (!aggregate.HasFrame
                    || snapshot.RecordedRealtimeSeconds > aggregate.RecordedRealtimeSeconds
                    || (snapshot.RecordedRealtimeSeconds == aggregate.RecordedRealtimeSeconds
                        && pair.Key > latestPublisherId))
                {
                    aggregate = snapshot;
                    latestPublisherId = pair.Key;
                }
            }

            return aggregate;
        }
    }
}
