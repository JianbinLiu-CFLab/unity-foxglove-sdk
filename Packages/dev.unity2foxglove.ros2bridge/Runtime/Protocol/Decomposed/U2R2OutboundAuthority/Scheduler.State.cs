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
        private sealed class ControlEntry
        {
            public ControlEntry(
                U2R2OutboundFrame frame,
                U2R2ContractKey? fenceContract)
            {
                Frame = frame;
                FenceContract = fenceContract;
            }

            public U2R2OutboundFrame Frame { get; }
            public U2R2ContractKey? FenceContract { get; }
            public bool PriorityFence => FenceContract.HasValue;
        }

        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private readonly LinkedList<ControlEntry> _control = new();
        private readonly Dictionary<U2R2ContractKey, LinkedList<U2R2OutboundFrame>>
            _data = new();
        private readonly Queue<U2R2ContractKey> _roundRobin = new();
        private readonly HashSet<U2R2ContractKey> _active = new();
        private readonly HashSet<U2R2ContractKey> _revoked = new();
        private readonly HashSet<U2R2ContractKey> _retireWhenDrained = new();
        private ulong _controlDepthUsed;
        private ulong _controlBytesUsed;
        private ulong _dataQueuedDepth;
        private ulong _dataQueuedBytes;
        private ulong _transientBytes;
        private ulong _inFlightBytes;
        private ulong _controlBurst;
        private bool _readerActive;
        private bool _writerActive;
        private U2R2ContractKey _writerContract;
        private bool _writerHasContract;
        private bool _closed;

        public U2R2BoundedOutboundScheduler(U2R2ProtocolLimits limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        public ulong QueuedBytes
        {
            get
            {
                lock (_gate)
                    return checked(_controlBytesUsed + _dataQueuedBytes);
            }
        }

        public ulong TotalQueuedDepth
        {
            get
            {
                lock (_gate)
                    return checked(_controlDepthUsed + _dataQueuedDepth);
            }
        }

        public ulong DataQueuedDepth
        {
            get
            {
                lock (_gate)
                    return _dataQueuedDepth;
            }
        }

        public ulong TransientBytes
        {
            get
            {
                lock (_gate)
                    return _transientBytes;
            }
        }

        public ulong InFlightBytes
        {
            get
            {
                lock (_gate)
                    return _inFlightBytes;
            }
        }

        public ulong RevokedContractCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_revoked.Count);
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
