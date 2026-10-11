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
        internal Ros2BridgeOutboundEnqueueDisposition Enqueue(
            Ros2BridgeFrame frame,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation = false,
            long enqueueConnectionGeneration = 0)
        {
            ValidateOverflowPolicy(overflowPolicy);
            var disposition = PrepareEnqueue(
                frame,
                out var prepared);
            if (disposition != Ros2BridgeOutboundEnqueueDisposition.Accepted)
                return disposition;
            using (prepared)
            {
                return CommitPrepared(
                    prepared,
                    overflowPolicy,
                    requiresPreparation,
                    enqueueConnectionGeneration);
            }
        }

        internal Ros2BridgeOutboundEnqueueDisposition PrepareEnqueue(
            Ros2BridgeFrame frame,
            out Ros2BridgePreparedOutboundEnqueue prepared)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            prepared = null;
            var identity = new ContractIdentity(frame);

            lock (_gate)
            {
                var existingContract = FindContract(identity);
                if (_terminalFault != null)
                {
                    IncrementFaulted(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                }
                if (_closed)
                {
                    IncrementRejectedAfterStop(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop;
                }
                if (existingContract == null
                    && (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                        || _lastContractId == ulong.MaxValue))
                {
                    IncrementBackpressureRejected(null);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
            }

            Ros2BridgeFrameMeasurement measurement;
            try
            {
                measurement = Ros2BridgeFrameWriter.Measure(frame);
                _ = U2R2FrameSize.Create(
                    _limits,
                    checked((ulong)measurement.HeaderBytes),
                    checked((ulong)measurement.PayloadBytes));
            }
            catch (ArgumentException)
            {
                lock (_gate)
                {
                    IncrementOversize(FindContract(identity));
                }
                return Ros2BridgeOutboundEnqueueDisposition.Oversize;
            }
            catch (U2R2ProtocolException error)
                when (string.Equals(
                    error.ErrorCode,
                    "capacity_exceeded",
                    StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    IncrementOversize(FindContract(identity));
                }
                return Ros2BridgeOutboundEnqueueDisposition.Oversize;
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    var existingContract = FindContract(identity);
                    FaultCore(error);
                    IncrementContractFaulted(existingContract);
                }
                return Ros2BridgeOutboundEnqueueDisposition.Faulted;
            }

            U2R2ByteLease transientLease;
            lock (_gate)
            {
                var existingContract = FindContract(identity);
                if (_terminalFault != null)
                {
                    IncrementFaulted(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                }
                if (_closed)
                {
                    IncrementRejectedAfterStop(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop;
                }
                if (existingContract == null
                    && (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                        || _lastContractId == ulong.MaxValue))
                {
                    IncrementBackpressureRejected(null);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
                if (!_inner.TryReserveTransient(
                        checked((ulong)measurement.TotalWireBytes),
                        out transientLease))
                {
                    IncrementBackpressureRejected(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
            }

            try
            {
                var encodedWire = Ros2BridgeFrameWriter.EncodeOwned(
                    frame,
                    measurement);
                prepared = new Ros2BridgePreparedOutboundEnqueue(
                    this,
                    frame,
                    measurement,
                    encodedWire,
                    transientLease);
                return Ros2BridgeOutboundEnqueueDisposition.Accepted;
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    var existingContract = FindContract(identity);
                    if (!TryReleaseTransient(
                            transientLease,
                            existingContract))
                    {
                        return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                    FaultCore(error);
                    IncrementContractFaulted(existingContract);
                }
                return Ros2BridgeOutboundEnqueueDisposition.Faulted;
            }
        }

        internal Ros2BridgeOutboundEnqueueDisposition CommitPrepared(
            Ros2BridgePreparedOutboundEnqueue prepared,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation = false,
            long enqueueConnectionGeneration = 0)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            ValidateOverflowPolicy(overflowPolicy);

            lock (_gate)
            {
                var transientLease = prepared.TakeForCommit(this);
                var identity = new ContractIdentity(prepared.Frame);
                var existingContract = FindContract(identity);
                ContractState contract = null;
                var disposition =
                    Ros2BridgeOutboundEnqueueDisposition.Faulted;
                try
                {
                    if (_terminalFault != null)
                    {
                        IncrementFaulted(existingContract);
                        disposition =
                            Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                    else if (_closed)
                    {
                        IncrementRejectedAfterStop(existingContract);
                        disposition = Ros2BridgeOutboundEnqueueDisposition
                            .RejectedAfterStop;
                    }
                    else if (!TryGetOrCreateContract(
                                 identity,
                                 out contract,
                                 out var contractCreated))
                    {
                        IncrementBackpressureRejected(null);
                        disposition = Ros2BridgeOutboundEnqueueDisposition
                            .BackpressureRejected;
                    }
                    else
                    {
                        var admitted = false;
                        try
                        {
                            disposition = EnqueuePreparedCore(
                                contract,
                                prepared,
                                overflowPolicy,
                                requiresPreparation,
                                enqueueConnectionGeneration);
                            admitted = disposition
                                       != Ros2BridgeOutboundEnqueueDisposition
                                           .BackpressureRejected
                                       && disposition
                                       != Ros2BridgeOutboundEnqueueDisposition
                                           .Faulted;
                        }
                        catch (Exception error)
                        {
                            FaultCore(error);
                            IncrementContractFaulted(contract);
                            disposition = Ros2BridgeOutboundEnqueueDisposition
                                .Faulted;
                        }
                        if (!admitted)
                        {
                            RemoveProvisionalContract(
                                identity,
                                contract,
                                contractCreated);
                        }
                    }
                }
                finally
                {
                    if (!TryReleaseTransient(transientLease, contract))
                    {
                        disposition =
                            Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                }
                return disposition;
            }
        }

        internal void CancelPrepared(U2R2ByteLease transientLease)
        {
            if (transientLease == null)
                return;
            lock (_gate)
            {
                _ = TryReleaseTransient(transientLease, null);
            }
        }

        internal bool TryReserveControl(
            ulong bytes,
            out U2R2ControlReservation reservation)
        {
            lock (_gate)
            {
                reservation = null;
                return !_closed
                       && _terminalFault == null
                       && _inner.TryReserveControl(
                           bytes,
                           out reservation);
            }
        }

        internal bool TryReserveTransient(
            Ros2BridgeOutboundWriteLease write,
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                return write != null
                       && write.IsActiveFor(this)
                       && !_closed
                       && _terminalFault == null
                       && _inner.TryReserveTransient(bytes, out lease);
            }
        }

        internal bool TryReserveSessionTransient(
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                return !_closed
                       && _terminalFault == null
                       && _inner.TryReserveTransient(bytes, out lease);
            }
        }

        internal bool TryBeginWrite(
            out Ros2BridgeOutboundWriteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (!_inner.TryBeginWrite(out var innerLease))
                    return false;

                QueueEntry entry = null;
                if (!innerLease.Frame.IsControl)
                {
                    if (!_queuedByToken.TryGetValue(
                            innerLease.Frame.Token,
                            out entry)
                        || entry.Contract.Key
                        != innerLease.Frame.Contract
                        || entry.Contract.Queue.First == null
                        || !ReferenceEquals(
                            entry,
                            entry.Contract.Queue.First.Value))
                    {
                        ReleaseBrokenInnerLease(
                            innerLease,
                            new InvalidOperationException(
                                "The ROS 2 Bridge outbound queue mirror diverged."));
                        return false;
                    }

                    entry.Contract.Queue.RemoveFirst();
                    _queuedByToken.Remove(entry.Token);
                }

                lease = new Ros2BridgeOutboundWriteLease(
                    this,
                    innerLease,
                    entry);
                return true;
            }
        }

        internal bool TryGetContractCounters(
            Ros2BridgeFrame identityFrame,
            out Ros2BridgeOutboundCounters counters)
        {
            if (identityFrame == null)
                throw new ArgumentNullException(nameof(identityFrame));
            lock (_gate)
            {
                var contract = FindContract(
                    new ContractIdentity(identityFrame));
                if (contract == null)
                {
                    counters = default;
                    return false;
                }
                counters = contract.Counters.Snapshot();
                return true;
            }
        }

        internal void Fault(Exception error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            lock (_gate)
                FaultCore(error);
        }

        internal Ros2BridgeOutboundCloseResult Close()
        {
            lock (_gate)
                return CloseCore();
        }

        internal void RecordDisposalFailure(
            Ros2BridgeFrame frame = null)
        {
            lock (_gate)
            {
                IncrementDisposalFailure(
                    frame == null
                        ? null
                        : FindContract(new ContractIdentity(frame)));
            }
        }

        public void Dispose()
        {
            Close();
        }

        internal void SettleWrite(
            QueueEntry entry,
            U2R2WriteLease inner,
            bool sent,
            Exception error)
        {
            lock (_gate)
            {
                _ = error;
                if (sent)
                {
                    if (entry != null)
                    {
                        Increment(
                            ref _counters.Sent,
                            ref entry.Contract.Counters.Sent);
                    }
                }
                else
                {
                    IncrementFaulted(entry?.Contract);
                }

                DisposeInnerLease(entry, inner);
            }
        }

        internal void ReleaseUnsettled(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            lock (_gate)
            {
                IncrementFaulted(entry?.Contract);
                DisposeInnerLease(entry, inner);
            }
        }

        internal void DropWrite(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            lock (_gate)
            {
                Increment(
                    ref _counters.Dropped,
                    ref entry.Contract.Counters.Dropped);
                DisposeInnerLease(entry, inner);
            }
        }

    }
}
