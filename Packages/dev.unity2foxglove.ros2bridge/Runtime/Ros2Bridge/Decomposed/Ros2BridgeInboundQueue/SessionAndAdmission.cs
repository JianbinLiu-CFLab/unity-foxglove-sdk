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
            }

            Ros2BridgeInboundFrame[] displaced;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                EnsureEpochAvailableLocked();
                displaced = DrainQueuedLocked();
                _active.Clear();
                _sequences.Clear();
                foreach (var contract in contracts.Contracts)
                {
                    _active.Add(contract.ContractId, contract);
                    _sequences.Add(
                        contract.ContractId,
                        new U2R2ContractSequence());
                }
                _usage.Clear();
                _sessionId = sessionId.Trim();
                _connectionGeneration = connectionGeneration;
                _running = true;
                IncrementEpochLocked();
                _sessionDeliveryFailure = false;
                _lastDiagnostic = string.Empty;
            }
            DisposeFrames(displaced);
        }

        public void RecordResolutionRejection(string reason)
        {
            lock (_gate)
            {
                Increment(ref _received);
                Increment(ref _resolutionRejections);
                SetDiagnosticLocked(
                    string.IsNullOrWhiteSpace(reason)
                        ? "The inbound Bridge message was rejected before queue admission."
                        : reason);
            }
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
                _sequences.Add(
                    contract.ContractId,
                    new U2R2ContractSequence());
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
                _sequences.Remove(contract.ContractId);
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
                        MarkSessionFailureLocked(
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
                        MarkSessionFailureLocked(
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
                        MarkSessionFailureLocked(
                            "The inbound frame belongs to a stale session generation.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (!_active.TryGetValue(
                                 frame.Contract.ContractId,
                                 out var expected))
                    {
                        MarkSessionFailureLocked(
                            "The inbound frame references an unknown contract.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (!expected.Equals(frame.Contract))
                    {
                        MarkSessionFailureLocked(
                            "The inbound frame conflicts with its frozen contract.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else if (!_sequences.TryGetValue(
                                 expected.ContractId,
                                 out var sequence))
                    {
                        MarkSessionFailureLocked(
                            "The inbound contract sequence authority is missing.");
                        result = Ros2BridgeSessionResult.Fault(
                            _lastDiagnostic);
                    }
                    else
                    {
                        var lastAccepted = sequence.LastAccepted;
                        try
                        {
                            sequence.Admit(frame.Sequence);
                            result = AdmitLocked(
                                frame,
                                expected,
                                out displaced);
                        }
                        catch (U2R2ProtocolException exception)
                        {
                            if (lastAccepted != ulong.MaxValue
                                && frame.Sequence > lastAccepted + 1)
                            {
                                Add(
                                    ref _sequenceGaps,
                                    frame.Sequence - lastAccepted - 1);
                            }
                            else
                            {
                                Increment(ref _staleSequences);
                            }
                            MarkSessionFailureLocked(
                                exception.ErrorCode + ": " + exception.Message);
                            result = Ros2BridgeSessionResult.Reject(
                                _lastDiagnostic);
                        }
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
            int projectedDepth;
            long projectedBytes;
            try
            {
                projectedDepth = checked(usage.Depth + 1);
                projectedBytes = checked(
                    usage.Bytes + frame.PayloadLength);
            }
            catch (OverflowException)
            {
                return RejectForCapacityLocked(out displaced);
            }
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
                    try
                    {
                        removedBytes = checked(
                            removedBytes
                            + node.Value.PayloadLength);
                    }
                    catch (OverflowException)
                    {
                        return RejectForCapacityLocked(out displaced);
                    }
                }
                node = node.Next;
            }

            int finalDepth;
            long finalUsageBytes;
            long finalQueuedBytes;
            try
            {
                finalDepth = checked(projectedDepth - removedDepth);
                finalUsageBytes = checked(
                    projectedBytes - removedBytes);
                finalQueuedBytes = checked(
                    _queuedBytes
                    - removedBytes
                    + frame.PayloadLength);
            }
            catch (OverflowException)
            {
                return RejectForCapacityLocked(out displaced);
            }

            if (finalDepth > _limits.MaxPerContractDepth
                || finalUsageBytes > _limits.MaxPerContractBytes
                || checked(finalQueuedBytes + _inFlightBytes)
                    > _limits.MaxTotalBytes)
            {
                return RejectForCapacityLocked(out displaced);
            }

            if (displaced != null)
            {
                foreach (var removed in displaced)
                    RemoveQueuedFrameLocked(removed);
                Add(ref _replaced, displaced.Count);
            }
            _queued.AddLast(frame);
            usage.Depth = finalDepth;
            usage.Bytes = finalUsageBytes;
            _usage[contract.ContractId] = usage;
            _queuedBytes = finalQueuedBytes;
            Increment(ref _accepted);
            return Ros2BridgeSessionResult.Accepted();
        }

        private Ros2BridgeSessionResult RejectForCapacityLocked(
            out List<Ros2BridgeInboundFrame> displaced)
        {
            displaced = null;
            Increment(ref _dropped);
            MarkSessionFailureLocked(
                "The inbound queue has no capacity for this contract.");
            return Ros2BridgeSessionResult.Reject(
                _lastDiagnostic);
        }

    }
}
