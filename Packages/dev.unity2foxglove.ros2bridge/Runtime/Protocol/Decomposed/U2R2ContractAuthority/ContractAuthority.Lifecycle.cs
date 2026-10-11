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
        public void Close(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            lock (_gate)
            {
                EnsureAuthorityPairCompatible(scheduler, replay);
                BindAuthorityPair(scheduler, replay);
                if (_closed)
                    return;
                replay.Close();
                scheduler.Close();
                _contracts.Clear();
                _tombstones.Clear();
                _tombstoneOrder.Clear();
                _closed = true;
            }
        }

        private void EnsureAuthorityPairCompatible(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            if (_boundScheduler == null && _boundReplay == null)
                return;
            if (!ReferenceEquals(_boundScheduler, scheduler)
                || !ReferenceEquals(_boundReplay, replay))
            {
                throw new InvalidOperationException(
                    "The U2R2 contract authority belongs to another scheduler and replay authority.");
            }
        }

        private void BindAuthorityPair(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            EnsureAuthorityPairCompatible(scheduler, replay);
            if (_boundScheduler != null)
                return;
            _boundScheduler = scheduler;
            _boundReplay = replay;
        }

        private void ValidateRegistrationTransaction(
            U2R2RegistrationAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (!ReferenceEquals(admission.Owner, this)
                || !ReferenceEquals(admission.Scheduler, scheduler))
            {
                throw new InvalidOperationException(
                    "The U2R2 registration admission belongs elsewhere.");
            }
            if (!ReferenceEquals(admission.Replay, replay)
                || admission.ResponseRequestId != response.RequestId)
            {
                throw new InvalidOperationException(
                    "The U2R2 registration response transaction belongs elsewhere.");
            }
        }

        private void ValidateRemovalTransaction(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (!ReferenceEquals(admission.Owner, this)
                || !ReferenceEquals(admission.Scheduler, scheduler))
            {
                throw new InvalidOperationException(
                    "The U2R2 removal admission belongs elsewhere.");
            }
            if (!ReferenceEquals(admission.Replay, replay)
                || admission.ResponseRequestId != response.RequestId)
            {
                throw new InvalidOperationException(
                    "The U2R2 removal response transaction belongs elsewhere.");
            }
        }

        private U2R2ProtocolException CommitSemanticRejection(
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2Operation responseOperation,
            U2R2ProtocolException error)
        {
            replay.RejectClaimed(
                response,
                CreateSemanticErrorFrame(
                    responseOperation,
                    response.RequestId,
                    error));
            return error;
        }

        private U2R2OutboundFrame CreateSemanticErrorFrame(
            U2R2Operation responseOperation,
            ulong requestId,
            U2R2ProtocolException error)
        {
            var frame = _semanticErrorFrameFactory(
                responseOperation,
                requestId,
                error);
            if (frame == null || !frame.IsControl)
            {
                throw new InvalidOperationException(
                    "The semantic error factory must return a control frame.");
            }
            return frame;
        }

        private void AddTombstone(
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (!_tombstones.ContainsKey(identity.Key))
            {
                _tombstones.Add(identity.Key, identity);
                _tombstoneOrder.Enqueue(identity.Key);
            }
            while (checked((ulong)_tombstones.Count) > _limits.MaxTombstones)
            {
                var evicted = _tombstoneOrder.Dequeue();
                _tombstones.Remove(evicted);
                scheduler.ForgetContract(evicted);
            }
        }

        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "The U2R2 contract authority is closed.");
            }
        }

        private static void ThrowCapacity(string message)
            => throw new U2R2ProtocolException(
                "capacity_exceeded",
                message,
                terminal: false);

        private static void ThrowInvalid(string message)
            => throw new U2R2ProtocolException(
                "invalid_contract",
                message,
                terminal: false);
    }
}
