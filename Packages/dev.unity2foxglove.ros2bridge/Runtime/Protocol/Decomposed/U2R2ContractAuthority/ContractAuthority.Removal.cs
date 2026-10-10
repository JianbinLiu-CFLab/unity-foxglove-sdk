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
        public U2R2RemovalAdmission BeginUnregister(
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            lock (_gate)
            {
                EnsureOpen();
                EnsureAuthorityPairCompatible(scheduler, replay);
                if (response.Decision == U2R2ReplayDecision.ReplayCached)
                {
                    if (!replay.IsCachedFor(
                            response,
                            scheduler,
                            U2R2Operation.UnregisterSubscription,
                            identity))
                    {
                        throw new InvalidOperationException(
                            "The replayed unregister response is not bound to this contract.");
                    }
                    BindAuthorityPair(scheduler, replay);
                    return new U2R2RemovalAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response)
                    {
                        Replayed = true,
                        IsSettled = true,
                    };
                }
                if (!replay.TryClaimForContract(
                        response,
                        scheduler,
                        U2R2Operation.UnregisterSubscription,
                        identity))
                {
                    throw new InvalidOperationException(
                        "Unregister requires the pending command response transaction bound to this contract.");
                }
                BindAuthorityPair(scheduler, replay);

                var removing = false;
                try
                {
                    if (!_contracts.TryGetValue(identity.Key, out var entry)
                        || entry.State != ContractState.Ready)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionRemoved,
                            new U2R2ProtocolException(
                                "unknown_contract",
                                "The U2R2 unregister request references no ready contract.",
                                terminal: true));
                    }
                    if (!entry.Identity.Equals(identity))
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionRemoved,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "The U2R2 unregister identity conflicts with the registered contract.",
                                terminal: false));
                    }
                    scheduler.RevokeContract(identity.Key);
                    entry.State = ContractState.Removing;
                    removing = true;
                    return new U2R2RemovalAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response);
                }
                catch
                {
                    if (removing)
                    {
                        _contracts.Remove(identity.Key);
                        scheduler.RetireContract(identity.Key);
                    }
                    replay.TryAbandonClaimed(response);
                    throw;
                }
            }
        }

        internal void AbandonRegistration(U2R2RegistrationAdmission admission)
        {
            if (admission == null)
                return;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.IsSettled
                    || admission.Replayed)
                {
                    return;
                }
                admission.Replay.TryAbandonClaimed(admission.Response);
                _contracts.Remove(admission.Identity.Key);
                try
                {
                    admission.Scheduler.RetireContract(admission.Identity.Key);
                }
                catch
                {
                    // Dispose is a last-resort rollback and must not mask the
                    // exception that abandoned the transaction.
                }
                admission.IsSettled = true;
            }
        }

        internal void AbandonRemoval(U2R2RemovalAdmission admission)
        {
            if (admission == null)
                return;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.IsSettled
                    || admission.Replayed)
                {
                    return;
                }
                admission.Replay.TryAbandonClaimed(admission.Response);
                _contracts.Remove(admission.Identity.Key);
                try
                {
                    admission.Scheduler.RetireContract(admission.Identity.Key);
                }
                catch
                {
                    // Dispose is a last-resort rollback and must not mask the
                    // exception that abandoned the transaction.
                }
                admission.IsSettled = true;
            }
        }

        public bool TryCommitRemoved(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2OutboundFrame exactRemovedFrame)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                if (admission.Replayed)
                {
                    if (!ReferenceEquals(admission.Owner, this)
                        || !ReferenceEquals(admission.Scheduler, scheduler)
                        || !ReferenceEquals(admission.Replay, replay)
                        || admission.ResponseRequestId != response.RequestId
                        || response.Decision != U2R2ReplayDecision.ReplayCached)
                    {
                        throw new InvalidOperationException(
                            "The replayed U2R2 removal belongs elsewhere.");
                    }
                    return true;
                }
                if (!ReferenceEquals(admission.Owner, this)
                    || !ReferenceEquals(admission.Scheduler, scheduler)
                    || !ReferenceEquals(admission.Replay, replay)
                    || admission.ResponseRequestId != response.RequestId
                    || admission.IsSettled
                    || !_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing
                    || !entry.Identity.Equals(admission.Identity))
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                if (!scheduler.IsContractRevokedAndDrained(
                        admission.Identity.Key))
                {
                    return false;
                }
                replay.CompleteFenced(
                    response,
                    exactRemovedFrame,
                    admission.Identity.Key);
                _contracts.Remove(admission.Identity.Key);
                AddTombstone(admission.Identity, scheduler);
                admission.IsSettled = true;
                return true;
            }
        }

        public void CancelRemoval(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                ValidateRemovalTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing)
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                replay.CancelClaimed(response);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public void AbortRemoval(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2ProtocolException error)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            lock (_gate)
            {
                ValidateRemovalTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing)
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                var exactErrorFrame = CreateSemanticErrorFrame(
                    U2R2Operation.SubscriptionRemoved,
                    response.RequestId,
                    error);
                replay.RejectClaimed(response, exactErrorFrame);
                entry.State = ContractState.Ready;
                scheduler.ActivateContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

    }
}
