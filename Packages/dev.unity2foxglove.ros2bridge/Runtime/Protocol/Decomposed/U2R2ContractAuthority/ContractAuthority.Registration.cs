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
        public U2R2RegistrationAdmission BeginRegistration(
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
                            U2R2Operation.RegisterSubscription,
                            identity))
                    {
                        throw new InvalidOperationException(
                            "The replayed registration response is not bound to this contract.");
                    }
                    BindAuthorityPair(scheduler, replay);
                    return new U2R2RegistrationAdmission(
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
                        U2R2Operation.RegisterSubscription,
                        identity))
                {
                    throw new InvalidOperationException(
                        "Registration requires the pending command response transaction bound to this contract.");
                }
                BindAuthorityPair(scheduler, replay);

                var inserted = false;
                try
                {
                    if (identity.Direction != U2R2ContractDirection.Subscribe)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "A register_subscription command requires subscribe direction.",
                                terminal: false));
                    }
                    if (_contracts.ContainsKey(identity.Key)
                        || _tombstones.ContainsKey(identity.Key))
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "The U2R2 contract ID and generation are already bound.",
                                terminal: false));
                    }
                    if (checked((ulong)_contracts.Count) == _limits.MaxContracts)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "capacity_exceeded",
                                "The U2R2 contract limit is exhausted.",
                                terminal: false));
                    }
                    _contracts.Add(
                        identity.Key,
                        new ContractEntry
                        {
                            Identity = identity,
                            State = ContractState.Registering,
                        });
                    inserted = true;
                    scheduler.ActivateContract(identity.Key);
                    return new U2R2RegistrationAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response);
                }
                catch
                {
                    if (inserted)
                    {
                        _contracts.Remove(identity.Key);
                        scheduler.RetireContract(identity.Key);
                    }
                    replay.TryAbandonClaimed(response);
                    throw;
                }
            }
        }

        public void CommitReady(
            U2R2RegistrationAdmission admission,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2OutboundFrame exactReadyFrame)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                if (admission.Replayed)
                {
                    if (!ReferenceEquals(admission.Owner, this)
                        || !ReferenceEquals(admission.Replay, replay)
                        || admission.ResponseRequestId != response.RequestId
                        || response.Decision != U2R2ReplayDecision.ReplayCached)
                    {
                        throw new InvalidOperationException(
                            "The replayed U2R2 registration belongs elsewhere.");
                    }
                    return;
                }
                if (!ReferenceEquals(admission.Owner, this)
                    || !ReferenceEquals(admission.Replay, replay)
                    || admission.ResponseRequestId != response.RequestId
                    || admission.IsSettled
                    || !_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering
                    || !entry.Identity.Equals(admission.Identity))
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                replay.CompleteFenced(
                    response,
                    exactReadyFrame,
                    admission.Identity.Key);
                entry.State = ContractState.Ready;
                admission.IsSettled = true;
            }
        }

        public void CancelRegistration(
            U2R2RegistrationAdmission admission,
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
                ValidateRegistrationTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering)
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                replay.CancelClaimed(response);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public void AbortRegistration(
            U2R2RegistrationAdmission admission,
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
                ValidateRegistrationTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering)
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                var exactErrorFrame = CreateSemanticErrorFrame(
                    U2R2Operation.SubscriptionReady,
                    response.RequestId,
                    error);
                replay.RejectClaimed(response, exactErrorFrame);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public U2R2MessageAdmission AdmitMessage(
            U2R2ContractIdentity identity,
            ulong sequence)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            lock (_gate)
            {
                EnsureOpen();
                if (_tombstones.TryGetValue(
                        identity.Key,
                        out var removedIdentity))
                {
                    EnsureMessageIdentity(identity, removedIdentity);
                    return U2R2MessageAdmission.LateTombstone;
                }
                if (!_contracts.TryGetValue(identity.Key, out var entry))
                {
                    throw new U2R2ProtocolException(
                        "unknown_contract",
                        "The U2R2 message references an unknown contract generation.",
                        terminal: true);
                }
                EnsureMessageIdentity(identity, entry.Identity);
                if (entry.State == ContractState.Registering)
                {
                    throw new U2R2ProtocolException(
                        "contract_not_ready",
                        "The U2R2 subscription_ready response is not committed.",
                        terminal: true);
                }
                if (entry.State == ContractState.Removing)
                    return U2R2MessageAdmission.LateTombstone;
                entry.Sequence.Admit(sequence);
                return U2R2MessageAdmission.Accepted;
            }
        }

        private static void EnsureMessageIdentity(
            U2R2ContractIdentity actual,
            U2R2ContractIdentity expected)
        {
            if (!expected.Equals(actual))
            {
                throw new U2R2ProtocolException(
                    "contract_identity_mismatch",
                    "The U2R2 message identity does not match its frozen contract.",
                    terminal: true);
            }
        }

    }
}
