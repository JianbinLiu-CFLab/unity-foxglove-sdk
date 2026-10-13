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
            => TryClaimForContract(
                admission,
                scheduler,
                operation: null,
                identity: null);

        internal bool TryClaimForContract(
            U2R2ReplayAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2Operation? operation,
            U2R2ContractIdentity identity)
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
                    || !ReferenceEquals(entry.Scheduler, scheduler)
                    || (entry.BoundOperation.HasValue
                        && (operation != entry.BoundOperation.Value
                            || identity == null
                            || !entry.BoundIdentity.Equals(identity))))
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
            => IsCachedFor(
                admission,
                scheduler,
                operation: null,
                identity: null);

        internal bool IsCachedFor(
            U2R2ReplayAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2Operation? operation,
            U2R2ContractIdentity identity)
        {
            if (admission == null || scheduler == null)
                return false;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.Decision != U2R2ReplayDecision.ReplayCached
                    || !admission.IsSettled)
                    return false;
                if (admission.BoundIdentity != null)
                {
                    return ReferenceEquals(admission.BoundScheduler, scheduler)
                           && admission.BoundOperation == operation
                           && identity != null
                           && admission.BoundIdentity.Equals(identity);
                }
                return _entries.TryGetValue(
                           admission.RequestId,
                           out var entry)
                       && entry.IsCompleted
                       && ReferenceEquals(entry.Scheduler, scheduler)
                       && (!entry.BoundOperation.HasValue
                           || (operation == entry.BoundOperation.Value
                               && identity != null
                               && entry.BoundIdentity.Equals(identity)));
            }
        }
    }
}
