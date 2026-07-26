// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Direct bounded-stream callback ownership and ordered teardown.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Owns one native stream subscription. Admission happens while the
    /// transport value is still borrowed; accepted values are materialized
    /// once and transferred directly into the stream without a latest slot.
    /// </summary>
    internal sealed class FoxRunRos2StreamSubscriptionBinding<TTransport, TSample>
        : IFoxRunRos2HostBinding
        where TTransport : ROS2.Message, new()
    {
        private const int CleanupNotStarted = 0;
        private const int CleanupInProgress = 1;
        private const int CleanupFinished = 2;
        private readonly object _lifecycleLock = new object();
        private readonly Func<long> _activeGeneration;
        private readonly long _maximumCopyBytes;
        private readonly Func<bool> _tryAdmitInput;
        private readonly Func<TTransport, FoxRunRos2CopyContext, TSample> _materializeOwned;
        private readonly Action<TSample> _transferOwned;
        private readonly Action _clearOwned;
        private readonly Action<Action> _dispatchCleanup;
        private readonly Func<TTransport, bool> _dropBorrowed;
        private readonly IFoxRunRos2NativeBackend _backend;
        private readonly FoxRunResolvedQos _qos;
        private readonly IFoxRunRos2NativeQosProfileFactory _qosFactory;
        private IFoxRunRos2NativeSubscriptionToken _token;
        private FoxRunRos2RegistrationResult _lastRegistration;
        private int _state;
        private int _admissionOpen;
        private int _callbacksInFlight;
        private int _stopping;
        private int _cleanupComplete;
        private int _cleanupDispatchPending;
        private int _failedRegistrationCleanupPending;
        private int _nodeReleased;
        private bool _teardownFailureRecorded;
        private bool _registrationInFlight;
        private ExceptionDispatchInfo _failedRegistrationFatal;
        private long _registrationAttemptSequence;
        private long _activeRegistrationAttempt;
        private long _received;
        private long _copyFailed;
        private long _staleCallbacks;
        private long _sameOriginDrops;

        internal FoxRunRos2StreamSubscriptionBinding(
            FoxRunRos2GeneratedContract contract,
            long sessionGeneration,
            Func<long> activeGeneration,
            long maximumCopyBytes,
            Func<bool> tryAdmitInput,
            Func<TTransport, FoxRunRos2CopyContext, TSample> materializeOwned,
            Action<TSample> transferOwned,
            Action clearOwned,
            Action<Action> dispatchCleanup,
            IFoxRunRos2NativeBackend backend,
            FoxRunResolvedQos? qos = null,
            IFoxRunRos2NativeQosProfileFactory qosFactory = null,
            Func<TTransport, bool> dropBorrowed = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (sessionGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(sessionGeneration));
            if (maximumCopyBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCopyBytes));
            SessionGeneration = sessionGeneration;
            _activeGeneration = activeGeneration ?? throw new ArgumentNullException(nameof(activeGeneration));
            _maximumCopyBytes = maximumCopyBytes;
            _tryAdmitInput = tryAdmitInput ?? throw new ArgumentNullException(nameof(tryAdmitInput));
            _materializeOwned = materializeOwned ?? throw new ArgumentNullException(nameof(materializeOwned));
            _transferOwned = transferOwned ?? throw new ArgumentNullException(nameof(transferOwned));
            _clearOwned = clearOwned ?? throw new ArgumentNullException(nameof(clearOwned));
            _dispatchCleanup = dispatchCleanup ?? throw new ArgumentNullException(nameof(dispatchCleanup));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _qos = qos ?? FoxRunResolvedQos.Default;
            _qosFactory = qosFactory;
            _dropBorrowed = dropBorrowed;
            _state = (int)FoxRunRos2SubscriptionBindingState.Configured;
            _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.RuntimeUnavailable,
                "Native stream subscription has not been registered.");
        }

        public FoxRunRos2GeneratedContract Contract { get; }
        public string ContractId => Contract.Id;
        public long SessionGeneration { get; }
        public FoxRunRos2SubscriptionBindingState State
            => (FoxRunRos2SubscriptionBindingState)Volatile.Read(ref _state);

        public void WaitForRuntime()
        {
            Interlocked.CompareExchange(
                ref _state,
                (int)FoxRunRos2SubscriptionBindingState.WaitingForRuntime,
                (int)FoxRunRos2SubscriptionBindingState.Configured);
        }

        public FoxRunRos2RegistrationResult TryRegister()
        {
            long registrationAttempt;
            lock (_lifecycleLock)
            {
                if (Volatile.Read(ref _stopping) != 0)
                    return StoppedResult();
                if (_token != null)
                    return _lastRegistration;
                if (_registrationInFlight)
                    return FoxRunRos2RegistrationResult.Failure(
                        FoxRunRos2RegistrationError.RegistrationRejected,
                        "Native stream subscription registration is already in progress.");
                _registrationInFlight = true;
                registrationAttempt = ++_registrationAttemptSequence;
                Volatile.Write(ref _activeRegistrationAttempt, registrationAttempt);
                Volatile.Write(ref _admissionOpen, 1);
                Volatile.Write(
                    ref _state,
                    (int)FoxRunRos2SubscriptionBindingState.WaitingForRuntime);
            }

            long generationBefore;
            try
            {
                generationBefore = _activeGeneration();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                return CompleteFailedRegistration(
                    FoxRunRos2RegistrationError.BackendFailure,
                    exception.GetType().Name,
                    null);
            }
            catch (Exception exception)
            {
                RethrowRegistrationFatal(exception, null);
                throw;
            }
            if (generationBefore != SessionGeneration)
            {
                return CompleteFailedRegistration(
                    FoxRunRos2RegistrationError.StaleGeneration,
                    string.Empty,
                    null);
            }

            FoxRunRos2NativeBackendRegistration backendResult = default;
            IFoxRunRos2NativeQosProfile qosProfile = null;
            Exception registrationFailure = null;
            try
            {
                try
                {
                    var qosResult = _qosFactory == null
                        ? Ros2ForUnityNativeQosMapper.TryCreate(_qos, out qosProfile)
                        : Ros2ForUnityNativeQosMapper.TryCreate(_qos, _qosFactory, out qosProfile);
                    if (!qosResult.Succeeded)
                    {
                        return CompleteFailedRegistration(
                            qosResult.Error,
                            qosResult.Diagnostic,
                            null);
                    }

                    backendResult = _backend.Register<TTransport>(
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
                        registrationFailure ??= exception;
                    }
                }
            }
            catch (Exception exception)
            {
                var fatalToken = backendResult.Succeeded ? backendResult.Token : null;
                RethrowRegistrationFatal(exception, fatalToken);
                throw;
            }

            var returnedToken = backendResult.Succeeded ? backendResult.Token : null;
            var tokenUsable = false;
            Exception tokenInspectionFailure = null;
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

            FoxRunRos2RegistrationResult result;
            IFoxRunRos2NativeSubscriptionToken rollbackToken = null;
            lock (_lifecycleLock)
            {
                if (Volatile.Read(ref _stopping) != 0)
                {
                    result = StoppedResult();
                    rollbackToken = returnedToken;
                }
                else if (generationAfterFailure != null || registrationFailure != null)
                {
                    result = SetRegistrationFailureUnderLock(
                        FoxRunRos2RegistrationError.BackendFailure,
                        (generationAfterFailure ?? registrationFailure).GetType().Name);
                    rollbackToken = returnedToken;
                }
                else if (generationAfter != SessionGeneration)
                {
                    result = SetRegistrationFailureUnderLock(
                        FoxRunRos2RegistrationError.StaleGeneration,
                        string.Empty);
                    rollbackToken = returnedToken;
                }
                else if (!backendResult.Succeeded)
                {
                    result = SetRegistrationFailureUnderLock(
                        backendResult.Error,
                        backendResult.Diagnostic);
                }
                else if (returnedToken == null || !tokenUsable || tokenInspectionFailure != null)
                {
                    result = SetRegistrationFailureUnderLock(
                        tokenInspectionFailure == null
                            ? FoxRunRos2RegistrationError.InvalidSubscriptionToken
                            : FoxRunRos2RegistrationError.BackendFailure,
                        tokenInspectionFailure?.GetType().Name ?? string.Empty);
                    rollbackToken = returnedToken;
                }
                else
                {
                    _registrationInFlight = false;
                    _token = returnedToken;
                    _lastRegistration = FoxRunRos2RegistrationResult.Success();
                    Volatile.Write(
                        ref _state,
                        (int)FoxRunRos2SubscriptionBindingState.Ready);
                    return _lastRegistration;
                }

                Volatile.Write(ref _activeRegistrationAttempt, 0L);
                Volatile.Write(ref _admissionOpen, 0);
            }

            CleanupFailedRegistration(rollbackToken);
            return result;
        }

        public bool TryApplyLatest(long activeSessionGeneration) => false;

        public void RecordApplyFailure(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));
            Stop();
        }

        public bool TryGetSnapshot(
            long activeSessionGeneration,
            out FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            if (activeSessionGeneration != SessionGeneration)
            {
                snapshot = default;
                return false;
            }
            FoxRunRos2RegistrationResult registration;
            lock (_lifecycleLock)
                registration = _lastRegistration;
            snapshot = new FoxRunRos2SubscriptionBindingSnapshot(
                Contract,
                _qos,
                SessionGeneration,
                State,
                registration.Error,
                registration.Diagnostic,
                Interlocked.Read(ref _received),
                0,
                0,
                0,
                0,
                Interlocked.Read(ref _copyFailed),
                Interlocked.Read(ref _staleCallbacks),
                0,
                0,
                Interlocked.Read(ref _sameOriginDrops));
            return true;
        }

        public FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            snapshot = default;
            return Volatile.Read(ref _stopping) == 0
                ? FoxRunRos2AcceptanceArmStatus.EndpointUnavailable
                : FoxRunRos2AcceptanceArmStatus.Stopped;
        }

        public bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }

        public bool EndAcceptanceAttempt(long epoch) => false;

        public bool TryCompleteAcceptanceAttempt(
            long epoch,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }

        public void Stop()
        {
            var beginStop = Interlocked.CompareExchange(ref _stopping, 1, 0) == 0;
            if (beginStop)
                Volatile.Write(ref _admissionOpen, 0);

            var fatal = TryCompleteFailedRegistrationCleanupForStop();
            if (!beginStop)
            {
                var retryFatal = TryCompleteStoppedCleanup();
                if (retryFatal != null)
                    RecordTeardownFailure("stream cleanup retry", retryFatal);
                if (Volatile.Read(ref _cleanupComplete) != CleanupFinished)
                    RecordPendingCleanup();
                fatal ??= retryFatal;
                if (fatal != null)
                    ExceptionDispatchInfo.Capture(fatal).Throw();
                return;
            }

            IFoxRunRos2NativeSubscriptionToken token;
            var registrationPending = false;
            lock (_lifecycleLock)
            {
                token = _token;
                _token = null;
                Volatile.Write(ref _activeRegistrationAttempt, 0L);
                Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.Stopped);
                if (!_teardownFailureRecorded)
                    _lastRegistration = StoppedResult();
                if (_registrationInFlight)
                {
                    RecordPendingCleanupUnderLock();
                    registrationPending = true;
                }
            }
            if (registrationPending)
            {
                if (fatal != null)
                    ExceptionDispatchInfo.Capture(fatal).Throw();
                return;
            }

            if (token != null)
            {
                try
                {
                    _backend.RemoveSubscription(token);
                }
                catch (Exception exception)
                {
                    fatal = exception;
                    RecordTeardownFailure("remove stream subscription", exception);
                }
            }

            var cleanupFatal = TryCompleteStoppedCleanup();
            if (cleanupFatal != null)
                RecordTeardownFailure("stream cleanup", cleanupFatal);
            if (Volatile.Read(ref _cleanupComplete) != CleanupFinished)
                RecordPendingCleanup();
            fatal ??= cleanupFatal;
            if (fatal != null)
                ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        private Exception TryCompleteFailedRegistrationCleanupForStop()
        {
            try
            {
                CompleteFailedRegistrationCleanup(throwFatal: true);
                return null;
            }
            catch (Exception exception)
            {
                RecordTeardownFailure(
                    "failed-registration stream cleanup retry",
                    exception);
                return exception;
            }
        }

        private void OnBorrowedMessage(long registrationAttempt, TTransport borrowed)
        {
            Interlocked.Increment(ref _callbacksInFlight);
            try
            {
                if (Volatile.Read(ref _admissionOpen) == 0)
                    return;
                if (registrationAttempt == 0
                    || registrationAttempt != Volatile.Read(ref _activeRegistrationAttempt))
                {
                    Interlocked.Increment(ref _staleCallbacks);
                    return;
                }
                if (!IsActiveGeneration())
                {
                    Interlocked.Increment(ref _staleCallbacks);
                    return;
                }
                if (_dropBorrowed != null && _dropBorrowed(borrowed))
                {
                    Interlocked.Increment(ref _sameOriginDrops);
                    return;
                }
                if (!_tryAdmitInput())
                    return;

                TSample owned;
                var context = FoxRunRos2CopyContext.Rent(_maximumCopyBytes);
                try
                {
                    owned = _materializeOwned(borrowed, context);
                    if (ReferenceEquals(owned, null))
                        throw new InvalidOperationException(
                            "Generated ROS2 stream materializer must not return null.");
                    if (ReferenceEquals(owned, borrowed))
                    {
                        throw new InvalidOperationException(
                            "Generated ROS2 stream materializer must not retain the callback-owned message.");
                    }
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    Interlocked.Increment(ref _copyFailed);
                    return;
                }
                finally
                {
                    context.Return();
                }

                Interlocked.Increment(ref _received);
                try
                {
                    // Invocation itself transfers ownership, including when
                    // the destination reports failure by throwing.
                    _transferOwned(owned);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // Never dispose here: transfer invocation owns the sample.
                }
                Interlocked.CompareExchange(
                    ref _state,
                    (int)FoxRunRos2SubscriptionBindingState.Receiving,
                    (int)FoxRunRos2SubscriptionBindingState.Ready);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                // No generated or stream failure may unwind into the ROS executor.
            }
            finally
            {
                if (Interlocked.Decrement(ref _callbacksInFlight) == 0
                    && (Volatile.Read(ref _stopping) != 0
                        || Volatile.Read(ref _failedRegistrationCleanupPending) != 0))
                {
                    DispatchDeferredCleanup();
                }
            }
        }

        private void DispatchDeferredCleanup()
        {
            if (Interlocked.CompareExchange(ref _cleanupDispatchPending, 1, 0) != 0)
                return;
            try
            {
                // The callback producer only signals readiness. Generated
                // disposers and node release must run through the Unity host
                // thread dispatcher.
                _dispatchCleanup(() =>
                {
                    Interlocked.Exchange(ref _cleanupDispatchPending, 0);
                    CompleteFailedRegistrationCleanup(throwFatal: false);
                    var cleanupFatal = TryCompleteStoppedCleanup();
                    if (cleanupFatal != null)
                        RecordTeardownFailure("deferred stream cleanup", cleanupFatal);
                });
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _cleanupDispatchPending, 0);
                RecordTeardownFailure("dispatch deferred stream cleanup", exception);
                // Dispatcher failures cannot escape a native callback.
            }
        }

        private Exception TryCompleteStoppedCleanup()
        {
            if (Volatile.Read(ref _stopping) == 0
                || Volatile.Read(ref _callbacksInFlight) != 0)
                return null;
            lock (_lifecycleLock)
            {
                if (_registrationInFlight)
                    return null;
            }

            if (Interlocked.CompareExchange(
                    ref _cleanupComplete,
                    CleanupInProgress,
                    CleanupNotStarted) != CleanupNotStarted)
                return null;

            Exception fatal = null;
            try
            {
                _clearOwned();
            }
            catch (Exception exception)
            {
                fatal = exception;
            }

            try
            {
                ReleaseNodeOnce();
            }
            catch (Exception exception)
            {
                fatal ??= exception;
            }
            finally
            {
                Volatile.Write(ref _cleanupComplete, CleanupFinished);
            }
            if (fatal == null)
                MarkStoppedCleanupComplete();
            return fatal;
        }

        private bool IsActiveGeneration()
        {
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

        private FoxRunRos2RegistrationResult SetRegistrationFailureUnderLock(
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            _lastRegistration = FoxRunRos2RegistrationResult.Failure(error, diagnostic);
            Volatile.Write(
                ref _state,
                error == FoxRunRos2RegistrationError.RuntimeUnavailable
                    ? (int)FoxRunRos2SubscriptionBindingState.WaitingForRuntime
                    : (int)FoxRunRos2SubscriptionBindingState.Failed);
            return _lastRegistration;
        }

        private FoxRunRos2RegistrationResult CompleteFailedRegistration(
            FoxRunRos2RegistrationError error,
            string diagnostic,
            IFoxRunRos2NativeSubscriptionToken rollbackToken)
        {
            FoxRunRos2RegistrationResult result;
            lock (_lifecycleLock)
            {
                Volatile.Write(ref _activeRegistrationAttempt, 0L);
                Volatile.Write(ref _admissionOpen, 0);
                result = Volatile.Read(ref _stopping) != 0
                    ? StoppedResult()
                    : SetRegistrationFailureUnderLock(error, diagnostic);
            }
            CleanupFailedRegistration(rollbackToken);
            return result;
        }

        private void CleanupFailedRegistration(
            IFoxRunRos2NativeSubscriptionToken rollbackToken)
        {
            ExceptionDispatchInfo fatal = null;
            if (rollbackToken != null)
            {
                try
                {
                    _backend.RemoveSubscription(rollbackToken);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // Preserve the primary registration result.
                }
                catch (Exception exception)
                {
                    fatal = ExceptionDispatchInfo.Capture(exception);
                }
            }

            lock (_lifecycleLock)
            {
                _failedRegistrationFatal = fatal;
                Volatile.Write(ref _failedRegistrationCleanupPending, 1);
            }
            if (Volatile.Read(ref _callbacksInFlight) == 0)
                CompleteFailedRegistrationCleanup(throwFatal: true);
        }

        private void CompleteFailedRegistrationCleanup(bool throwFatal)
        {
            if (Volatile.Read(ref _callbacksInFlight) != 0
                || Interlocked.CompareExchange(
                    ref _failedRegistrationCleanupPending,
                    0,
                    1) != 1)
                return;

            ExceptionDispatchInfo fatal;
            lock (_lifecycleLock)
            {
                fatal = _failedRegistrationFatal;
                _failedRegistrationFatal = null;
            }
            try
            {
                _clearOwned();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                // Preserve the primary registration result.
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            var terminalCleanup = false;
            var terminalCleanupClaimed = false;
            lock (_lifecycleLock)
            {
                _registrationInFlight = false;
                if (fatal != null && Volatile.Read(ref _stopping) == 0)
                {
                    Volatile.Write(ref _stopping, 1);
                    Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.Failed);
                }
                terminalCleanup = Volatile.Read(ref _stopping) != 0;
                if (terminalCleanup)
                {
                    terminalCleanupClaimed = Interlocked.CompareExchange(
                        ref _cleanupComplete,
                        CleanupInProgress,
                        CleanupNotStarted) == CleanupNotStarted;
                }
            }

            if (terminalCleanup && terminalCleanupClaimed)
            {
                try
                {
                    ReleaseNodeOnce();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    // Preserve the primary registration or teardown result.
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    Volatile.Write(ref _cleanupComplete, CleanupFinished);
                }
            }
            if (terminalCleanup && terminalCleanupClaimed && fatal == null)
                MarkStoppedCleanupComplete();
            if (fatal != null && !throwFatal)
            {
                RecordTeardownFailure(
                    "deferred failed-registration cleanup",
                    fatal.SourceException);
            }
            if (throwFatal)
                fatal?.Throw();
        }

        private void RethrowRegistrationFatal(
            Exception exception,
            IFoxRunRos2NativeSubscriptionToken rollbackToken)
        {
            var primary = ExceptionDispatchInfo.Capture(exception);
            lock (_lifecycleLock)
            {
                Volatile.Write(ref _activeRegistrationAttempt, 0L);
                Volatile.Write(ref _admissionOpen, 0);
                Volatile.Write(ref _stopping, 1);
                _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.BackendFailure,
                    exception.GetType().Name);
                Volatile.Write(ref _state, (int)FoxRunRos2SubscriptionBindingState.Failed);
            }
            try
            {
                CleanupFailedRegistration(rollbackToken);
            }
            catch
            {
                // Preserve the primary fatal failure after mandatory cleanup.
            }
            primary.Throw();
        }

        private void ReleaseNodeOnce()
        {
            if (Interlocked.CompareExchange(ref _nodeReleased, 1, 0) == 0)
                _backend.ReleaseNodeOwnership();
        }

        private void RecordTeardownFailure(string stage, Exception exception)
        {
            var diagnostic = exception.GetType().Name + ": " + stage;
            lock (_lifecycleLock)
            {
                if (_teardownFailureRecorded)
                    return;
                _teardownFailureRecorded = true;
                _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.TeardownFailure,
                    diagnostic);
            }
        }

        private void RecordPendingCleanup()
        {
            lock (_lifecycleLock)
                RecordPendingCleanupUnderLock();
        }

        private void RecordPendingCleanupUnderLock()
        {
            if (_teardownFailureRecorded
                || Volatile.Read(ref _cleanupComplete) == CleanupFinished)
                return;
            _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.TeardownFailure,
                "CleanupPending");
        }

        private void MarkStoppedCleanupComplete()
        {
            lock (_lifecycleLock)
            {
                if (!_teardownFailureRecorded)
                    _lastRegistration = StoppedResult();
            }
        }

        private static FoxRunRos2RegistrationResult StoppedResult()
            => FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.Stopped,
                string.Empty);
    }
}
#endif
