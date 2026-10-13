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
            U2R2ControlReservation responseReservation,
            U2R2BoundedOutboundScheduler boundScheduler = null,
            U2R2Operation? boundOperation = null,
            U2R2ContractIdentity boundIdentity = null)
        {
            Owner = owner;
            RequestId = requestId;
            Decision = decision;
            _cachedResponse = cachedResponse == null
                ? Array.Empty<byte>()
                : (byte[])cachedResponse.Clone();
            ResponseReservation = responseReservation;
            BoundScheduler = boundScheduler;
            BoundOperation = boundOperation;
            BoundIdentity = boundIdentity;
        }

        public U2R2ReplayDecision Decision { get; }
        public ulong RequestId { get; }
        public ReadOnlyMemory<byte> CachedResponse => new(_cachedResponse);
        internal U2R2RequestReplayAuthority Owner { get; }
        internal U2R2ControlReservation ResponseReservation { get; }
        internal U2R2BoundedOutboundScheduler BoundScheduler { get; }
        internal U2R2Operation? BoundOperation { get; }
        internal U2R2ContractIdentity BoundIdentity { get; }
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.TryAbandon(this, requireClaimed: false);
    }
}
