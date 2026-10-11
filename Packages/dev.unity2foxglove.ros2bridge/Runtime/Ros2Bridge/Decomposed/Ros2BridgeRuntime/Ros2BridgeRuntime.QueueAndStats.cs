// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using Components = Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeWorkerLease
    {
        public bool TryEnqueue(Ros2BridgeFrame frame, out string reason)
        {
            if (!(_ownedSink is IRos2BridgePublisherPreparationTransport)
                && !(_ownedSink is IRos2BridgeV2SessionTransport))
            {
                return TryEnqueueCore(
                    frame,
                    default,
                    requiresPreparation: false,
                    requireReadyAtAdmission: false,
                    out reason);
            }
            if (!TryValidateFrame(frame, out reason))
                return false;
            if (!frame.Qos.HasValue)
            {
                reason =
                    "ROS2 Bridge publisher preparation requires an exact QoS contract.";
                return false;
            }

            var readiness = PreparePublisher(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value,
                out reason);
            if (readiness == Ros2BridgePublisherReadiness.Rejected)
                return false;
            var key = new PublisherPreparationKey(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value);
            return TryEnqueueCore(
                frame,
                key,
                requiresPreparation: true,
                requireReadyAtAdmission: false,
                out reason);
        }

        internal bool TryEnqueuePrepared(Ros2BridgeFrame frame, out string reason)
        {
            if (!TryValidateFrame(frame, out reason))
                return false;
            if (!frame.Qos.HasValue)
            {
                reason = "Prepared ROS2 Bridge frame requires an exact QoS contract.";
                return false;
            }
            var key = new PublisherPreparationKey(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value);
            return TryEnqueueCore(
                frame,
                key,
                requiresPreparation: true,
                requireReadyAtAdmission: true,
                out reason);
        }

        private bool TryEnqueueCore(
            Ros2BridgeFrame frame,
            PublisherPreparationKey preparationKey,
            bool requiresPreparation,
            bool requireReadyAtAdmission,
            out string reason)
        {
            if (!TryValidateFrame(frame, out reason))
                return false;

            lock (_gate)
            {
                if (!TryValidateEnqueueState(
                        preparationKey,
                        requiresPreparation,
                        requireReadyAtAdmission,
                        out reason))
                    return false;
            }

            var preparationDisposition = _outbound.PrepareEnqueue(
                frame,
                out var prepared);
            if (preparationDisposition
                != Ros2BridgeOutboundEnqueueDisposition.Accepted)
            {
                lock (_gate)
                {
                    return TryApplyEnqueueDisposition(
                        preparationDisposition,
                        out reason);
                }
            }

            using (prepared)
            {
                lock (_gate)
                {
                    if (!TryValidateEnqueueState(
                            preparationKey,
                            requiresPreparation,
                            requireReadyAtAdmission,
                            out reason))
                        return false;

                    var disposition = _outbound.CommitPrepared(
                        prepared,
                        U2R2QueueOverflowPolicy.DropOldest,
                        requiresPreparation,
                        _connectionGeneration);
                    if (!TryApplyEnqueueDisposition(
                            disposition,
                            out reason))
                        return false;
                }
            }

            _signal.Set();
            return true;
        }

        private bool TryValidateEnqueueState(
            PublisherPreparationKey preparationKey,
            bool requiresPreparation,
            bool requireReadyAtAdmission,
            out string reason)
        {
            if (!_enabled)
            {
                reason = "ROS2 Bridge is disabled.";
                return false;
            }
            if (!_autoConnect)
            {
                reason =
                    "ROS2 Bridge auto-connect is disabled; connect before sending frames.";
                return false;
            }

            if (requiresPreparation)
            {
                if (!_preparations.TryGetValue(preparationKey, out var entry))
                {
                    reason =
                        "ROS2 Bridge publisher preparation was not registered.";
                    return false;
                }
                if (entry.Readiness == Ros2BridgePublisherReadiness.Rejected)
                {
                    reason = entry.Reason;
                    return false;
                }
                if (requireReadyAtAdmission
                    && (entry.Readiness
                            != Ros2BridgePublisherReadiness.Ready
                        || entry.ReadyConnectionGeneration
                            != _connectionGeneration))
                {
                    reason = "ROS2 Bridge publisher preparation is pending.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private bool TryApplyEnqueueDisposition(
            Ros2BridgeOutboundEnqueueDisposition disposition,
            out string reason)
        {
            switch (disposition)
            {
                case Ros2BridgeOutboundEnqueueDisposition.Accepted:
                    reason = string.Empty;
                    return true;
                case Ros2BridgeOutboundEnqueueDisposition.DroppedOldest:
                case Ros2BridgeOutboundEnqueueDisposition.ReplacedLatest:
                    _droppedFrames++;
                    reason = string.Empty;
                    return true;
                case Ros2BridgeOutboundEnqueueDisposition.Oversize:
                    reason =
                        "ROS2 Bridge full wire frame exceeds the bounded outbound limits.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected:
                    reason =
                        "ROS2 Bridge bounded outbound capacity is exhausted.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop:
                    reason = "ROS2 Bridge is disabled.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.Faulted:
                    reason = "ROS2 Bridge outbound scheduler faulted.";
                    _failedFrames++;
                    _signal.Set();
                    return false;
                default:
                    throw new InvalidOperationException(
                        "ROS2 Bridge outbound scheduler returned an unknown admission result.");
            }
        }

        private static bool TryValidateFrame(
            Ros2BridgeFrame frame,
            out string reason)
        {
            reason = string.Empty;
            if (frame == null)
            {
                reason = "ROS2 Bridge frame is null.";
                return false;
            }
            return true;
        }

        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
        {
            lock (_gate)
            {
                var outboundCounters = _outbound.Counters;
                return new Ros2BridgeStatsSnapshot(
                    _enabled,
                    _connected,
                    _connecting,
                    checked((int)_outbound.DataQueuedDepth),
                    _sentFrames,
                    _droppedFrames,
                    _failedFrames,
                    _lastError,
                    _lastConnectedUnixMs,
                    _lastDisconnectedUnixMs,
                    AddCounter(
                        _initialStats.AcceptedFrames,
                        outboundCounters.Accepted),
                    AddCounter(
                        _initialStats.ReplacedFrames,
                        outboundCounters.Replaced),
                    AddCounter(
                        _initialStats.OversizeFrames,
                        outboundCounters.Oversize),
                    AddCounter(
                        _initialStats.BackpressureRejectedFrames,
                        outboundCounters.BackpressureRejected),
                    AddCounter(
                        _initialStats.RejectedAfterStopFrames,
                        outboundCounters.RejectedAfterStop),
                    AddCounter(
                        _initialStats.FaultedFrames,
                        outboundCounters.Faulted),
                    AddCounter(
                        _initialStats.DisposalFailures,
                        outboundCounters.DisposalFailures),
                    ToCounter(_outbound.QueuedBytes),
                    ToCounter(_outbound.TransientBytes),
                    ToCounter(_outbound.InFlightBytes));
            }
        }

        internal Ros2BridgePublisherObservationSnapshot
            GetPublisherObservationSnapshot()
        {
            lock (_gate)
            {
                var ready = 0;
                var pending = 0;
                var rejected = 0;
                string selectedTopic = null;
                string selectedSchema = null;
                string selectedReason = string.Empty;
                var selectedPriority = 0;
                foreach (var pair in _preparations)
                {
                    var priority = 0;
                    switch (pair.Value.Readiness)
                    {
                        case Ros2BridgePublisherReadiness.Ready:
                            ready++;
                            break;
                        case Ros2BridgePublisherReadiness.Pending:
                            pending++;
                            priority = 1;
                            break;
                        case Ros2BridgePublisherReadiness.Rejected:
                            rejected++;
                            priority = 2;
                            break;
                    }
                    if (priority == 0
                        || priority < selectedPriority
                        || (priority == selectedPriority
                            && ComparePreparationKey(
                                pair.Key,
                                selectedTopic,
                                selectedSchema) >= 0))
                    {
                        continue;
                    }
                    selectedPriority = priority;
                    selectedTopic = pair.Key.Topic;
                    selectedSchema = pair.Key.SchemaName;
                    selectedReason = pair.Value.Reason;
                }
                var schedulerTerminal =
                    _outbound.TryGetTerminalState(
                        out var schedulerFault);
                if (schedulerTerminal)
                {
                    selectedReason = BoundRuntimeDiagnostic(
                        OutboundTerminalReason(schedulerFault));
                }
                return new Ros2BridgePublisherObservationSnapshot(
                    _preparations.Count,
                    ready,
                    pending,
                    rejected,
                    schedulerTerminal,
                    selectedReason);
            }
        }

        private static int ComparePreparationKey(
            PublisherPreparationKey candidate,
            string selectedTopic,
            string selectedSchema)
        {
            if (selectedTopic == null)
                return -1;
            var topic = string.CompareOrdinal(
                candidate.Topic,
                selectedTopic);
            return topic != 0
                ? topic
                : string.CompareOrdinal(
                    candidate.SchemaName,
                    selectedSchema);
        }

    }
}
