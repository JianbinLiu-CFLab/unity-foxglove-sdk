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
        private void StopCore(FoxRunRos2RegistrationResult? primaryFailure)
        {
            IFoxRunRos2NativeSubscriptionToken token = null;
            var beginStop = false;
            lock (_lifecycleLock)
            {
                if ((_slotCleanupComplete
                     && _token == null
                     && _registrationRollbackToken == null
                     && _nodeReleaseClaimed)
                    || _stopCleanupInProgress)
                    return;
                _stopCleanupInProgress = true;
                token = _token ?? _registrationRollbackToken;
                if (Volatile.Read(ref _stopping) == 0)
                {
                    beginStop = true;
                    Volatile.Write(ref _stopping, 1);
                    Interlocked.Exchange(ref _acceptanceAdmission, 0);
                    Interlocked.Exchange(ref _acceptanceCompletingEpoch, 0);
                    Volatile.Write(ref _activeRegistrationAttempt, 0L);
                    if (primaryFailure.HasValue)
                    {
                        _preserveTerminalFailure = true;
                        _lastRegistration = primaryFailure.Value;
                        Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.Failed);
                    }
                    else
                    {
                        Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.Stopped);
                        if (!_teardownFailureRecorded)
                            _lastRegistration = StoppedResult();
                    }
                }
            }

            ExceptionDispatchInfo fatal = null;
            if (beginStop)
            {
                try
                {
                    _slot.BeginStop(_clearOwned);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    RecordTeardownFailure("begin owned-message drain", exception);
                }
                catch (Exception exception)
                {
                    fatal = ExceptionDispatchInfo.Capture(exception);
                }

            }

            if (token != null)
            {
                try
                {
                    _backend.RemoveSubscription(token);
                    lock (_lifecycleLock)
                    {
                        if (ReferenceEquals(_token, token))
                            _token = null;
                        if (ReferenceEquals(_registrationRollbackToken, token))
                            _registrationRollbackToken = null;
                        _cleanupPending = false;
                    }
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    lock (_lifecycleLock)
                        _cleanupPending = true;
                    RecordTeardownFailure("remove subscription", exception);
                }
                catch (Exception exception)
                {
                    lock (_lifecycleLock)
                        _cleanupPending = true;
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                _slot.Stop(_clearOwned);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                RecordTeardownFailure("drain owned messages", exception);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            var slotStopped = _slot.IsStopped;
            var releaseNode = false;
            lock (_lifecycleLock)
            {
                _stopCleanupInProgress = false;
                if (slotStopped)
                {
                    _slotCleanupComplete = true;
                    releaseNode = TryClaimNodeReleaseUnderLock();
                }
            }
            try
            {
                ReleaseNodeIfClaimed(releaseNode);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
        }

        private FoxRunRos2RegistrationResult CompleteWithoutBackend(
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            FoxRunRos2RegistrationResult result;
            var releaseNode = false;
            lock (_lifecycleLock)
            {
                _registrationInFlight = false;
                if (Volatile.Read(ref _stopping) != 0)
                    result = StoppedResult();
                else
                    result = SetRegistrationFailureUnderLock(error, diagnostic);
                releaseNode = TryClaimNodeReleaseUnderLock();
            }
            ReleaseNodeIfClaimed(releaseNode);
            return result;
        }

        private void RethrowRegistrationFatal(
            Exception exception,
            IFoxRunRos2NativeSubscriptionToken returnedToken)
        {
            var primary = ExceptionDispatchInfo.Capture(exception);
            lock (_lifecycleLock)
                _registrationInFlight = false;

            try
            {
                RollbackToken(returnedToken);
            }
            catch (Exception)
            {
                // Preserve the fatal admission/registration failure while
                // continuing the mandatory binding and node teardown.
            }

            try
            {
                StopCore(null);
            }
            catch (Exception)
            {
                // StopCore is best-effort across all teardown stages. The
                // original fatal exception remains the primary signal.
            }

            // Stop may have completed on another thread while registration
            // was still marked in flight. Re-check the deferred release gate.
            var releaseNode = false;
            lock (_lifecycleLock)
                releaseNode = TryClaimNodeReleaseUnderLock();
            try
            {
                ReleaseNodeIfClaimed(releaseNode);
            }
            catch (Exception)
            {
                // Preserve the primary fatal exception.
            }

            primary.Throw();
        }

        private FoxRunRos2RegistrationResult SetRegistrationFailureUnderLock(
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            _lastRegistration = FoxRunRos2RegistrationResult.Failure(error, diagnostic);
            var target = error == FoxRunRos2RegistrationError.RuntimeUnavailable
                ? FoxRunRos2SubscriptionBindingState.WaitingForRuntime
                : error == FoxRunRos2RegistrationError.UnsupportedMessageType
                  || error == FoxRunRos2RegistrationError.UnsupportedQos
                    ? FoxRunRos2SubscriptionBindingState.Unsupported
                    : FoxRunRos2SubscriptionBindingState.Failed;
            Volatile.Write(ref _state, (int)target);
            return _lastRegistration;
        }

        private bool CanRetryRegistrationUnderLock()
        {
            if (Volatile.Read(ref _stopping) != 0 || _registrationInFlight)
                return false;
            if (_registrationRollbackToken != null)
                return true;
            return State == FoxRunRos2SubscriptionBindingState.Failed
                   && (_lastRegistration.Error == FoxRunRos2RegistrationError.BackendFailure
                       || _lastRegistration.Error == FoxRunRos2RegistrationError.InvalidSubscriptionToken)
                   && _registrationAttemptSequence < MaximumRecoverableRegistrationAttempts;
        }

        private bool IsActiveGeneration(long callerGeneration)
        {
            if (callerGeneration != SessionGeneration)
                return false;
            try
            {
                return _activeGeneration() == SessionGeneration;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                return false;
            }
        }

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
