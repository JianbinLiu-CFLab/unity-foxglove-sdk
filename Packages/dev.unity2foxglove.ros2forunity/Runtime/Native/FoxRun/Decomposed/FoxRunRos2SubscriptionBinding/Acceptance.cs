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
        public FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            // Arming is a diagnostic main-thread operation, but serialize it
            // with lifecycle changes so a second caller cannot erase the
            // first caller's arming-race sentinel before admission completes.
            lock (_lifecycleLock)
                return ArmAcceptanceAttemptUnderLock(out snapshot);
        }

        private FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttemptUnderLock(
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            snapshot = default;
            if (Volatile.Read(ref _stopping) != 0)
                return FoxRunRos2AcceptanceArmStatus.Stopped;
            // Clear the prior arming sentinel before publishing -1. A callback
            // can only increment this counter after the compare-exchange, so
            // no arming-race evidence can be erased after admission begins.
            Interlocked.Exchange(ref _acceptanceArmingPublished, 0);
            var priorAdmission = Volatile.Read(ref _acceptanceAdmission);
            if (priorAdmission != 0 && priorAdmission != AcceptanceCompleted)
                return FoxRunRos2AcceptanceArmStatus.AlreadyArmed;
            if (Interlocked.CompareExchange(
                    ref _acceptanceAdmission,
                    AcceptanceArming,
                    priorAdmission) != priorAdmission)
                return FoxRunRos2AcceptanceArmStatus.AlreadyArmed;

            var status = FoxRunRos2AcceptanceArmStatus.Armed;
            try
            {
                if (_slot.PendingCount != 0)
                    status = FoxRunRos2AcceptanceArmStatus.PendingNotIdle;
                else if (Volatile.Read(ref _acceptanceCallbacksInFlight) != 0)
                    status = FoxRunRos2AcceptanceArmStatus.CallbackInFlight;
                if (status != FoxRunRos2AcceptanceArmStatus.Armed)
                    return status;

                Interlocked.Exchange(ref _acceptanceReceived, 0);
                Interlocked.Exchange(ref _acceptanceReplaced, 0);
                Interlocked.Exchange(ref _acceptanceApplied, 0);
                var epoch = Interlocked.Increment(ref _acceptanceEpochSequence);
                if (epoch <= 0)
                {
                    Interlocked.Exchange(ref _acceptanceEpochSequence, 1);
                    epoch = 1;
                }
                Volatile.Write(ref _acceptanceAdmission, epoch);
                Thread.MemoryBarrier();
                if (Volatile.Read(ref _acceptanceArmingCallbacksInFlight) != 0
                    || Interlocked.Read(ref _acceptanceArmingPublished) != 0)
                {
                    Interlocked.CompareExchange(
                        ref _acceptanceAdmission,
                        priorAdmission,
                        epoch);
                    status = FoxRunRos2AcceptanceArmStatus.ConcurrentCallbackRace;
                    return status;
                }

                Volatile.Write(ref _acceptanceCompletingEpoch, 0);
                snapshot = AcceptanceSnapshot(epoch);
                return status;
            }
            finally
            {
                if (status != FoxRunRos2AcceptanceArmStatus.Armed)
                    Interlocked.CompareExchange(
                        ref _acceptanceAdmission,
                        priorAdmission,
                        AcceptanceArming);
            }
        }

        public bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            var epoch = Volatile.Read(ref _acceptanceAdmission);
            if (epoch <= 0)
            {
                snapshot = default;
                return false;
            }
            snapshot = AcceptanceSnapshot(epoch);
            return epoch == Volatile.Read(ref _acceptanceAdmission);
        }

        public bool EndAcceptanceAttempt(long epoch)
        {
            if (epoch <= 0)
                return false;
            lock (_lifecycleLock)
            {
                var admission = Volatile.Read(ref _acceptanceAdmission);
                var matchesActive = admission == epoch;
                var matchesClosed = (admission == AcceptanceCompleting
                                     || admission == AcceptanceCompleted)
                                    && Volatile.Read(ref _acceptanceCompletingEpoch) == epoch;
                if (!matchesActive && !matchesClosed)
                    return false;
                Volatile.Write(ref _acceptanceAdmission, 0);
                Volatile.Write(ref _acceptanceCompletingEpoch, 0);
                return true;
            }
        }

        public bool TryCompleteAcceptanceAttempt(
            long epoch,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            snapshot = default;
            if (epoch <= 0)
                return false;
            lock (_lifecycleLock)
            {
                var admission = Volatile.Read(ref _acceptanceAdmission);
                if (admission == epoch)
                {
                    Volatile.Write(ref _acceptanceCompletingEpoch, epoch);
                    if (Interlocked.CompareExchange(
                            ref _acceptanceAdmission,
                            AcceptanceCompleting,
                            epoch) != epoch)
                    {
                        Volatile.Write(ref _acceptanceCompletingEpoch, 0);
                        return false;
                    }
                }
                else if (admission != AcceptanceCompleting
                         || Volatile.Read(ref _acceptanceCompletingEpoch) != epoch)
                {
                    return false;
                }

                // A callback increments this before reading admission. Thus
                // every callback that could have captured the active epoch is
                // visible here, while later entrants see the closed sentinel
                // and return before copying or publishing.
                if (Volatile.Read(ref _acceptanceCallbacksInFlight) != 0)
                    return false;

                snapshot = new FoxRunRos2AcceptanceAttemptSnapshot(
                    epoch,
                    true,
                    Interlocked.Read(ref _acceptanceReceived),
                    Interlocked.Read(ref _acceptanceReplaced),
                    Interlocked.Read(ref _acceptanceApplied),
                    _slot.PendingCount,
                    0);
                // Keep admission closed after completion. This guarantees no
                // post-close callback can leave a pending value before the
                // caller evaluates and reports the immutable snapshot. A new
                // explicit arm or EndAcceptanceAttempt reopens normal input.
                Volatile.Write(ref _acceptanceAdmission, AcceptanceCompleted);
                return true;
            }
        }

        private FoxRunRos2AcceptanceAttemptSnapshot AcceptanceSnapshot(long epoch)
            => new FoxRunRos2AcceptanceAttemptSnapshot(
                epoch,
                epoch > 0 && epoch == Volatile.Read(ref _acceptanceAdmission),
                Interlocked.Read(ref _acceptanceReceived),
                Interlocked.Read(ref _acceptanceReplaced),
                Interlocked.Read(ref _acceptanceApplied),
                _slot.PendingCount,
                Volatile.Read(ref _acceptanceCallbacksInFlight));

    }
}
#endif
