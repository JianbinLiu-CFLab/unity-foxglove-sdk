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
        private sealed class Entry
        {
            public Entry(
                ulong requestId,
                byte[] request,
                ulong reservedResponseBytes,
                U2R2BoundedOutboundScheduler scheduler,
                U2R2ControlReservation reservation,
                U2R2Operation? boundOperation,
                U2R2ContractIdentity boundIdentity)
            {
                RequestId = requestId;
                Request = (byte[])request.Clone();
                ReservedResponseBytes = reservedResponseBytes;
                Scheduler = scheduler;
                Reservation = reservation;
                BoundOperation = boundOperation;
                BoundIdentity = boundIdentity;
            }

            public ulong RequestId { get; }
            public byte[] Request { get; }
            public ulong ReservedResponseBytes { get; }
            public U2R2BoundedOutboundScheduler Scheduler { get; }
            public U2R2ControlReservation Reservation { get; }
            public U2R2Operation? BoundOperation { get; }
            public U2R2ContractIdentity BoundIdentity { get; }
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
    }
}
