// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bounded generation-scoped ownership for inbound Bridge frames.

using System;
using System.Collections.Generic;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <remarks>Phase192 source sections: ApplyLifecycle.cs, SessionAndAdmission.cs</remarks>
    internal sealed class Ros2BridgeInboundQueueLimits
    {
        internal Ros2BridgeInboundQueueLimits(
            int maxPayloadBytes,
            long maxTotalBytes,
            int maxPerContractDepth,
            long maxPerContractBytes)
        {
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxPayloadBytes));
            if (maxTotalBytes <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxTotalBytes));
            if (maxPerContractDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPerContractDepth));
            }
            if (maxPerContractBytes <= 0
                || maxPerContractBytes > maxTotalBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPerContractBytes));
            }
            if (maxPayloadBytes > maxPerContractBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPayloadBytes));
            }

            MaxPayloadBytes = maxPayloadBytes;
            MaxTotalBytes = maxTotalBytes;
            MaxPerContractDepth = maxPerContractDepth;
            MaxPerContractBytes = maxPerContractBytes;
        }

        internal int MaxPayloadBytes { get; }

        internal long MaxTotalBytes { get; }

        internal int MaxPerContractDepth { get; }

        internal long MaxPerContractBytes { get; }
    }

    internal sealed class Ros2BridgeInboundStatsSnapshot
    {
        internal Ros2BridgeInboundStatsSnapshot(
            long received,
            long accepted,
            long replaced,
            long dropped,
            long applied,
            long rejectedAfterStop,
            long sequenceGaps,
            long staleSequences,
            long oversize,
            long decodeFailures,
            long disposalFailures,
            int queuedFrames,
            long queuedBytes,
            long transientBytes,
            long inFlightBytes,
            string lastDiagnostic,
            long resolutionRejections,
            bool hasSessionDeliveryFailure)
        {
            Received = received;
            Accepted = accepted;
            Replaced = replaced;
            Dropped = dropped;
            Applied = applied;
            RejectedAfterStop = rejectedAfterStop;
            SequenceGaps = sequenceGaps;
            StaleSequences = staleSequences;
            Oversize = oversize;
            DecodeFailures = decodeFailures;
            DisposalFailures = disposalFailures;
            QueuedFrames = queuedFrames;
            QueuedBytes = queuedBytes;
            TransientBytes = transientBytes;
            InFlightBytes = inFlightBytes;
            LastDiagnostic = lastDiagnostic ?? string.Empty;
            ResolutionRejections = resolutionRejections;
            HasSessionDeliveryFailure = hasSessionDeliveryFailure;
        }

        internal long Received { get; }

        internal long Accepted { get; }

        internal long Replaced { get; }

        internal long Dropped { get; }

        internal long Applied { get; }

        internal long RejectedAfterStop { get; }

        internal long SequenceGaps { get; }

        internal long StaleSequences { get; }

        internal long Oversize { get; }

        internal long DecodeFailures { get; }

        internal long DisposalFailures { get; }

        internal int QueuedFrames { get; }

        internal long QueuedBytes { get; }

        internal long TransientBytes { get; }

        internal long InFlightBytes { get; }

        internal string LastDiagnostic { get; }

        internal long ResolutionRejections { get; }

        internal bool HasSessionDeliveryFailure { get; }
    }

    internal sealed class Ros2BridgeInboundApplyLease :
        IDisposable
    {
        private readonly Ros2BridgeInboundQueue _owner;
        private readonly long _epoch;
        private int _outcome;
        private int _disposed;
        private string _reason = string.Empty;

        internal Ros2BridgeInboundApplyLease(
            Ros2BridgeInboundQueue owner,
            Ros2BridgeInboundFrame frame,
            long epoch)
        {
            _owner = owner
                ?? throw new ArgumentNullException(nameof(owner));
            Frame = frame
                ?? throw new ArgumentNullException(nameof(frame));
            _epoch = epoch;
        }

        internal Ros2BridgeInboundFrame Frame { get; }

        internal bool CanApply
            => VolatileDisposed == 0
               && _owner.IsCurrent(this, _epoch);

        private int VolatileDisposed
            => System.Threading.Volatile.Read(ref _disposed);

        internal void MarkApplied()
            => SetOutcome(
                outcome: 1,
                reason: string.Empty);

        internal void MarkDecodeFailure(string reason)
            => SetOutcome(
                outcome: 2,
                reason);

        private void SetOutcome(int outcome, string reason)
        {
            if (VolatileDisposed != 0)
            {
                throw new ObjectDisposedException(
                    nameof(Ros2BridgeInboundApplyLease));
            }
            if (System.Threading.Interlocked.CompareExchange(
                    ref _outcome,
                    outcome,
                    0) != 0)
            {
                throw new InvalidOperationException(
                    "The inbound apply lease already has an outcome.");
            }
            _reason = reason ?? string.Empty;
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(
                    ref _disposed,
                    1) != 0)
            {
                return;
            }
            _owner.Complete(
                this,
                _epoch,
                _outcome,
                _reason);
        }
    }

    internal sealed partial class Ros2BridgeInboundQueue :
        IRos2BridgeInboundFrameReceiver,
        IDisposable
    {
        private const int MaxDiagnosticChars = 512;

        private sealed class ContractUsage
        {
            internal int Depth;
            internal long Bytes;
        }

        private readonly object _gate = new object();
        private readonly Ros2BridgeInboundQueueLimits _limits;
        private readonly LinkedList<Ros2BridgeInboundFrame> _queued =
            new LinkedList<Ros2BridgeInboundFrame>();
        private readonly Dictionary<ulong, ContractUsage> _usage =
            new Dictionary<ulong, ContractUsage>();
        private readonly Dictionary<ulong, U2R2ContractSequence> _sequences =
            new Dictionary<ulong, U2R2ContractSequence>();
        private readonly Dictionary<
            ulong,
            Ros2BridgeSessionContract> _active =
            new Dictionary<ulong, Ros2BridgeSessionContract>();

        private string _sessionId = string.Empty;
        private ulong _connectionGeneration;
        private bool _running;
        private bool _disposed;
        private long _epoch;
        private long _queuedBytes;
        private long _transientBytes;
        private long _inFlightBytes;
        private Ros2BridgeInboundApplyLease _inFlight;
        private long _received;
        private long _accepted;
        private long _replaced;
        private long _dropped;
        private long _applied;
        private long _rejectedAfterStop;
        private long _sequenceGaps;
        private long _staleSequences;
        private long _oversize;
        private long _decodeFailures;
        private long _disposalFailures;
        private long _resolutionRejections;
        private bool _sessionDeliveryFailure;
        private string _lastDiagnostic = string.Empty;
    }
}
