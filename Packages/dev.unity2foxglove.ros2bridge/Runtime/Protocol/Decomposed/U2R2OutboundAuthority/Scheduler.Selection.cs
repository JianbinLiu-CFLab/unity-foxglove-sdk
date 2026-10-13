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
        private bool CanFitData(
            LinkedList<U2R2OutboundFrame> queue,
            ulong incomingBytes,
            ulong removingDepth,
            ulong removingBytes)
        {
            var queueDepth = checked((ulong)queue.Count);
            if (removingDepth > queueDepth)
                throw new InvalidOperationException("Data queue depth accounting underflow.");
            var contractDepth = queueDepth - removingDepth;
            var contractBytes = 0UL;
            foreach (var queued in queue)
                contractBytes = checked(contractBytes + queued.ByteCount);
            if (removingBytes > contractBytes
                || removingDepth > _dataQueuedDepth
                || removingBytes > _dataQueuedBytes)
            {
                throw new InvalidOperationException(
                    "Data queue byte accounting underflow.");
            }
            contractBytes -= removingBytes;

            var dataDepthLimit =
                _limits.MaxTotalQueueDepth - _limits.ReservedControlQueueDepth;
            var dataByteLimit =
                _limits.MaxQueuedBytes - _limits.ReservedControlQueueBytes;
            return contractDepth < _limits.MaxPerContractQueueDepth
                   && incomingBytes
                   <= _limits.MaxPerContractQueueBytes - contractBytes
                   && _dataQueuedDepth - removingDepth < dataDepthLimit
                   && incomingBytes
                   <= dataByteLimit - (_dataQueuedBytes - removingBytes);
        }

        private void AddData(
            U2R2ContractKey key,
            LinkedList<U2R2OutboundFrame> queue,
            U2R2OutboundFrame frame,
            bool activate = true)
        {
            queue.AddLast(frame);
            _dataQueuedDepth = checked(_dataQueuedDepth + 1);
            _dataQueuedBytes = checked(_dataQueuedBytes + frame.ByteCount);
            if (activate)
                Activate(key);
        }

        private void Activate(U2R2ContractKey key)
        {
            if (_active.Add(key))
                _roundRobin.Enqueue(key);
        }

        private void RemoveEmptyQueue(
            U2R2ContractKey key,
            LinkedList<U2R2OutboundFrame> queue)
        {
            if (queue.Count == 0)
                _data.Remove(key);
        }

        private bool ShouldChooseControl(
            out U2R2ContractKey? selectedDataContract)
        {
            selectedDataContract = null;
            if (_control.Count == 0)
            {
                if (_roundRobin.Count > 0)
                    selectedDataContract = _roundRobin.Peek();
                return false;
            }
            if (_roundRobin.Count == 0
                || _controlBurst < _limits.ControlBurstLimit)
            {
                return true;
            }

            foreach (var candidate in _roundRobin)
            {
                var blocked = false;
                foreach (var control in _control)
                {
                    if (control.FenceContract.HasValue
                        && control.FenceContract.Value == candidate)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked)
                {
                    selectedDataContract = candidate;
                    return false;
                }
            }

            return true;
        }

        private void RemoveActiveContract(U2R2ContractKey key)
        {
            if (!_active.Remove(key))
                throw new InvalidOperationException(
                    "The selected U2R2 contract is not active.");
            var retained = new Queue<U2R2ContractKey>();
            var removed = false;
            while (_roundRobin.Count > 0)
            {
                var candidate = _roundRobin.Dequeue();
                if (!removed && candidate == key)
                    removed = true;
                else
                    retained.Enqueue(candidate);
            }
            while (retained.Count > 0)
                _roundRobin.Enqueue(retained.Dequeue());
            if (!removed)
                throw new InvalidOperationException(
                    "The selected U2R2 contract is not scheduled.");
        }

        private void ReleaseTransient(ulong bytes)
        {
            lock (_gate)
            {
                if (_transientBytes < bytes)
                    throw new InvalidOperationException("Transient byte accounting underflow.");
                _transientBytes -= bytes;
            }
        }

        private void EndRead(ulong bytes)
        {
            lock (_gate)
            {
                if (!_readerActive || _inFlightBytes < bytes)
                    throw new InvalidOperationException("Reader accounting underflow.");
                _readerActive = false;
                _inFlightBytes -= bytes;
            }
        }

        private void EndWrite(ulong bytes)
        {
            lock (_gate)
            {
                if (!_writerActive || _inFlightBytes < bytes)
                    throw new InvalidOperationException("Writer accounting underflow.");
                var completedContract = _writerContract;
                var completedHasContract = _writerHasContract;
                _writerActive = false;
                _writerHasContract = false;
                _inFlightBytes -= bytes;
                if (completedHasContract)
                    TryForgetRetired(completedContract);
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                _closed = true;
                _control.Clear();
                _data.Clear();
                _roundRobin.Clear();
                _active.Clear();
                _revoked.Clear();
                _retireWhenDrained.Clear();
                _controlDepthUsed = 0;
                _controlBytesUsed = 0;
                _dataQueuedDepth = 0;
                _dataQueuedBytes = 0;
                _controlBurst = 0;
            }
        }

        private void TryForgetRetired(U2R2ContractKey key)
        {
            if (!_retireWhenDrained.Contains(key)
                || _data.ContainsKey(key)
                || _writerActive
                && _writerHasContract
                && _writerContract == key)
            {
                return;
            }
            _retireWhenDrained.Remove(key);
            _revoked.Remove(key);
        }

    }
}
