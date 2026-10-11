// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict U2R2 contract parsing, readiness, sequencing, and removal.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{

    public sealed partial class U2R2ContractAuthority
    {
        private enum ContractState
        {
            Registering = 1,
            Ready = 2,
            Removing = 3,
        }

        private sealed class ContractEntry
        {
            public U2R2ContractIdentity Identity { get; set; }
            public ContractState State { get; set; }
            public U2R2ContractSequence Sequence { get; } = new();
        }

        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private readonly Func<
            U2R2Operation,
            ulong,
            U2R2ProtocolException,
            U2R2OutboundFrame> _semanticErrorFrameFactory;
        private readonly Dictionary<U2R2ContractKey, ContractEntry> _contracts = new();
        private readonly Dictionary<U2R2ContractKey, U2R2ContractIdentity>
            _tombstones = new();
        private readonly Queue<U2R2ContractKey> _tombstoneOrder = new();
        private U2R2BoundedOutboundScheduler _boundScheduler;
        private U2R2RequestReplayAuthority _boundReplay;
        private bool _closed;

        public U2R2ContractAuthority(
            U2R2ProtocolLimits limits,
            Func<
                U2R2Operation,
                ulong,
                U2R2ProtocolException,
                U2R2OutboundFrame> semanticErrorFrameFactory)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _semanticErrorFrameFactory = semanticErrorFrameFactory
                ?? throw new ArgumentNullException(nameof(semanticErrorFrameFactory));
        }

        public ulong ContractCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_contracts.Count);
            }
        }

        public ulong TombstoneCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_tombstones.Count);
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
