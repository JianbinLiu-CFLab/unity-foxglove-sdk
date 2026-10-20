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
    /// <summary>
    /// Lock-free minimum-interval admission gate for a single generated native
    /// subscription. Spacing accepted callbacks prevents a burst at the start
    /// of a wall-clock bucket from suppressing fresher values for its remainder.
    /// </summary>
    internal sealed class FoxRunRos2TransportAdmissionGate
    {
        private const long NoAcceptedTimestamp = long.MinValue;
        private readonly long _minimumIntervalTicks;
        private long _lastAcceptedTimestamp = NoAcceptedTimestamp;

        internal FoxRunRos2TransportAdmissionGate(int maximumAcceptedPerSecond)
        {
            var normalized = Math.Max(1, maximumAcceptedPerSecond);
            _minimumIntervalTicks = normalized == int.MaxValue
                ? 0L
                : Math.Max(1L, (Stopwatch.Frequency + normalized - 1L) / normalized);
        }

        internal bool TryAccept(long stopwatchTimestamp)
        {
            if (_minimumIntervalTicks == 0L)
                return true;

            while (true)
            {
                var observed = Volatile.Read(ref _lastAcceptedTimestamp);
                if (observed != NoAcceptedTimestamp
                    && (stopwatchTimestamp <= observed
                        || stopwatchTimestamp - observed < _minimumIntervalTicks))
                    return false;

                if (Interlocked.CompareExchange(
                        ref _lastAcceptedTimestamp,
                        stopwatchTimestamp,
                        observed) == observed)
                    return true;
            }
        }
    }

    internal readonly struct FoxRunRos2SubscriptionBindingSnapshot
    {
        public FoxRunRos2SubscriptionBindingSnapshot(
            string contractId,
            long sessionGeneration,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received,
            long replaced,
            long applied,
            int pending,
            long rejectedAfterStop,
            long copyFailed,
            long staleCallbacks)
            : this(
                contractId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                FoxRunResolvedQos.Default,
                sessionGeneration,
                state,
                error,
                diagnostic,
                received,
                replaced,
                applied,
                pending,
                rejectedAfterStop,
                copyFailed,
                staleCallbacks,
                0,
                0,
                0)
        {
        }

        /// <summary>
        /// Contract-metadata overload for runtime diagnostics. The older
        /// identifier-only constructor remains for generated Phase179-B callers
        /// and tests that do not have a full generated contract object.
        /// </summary>
        public FoxRunRos2SubscriptionBindingSnapshot(
            FoxRunRos2GeneratedContract contract,
            FoxRunResolvedQos qos,
            long sessionGeneration,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received,
            long replaced,
            long applied,
            int pending,
            long rejectedAfterStop,
            long copyFailed,
            long staleCallbacks,
            long lastReceiveStopwatchTimestamp,
            long lastApplyStopwatchTimestamp,
            long sameOriginDrops = 0)
            : this(
                RequireContract(contract).Id,
                contract.Topic,
                contract.DeclaringType,
                contract.MemberName,
                contract.CanonicalRosType,
                qos,
                sessionGeneration,
                state,
                error,
                diagnostic,
                received,
                replaced,
                applied,
                pending,
                rejectedAfterStop,
                copyFailed,
                staleCallbacks,
                lastReceiveStopwatchTimestamp,
                lastApplyStopwatchTimestamp,
                sameOriginDrops)
        {
        }

        private FoxRunRos2SubscriptionBindingSnapshot(
            string contractId,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            FoxRunResolvedQos qos,
            long sessionGeneration,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received,
            long replaced,
            long applied,
            int pending,
            long rejectedAfterStop,
            long copyFailed,
            long staleCallbacks,
            long lastReceiveStopwatchTimestamp,
            long lastApplyStopwatchTimestamp,
            long sameOriginDrops)
        {
            ContractId = contractId ?? string.Empty;
            Topic = topic ?? string.Empty;
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            CanonicalRosType = canonicalRosType ?? string.Empty;
            Qos = qos;
            SessionGeneration = sessionGeneration;
            State = state;
            Error = error;
            Diagnostic = FoxRunRos2PublicDiagnostic.Describe(error);
            Received = received;
            Replaced = replaced;
            Applied = applied;
            Pending = pending;
            RejectedAfterStop = rejectedAfterStop;
            CopyFailed = copyFailed;
            StaleCallbacks = staleCallbacks;
            LastReceiveStopwatchTimestamp = lastReceiveStopwatchTimestamp;
            LastApplyStopwatchTimestamp = lastApplyStopwatchTimestamp;
            SameOriginDrops = sameOriginDrops;
        }

        public string ContractId { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public FoxRunResolvedQos Qos { get; }
        public long SessionGeneration { get; }
        public FoxRunRos2SubscriptionBindingState State { get; }
        public FoxRunRos2RegistrationError Error { get; }
        public string Diagnostic { get; }
        public long Received { get; }
        public long Replaced { get; }
        public long Applied { get; }
        public int Pending { get; }
        public long RejectedAfterStop { get; }
        public long CopyFailed { get; }
        public long StaleCallbacks { get; }
        public long LastReceiveStopwatchTimestamp { get; }
        public long LastApplyStopwatchTimestamp { get; }
        public long SameOriginDrops { get; }

        private static FoxRunRos2GeneratedContract RequireContract(
            FoxRunRos2GeneratedContract contract)
            => contract ?? throw new ArgumentNullException(nameof(contract));
    }

    /// <summary>
    /// Owns one generated closed-generic subscription. Values assigned to the
    /// component are borrowed: they remain valid only until the next successful
    /// apply or stop. User code retaining a value must deep-copy it. Stop clears
    /// the member only when it still references the framework-owned value.
    /// </summary>
    internal sealed partial class FoxRunRos2SubscriptionBinding<T> : IFoxRunRos2HostBinding, IFoxRunRos2TimedHostBinding, IFoxRunRos2DeferredCleanupStatus
        where T : ROS2.Message, new()
    {
        private const long AcceptanceArming = -1;
        private const long AcceptanceCompleting = -2;
        private const long AcceptanceCompleted = -3;
        private const long MaximumRecoverableRegistrationAttempts = 4;
        private readonly object _lifecycleLock = new object();
        private readonly Func<long> _activeGeneration;
        private readonly long _maximumCopyBytes;
        private readonly Func<T, FoxRunRos2CopyContext, T> _copy;
        private readonly Action<T> _apply;
        private readonly Action<T> _dispose;
        private readonly Func<T, bool> _clearIfOwned;
        private readonly Func<T, bool> _dropBeforeApply;
        private readonly Func<T, T, bool> _valuesEqual;
        private readonly Func<bool> _consumeTrigger;
        private readonly Func<bool> _canApply;
        private readonly FoxRunRos2TransportAdmissionGate _transportAdmission;
        private readonly Func<long> _admissionTimestamp;
        private readonly IFoxRunRos2NativeBackend _backend;
        private readonly FoxRunResolvedQos _qos;
        private readonly IFoxRunRos2NativeQosProfileFactory _qosFactory;
        private readonly FoxRunRos2OwnedLatestSlot<object> _slot;
        private readonly Func<T, object> _copyBorrowed;
        private readonly Action<object> _applyOwned;
        private readonly Action<object> _disposeOwned;
        private readonly Func<object, bool> _clearOwned;
        private readonly Func<object, object, FoxRunRos2PendingDecision> _decideOwned;
        private IFoxRunRos2NativeSubscriptionToken _token;
        private IFoxRunRos2NativeSubscriptionToken _registrationRollbackToken;
        private int _registrationRollbackInFlight;
        private int _state;
        private int _stopping;
        private long _registrationAttemptSequence;
        private long _activeRegistrationAttempt;
        private long _staleCallbacks;
        private long _lastReceiveStopwatchTimestamp;
        private long _lastApplyStopwatchTimestamp;
        private long _sameOriginDrops;
        private long _transportAdmissionDrops;
        private double _policyNowSeconds;
        private double _lastSemanticApplySeconds = double.NegativeInfinity;
        private bool _registrationInFlight;
        private bool _stopCleanupInProgress;
        private bool _slotCleanupComplete;
        private bool _cleanupPending;
        private bool _nodeReleaseClaimed;
        private bool _nodeReleaseInFlight;
        private bool _teardownFailureRecorded;
        private bool _preserveTerminalFailure;
        private bool _conditionRejectedSinceLastApply;
        private FoxRunRos2RegistrationResult _lastRegistration;
        private long _acceptanceAdmission;
        private long _acceptanceEpochSequence;
        private long _acceptanceReceived;
        private long _acceptanceReplaced;
        private long _acceptanceApplied;
        private int _acceptanceCallbacksInFlight;
        private int _acceptanceArmingCallbacksInFlight;
        private long _acceptanceArmingPublished;
        private long _acceptanceCompletingEpoch;

        public FoxRunRos2SubscriptionBinding(
            FoxRunRos2GeneratedContract contract,
            long sessionGeneration,
            Func<long> activeGeneration,
            long maximumCopyBytes,
            Func<T, FoxRunRos2CopyContext, T> copy,
            Action<T> dispose,
            Action<T> apply,
            Func<T, bool> clearIfOwned,
            IFoxRunRos2NativeBackend backend,
            FoxRunResolvedQos? qos = null,
            IFoxRunRos2NativeQosProfileFactory qosFactory = null,
            Func<T, bool> dropBeforeApply = null,
            Func<T, T, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null,
            Func<bool> canApply = null,
            int transportAdmissionRateLimitHz = int.MaxValue,
            Func<long> admissionTimestamp = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (sessionGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(sessionGeneration));
            if (maximumCopyBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCopyBytes));
            SessionGeneration = sessionGeneration;
            _activeGeneration = activeGeneration ?? throw new ArgumentNullException(nameof(activeGeneration));
            _maximumCopyBytes = maximumCopyBytes;
            _copy = copy ?? throw new ArgumentNullException(nameof(copy));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _clearIfOwned = clearIfOwned ?? throw new ArgumentNullException(nameof(clearIfOwned));
            _dropBeforeApply = dropBeforeApply;
            _valuesEqual = valuesEqual;
            _consumeTrigger = consumeTrigger;
            _canApply = canApply;
            _transportAdmission = new FoxRunRos2TransportAdmissionGate(
                transportAdmissionRateLimitHz);
            _admissionTimestamp = admissionTimestamp ?? Stopwatch.GetTimestamp;
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _qos = qos ?? FoxRunResolvedQos.Default;
            _qosFactory = qosFactory;
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
            _copyBorrowed = CopyBorrowed;
            _applyOwned = ApplyOwned;
            _disposeOwned = DisposeOwned;
            _clearOwned = ClearOwned;
            _decideOwned = DecideOwned;
            _slot = new FoxRunRos2OwnedLatestSlot<object>(_disposeOwned);
            _state = (int)FoxRunRos2SubscriptionBindingState.Configured;
            _lastRegistration = FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.RuntimeUnavailable,
                "Native subscription has not been registered.");
        }

        public FoxRunRos2GeneratedContract Contract { get; }
        public string ContractId => Contract.Id;
        public long SessionGeneration { get; }
        public FoxRunRos2SubscriptionBindingState State
            => (FoxRunRos2SubscriptionBindingState)Volatile.Read(ref _state);
        public bool CanRetryRegistration
        {
            get
            {
                lock (_lifecycleLock)
                    return CanRetryRegistrationUnderLock();
            }
        }
        public long ReceivedCount => _slot.ReceivedCount;
        public long ReplacedCount => _slot.ReplacedCount;
        public long AppliedCount => _slot.AppliedCount;
        public long RejectedAfterStopCount => _slot.RejectedAfterStopCount;
        public long CopyFailedCount => _slot.CopyFailedCount;
        public long StaleCallbackCount => Interlocked.Read(ref _staleCallbacks);
        internal long SameOriginDropCount => Interlocked.Read(ref _sameOriginDrops);
        internal long TransportAdmissionDropCount =>
            Interlocked.Read(ref _transportAdmissionDrops);

        bool IFoxRunRos2DeferredCleanupStatus.CleanupComplete
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _slotCleanupComplete
                           && _token == null
                           && _registrationRollbackToken == null
                           && _nodeReleaseClaimed;
                }
            }
        }

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
