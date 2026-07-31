// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bounded generation-scoped ownership for inbound Bridge frames.

using System;
using System.Collections.Generic;

namespace Unity2Foxglove.Ros2Bridge
{
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
            string lastDiagnostic)
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

    internal sealed class Ros2BridgeInboundQueue :
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
        private readonly Dictionary<ulong, ulong> _lastSequence =
            new Dictionary<ulong, ulong>();
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
        private string _lastDiagnostic = string.Empty;

        internal Ros2BridgeInboundQueue(
            Ros2BridgeInboundQueueLimits limits)
        {
            _limits = limits
                ?? throw new ArgumentNullException(nameof(limits));
        }

        internal void BeginSession(
            string sessionId,
            ulong connectionGeneration,
            Ros2BridgeSessionContractSnapshot contracts)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException(
                    "An inbound queue session ID is required.",
                    nameof(sessionId));
            if (connectionGeneration == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionGeneration));
            }
            if (contracts == null)
                throw new ArgumentNullException(nameof(contracts));

            Ros2BridgeInboundFrame[] displaced;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                displaced = DrainQueuedLocked();
                _active.Clear();
                foreach (var contract in contracts.Contracts)
                {
                    if (contract.Direction
                        != Unity.FoxgloveSDK.Components
                            .FoxRunTransportDirection.Subscribe)
                    {
                        throw new ArgumentException(
                            "The inbound queue accepts subscription contracts only.",
                            nameof(contracts));
                    }
                    _active.Add(contract.ContractId, contract);
                }
                _usage.Clear();
                _lastSequence.Clear();
                _sessionId = sessionId.Trim();
                _connectionGeneration = connectionGeneration;
                _running = true;
                IncrementEpochLocked();
            }
            DisposeFrames(displaced);
        }

        internal bool TryActivateContract(
            Ros2BridgeSessionContract contract,
            string sessionId,
            ulong connectionGeneration,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The inbound queue contract is null.";
                return false;
            }
            if (contract.Direction
                != Unity.FoxgloveSDK.Components
                    .FoxRunTransportDirection.Subscribe)
            {
                reason = "The inbound queue accepts subscription contracts only.";
                return false;
            }

            lock (_gate)
            {
                if (_disposed || !_running)
                {
                    reason = "The inbound queue is stopped.";
                    return false;
                }
                if (!string.Equals(
                        _sessionId,
                        sessionId,
                        StringComparison.Ordinal)
                    || _connectionGeneration != connectionGeneration)
                {
                    reason =
                        "The inbound queue session changed before contract activation.";
                    return false;
                }
                if (_active.TryGetValue(
                        contract.ContractId,
                        out var existing))
                {
                    if (!existing.Equals(contract))
                    {
                        reason =
                            "The inbound queue contract ID conflicts with an active contract.";
                        return false;
                    }
                    reason = string.Empty;
                    return true;
                }

                _active.Add(contract.ContractId, contract);
                reason = string.Empty;
                return true;
            }
        }

        internal bool TryRevokeContract(
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The inbound queue contract is null.";
                return false;
            }

            List<Ros2BridgeInboundFrame> displaced = null;
            lock (_gate)
            {
                if (!_active.TryGetValue(
                        contract.ContractId,
                        out var existing))
                {
                    reason = string.Empty;
                    return true;
                }
                if (!existing.Equals(contract))
                {
                    reason =
                        "The inbound queue contract ID belongs to another contract.";
                    return false;
                }

                _active.Remove(contract.ContractId);
                _lastSequence.Remove(contract.ContractId);
                var node = _queued.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (node.Value.Contract.ContractId
                        == contract.ContractId)
                    {
                        displaced ??= new List<Ros2BridgeInboundFrame>();
                        displaced.Add(node.Value);
                        _queued.Remove(node);
                        ReduceUsageLocked(node.Value);
                        _queuedBytes = checked(
                            _queuedBytes - node.Value.PayloadLength);
                    }
                    node = next;
                }
                _usage.Remove(contract.ContractId);
                if (_inFlight != null
                    && _inFlight.Frame.Contract.ContractId
                    == contract.ContractId)
                {
                    IncrementEpochLocked();
                }
                reason = string.Empty;
            }
            DisposeFrames(displaced);
            return true;
        }

        public Ros2BridgeSessionResult TryAccept(
            Ros2BridgeInboundFrame frame)
        {
            if (frame == null)
            {
                return Ros2BridgeSessionResult.Fault(
                    "The inbound frame is null.");
            }

            List<Ros2BridgeInboundFrame> displaced = null;
            Ros2BridgeSessionResult result;
            lock (_gate)
            {
                Increment(ref _received);
                _transientBytes = frame.PayloadLength;
                try
                {
                    if (!_running || _disposed)
                    {
                        Increment(ref _rejectedAfterStop);
                        SetDiagnosticLocked(
                            "The inbound queue is stopped.");
                        result = Ros2BridgeSessionResult.Reject(
                            _lastDiagnostic);
                    }
                    else if (frame.PayloadLength
                             > _limits.MaxPayloadBytes
                             || frame.PayloadLength
                             > _limits.MaxPerContractBytes)
                    {
                        Increment(ref _oversize);
                        SetDiagnosticLocked(
                            "The inbound payload exceeds its configured bound.");
                        result = Ros2BridgeSessionResult.Reject(
                            _lastDiagnostic);
                    }
                    else if (!string.Equals(
                                 frame.SessionId,
                                 _sessionId,
                                 StringComparison.Ordinal)
                             || frame.ConnectionGeneration
                             != _connectionGeneration)
                    {
                        SetDiagnosticLocked(
                            "The inbound frame belongs to a stale session generation.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (!_active.TryGetValue(
                                 frame.Contract.ContractId,
                                 out var expected))
                    {
                        SetDiagnosticLocked(
                            "The inbound frame references an unknown contract.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (!expected.Equals(frame.Contract))
                    {
                        SetDiagnosticLocked(
                            "The inbound frame conflicts with its frozen contract.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (_lastSequence.TryGetValue(
                                 expected.ContractId,
                                 out var last)
                             && frame.Sequence <= last)
                    {
                        Increment(ref _staleSequences);
                        SetDiagnosticLocked(
                            "The inbound frame sequence is stale.");
                        result = Ros2BridgeSessionResult.Reject(
                            _lastDiagnostic);
                    }
                    else
                    {
                        result = AdmitLocked(
                            frame,
                            expected,
                            out displaced);
                    }
                }
                finally
                {
                    _transientBytes = 0;
                }
            }

            if (!result.IsAccepted)
                DisposeFrame(frame);
            if (displaced != null)
                DisposeFrames(displaced);
            return result;
        }

        private Ros2BridgeSessionResult AdmitLocked(
            Ros2BridgeInboundFrame frame,
            Ros2BridgeSessionContract contract,
            out List<Ros2BridgeInboundFrame> displaced)
        {
            displaced = null;
            _usage.TryGetValue(
                contract.ContractId,
                out var usage);
            usage ??= new ContractUsage();
            var projectedDepth = usage.Depth + 1;
            var projectedBytes =
                checked(usage.Bytes + frame.PayloadLength);
            var removedBytes = 0L;
            var removedDepth = 0;
            var node = _queued.First;
            while ((projectedDepth - removedDepth
                        > _limits.MaxPerContractDepth
                    || projectedBytes - removedBytes
                        > _limits.MaxPerContractBytes)
                   && node != null)
            {
                if (node.Value.Contract.ContractId
                    == contract.ContractId)
                {
                    displaced ??= new List<Ros2BridgeInboundFrame>();
                    displaced.Add(node.Value);
                    removedDepth++;
                    removedBytes = checked(
                        removedBytes
                        + node.Value.PayloadLength);
                }
                node = node.Next;
            }

            if (projectedDepth - removedDepth
                    > _limits.MaxPerContractDepth
                || projectedBytes - removedBytes
                    > _limits.MaxPerContractBytes
                || checked(
                    _queuedBytes
                    + _inFlightBytes
                    - removedBytes
                    + frame.PayloadLength)
                    > _limits.MaxTotalBytes)
            {
                displaced = null;
                Increment(ref _dropped);
                SetDiagnosticLocked(
                    "The inbound queue has no capacity for this contract.");
                return Ros2BridgeSessionResult.Reject(
                    _lastDiagnostic);
            }

            if (displaced != null)
            {
                foreach (var removed in displaced)
                    RemoveQueuedFrameLocked(removed);
                Add(ref _replaced, displaced.Count);
            }
            _queued.AddLast(frame);
            usage.Depth++;
            usage.Bytes = checked(
                usage.Bytes + frame.PayloadLength);
            _usage[contract.ContractId] = usage;
            _queuedBytes = checked(
                _queuedBytes + frame.PayloadLength);
            if (_lastSequence.TryGetValue(
                    contract.ContractId,
                    out var lastSequence)
                && frame.Sequence > lastSequence + 1)
            {
                Add(
                    ref _sequenceGaps,
                    frame.Sequence - lastSequence - 1);
            }
            _lastSequence[contract.ContractId] =
                frame.Sequence;
            Increment(ref _accepted);
            return Ros2BridgeSessionResult.Accepted();
        }

        internal bool TryBeginApply(
            out Ros2BridgeInboundApplyLease lease)
        {
            lock (_gate)
            {
                if (_disposed
                    || _inFlight != null
                    || _queued.First == null)
                {
                    lease = null;
                    return false;
                }

                var frame = _queued.First.Value;
                _queued.RemoveFirst();
                ReduceUsageLocked(frame);
                _queuedBytes = checked(
                    _queuedBytes - frame.PayloadLength);
                _inFlightBytes = frame.PayloadLength;
                lease = new Ros2BridgeInboundApplyLease(
                    this,
                    frame,
                    _epoch);
                _inFlight = lease;
                return true;
            }
        }

        internal bool IsCurrent(
            Ros2BridgeInboundApplyLease lease,
            long epoch)
        {
            lock (_gate)
            {
                return !_disposed
                       && _running
                       && ReferenceEquals(_inFlight, lease)
                       && epoch == _epoch
                       && _active.TryGetValue(
                           lease.Frame.Contract.ContractId,
                           out var current)
                       && current.Equals(lease.Frame.Contract)
                       && string.Equals(
                           lease.Frame.SessionId,
                           _sessionId,
                           StringComparison.Ordinal)
                       && lease.Frame.ConnectionGeneration
                       == _connectionGeneration;
            }
        }

        internal void Complete(
            Ros2BridgeInboundApplyLease lease,
            long epoch,
            int outcome,
            string reason)
        {
            Ros2BridgeInboundFrame frame = null;
            lock (_gate)
            {
                if (!ReferenceEquals(_inFlight, lease))
                    return;
                frame = lease.Frame;
                var current =
                    !_disposed
                    && _running
                    && epoch == _epoch
                    && _active.TryGetValue(
                        frame.Contract.ContractId,
                        out var active)
                    && active.Equals(frame.Contract)
                    && string.Equals(
                        frame.SessionId,
                        _sessionId,
                        StringComparison.Ordinal)
                    && frame.ConnectionGeneration
                    == _connectionGeneration;
                _inFlight = null;
                _inFlightBytes = 0;
                if (outcome == 1 && current)
                {
                    Increment(ref _applied);
                }
                else
                {
                    Increment(ref _decodeFailures);
                    SetDiagnosticLocked(
                        string.IsNullOrWhiteSpace(reason)
                            ? "The inbound apply lease ended without a successful outcome."
                            : reason);
                }
            }
            DisposeFrame(frame);
        }

        internal Ros2BridgeInboundStatsSnapshot
            GetStatsSnapshot()
        {
            lock (_gate)
            {
                return new Ros2BridgeInboundStatsSnapshot(
                    _received,
                    _accepted,
                    _replaced,
                    _dropped,
                    _applied,
                    _rejectedAfterStop,
                    _sequenceGaps,
                    _staleSequences,
                    _oversize,
                    _decodeFailures,
                    _disposalFailures,
                    _queued.Count,
                    _queuedBytes,
                    _transientBytes,
                    _inFlightBytes,
                    _lastDiagnostic);
            }
        }

        internal void Stop()
        {
            Ros2BridgeInboundFrame[] displaced;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _running = false;
                _sessionId = string.Empty;
                _connectionGeneration = 0;
                _active.Clear();
                _usage.Clear();
                _lastSequence.Clear();
                IncrementEpochLocked();
                displaced = DrainQueuedLocked();
            }
            DisposeFrames(displaced);
        }

        public void Dispose()
        {
            Ros2BridgeInboundFrame[] displaced;
            Ros2BridgeInboundFrame inFlight = null;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _running = false;
                _sessionId = string.Empty;
                _connectionGeneration = 0;
                _active.Clear();
                _usage.Clear();
                _lastSequence.Clear();
                IncrementEpochLocked();
                displaced = DrainQueuedLocked();
                if (_inFlight != null)
                {
                    inFlight = _inFlight.Frame;
                    _inFlight = null;
                    _inFlightBytes = 0;
                }
            }
            DisposeFrames(displaced);
            DisposeFrame(inFlight);
        }

        private Ros2BridgeInboundFrame[] DrainQueuedLocked()
        {
            if (_queued.Count == 0)
            {
                _queuedBytes = 0;
                return Array.Empty<Ros2BridgeInboundFrame>();
            }
            var result = new Ros2BridgeInboundFrame[_queued.Count];
            _queued.CopyTo(result, 0);
            _queued.Clear();
            _queuedBytes = 0;
            return result;
        }

        private void RemoveQueuedFrameLocked(
            Ros2BridgeInboundFrame frame)
        {
            var node = _queued.First;
            while (node != null)
            {
                if (ReferenceEquals(node.Value, frame))
                {
                    _queued.Remove(node);
                    ReduceUsageLocked(frame);
                    _queuedBytes = checked(
                        _queuedBytes - frame.PayloadLength);
                    return;
                }
                node = node.Next;
            }
            throw new InvalidOperationException(
                "The displaced inbound frame is no longer queued.");
        }

        private void ReduceUsageLocked(
            Ros2BridgeInboundFrame frame)
        {
            if (!_usage.TryGetValue(
                    frame.Contract.ContractId,
                    out var usage)
                || usage.Depth <= 0
                || usage.Bytes < frame.PayloadLength)
            {
                throw new InvalidOperationException(
                    "The inbound per-contract accounting is inconsistent.");
            }
            usage.Depth--;
            usage.Bytes -= frame.PayloadLength;
            if (usage.Depth == 0)
                _usage.Remove(frame.Contract.ContractId);
        }

        private void DisposeFrames(
            IEnumerable<Ros2BridgeInboundFrame> frames)
        {
            if (frames == null)
                return;
            foreach (var frame in frames)
                DisposeFrame(frame);
        }

        private void DisposeFrame(
            Ros2BridgeInboundFrame frame)
        {
            if (frame == null)
                return;
            try
            {
                frame.Dispose();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    Increment(ref _disposalFailures);
                    SetDiagnosticLocked(exception.Message);
                }
            }
        }

        private void IncrementEpochLocked()
        {
            if (_epoch == long.MaxValue)
            {
                throw new InvalidOperationException(
                    "The inbound queue generation is exhausted.");
            }
            _epoch++;
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(Ros2BridgeInboundQueue));
            }
        }

        private void SetDiagnosticLocked(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _lastDiagnostic = string.Empty;
                return;
            }
            var normalized = value.Trim();
            _lastDiagnostic =
                normalized.Length <= MaxDiagnosticChars
                    ? normalized
                    : normalized.Substring(
                        0,
                        MaxDiagnosticChars);
        }

        private static void Increment(ref long value)
        {
            if (value != long.MaxValue)
                value++;
        }

        private static void Add(
            ref long value,
            long amount)
        {
            if (amount <= 0 || value == long.MaxValue)
                return;
            value = amount > long.MaxValue - value
                ? long.MaxValue
                : value + amount;
        }

        private static void Add(
            ref long value,
            ulong amount)
        {
            if (amount == 0 || value == long.MaxValue)
                return;
            if (amount > long.MaxValue
                || (long)amount > long.MaxValue - value)
            {
                value = long.MaxValue;
                return;
            }
            value += (long)amount;
        }
    }
}
