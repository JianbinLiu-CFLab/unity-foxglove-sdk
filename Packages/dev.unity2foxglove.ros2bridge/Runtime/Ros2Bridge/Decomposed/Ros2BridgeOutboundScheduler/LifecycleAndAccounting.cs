// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bridge-local composition of the bounded U2R2 outbound authority.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeOutboundScheduler
    {
        private Ros2BridgeOutboundEnqueueDisposition EnqueuePreparedCore(
            ContractState contract,
            Ros2BridgePreparedOutboundEnqueue prepared,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation,
            long enqueueConnectionGeneration)
        {
            var frame = prepared.Frame;
            var measurement = prepared.Measurement;
            var token = NextToken();
            var sequence = NextSequence(contract);
            var outbound = U2R2OutboundFrame.DataOwned(
                token,
                contract.Key,
                sequence,
                prepared.EncodedWire);
            if (!MemoryMarshal.TryGetArray(
                    outbound.Bytes,
                    out ArraySegment<byte> ownedWire)
                || ownedWire.Array == null
                || ownedWire.Count != measurement.TotalWireBytes)
            {
                throw new InvalidOperationException(
                    "The U2R2 outbound authority did not retain an array-backed owned frame.");
            }
            var ownedFrame = Ros2BridgeFrame.CreateWireOwnedView(
                frame,
                ownedWire.Array,
                checked(
                    ownedWire.Offset
                    + 16
                    + measurement.HeaderBytes),
                measurement.PayloadBytes);
            var innerDisposition = _inner.EnqueueData(
                outbound,
                overflowPolicy);
            if (innerDisposition == U2R2EnqueueDisposition.Rejected)
            {
                IncrementBackpressureRejected(contract);
                return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
            }

            var entry = new QueueEntry(
                token,
                contract,
                ownedFrame,
                outbound.ByteCount,
                requiresPreparation,
                enqueueConnectionGeneration);
            switch (innerDisposition)
            {
                case U2R2EnqueueDisposition.Accepted:
                    break;
                case U2R2EnqueueDisposition.DroppedOldest:
                    RemoveVictim(
                        contract,
                        contract.Queue.First,
                        replaced: false);
                    break;
                case U2R2EnqueueDisposition.ReplacedLatest:
                    RemoveVictim(
                        contract,
                        contract.Queue.Last,
                        replaced: true);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler returned an unknown disposition.");
            }

            contract.Queue.AddLast(entry);
            _queuedByToken.Add(token, entry);
            Increment(
                ref _counters.Accepted,
                ref contract.Counters.Accepted);
            switch (innerDisposition)
            {
                case U2R2EnqueueDisposition.DroppedOldest:
                    return Ros2BridgeOutboundEnqueueDisposition.DroppedOldest;
                case U2R2EnqueueDisposition.ReplacedLatest:
                    return Ros2BridgeOutboundEnqueueDisposition.ReplacedLatest;
                default:
                    return Ros2BridgeOutboundEnqueueDisposition.Accepted;
            }
        }

        private bool TryReleaseTransient(
            U2R2ByteLease transientLease,
            ContractState contract)
        {
            try
            {
                transientLease.Dispose();
                return true;
            }
            catch (Exception error)
            {
                IncrementDisposalFailure(contract);
                FaultCore(error);
                IncrementContractFaulted(contract);
                return false;
            }
        }

        private bool TryGetOrCreateContract(
            ContractIdentity identity,
            out ContractState contract,
            out bool created)
        {
            if (_contracts.TryGetValue(identity, out contract))
            {
                created = false;
                return true;
            }
            if (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                || _lastContractId == ulong.MaxValue)
            {
                contract = null;
                created = false;
                return false;
            }

            _lastContractId++;
            contract = new ContractState(
                identity,
                new U2R2ContractKey(
                    _lastContractId,
                    _sessionGeneration));
            _contracts.Add(identity, contract);
            created = true;
            return true;
        }

        private void RemoveProvisionalContract(
            ContractIdentity identity,
            ContractState contract,
            bool created)
        {
            if (!created)
                return;
            if (contract == null
                || contract.Queue.Count != 0
                || !_contracts.TryGetValue(identity, out var current)
                || !ReferenceEquals(current, contract))
            {
                throw new InvalidOperationException(
                    "A provisional ROS 2 Bridge contract could not be rolled back safely.");
            }
            _contracts.Remove(identity);
        }

        private ContractState FindContract(ContractIdentity identity)
            => _contracts.TryGetValue(identity, out var contract)
                ? contract
                : null;

        private string NextToken()
        {
            if (_lastToken == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound token counter is exhausted.");
            }
            _lastToken++;
            return "bridge-data:"
                   + _lastToken.ToString(CultureInfo.InvariantCulture);
        }

        private static ulong NextSequence(ContractState contract)
        {
            if (contract.Sequence == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge contract sequence is exhausted.");
            }
            contract.Sequence++;
            return contract.Sequence;
        }

        private void RemoveVictim(
            ContractState contract,
            LinkedListNode<QueueEntry> victim,
            bool replaced)
        {
            if (victim == null)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound scheduler displaced a missing frame.");
            }
            contract.Queue.Remove(victim);
            if (!_queuedByToken.Remove(victim.Value.Token))
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound queue mirror lost a displaced frame.");
            }
            if (replaced)
            {
                Increment(
                    ref _counters.Replaced,
                    ref contract.Counters.Replaced);
            }
            else
            {
                Increment(
                    ref _counters.Dropped,
                    ref contract.Counters.Dropped);
            }
        }

        private Ros2BridgeOutboundCloseResult CloseCore()
        {
            if (_closed)
                return default;

            ulong depth = 0;
            ulong bytes = 0;
            foreach (var entry in _queuedByToken.Values)
            {
                depth = checked(depth + 1);
                bytes = checked(bytes + entry.WireBytes);
                Increment(
                    ref _counters.Dropped,
                    ref entry.Contract.Counters.Dropped);
            }
            foreach (var contract in _contracts.Values)
                contract.Queue.Clear();
            _queuedByToken.Clear();
            _inner.Close();
            _closed = true;
            _lastCloseResult = new Ros2BridgeOutboundCloseResult(
                depth,
                bytes);
            return _lastCloseResult;
        }

        private void FaultCore(Exception error)
        {
            if (_terminalFault != null || _closed)
                return;
            _terminalFault = error;
            checked
            {
                _counters.Faulted++;
            }
            CloseCore();
        }

        private void DisposeInnerLease(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            try
            {
                inner.Dispose();
            }
            catch (Exception error)
            {
                IncrementDisposalFailure(entry?.Contract);
                FaultCore(error);
                IncrementContractFaulted(entry?.Contract);
                throw;
            }
        }

        private void ReleaseBrokenInnerLease(
            U2R2WriteLease inner,
            Exception error)
        {
            try
            {
                inner.Dispose();
            }
            catch (Exception disposeError)
            {
                IncrementDisposalFailure(null);
                FaultCore(disposeError);
                return;
            }
            FaultCore(error);
        }

        private void IncrementOversize(ContractState contract)
        {
            checked
            {
                _counters.Oversize++;
                if (contract != null)
                    contract.Counters.Oversize++;
            }
        }

        private void IncrementBackpressureRejected(
            ContractState contract)
        {
            checked
            {
                _counters.BackpressureRejected++;
                if (contract != null)
                    contract.Counters.BackpressureRejected++;
            }
        }

        private void IncrementRejectedAfterStop(
            ContractState contract)
        {
            checked
            {
                _counters.RejectedAfterStop++;
                if (contract != null)
                    contract.Counters.RejectedAfterStop++;
            }
        }

        private void IncrementFaulted(ContractState contract)
        {
            checked
            {
                _counters.Faulted++;
                if (contract != null)
                    contract.Counters.Faulted++;
            }
        }

        private static void IncrementContractFaulted(
            ContractState contract)
        {
            if (contract == null)
                return;
            checked
            {
                contract.Counters.Faulted++;
            }
        }

        private void IncrementDisposalFailure(
            ContractState contract)
        {
            checked
            {
                _counters.DisposalFailures++;
                if (contract != null)
                    contract.Counters.DisposalFailures++;
            }
        }

        private static void Increment(
            ref ulong aggregate,
            ref ulong contract)
        {
            checked
            {
                aggregate++;
                contract++;
            }
        }

        private static void ValidateOverflowPolicy(
            U2R2QueueOverflowPolicy overflowPolicy)
        {
            if (overflowPolicy != U2R2QueueOverflowPolicy.Reject
                && overflowPolicy != U2R2QueueOverflowPolicy.DropOldest
                && overflowPolicy
                != U2R2QueueOverflowPolicy.ReplaceLatest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(overflowPolicy));
            }
        }
    }
}
