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
        public void WaitForRuntime()
        {
            Interlocked.CompareExchange(
                ref _state,
                (int)FoxRunRos2SubscriptionBindingState.WaitingForRuntime,
                (int)FoxRunRos2SubscriptionBindingState.Configured);
        }

        public FoxRunRos2RegistrationResult TryRegister()
        {
            if (!TryRetryRegistrationRollback(out var rollbackFailure))
            {
                if (rollbackFailure != null
                    && !FoxRunRos2NativeExceptionPolicy.IsRecoverable(rollbackFailure))
                    ExceptionDispatchInfo.Capture(rollbackFailure).Throw();
                lock (_lifecycleLock)
                    return _lastRegistration;
            }

            long registrationAttempt;
            lock (_lifecycleLock)
            {
                if (Volatile.Read(ref _stopping) != 0)
                    return FoxRunRos2RegistrationResult.Failure(
                        FoxRunRos2RegistrationError.Stopped,
                        "Native subscription binding is stopped.");

                var state = State;
                if (state == FoxRunRos2SubscriptionBindingState.Ready
                    || state == FoxRunRos2SubscriptionBindingState.Receiving)
                    return _lastRegistration;
                if (state == FoxRunRos2SubscriptionBindingState.Unsupported
                    || (state == FoxRunRos2SubscriptionBindingState.Failed
                        && !CanRetryRegistrationUnderLock()))
                    return _lastRegistration;
                if (_registrationInFlight)
                    return FoxRunRos2RegistrationResult.Failure(
                        FoxRunRos2RegistrationError.RegistrationRejected,
                        "Native subscription registration is already in progress.");

                Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.WaitingForRuntime);
                registrationAttempt = ++_registrationAttemptSequence;
                Volatile.Write(ref _activeRegistrationAttempt, 0L);
                _registrationInFlight = true;
            }

            long generationBefore;
            try
            {
                generationBefore = _activeGeneration();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                return CompleteWithoutBackend(
                    FoxRunRos2RegistrationError.BackendFailure,
                    DescribeException(exception));
            }
            catch (Exception exception)
            {
                RethrowRegistrationFatal(exception, null);
                throw;
            }
            if (generationBefore != SessionGeneration)
                return CompleteWithoutBackend(
                    FoxRunRos2RegistrationError.StaleGeneration,
                    "Native subscription belongs to an inactive session generation.");

            var mayRegister = true;
            FoxRunRos2RegistrationResult earlyResult = default;
            var releaseAfterEarlyExit = false;
            lock (_lifecycleLock)
            {
                if (Volatile.Read(ref _stopping) != 0 || !_registrationInFlight)
                {
                    mayRegister = false;
                    _registrationInFlight = false;
                    earlyResult = StoppedResult();
                    releaseAfterEarlyExit = TryClaimNodeReleaseUnderLock();
                }
            }
            if (!mayRegister)
            {
                ReleaseNodeIfClaimed(releaseAfterEarlyExit);
                return earlyResult;
            }

            FoxRunRos2NativeBackendRegistration backendResult = default;
            Exception registrationFailure = null;
            IFoxRunRos2NativeQosProfile qosProfile = null;
            try
            {
                try
                {
                    var qosResult = _qosFactory == null
                        ? Ros2ForUnityNativeQosMapper.TryCreate(_qos, out qosProfile)
                        : Ros2ForUnityNativeQosMapper.TryCreate(_qos, _qosFactory, out qosProfile);
                    if (!qosResult.Succeeded)
                        return CompleteWithoutBackend(qosResult.Error, qosResult.Diagnostic);

                    backendResult = _backend.Register<T>(
                        Contract,
                        qosProfile,
                        borrowed => OnBorrowedMessage(registrationAttempt, borrowed));
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    registrationFailure = exception;
                }
                finally
                {
                    try
                    {
                        qosProfile?.Dispose();
                    }
                    catch (Exception exception) when (
                        FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                    {
                        if (registrationFailure == null)
                            registrationFailure = exception;
                    }
                }
            }
            catch (Exception exception)
            {
                var fatalToken = backendResult.Succeeded ? backendResult.Token : null;
                RethrowRegistrationFatal(exception, fatalToken);
                throw;
            }

            bool tokenUsable = false;
            Exception tokenInspectionFailure = null;
            var returnedToken = backendResult.Succeeded ? backendResult.Token : null;
            if (returnedToken != null)
            {
                try
                {
                    tokenUsable = returnedToken.IsUsable;
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    tokenInspectionFailure = exception;
                }
                catch (Exception exception)
                {
                    RethrowRegistrationFatal(exception, returnedToken);
                    throw;
                }
            }

            long generationAfter = 0;
            Exception generationAfterFailure = null;
            try
            {
                generationAfter = _activeGeneration();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                generationAfterFailure = exception;
            }
            catch (Exception exception)
            {
                RethrowRegistrationFatal(exception, returnedToken);
                throw;
            }
            var generationAfterDiagnostic = generationAfterFailure == null
                ? string.Empty
                : DescribeException(generationAfterFailure);
            var registrationFailureDiagnostic = registrationFailure == null
                ? string.Empty
                : DescribeException(registrationFailure);
            var tokenInspectionDiagnostic = tokenInspectionFailure == null
                ? string.Empty
                : DescribeException(tokenInspectionFailure);

            FoxRunRos2RegistrationResult result;
            IFoxRunRos2NativeSubscriptionToken rollbackToken = null;
            var releaseAfterRegistration = false;
            lock (_lifecycleLock)
            {
                _registrationInFlight = false;
                if (Volatile.Read(ref _stopping) != 0)
                {
                    result = StoppedResult();
                    rollbackToken = returnedToken;
                }
                else if (generationAfterFailure != null)
                {
                    result = SetRegistrationFailureUnderLock(
                        FoxRunRos2RegistrationError.BackendFailure,
                        generationAfterDiagnostic);
                    rollbackToken = returnedToken;
                }
                else if (generationAfter != SessionGeneration)
                {
                    result = SetRegistrationFailureUnderLock(
                        FoxRunRos2RegistrationError.StaleGeneration,
                        "Native subscription belongs to an inactive session generation.");
                    rollbackToken = returnedToken;
                }
                else if (registrationFailure != null)
                {
                    var error = FoxRunRos2NativeExceptionPolicy.TryGetNativeRuntimeSurfaceFailure(
                        registrationFailure,
                        out _)
                        ? FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable
                        : FoxRunRos2RegistrationError.BackendFailure;
                    result = SetRegistrationFailureUnderLock(error, registrationFailureDiagnostic);
                    rollbackToken = returnedToken;
                }
                else if (!backendResult.Succeeded)
                {
                    result = SetRegistrationFailureUnderLock(backendResult.Error, backendResult.Diagnostic);
                    rollbackToken = backendResult.Token;
                }
                else if (returnedToken == null || !tokenUsable || tokenInspectionFailure != null)
                {
                    result = SetRegistrationFailureUnderLock(
                        tokenInspectionFailure == null
                            ? FoxRunRos2RegistrationError.InvalidSubscriptionToken
                            : FoxRunRos2NativeExceptionPolicy.TryGetNativeRuntimeSurfaceFailure(
                                tokenInspectionFailure,
                                out _)
                                ? FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable
                                : FoxRunRos2RegistrationError.BackendFailure,
                        tokenInspectionFailure == null
                            ? "Native backend returned no usable subscription token."
                            : tokenInspectionDiagnostic);
                    rollbackToken = returnedToken;
                }
                else
                {
                    _token = returnedToken;
                    Volatile.Write(ref _activeRegistrationAttempt, registrationAttempt);
                    _lastRegistration = FoxRunRos2RegistrationResult.Success();
                    Volatile.Write(
                        ref _state,
                        (int)FoxRunRos2SubscriptionBindingState.Ready);
                    result = _lastRegistration;
                }
                if (rollbackToken != null)
                    _registrationRollbackToken = rollbackToken;
                releaseAfterRegistration = TryClaimNodeReleaseUnderLock();
            }

            RollbackTokenAndReleaseNode(
                rollbackToken,
                releaseAfterRegistration);
            return result;
        }

    }
}
#endif
