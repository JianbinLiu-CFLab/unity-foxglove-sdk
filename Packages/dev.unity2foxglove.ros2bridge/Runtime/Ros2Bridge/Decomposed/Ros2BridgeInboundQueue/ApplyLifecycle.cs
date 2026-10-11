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
    internal sealed partial class Ros2BridgeInboundQueue :
        IRos2BridgeInboundFrameReceiver,
        IDisposable
    {
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
                    if (current)
                    {
                        MarkSessionFailureLocked(
                            string.IsNullOrWhiteSpace(reason)
                                ? "The inbound apply lease ended without a successful outcome."
                                : reason);
                    }
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
                    _lastDiagnostic,
                    _resolutionRejections,
                    _sessionDeliveryFailure);
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
                _sequences.Clear();
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
                _sequences.Clear();
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
                    MarkSessionFailureLocked(exception.Message);
                }
            }
        }

        private void EnsureEpochAvailableLocked()
        {
            if (_epoch == long.MaxValue)
            {
                throw new InvalidOperationException(
                    "The inbound queue generation is exhausted.");
            }
        }

        private void IncrementEpochLocked()
        {
            EnsureEpochAvailableLocked();
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

        private void MarkSessionFailureLocked(string value)
        {
            _sessionDeliveryFailure = true;
            SetDiagnosticLocked(value);
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
