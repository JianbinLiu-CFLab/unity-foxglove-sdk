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
        public void RevokeContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (!_revoked.Contains(key))
                {
                    var limit = checked(
                        _limits.MaxContracts + _limits.MaxTombstones);
                    if (checked((ulong)_revoked.Count) >= limit)
                    {
                        throw new InvalidOperationException(
                            "The U2R2 revoked-contract lifecycle exceeded its bound.");
                    }
                    _revoked.Add(key);
                }
                if (_data.TryGetValue(key, out var queue))
                {
                    foreach (var frame in queue)
                    {
                        _dataQueuedDepth--;
                        _dataQueuedBytes -= frame.ByteCount;
                    }
                    _data.Remove(key);
                }
                if (_active.Remove(key))
                {
                    var retained = new Queue<U2R2ContractKey>();
                    while (_roundRobin.Count > 0)
                    {
                        var candidate = _roundRobin.Dequeue();
                        if (candidate != key)
                            retained.Enqueue(candidate);
                    }
                    while (retained.Count > 0)
                        _roundRobin.Enqueue(retained.Dequeue());
                }
            }
        }

        internal void RetireContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                RevokeContract(key);
                _retireWhenDrained.Add(key);
                TryForgetRetired(key);
            }
        }

        internal void ForgetContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (_data.ContainsKey(key)
                    || _writerActive
                    && _writerHasContract
                    && _writerContract == key)
                {
                    _retireWhenDrained.Add(key);
                    return;
                }
                _revoked.Remove(key);
                _retireWhenDrained.Remove(key);
            }
        }

        public bool IsContractRevokedAndDrained(U2R2ContractKey key)
        {
            lock (_gate)
            {
                return _revoked.Contains(key)
                       && !_data.ContainsKey(key)
                       && !(_writerActive
                            && _writerHasContract
                            && _writerContract == key);
            }
        }

        internal void ActivateContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler is closed.");
                }
                _revoked.Remove(key);
                _retireWhenDrained.Remove(key);
            }
        }

        internal void CommitControl(
            U2R2ControlReservation reservation,
            U2R2OutboundFrame frame,
            ulong reservedBytes,
            U2R2ContractKey? fenceContract)
        {
            if (reservation == null)
                throw new ArgumentNullException(nameof(reservation));
            if (frame == null || !frame.IsControl)
                throw new ArgumentException("A control reservation requires a control frame.");
            if (frame.ByteCount > reservedBytes)
            {
                throw new U2R2ProtocolException(
                    "capacity_exceeded",
                    "The U2R2 control response exceeds its reservation.",
                    terminal: false);
            }
            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler is closed.");
                }
                _controlBytesUsed -= reservedBytes - frame.ByteCount;
                var entry = new ControlEntry(frame, fenceContract);
                if (fenceContract.HasValue)
                {
                    var insertion = _control.First;
                    while (insertion != null && insertion.Value.PriorityFence)
                        insertion = insertion.Next;
                    if (insertion == null)
                        _control.AddLast(entry);
                    else
                        _control.AddBefore(insertion, entry);
                }
                else
                    _control.AddLast(entry);
            }
        }

        internal void CancelControl(ulong reservedBytes)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (_controlDepthUsed == 0 || _controlBytesUsed < reservedBytes)
                {
                    throw new InvalidOperationException(
                        "The U2R2 control reservation is not active.");
                }
                _controlDepthUsed--;
                _controlBytesUsed -= reservedBytes;
            }
        }
    }
}
