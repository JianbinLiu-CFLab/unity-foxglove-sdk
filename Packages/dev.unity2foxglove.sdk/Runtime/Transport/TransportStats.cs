// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Read-only transport health surface — immutable snapshots of
// per-client queue and aggregate counters. Kept UnityEngine-free for
// runtime testability.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Optional interface for transports that can report internal health stats.
    /// Implemented by <see cref="ManagedWsBackend"/>; fake/native transports skip it.
    /// </summary>
    public interface IFoxgloveTransportStatsProvider
    {
        /// <summary>Produce an immutable snapshot of current transport health.</summary>
        TransportStatsSnapshot GetStatsSnapshot();
    }

    /// <summary>
    /// Immutable aggregate snapshot of transport health and per-client details.
    /// Callers receive a defensive copy; mutating the snapshot does not affect
    /// the running transport.
    /// </summary>
    public sealed class TransportStatsSnapshot
    {
        /// <summary>Whether this transport backend supports health reporting.</summary>
        public bool Supported { get; init; }
        /// <summary>Whether the transport is currently accepting connections.</summary>
        public bool IsRunning { get; init; }
        /// <summary>Number of currently connected clients.</summary>
        public int ActiveClientCount { get; init; }
        /// <summary>Lifetime count of clients that completed a WebSocket handshake.</summary>
        public long TotalAcceptedClients { get; init; }
        /// <summary>Lifetime count of clients that disconnected or were removed.</summary>
        public long TotalDisconnectedClients { get; init; }
        /// <summary>Lifetime count of stale data frames dropped under backpressure.</summary>
        public long TotalDroppedDataFrames { get; init; }
        /// <summary>Lifetime count of slow clients disconnected because a control frame could not fit.</summary>
        public long ControlOverflowDisconnects { get; init; }
        /// <summary>Total queued frames across all active clients (sum of snapshots).</summary>
        public long TotalQueuedFrames { get; init; }
        /// <summary>Total queued bytes across all active clients (sum of snapshots).</summary>
        public long TotalQueuedBytes { get; init; }
        /// <summary>Per-client health details, one entry per connected client.</summary>
        public IReadOnlyList<TransportClientStats> Clients { get; init; }

        /// <summary>Pre-built unsupported snapshot for transports without the optional interface.</summary>
        public static readonly TransportStatsSnapshot Unsupported = new()
        {
            Supported = false,
            IsRunning = false,
            Clients = Array.Empty<TransportClientStats>()
        };
    }

    /// <summary>
    /// Immutable per-client health snapshot.
    /// </summary>
    public sealed class TransportClientStats
    {
        /// <summary>Transport-assigned client ID.</summary>
        public uint ClientId { get; init; }
        /// <summary>UTC time the WebSocket handshake completed.</summary>
        public DateTime ConnectedAtUtc { get; init; }
        /// <summary>Elapsed wall-clock time since the handshake completed (ms).</summary>
        public long ConnectedDurationMs { get; init; }
        /// <summary>Time since the client last sent or received a frame (ms, monotonic).</summary>
        public long LastActivityAgeMs { get; init; }
        /// <summary>Total frames currently queued for this client.</summary>
        public int QueuedFrames { get; init; }
        /// <summary>Control frames currently queued.</summary>
        public int QueuedControlFrames { get; init; }
        /// <summary>Data frames currently queued.</summary>
        public int QueuedDataFrames { get; init; }
        /// <summary>Bytes currently queued for this client.</summary>
        public int QueuedBytes { get; init; }
        /// <summary>Lifetime stale data frames dropped for this client.</summary>
        public long DroppedDataFrames { get; init; }
        /// <summary>Lifetime frames successfully sent to this client.</summary>
        public long SentFrames { get; init; }
        /// <summary>Lifetime bytes successfully sent to this client.</summary>
        public long SentBytes { get; init; }
    }
}
