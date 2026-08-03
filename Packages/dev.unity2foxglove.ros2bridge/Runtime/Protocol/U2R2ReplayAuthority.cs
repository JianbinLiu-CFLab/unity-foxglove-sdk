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
    public enum U2R2ReplayDecision
    {
        BeginMutation = 1,
        ReplayCached = 2,
    }

    public sealed class U2R2ReplayAdmission : IDisposable
    {
        private readonly byte[] _cachedResponse;

        internal U2R2ReplayAdmission(
            U2R2RequestReplayAuthority owner,
            ulong requestId,
            U2R2ReplayDecision decision,
            byte[] cachedResponse,
            U2R2ControlReservation responseReservation)
        {
            Owner = owner;
            RequestId = requestId;
            Decision = decision;
            _cachedResponse = cachedResponse == null
                ? Array.Empty<byte>()
                : (byte[])cachedResponse.Clone();
            ResponseReservation = responseReservation;
        }

        public U2R2ReplayDecision Decision { get; }
        public ulong RequestId { get; }
        public ReadOnlyMemory<byte> CachedResponse => new(_cachedResponse);
        internal U2R2RequestReplayAuthority Owner { get; }
        internal U2R2ControlReservation ResponseReservation { get; }
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.TryAbandon(this, requireClaimed: false);
    }

    public sealed class U2R2RequestReplayAuthority
    {
        private sealed class Entry
        {
            public Entry(
                ulong requestId,
                byte[] request,
                ulong reservedResponseBytes,
                U2R2BoundedOutboundScheduler scheduler,
                U2R2ControlReservation reservation)
            {
                RequestId = requestId;
                Request = (byte[])request.Clone();
                ReservedResponseBytes = reservedResponseBytes;
                Scheduler = scheduler;
                Reservation = reservation;
            }

            public ulong RequestId { get; }
            public byte[] Request { get; }
            public ulong ReservedResponseBytes { get; }
            public U2R2BoundedOutboundScheduler Scheduler { get; }
            public U2R2ControlReservation Reservation { get; }
            public byte[] Response { get; set; }
            public bool IsCompleted { get; set; }
            public bool IsClaimed { get; set; }
        }

        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private readonly Dictionary<ulong, Entry> _entries = new();
        private readonly LinkedList<ulong> _completedOrder = new();
        private ulong _highWaterMark;
        private ulong _outstandingRequests;
        private ulong _replayBytes;
        private bool _closed;

        public U2R2RequestReplayAuthority(U2R2ProtocolLimits limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        public ulong HighWaterMark
        {
            get
            {
                lock (_gate)
                    return _highWaterMark;
            }
        }

        public ulong OutstandingRequests
        {
            get
            {
                lock (_gate)
                    return _outstandingRequests;
            }
        }

        public ulong RetainedEntries
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_entries.Count);
            }
        }

        public ulong ReplayBytes
        {
            get
            {
                lock (_gate)
                    return _replayBytes;
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (_gate)
                    return _closed;
            }
        }

        public U2R2ReplayAdmission Admit(
            ulong requestId,
            byte[] canonicalRequest,
            ulong maximumResponseBytes,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (requestId == 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_request_id",
                    "A U2R2 request ID must be nonzero.",
                    terminal: true);
            }
            if (requestId == ulong.MaxValue)
            {
                throw new U2R2ProtocolException(
                    "request_id_exhausted",
                    "The U2R2 request ID space exhausted before the session high-water mark could wrap.",
                    terminal: true);
            }
            if (canonicalRequest == null)
                throw new ArgumentNullException(nameof(canonicalRequest));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));

            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay authority is closed.");
                }
                if (_entries.TryGetValue(requestId, out var retained))
                {
                    if (!canonicalRequest.SequenceEqual(retained.Request))
                    {
                        throw new U2R2ProtocolException(
                            "request_id_conflict",
                            "A retained U2R2 request ID has different canonical bytes.",
                            terminal: true);
                    }
                    if (!retained.IsCompleted)
                    {
                        throw new U2R2ProtocolException(
                            "request_in_flight",
                            "The identical U2R2 request is still in flight.",
                            terminal: false);
                    }
                    if (!ReferenceEquals(retained.Scheduler, scheduler))
                    {
                        throw new InvalidOperationException(
                            "The retained U2R2 response belongs to another scheduler.");
                    }
                    if (!scheduler.TryReserveControl(
                            checked((ulong)retained.Response.LongLength),
                            out var replayReservation))
                    {
                        ThrowCapacity("No control capacity remains for exact replay.");
                    }
                    replayReservation.Commit(
                        U2R2OutboundFrame.Control(
                            "replay:" + requestId,
                            retained.Response));
                    return new U2R2ReplayAdmission(
                        this,
                        requestId,
                        U2R2ReplayDecision.ReplayCached,
                        retained.Response,
                        responseReservation: null)
                    {
                        IsSettled = true,
                    };
                }
                if (requestId <= _highWaterMark)
                {
                    throw new U2R2ProtocolException(
                        "stale_request",
                        "The U2R2 request ID is below the retained session high-water mark.",
                        terminal: false);
                }
                if (_outstandingRequests == _limits.MaxOutstandingRequests)
                    ThrowCapacity("The outstanding U2R2 request limit is exhausted.");

                var requestBytes = checked((ulong)canonicalRequest.LongLength);
                ulong requestedReplayBytes;
                try
                {
                    requestedReplayBytes = checked(requestBytes + maximumResponseBytes);
                }
                catch (OverflowException)
                {
                    ThrowCapacity("Replay byte arithmetic overflowed.");
                    return null;
                }
                if (requestedReplayBytes > _limits.MaxReplayBytes)
                    ThrowCapacity("The request and response reservation exceed replay bytes.");

                if (!scheduler.TryReserveControl(
                        maximumResponseBytes,
                        out var responseReservation))
                {
                    ThrowCapacity("No control capacity remains for the required response.");
                }

                var evictions = SelectEvictions(requestedReplayBytes);
                if (evictions == null)
                {
                    responseReservation.Dispose();
                    ThrowCapacity("The bounded replay cache cannot admit this request.");
                }
                foreach (var evictedId in evictions)
                    Evict(evictedId);

                var entry = new Entry(
                    requestId,
                    canonicalRequest,
                    maximumResponseBytes,
                    scheduler,
                    responseReservation);
                _entries.Add(requestId, entry);
                _replayBytes += requestedReplayBytes;
                _outstandingRequests++;
                _highWaterMark = requestId;
                return new U2R2ReplayAdmission(
                    this,
                    requestId,
                    U2R2ReplayDecision.BeginMutation,
                    cachedResponse: null,
                    responseReservation);
            }
        }

        public void Complete(
            U2R2ReplayAdmission admission,
            byte[] exactResponse)
            => Finish(
                admission,
                U2R2OutboundFrame.Control(
                    "response:" + admission?.RequestId,
                    exactResponse),
                priorityFence: false,
                requireClaimed: false);

        public void Abort(
            U2R2ReplayAdmission admission,
            byte[] exactErrorResponse)
            => Finish(
                admission,
                U2R2OutboundFrame.Control(
                    "error:" + admission?.RequestId,
                    exactErrorResponse),
                priorityFence: false,
                requireClaimed: false);

        internal void CompleteFenced(
            U2R2ReplayAdmission admission,
            U2R2OutboundFrame exactResponse,
            U2R2ContractKey fenceContract)
            => Finish(
                admission,
                exactResponse,
                priorityFence: true,
                requireClaimed: true,
                fenceContract: fenceContract);

        internal void RejectClaimed(
            U2R2ReplayAdmission admission,
            U2R2OutboundFrame exactErrorResponse)
            => Finish(
                admission,
                exactErrorResponse,
                priorityFence: false,
                requireClaimed: true);

        internal bool TryClaimForContract(
            U2R2ReplayAdmission admission,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (admission == null || scheduler == null)
                return false;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.Decision != U2R2ReplayDecision.BeginMutation
                    || admission.IsSettled
                    || !_entries.TryGetValue(
                        admission.RequestId,
                        out var entry)
                    || entry.IsCompleted
                    || entry.IsClaimed
                    || !ReferenceEquals(entry.Scheduler, scheduler))
                {
                    return false;
                }
                entry.IsClaimed = true;
                return true;
            }
        }

        internal void ReleaseContractClaim(
            U2R2ReplayAdmission admission,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.Decision != U2R2ReplayDecision.BeginMutation
                    || admission.IsSettled
                    || !_entries.TryGetValue(
                        admission.RequestId,
                        out var entry)
                    || entry.IsCompleted
                    || !entry.IsClaimed
                    || !ReferenceEquals(entry.Scheduler, scheduler))
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay admission has no matching contract claim.");
                }
                entry.IsClaimed = false;
            }
        }

        internal bool IsCachedFor(
            U2R2ReplayAdmission admission,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (admission == null || scheduler == null)
                return false;
            lock (_gate)
            {
                return ReferenceEquals(admission.Owner, this)
                       && admission.Decision == U2R2ReplayDecision.ReplayCached
                       && admission.IsSettled
                       && _entries.TryGetValue(
                           admission.RequestId,
                           out var entry)
                       && entry.IsCompleted
                       && ReferenceEquals(entry.Scheduler, scheduler);
            }
        }

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
