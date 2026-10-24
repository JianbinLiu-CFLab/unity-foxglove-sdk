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
        public void Stop() => StopCore(null);

        internal bool CleanupPending
        {
            get
            {
                lock (_lifecycleLock)
                    return _cleanupPending;
            }
        }

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

    }
}
#endif
