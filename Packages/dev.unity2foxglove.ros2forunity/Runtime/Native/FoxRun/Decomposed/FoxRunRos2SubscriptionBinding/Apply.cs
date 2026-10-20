// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun/Decomposed
// Purpose: Decomposed lifecycle surface for the typed native subscription binding.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2SubscriptionBinding<T>
    {
        public bool TryApplyLatest(long activeSessionGeneration)
            => TryApplyLatest(
                activeSessionGeneration,
                (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

        public bool TryApplyLatest(long activeSessionGeneration, double nowSeconds)
        {
            if (Volatile.Read(ref _stopping) != 0)
                return false;
            if (!IsActiveGeneration(activeSessionGeneration))
                return false;
            var state = State;
            if (state != FoxRunRos2SubscriptionBindingState.Ready
                && state != FoxRunRos2SubscriptionBindingState.Receiving)
                return false;
            if (!IsActiveGeneration(activeSessionGeneration))
                return false;
            var acceptanceAdmission = Volatile.Read(ref _acceptanceAdmission);
            if (acceptanceAdmission == AcceptanceCompleted)
                return false;
            var acceptanceEpoch = acceptanceAdmission > 0
                ? acceptanceAdmission
                : acceptanceAdmission == AcceptanceCompleting
                    ? Volatile.Read(ref _acceptanceCompletingEpoch)
                    : 0;
            var usesPolicy = Contract.Policy != FoxRunPolicy.FixedRate
                             || _dropBeforeApply != null
                             || _canApply != null;
            _policyNowSeconds = nowSeconds;
            var applied = usesPolicy
                ? _slot.TryApplyLatest(_decideOwned, _applyOwned, _clearOwned)
                : _slot.TryApplyLatest(_applyOwned, _clearOwned);
            if (applied)
            {
                _conditionRejectedSinceLastApply = false;
                _lastSemanticApplySeconds = nowSeconds;
                Interlocked.Exchange(ref _lastApplyStopwatchTimestamp, Stopwatch.GetTimestamp());
            }
            // A generated main-thread apply delegate can synchronously stop its
            // Manager/session. The slot correctly defers its drain while this
            // apply operation is on-stack, but the binding still owns the node
            // lease. Re-enter normal stop cleanup only after a successful
            // apply has left the slot operation, so framework-owned values are
            // cleared and disposed on this consumer thread rather than in the
            // ROS callback.
            if (applied && Volatile.Read(ref _stopping) != 0)
                StopCore(null);
            if (applied
                && acceptanceEpoch > 0
                && IsAcceptanceEpochStillOwned(acceptanceEpoch))
                Interlocked.Increment(ref _acceptanceApplied);
            return applied;
        }

        private FoxRunRos2PendingDecision DecideOwned(object candidate, object applied)
        {
            var typedCandidate = (T)candidate;
            if (_dropBeforeApply != null && _dropBeforeApply(typedCandidate))
            {
                Interlocked.Increment(ref _sameOriginDrops);
                return FoxRunRos2PendingDecision.Drop;
            }

            if (_canApply != null && !_canApply())
            {
                _conditionRejectedSinceLastApply = true;
                return FoxRunRos2PendingDecision.Drop;
            }

            if (Contract.Policy == FoxRunPolicy.Trigger)
                return _consumeTrigger != null && _consumeTrigger()
                    ? FoxRunRos2PendingDecision.Apply
                    : FoxRunRos2PendingDecision.Defer;

            var hasApplied = applied != null && !_conditionRejectedSinceLastApply;
            var changed = !hasApplied
                          || _valuesEqual == null
                          || !_valuesEqual(typedCandidate, (T)applied);
            return Unity.FoxgloveSDK.Util.FoxRunUpdatePolicy.ShouldApply(
                Contract.Policy,
                hasPendingValue: true,
                hasLastAppliedValue: hasApplied,
                valueChanged: changed,
                nowSec: _policyNowSeconds,
                lastApplySec: _lastSemanticApplySeconds,
                heartbeatIntervalSec: Contract.HeartbeatIntervalSeconds)
                ? FoxRunRos2PendingDecision.Apply
                : Contract.Policy == FoxRunPolicy.Change
                    ? HasFinitePositiveHeartbeat()
                        ? FoxRunRos2PendingDecision.Defer
                        : FoxRunRos2PendingDecision.Drop
                    : FoxRunRos2PendingDecision.Defer;
        }

        private bool HasFinitePositiveHeartbeat()
        {
            var interval = Contract.HeartbeatIntervalSeconds;
            return interval > 0f
                   && !float.IsNaN(interval)
                   && !float.IsInfinity(interval);
        }

    }
}
#endif
