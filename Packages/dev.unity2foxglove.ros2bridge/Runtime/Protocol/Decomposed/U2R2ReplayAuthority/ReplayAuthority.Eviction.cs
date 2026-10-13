// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Bounded exact-request replay authority for U2R2 v2.


using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public sealed partial class U2R2RequestReplayAuthority
    {
        public void CancelPending(U2R2ReplayAdmission admission)
            => Cancel(admission, requireClaimed: false);

        internal void CancelClaimed(U2R2ReplayAdmission admission)
            => Cancel(admission, requireClaimed: true);

        internal bool TryAbandonClaimed(U2R2ReplayAdmission admission)
            => TryAbandon(admission, requireClaimed: true);

        internal bool TryAbandon(
            U2R2ReplayAdmission admission,
            bool requireClaimed)
        {
            if (admission == null)
                return false;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.IsSettled)
                {
                    return false;
                }
                if (admission.Decision != U2R2ReplayDecision.BeginMutation
                    || !_entries.TryGetValue(admission.RequestId, out var entry)
                    || entry.IsCompleted
                    || entry.IsClaimed != requireClaimed)
                {
                    return false;
                }
                entry.Reservation.Dispose();
                _entries.Remove(admission.RequestId);
                _replayBytes -= checked(
                    (ulong)entry.Request.LongLength
                    + entry.ReservedResponseBytes);
                _outstandingRequests--;
                admission.IsSettled = true;
                return true;
            }
        }

        private void Cancel(
            U2R2ReplayAdmission admission,
            bool requireClaimed)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this))
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay admission belongs to another authority.");
                }
                if (admission.IsSettled)
                    return;
                if (admission.Decision != U2R2ReplayDecision.BeginMutation
                    || !_entries.TryGetValue(admission.RequestId, out var entry)
                    || entry.IsCompleted
                    || entry.IsClaimed != requireClaimed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay admission is not pending.");
                }
                entry.Reservation.Dispose();
                _entries.Remove(admission.RequestId);
                _replayBytes -= checked(
                    (ulong)entry.Request.LongLength
                    + entry.ReservedResponseBytes);
                _outstandingRequests--;
                admission.IsSettled = true;
            }
        }

        private void Finish(
            U2R2ReplayAdmission admission,
            U2R2OutboundFrame exactResponse,
            bool priorityFence,
            bool requireClaimed,
            U2R2ContractKey? fenceContract = null)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (exactResponse == null || !exactResponse.IsControl)
                throw new ArgumentNullException(nameof(exactResponse));

            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.Decision != U2R2ReplayDecision.BeginMutation
                    || admission.IsSettled)
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay admission is not pending.");
                }
                if (!_entries.TryGetValue(admission.RequestId, out var entry)
                    || entry.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay entry is not pending.");
                }
                if (entry.IsClaimed != requireClaimed)
                {
                    throw new InvalidOperationException(
                        requireClaimed
                            ? "The U2R2 replay entry is not claimed by a contract."
                            : "The U2R2 replay entry is owned by a contract transaction.");
                }
                var responseBytes = exactResponse.ByteCount;
                if (responseBytes > entry.ReservedResponseBytes)
                    ThrowCapacity("The exact response exceeds its pre-mutation reservation.");

                if (priorityFence)
                {
                    if (!fenceContract.HasValue)
                    {
                        throw new InvalidOperationException(
                            "A fenced U2R2 response requires a contract.");
                    }
                    entry.Reservation.CommitFenced(
                        exactResponse,
                        fenceContract.Value);
                }
                else
                    entry.Reservation.Commit(exactResponse);
                entry.Response = exactResponse.Bytes.ToArray();
                entry.IsCompleted = true;
                _replayBytes -= entry.ReservedResponseBytes - responseBytes;
                _outstandingRequests--;
                _completedOrder.AddLast(entry.RequestId);
                admission.IsSettled = true;
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                foreach (var entry in _entries.Values)
                {
                    if (!entry.IsCompleted)
                        entry.Reservation.Dispose();
                }
                _entries.Clear();
                _completedOrder.Clear();
                _outstandingRequests = 0;
                _replayBytes = 0;
                _closed = true;
            }
        }

        private List<ulong> SelectEvictions(ulong requestedBytes)
        {
            var count = checked((ulong)_entries.Count);
            var bytes = _replayBytes;
            var selected = new List<ulong>();
            var cursor = _completedOrder.First;
            while (count >= _limits.MaxReplayEntries
                   || requestedBytes > _limits.MaxReplayBytes - bytes)
            {
                if (cursor == null)
                    return null;
                var entry = _entries[cursor.Value];
                selected.Add(cursor.Value);
                count--;
                bytes -= checked(
                    (ulong)entry.Request.LongLength
                    + (ulong)entry.Response.LongLength);
                cursor = cursor.Next;
            }
            return selected;
        }

        private void Evict(ulong requestId)
        {
            var entry = _entries[requestId];
            _entries.Remove(requestId);
            _completedOrder.Remove(requestId);
            _replayBytes -= checked(
                (ulong)entry.Request.LongLength
                + (ulong)entry.Response.LongLength);
        }

        private static void ThrowCapacity(string message)
            => throw new U2R2ProtocolException(
                "capacity_exceeded",
                message,
                terminal: false);
    }
}
