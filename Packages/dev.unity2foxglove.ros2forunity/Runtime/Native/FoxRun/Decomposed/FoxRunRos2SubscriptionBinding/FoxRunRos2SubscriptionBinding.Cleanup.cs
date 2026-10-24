// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Typed native callback ownership, application, and teardown binding.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2SubscriptionBinding<T>
        where T : ROS2.Message, new()
    {
        private void OnBorrowedMessage(long registrationAttempt, T borrowed)
        {
            Interlocked.Increment(ref _acceptanceCallbacksInFlight);
            var acceptanceEpoch = Volatile.Read(ref _acceptanceAdmission);
            var enteredWhileArming = acceptanceEpoch == AcceptanceArming;
            if (enteredWhileArming)
                Interlocked.Increment(ref _acceptanceArmingCallbacksInFlight);
            try
            {
                if (acceptanceEpoch == AcceptanceCompleting
                    || acceptanceEpoch == AcceptanceCompleted)
                    return;
                if (Volatile.Read(ref _stopping) != 0)
                {
                    // Preserve the slot's rejected-after-stop accounting. Its
                    // admission check runs before _copyBorrowed, so this late
                    // callback cannot allocate an owned message graph.
                    _slot.TryPublish(borrowed, _copyBorrowed);
                    return;
                }
                if (registrationAttempt == 0
                    || registrationAttempt != Volatile.Read(ref _activeRegistrationAttempt))
                {
                    Interlocked.Increment(ref _staleCallbacks);
                    return;
                }
                if (_activeGeneration() != SessionGeneration)
                {
                    Interlocked.Increment(ref _staleCallbacks);
                    return;
                }
                if (!_transportAdmission.TryAccept(_admissionTimestamp()))
                {
                    Interlocked.Increment(ref _transportAdmissionDrops);
                    return;
                }

                var accepted = _slot.TryPublish(
                    borrowed,
                    _copyBorrowed,
                    out _,
                    out var replacedPending);
                if (accepted)
                {
                    Interlocked.Exchange(ref _lastReceiveStopwatchTimestamp, Stopwatch.GetTimestamp());
                    if (enteredWhileArming)
                        Interlocked.Increment(ref _acceptanceArmingPublished);
                    else if (acceptanceEpoch > 0 && IsAcceptanceEpochStillOwned(acceptanceEpoch))
                    {
                        Interlocked.Increment(ref _acceptanceReceived);
                        if (replacedPending)
                            Interlocked.Increment(ref _acceptanceReplaced);
                    }
                    Interlocked.CompareExchange(
                        ref _state,
                        (int)FoxRunRos2SubscriptionBindingState.Receiving,
                        (int)FoxRunRos2SubscriptionBindingState.Ready);
                }
            }
            catch (Exception exception) when (FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                // Never unwind generated copy/backend failures into the ROS executor.
            }
            finally
            {
                if (enteredWhileArming)
                    Interlocked.Decrement(ref _acceptanceArmingCallbacksInFlight);
                Interlocked.Decrement(ref _acceptanceCallbacksInFlight);
            }
        }

        private bool IsAcceptanceEpochStillOwned(long epoch)
        {
            var admission = Volatile.Read(ref _acceptanceAdmission);
            return admission == epoch
                   || ((admission == AcceptanceCompleting
                        || admission == AcceptanceCompleted)
                       && Volatile.Read(ref _acceptanceCompletingEpoch) == epoch);
        }

        private object CopyBorrowed(T borrowed)
        {
            var context = FoxRunRos2CopyContext.Rent(_maximumCopyBytes);
            try
            {
                var copied = _copy(borrowed, context);
                if (ReferenceEquals(copied, null))
                    throw new InvalidOperationException("Generated ROS2 copy must not be null.");
                if (ReferenceEquals(copied, borrowed))
                    throw new InvalidOperationException(
                        "Generated ROS2 copy must not retain the callback-owned message.");
                return copied;
            }
            finally
            {
                context.Return();
            }
        }

        private void ApplyOwned(object owned) => _apply((T)owned);

        private void DisposeOwned(object owned) => _dispose((T)owned);

        private bool ClearOwned(object owned) => _clearIfOwned((T)owned);

        private bool TryReadActiveGeneration(out long generation)
        {
            try
            {
                generation = _activeGeneration();
                return true;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                generation = 0;
                return false;
            }
        }

        private bool TryClaimNodeReleaseUnderLock()
        {
            if (Volatile.Read(ref _stopping) == 0
                || !_slotCleanupComplete
                || _token != null
                || _registrationRollbackToken != null
                || _registrationInFlight
                || _nodeReleaseClaimed
                || _nodeReleaseInFlight)
                return false;
            _nodeReleaseInFlight = true;
            return true;
        }

        private bool RollbackToken(IFoxRunRos2NativeSubscriptionToken token)
        {
            if (token == null)
                return true;
            lock (_lifecycleLock)
                _registrationRollbackToken = token;
            if (TryRetryRegistrationRollback(out var failure))
                return true;
            if (failure != null
                && !FoxRunRos2NativeExceptionPolicy.IsRecoverable(failure))
                ExceptionDispatchInfo.Capture(failure).Throw();
            return false;
        }

        private bool TryRetryRegistrationRollback()
            => TryRetryRegistrationRollback(out _);

        private bool TryRetryRegistrationRollback(out Exception failure)
        {
            failure = null;
            IFoxRunRos2NativeSubscriptionToken token;
            lock (_lifecycleLock)
            {
                token = _registrationRollbackToken;
                if (token == null)
                    return true;
                if (Interlocked.CompareExchange(
                        ref _registrationRollbackInFlight,
                        1,
                        0) != 0)
                    return false;
            }

            var removed = false;
            try
            {
                _backend.RemoveSubscription(token);
                removed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    if (removed && ReferenceEquals(_registrationRollbackToken, token))
                        _registrationRollbackToken = null;
                    Volatile.Write(ref _registrationRollbackInFlight, 0);
                }
            }
            return removed;
        }

        private void RollbackTokenAndReleaseNode(
            IFoxRunRos2NativeSubscriptionToken token,
            bool releaseNode)
        {
            ExceptionDispatchInfo fatal = null;
            var removed = false;
            try
            {
                removed = RollbackToken(token);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }

            if (removed)
            {
                var releaseAfterRollback = false;
                lock (_lifecycleLock)
                    releaseAfterRollback = TryClaimNodeReleaseUnderLock();
                try
                {
                    ReleaseNodeIfClaimed(releaseNode || releaseAfterRollback);
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
        }

        private void ReleaseNodeIfClaimed(bool claimed)
        {
            if (!claimed)
                return;
            var released = false;
            try
            {
                released = _backend.ReleaseNodeOwnership();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                RecordTeardownFailure("release node", exception);
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    _nodeReleaseInFlight = false;
                    if (released)
                        _nodeReleaseClaimed = true;
                }
            }
        }

        private void RecordTeardownFailure(string stage, Exception exception)
        {
            var diagnostic = stage + ": " + DescribeException(exception);
            lock (_lifecycleLock)
            {
                if (_teardownFailureRecorded || _preserveTerminalFailure)
                    return;
                _teardownFailureRecorded = true;
                _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.TeardownFailure,
                    diagnostic);
            }
        }

        private static FoxRunRos2RegistrationResult StoppedResult()
            => FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.Stopped,
                "Native subscription binding is stopped.");

        private static string DescribeException(Exception exception)
        {
            if (FoxRunRos2NativeExceptionPolicy.TryGetNativeRuntimeSurfaceFailure(
                exception,
                out var failureKind))
                return failureKind + ": " + exception.Message;
            return exception.GetType().Name + ": " + exception.Message;
        }
    }
}
#endif
