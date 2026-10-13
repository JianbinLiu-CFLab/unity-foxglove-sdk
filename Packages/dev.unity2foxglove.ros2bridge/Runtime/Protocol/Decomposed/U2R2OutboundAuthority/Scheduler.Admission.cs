// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Pure bounded U2R2 outbound scheduling and byte accounting.


using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public sealed partial class U2R2BoundedOutboundScheduler
    {
        public bool TryReserveControl(
            ulong bytes,
            out U2R2ControlReservation reservation)
        {
            lock (_gate)
            {
                reservation = null;
                if (_closed
                    || _controlDepthUsed >= _limits.ReservedControlQueueDepth
                    || _controlBytesUsed > _limits.ReservedControlQueueBytes
                    || bytes > _limits.ReservedControlQueueBytes - _controlBytesUsed)
                {
                    return false;
                }
                _controlDepthUsed++;
                _controlBytesUsed += bytes;
                reservation = new U2R2ControlReservation(this, bytes);
                return true;
            }
        }

        public U2R2EnqueueDisposition EnqueueData(
            U2R2OutboundFrame frame,
            U2R2QueueOverflowPolicy policy)
        {
            if (policy != U2R2QueueOverflowPolicy.Reject
                && policy != U2R2QueueOverflowPolicy.DropOldest
                && policy != U2R2QueueOverflowPolicy.ReplaceLatest)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "The U2R2 queue overflow policy is invalid.",
                    terminal: false);
            }
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.IsControl)
                throw new ArgumentException("A data queue requires a data frame.", nameof(frame));

            lock (_gate)
            {
                if (_closed)
                    return U2R2EnqueueDisposition.Rejected;
                if (_revoked.Contains(frame.Contract))
                    return U2R2EnqueueDisposition.Rejected;

                if (!_data.TryGetValue(frame.Contract, out var queue))
                {
                    queue = new LinkedList<U2R2OutboundFrame>();
                    _data.Add(frame.Contract, queue);
                }

                if (CanFitData(
                        queue,
                        frame.ByteCount,
                        removingDepth: 0,
                        removingBytes: 0))
                {
                    AddData(frame.Contract, queue, frame);
                    return U2R2EnqueueDisposition.Accepted;
                }
                if (policy == U2R2QueueOverflowPolicy.Reject || queue.Count == 0)
                {
                    RemoveEmptyQueue(frame.Contract, queue);
                    return U2R2EnqueueDisposition.Rejected;
                }

                var victim = policy == U2R2QueueOverflowPolicy.DropOldest
                    ? queue.First
                    : queue.Last;
                if (!CanFitData(
                        queue,
                        frame.ByteCount,
                        removingDepth: 1,
                        removingBytes: victim.Value.ByteCount))
                    return U2R2EnqueueDisposition.Rejected;

                queue.Remove(victim);
                _dataQueuedDepth--;
                _dataQueuedBytes -= victim.Value.ByteCount;
                AddData(frame.Contract, queue, frame, activate: false);
                return policy == U2R2QueueOverflowPolicy.DropOldest
                    ? U2R2EnqueueDisposition.DroppedOldest
                    : U2R2EnqueueDisposition.ReplacedLatest;
            }
        }

        public bool TryReserveTransient(
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed
                    || _transientBytes > _limits.MaxTransientBytes
                    || bytes > _limits.MaxTransientBytes - _transientBytes)
                    return false;
                _transientBytes += bytes;
                lease = new U2R2ByteLease(() => ReleaseTransient(bytes));
                return true;
            }
        }

        public bool TryBeginRead(ulong bytes, out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed
                    || _readerActive
                    || _inFlightBytes > _limits.MaxInFlightBytes
                    || bytes > _limits.MaxInFlightBytes - _inFlightBytes)
                {
                    return false;
                }
                _readerActive = true;
                _inFlightBytes += bytes;
                lease = new U2R2ByteLease(() => EndRead(bytes));
                return true;
            }
        }

        public bool TryBeginWrite(out U2R2WriteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed || _writerActive)
                    return false;

                var chooseControl = ShouldChooseControl(
                    out var selectedDataContract);
                U2R2OutboundFrame frame;
                if (chooseControl)
                {
                    frame = _control.First.Value.Frame;
                }
                else if (selectedDataContract.HasValue)
                {
                    var key = selectedDataContract.Value;
                    frame = _data[key].First.Value;
                }
                else if (_control.Count > 0)
                {
                    chooseControl = true;
                    frame = _control.First.Value.Frame;
                }
                else
                {
                    return false;
                }

                if (_inFlightBytes > _limits.MaxInFlightBytes
                    || frame.ByteCount > _limits.MaxInFlightBytes - _inFlightBytes)
                    return false;

                if (chooseControl)
                {
                    _control.RemoveFirst();
                    _controlDepthUsed--;
                    _controlBytesUsed -= frame.ByteCount;
                    _controlBurst++;
                    _writerHasContract = false;
                }
                else
                {
                    var key = selectedDataContract.Value;
                    RemoveActiveContract(key);
                    var queue = _data[key];
                    queue.RemoveFirst();
                    _dataQueuedDepth--;
                    _dataQueuedBytes -= frame.ByteCount;
                    if (queue.Count == 0)
                        _data.Remove(key);
                    else
                        Activate(key);
                    _controlBurst = 0;
                    _writerContract = key;
                    _writerHasContract = true;
                }

                _writerActive = true;
                _inFlightBytes += frame.ByteCount;
                lease = new U2R2WriteLease(
                    frame,
                    () => EndWrite(frame.ByteCount));
                return true;
            }
        }
    }
}
