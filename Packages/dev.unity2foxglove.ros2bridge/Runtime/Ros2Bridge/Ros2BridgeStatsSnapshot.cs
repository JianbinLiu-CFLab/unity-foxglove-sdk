// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable stats snapshot for the ROS2 Bridge background runtime.

using System;
using Unity.FoxgloveSDK.Components;

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

    internal readonly struct Ros2BridgePublisherObservationSnapshot
    {
        internal Ros2BridgePublisherObservationSnapshot(
            int observedContracts,
            int readyContracts,
            int pendingContracts,
            int rejectedContracts,
            string lastReason)
        {
            ValidateCounts(
                observedContracts,
                readyContracts,
                pendingContracts,
                rejectedContracts);
            ObservedContracts = observedContracts;
            ReadyContracts = readyContracts;
            PendingContracts = pendingContracts;
            RejectedContracts = rejectedContracts;
            LastReason = lastReason ?? string.Empty;
        }

        internal static Ros2BridgePublisherObservationSnapshot Empty { get; } =
            new Ros2BridgePublisherObservationSnapshot(0, 0, 0, 0, string.Empty);

        internal int ObservedContracts { get; }
        internal int ReadyContracts { get; }
        internal int PendingContracts { get; }
        internal int RejectedContracts { get; }
        internal string LastReason { get; }

        private static void ValidateCounts(
            int observed,
            int ready,
            int pending,
            int rejected)
        {
            if (observed < 0)
                throw new ArgumentOutOfRangeException(nameof(observed));
            if (ready < 0 || pending < 0 || rejected < 0
                || (long)ready + pending + rejected > observed)
                throw new ArgumentOutOfRangeException(nameof(ready));
        }
    }

    internal readonly struct Ros2BridgeSubscriptionObservationSnapshot
    {
        internal Ros2BridgeSubscriptionObservationSnapshot(
            int observedContracts,
            int activeContracts,
            int pendingContracts,
            int unavailableContracts,
            int rejectedContracts,
            int faultedContracts,
            string lastReason)
        {
            if (observedContracts < 0)
                throw new ArgumentOutOfRangeException(nameof(observedContracts));
            if (activeContracts < 0
                || pendingContracts < 0
                || unavailableContracts < 0
                || rejectedContracts < 0
                || faultedContracts < 0
                || (long)activeContracts
                + pendingContracts
                + unavailableContracts
                + rejectedContracts
                + faultedContracts
                > observedContracts)
            {
                throw new ArgumentOutOfRangeException(nameof(activeContracts));
            }

            ObservedContracts = observedContracts;
            ActiveContracts = activeContracts;
            PendingContracts = pendingContracts;
            UnavailableContracts = unavailableContracts;
            RejectedContracts = rejectedContracts;
            FaultedContracts = faultedContracts;
            LastReason = lastReason ?? string.Empty;
        }

        internal static Ros2BridgeSubscriptionObservationSnapshot Empty { get; } =
            new Ros2BridgeSubscriptionObservationSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                string.Empty);

        internal int ObservedContracts { get; }
        internal int ActiveContracts { get; }
        internal int PendingContracts { get; }
        internal int UnavailableContracts { get; }
        internal int RejectedContracts { get; }
        internal int FaultedContracts { get; }
        internal string LastReason { get; }
    }

    /// <summary>
    /// Pure mapping from observed Bridge runtime state to the neutral Manager
    /// status contract. Configuration values never manufacture readiness.
    /// </summary>
    internal static class Ros2BridgeTransportStatusMapper
    {
        private static readonly FoxRunTransportId ProviderId =
            new FoxRunTransportId("unity2foxglove.ros2bridge");

        internal static FoxRunTransportStatusSnapshot Create(
            ulong generation,
            FoxRunTransportCapabilities selectedDirections,
            Ros2BridgeRuntimeLifecycleState lifecycle,
            Ros2BridgeStatsSnapshot stats,
            bool hasInboundPipeline,
            Ros2BridgePublisherObservationSnapshot publisher,
            Ros2BridgeSubscriptionObservationSnapshot subscription)
        {
            var known = FoxRunTransportCapabilities.Publish
                        | FoxRunTransportCapabilities.Subscribe;
            if (selectedDirections == 0
                || (selectedDirections & ~known) != 0)
                throw new ArgumentOutOfRangeException(nameof(selectedDirections));

            var publishSelected =
                (selectedDirections
                 & FoxRunTransportCapabilities.Publish) != 0;
            var subscribeSelected =
                (selectedDirections
                 & FoxRunTransportCapabilities.Subscribe) != 0;
            var publish = publishSelected
                ? PublishStatus(lifecycle, stats, publisher)
                : FoxRunTransportDirectionStatus.Unselected(
                    FoxRunTransportDirection.Publish);
            var subscribe = subscribeSelected
                ? SubscribeStatus(
                    lifecycle,
                    stats,
                    hasInboundPipeline,
                    subscription)
                : FoxRunTransportDirectionStatus.Unselected(
                    FoxRunTransportDirection.Subscribe);
            return new FoxRunTransportStatusSnapshot(
                ProviderId,
                generation,
                publish,
                subscribe);
        }

        private static FoxRunTransportDirectionStatus PublishStatus(
            Ros2BridgeRuntimeLifecycleState lifecycle,
            Ros2BridgeStatsSnapshot stats,
            Ros2BridgePublisherObservationSnapshot observation)
        {
            if (!stats.Connected)
            {
                return Disconnected(
                    FoxRunTransportDirection.Publish,
                    lifecycle,
                    stats,
                    observation.ObservedContracts,
                    observation.ReadyContracts,
                    observation.RejectedContracts);
            }

            if (observation.RejectedContracts != 0)
            {
                return Direction(
                    FoxRunTransportDirection.Publish,
                    observation.ReadyContracts == 0
                        ? FoxRunTransportObservedState.Failed
                        : FoxRunTransportObservedState.Degraded,
                    observation.ObservedContracts,
                    observation.ReadyContracts,
                    observation.RejectedContracts,
                    "ROS2BRIDGE004",
                    Reason(
                        observation.LastReason,
                        "One or more Bridge publisher contracts were rejected."));
            }
            if (observation.PendingContracts != 0)
            {
                return Direction(
                    FoxRunTransportDirection.Publish,
                    observation.ReadyContracts == 0
                        ? FoxRunTransportObservedState.Starting
                        : FoxRunTransportObservedState.Degraded,
                    observation.ObservedContracts,
                    observation.ReadyContracts,
                    0,
                    "ROS2BRIDGE003",
                    Reason(
                        observation.LastReason,
                        "Bridge publisher preparation is pending."));
            }
            return Direction(
                FoxRunTransportDirection.Publish,
                FoxRunTransportObservedState.Ready,
                observation.ObservedContracts,
                observation.ReadyContracts,
                0);
        }

        private static FoxRunTransportDirectionStatus SubscribeStatus(
            Ros2BridgeRuntimeLifecycleState lifecycle,
            Ros2BridgeStatsSnapshot stats,
            bool hasInboundPipeline,
            Ros2BridgeSubscriptionObservationSnapshot observation)
        {
            var failed = checked(
                observation.UnavailableContracts
                + observation.RejectedContracts
                + observation.FaultedContracts);
            if (!stats.Connected)
            {
                return Disconnected(
                    FoxRunTransportDirection.Subscribe,
                    lifecycle,
                    stats,
                    observation.ObservedContracts,
                    observation.ActiveContracts,
                    failed);
            }
            if (!hasInboundPipeline)
            {
                return Direction(
                    FoxRunTransportDirection.Subscribe,
                    FoxRunTransportObservedState.Failed,
                    observation.ObservedContracts,
                    observation.ActiveContracts,
                    failed,
                    "ROS2BRIDGE006",
                    "The connected Bridge session has no inbound decode pipeline.");
            }
            if (failed != 0)
            {
                return Direction(
                    FoxRunTransportDirection.Subscribe,
                    observation.ActiveContracts == 0
                        ? FoxRunTransportObservedState.Failed
                        : FoxRunTransportObservedState.Degraded,
                    observation.ObservedContracts,
                    observation.ActiveContracts,
                    failed,
                    "ROS2BRIDGE006",
                    Reason(
                        observation.LastReason,
                        "One or more Bridge subscription contracts failed."));
            }
            if (observation.ObservedContracts == 0
                || observation.PendingContracts != 0)
            {
                return Direction(
                    FoxRunTransportDirection.Subscribe,
                    observation.ActiveContracts == 0
                        ? FoxRunTransportObservedState.Starting
                        : FoxRunTransportObservedState.Degraded,
                    observation.ObservedContracts,
                    observation.ActiveContracts,
                    0,
                    "ROS2BRIDGE005",
                    Reason(
                        observation.LastReason,
                        "Bridge subscription decode bindings are not ready."));
            }
            return Direction(
                FoxRunTransportDirection.Subscribe,
                FoxRunTransportObservedState.Ready,
                observation.ObservedContracts,
                observation.ActiveContracts,
                0);
        }

        private static FoxRunTransportDirectionStatus Disconnected(
            FoxRunTransportDirection direction,
            Ros2BridgeRuntimeLifecycleState lifecycle,
            Ros2BridgeStatsSnapshot stats,
            int observed,
            int ready,
            int failed)
        {
            if (!stats.Enabled
                || lifecycle == Ros2BridgeRuntimeLifecycleState.Stopped
                || lifecycle == Ros2BridgeRuntimeLifecycleState.Stopping)
            {
                return Direction(
                    direction,
                    FoxRunTransportObservedState.Stopped,
                    observed,
                    ready,
                    failed);
            }

            var reconnecting = stats.LastConnectedUnixMs != 0
                               || !string.IsNullOrWhiteSpace(stats.LastError);
            return Direction(
                direction,
                reconnecting
                    ? FoxRunTransportObservedState.Reconnecting
                    : FoxRunTransportObservedState.Starting,
                observed,
                ready,
                failed,
                reconnecting ? "ROS2BRIDGE002" : "ROS2BRIDGE001",
                reconnecting
                    ? Reason(
                        stats.LastError,
                        "ROS2 Bridge is reconnecting.")
                    : "ROS2 Bridge is starting.");
        }

        private static FoxRunTransportDirectionStatus Direction(
            FoxRunTransportDirection direction,
            FoxRunTransportObservedState state,
            int observed,
            int ready,
            int failed,
            string code = null,
            string message = null)
            => new FoxRunTransportDirectionStatus(
                direction,
                selected: true,
                state,
                observed,
                ready,
                failed,
                string.IsNullOrEmpty(code)
                    ? (FoxRunTransportDiagnostic?)null
                    : new FoxRunTransportDiagnostic(code, message));

        private static string Reason(string observed, string fallback)
            => string.IsNullOrWhiteSpace(observed)
                ? fallback
                : observed;
    }
}
