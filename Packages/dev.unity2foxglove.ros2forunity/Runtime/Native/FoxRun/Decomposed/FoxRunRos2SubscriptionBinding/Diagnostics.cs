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
        public bool TryGetSnapshot(
            long activeSessionGeneration,
            out FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            if (activeSessionGeneration != SessionGeneration)
            {
                snapshot = default;
                return false;
            }
            var stoppingBefore = Volatile.Read(ref _stopping);
            if (!TryReadActiveGeneration(out var generationBefore)
                || generationBefore != SessionGeneration
                || (stoppingBefore == 0 && Volatile.Read(ref _stopping) != 0))
            {
                snapshot = default;
                return false;
            }

            var received = ReceivedCount;
            var replaced = ReplacedCount;
            var applied = AppliedCount;
            var pending = _slot.PendingCount;
            var rejectedAfterStop = RejectedAfterStopCount;
            var copyFailed = CopyFailedCount;
            var staleCallbacks = StaleCallbackCount;
            var sameOriginDrops = SameOriginDropCount;
            var lastReceiveStopwatchTimestamp = Interlocked.Read(ref _lastReceiveStopwatchTimestamp);
            var lastApplyStopwatchTimestamp = Interlocked.Read(ref _lastApplyStopwatchTimestamp);
            lock (_lifecycleLock)
            {
                var registration = _lastRegistration;
                snapshot = new FoxRunRos2SubscriptionBindingSnapshot(
                    Contract,
                    _qos,
                    SessionGeneration,
                    State,
                    registration.Error,
                    registration.Diagnostic,
                    received,
                    replaced,
                    applied,
                    pending,
                    rejectedAfterStop,
                    copyFailed,
                    staleCallbacks,
                    lastReceiveStopwatchTimestamp,
                    lastApplyStopwatchTimestamp,
                    sameOriginDrops);
            }
            if (!TryReadActiveGeneration(out var generationAfter)
                || generationAfter != SessionGeneration
                || (stoppingBefore == 0 && Volatile.Read(ref _stopping) != 0))
            {
                snapshot = default;
                return false;
            }
            return true;
        }

        public void RecordApplyFailure(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));
            StopCore(FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.ApplyFailure,
                DescribeException(exception)));
        }

        public void Stop() => StopCore(null);

        internal bool CleanupPending
        {
            get
            {
                lock (_lifecycleLock)
                    return _cleanupPending;
            }
        }

    }
}
#endif
