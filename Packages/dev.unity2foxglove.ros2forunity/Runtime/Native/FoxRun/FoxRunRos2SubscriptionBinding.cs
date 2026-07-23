// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Typed native callback ownership, application, and teardown binding.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Diagnostics;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Lock-free fixed-window admission gate for a single generated native
    /// subscription. The packed state keeps the stopwatch-second bucket and
    /// accepted count in one compare/exchange operation so callback threads
    /// never allocate or block before generated deep copy.
    /// </summary>
    internal sealed class FoxRunRos2TransportAdmissionGate
    {
        private readonly int _maximumAcceptedPerSecond;
        private long _state;

        internal FoxRunRos2TransportAdmissionGate(int maximumAcceptedPerSecond)
        {
            _maximumAcceptedPerSecond = Math.Max(1, maximumAcceptedPerSecond);
        }

        internal bool TryAccept(long stopwatchTimestamp)
        {
            var bucket = stopwatchTimestamp / Stopwatch.Frequency;
            while (true)
            {
                var observed = Volatile.Read(ref _state);
                var observedBucket = (long)((ulong)observed >> 32);
                var observedCount = (uint)observed;
                if (observedBucket != bucket)
                {
                    var reset = Pack(bucket, 1U);
                    if (Interlocked.CompareExchange(ref _state, reset, observed) == observed)
                        return true;
                    continue;
                }

                if (observedCount >= (uint)_maximumAcceptedPerSecond)
                    return false;
                var incremented = Pack(bucket, observedCount + 1U);
                if (Interlocked.CompareExchange(ref _state, incremented, observed) == observed)
                    return true;
            }
        }

        private static long Pack(long bucket, uint count)
            => unchecked((bucket << 32) | count);
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
                FoxRunRos2QosPreset.Inherit,
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
            FoxRunRos2QosPreset qosPreset,
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
                qosPreset,
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
            FoxRunRos2QosPreset qosPreset,
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
            QosPreset = qosPreset;
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
        public FoxRunRos2QosPreset QosPreset { get; }
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
    internal sealed class FoxRunRos2SubscriptionBinding<T> : IFoxRunRos2HostBinding, IFoxRunRos2TimedHostBinding
        where T : ROS2.Message, new()
    {
        private const long AcceptanceArming = -1;
        private const long AcceptanceCompleting = -2;
        private const long AcceptanceCompleted = -3;
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
        private readonly FoxRunRos2TransportAdmissionGate _transportAdmission;
        private readonly Func<long> _admissionTimestamp;
        private readonly IFoxRunRos2NativeBackend _backend;
        private readonly FoxRunRos2QosPreset _qosPreset;
        private readonly IFoxRunRos2NativeQosProfileFactory _qosFactory;
        private readonly FoxRunRos2OwnedLatestSlot<object> _slot;
        private readonly Func<T, object> _copyBorrowed;
        private readonly Action<object> _applyOwned;
        private readonly Action<object> _disposeOwned;
        private readonly Func<object, bool> _clearOwned;
        private readonly Func<object, object, FoxRunRos2PendingDecision> _decideOwned;
        private IFoxRunRos2NativeSubscriptionToken _token;
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
        private bool _nodeReleaseClaimed;
        private bool _teardownFailureRecorded;
        private bool _preserveTerminalFailure;
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
            FoxRunRos2QosPreset qosPreset = FoxRunRos2QosPreset.Default,
            IFoxRunRos2NativeQosProfileFactory qosFactory = null,
            Func<T, bool> dropBeforeApply = null,
            Func<T, T, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null,
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
            _transportAdmission = new FoxRunRos2TransportAdmissionGate(
                transportAdmissionRateLimitHz);
            _admissionTimestamp = admissionTimestamp ?? Stopwatch.GetTimestamp;
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _qosPreset = qosPreset;
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
        public long ReceivedCount => _slot.ReceivedCount;
        public long ReplacedCount => _slot.ReplacedCount;
        public long AppliedCount => _slot.AppliedCount;
        public long RejectedAfterStopCount => _slot.RejectedAfterStopCount;
        public long CopyFailedCount => _slot.CopyFailedCount;
        public long StaleCallbackCount => Interlocked.Read(ref _staleCallbacks);
        internal long SameOriginDropCount => Interlocked.Read(ref _sameOriginDrops);
        internal long TransportAdmissionDropCount =>
            Interlocked.Read(ref _transportAdmissionDrops);

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
                    return FoxRunRos2RegistrationResult.Failure(
                        FoxRunRos2RegistrationError.Stopped,
                        "Native subscription binding is stopped.");

                var state = State;
                if (state == FoxRunRos2SubscriptionBindingState.Ready
                    || state == FoxRunRos2SubscriptionBindingState.Receiving)
                    return _lastRegistration;
                if (state == FoxRunRos2SubscriptionBindingState.Unsupported
                    || state == FoxRunRos2SubscriptionBindingState.Failed)
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
            catch (Exception exception)
            {
                return CompleteWithoutBackend(
                    FoxRunRos2RegistrationError.BackendFailure,
                    DescribeException(exception));
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
                var qosResult = _qosFactory == null
                    ? Ros2ForUnityNativeQosMapper.TryCreate(_qosPreset, out qosProfile)
                    : Ros2ForUnityNativeQosMapper.TryCreate(_qosPreset, _qosFactory, out qosProfile);
                if (!qosResult.Succeeded)
                    return CompleteWithoutBackend(qosResult.Error, qosResult.Diagnostic);

                backendResult = _backend.Register<T>(
                    Contract,
                    qosProfile,
                    borrowed => OnBorrowedMessage(registrationAttempt, borrowed));
            }
            catch (Exception exception)
            {
                registrationFailure = exception;
            }
            finally
            {
                try
                {
                    qosProfile?.Dispose();
                }
                catch (Exception exception)
                {
                    if (registrationFailure == null)
                        registrationFailure = exception;
                }
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
                catch (Exception exception)
                {
                    tokenInspectionFailure = exception;
                }
            }

            long generationAfter = 0;
            Exception generationAfterFailure = null;
            try
            {
                generationAfter = _activeGeneration();
            }
            catch (Exception exception)
            {
                generationAfterFailure = exception;
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
                    result = SetRegistrationFailureUnderLock(
                        FoxRunRos2RegistrationError.BackendFailure,
                        registrationFailureDiagnostic);
                    rollbackToken = returnedToken;
                }
                else if (!backendResult.Succeeded)
                {
                    result = SetRegistrationFailureUnderLock(backendResult.Error, backendResult.Diagnostic);
                }
                else if (returnedToken == null || !tokenUsable || tokenInspectionFailure != null)
                {
                    result = SetRegistrationFailureUnderLock(
                        tokenInspectionFailure == null
                            ? FoxRunRos2RegistrationError.InvalidSubscriptionToken
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
                releaseAfterRegistration = TryClaimNodeReleaseUnderLock();
            }

            RollbackToken(rollbackToken);
            ReleaseNodeIfClaimed(releaseAfterRegistration);
            return result;
        }

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
                             || _dropBeforeApply != null;
            _policyNowSeconds = nowSeconds;
            var applied = usesPolicy
                ? _slot.TryApplyLatest(_decideOwned, _applyOwned, _clearOwned)
                : _slot.TryApplyLatest(_applyOwned, _clearOwned);
            if (applied)
            {
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

            if (Contract.Policy == FoxRunPolicy.Trigger)
                return _consumeTrigger != null && _consumeTrigger()
                    ? FoxRunRos2PendingDecision.Apply
                    : FoxRunRos2PendingDecision.Defer;

            var hasApplied = applied != null;
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
                forceIntervalSec: Contract.ForceIntervalSeconds)
                ? FoxRunRos2PendingDecision.Apply
                : Contract.Policy == FoxRunPolicy.Change
                    ? FoxRunRos2PendingDecision.Drop
                    : FoxRunRos2PendingDecision.Defer;
        }

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
                    _qosPreset,
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

        private void StopCore(FoxRunRos2RegistrationResult? primaryFailure)
        {
            IFoxRunRos2NativeSubscriptionToken token = null;
            var beginStop = false;
            lock (_lifecycleLock)
            {
                if (_slotCleanupComplete || _stopCleanupInProgress)
                    return;
                _stopCleanupInProgress = true;
                if (Volatile.Read(ref _stopping) == 0)
                {
                    beginStop = true;
                    Volatile.Write(ref _stopping, 1);
                    Interlocked.Exchange(ref _acceptanceAdmission, 0);
                    Interlocked.Exchange(ref _acceptanceCompletingEpoch, 0);
                    Volatile.Write(ref _activeRegistrationAttempt, 0L);
                    token = _token;
                    _token = null;
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

            if (beginStop)
            {
                try
                {
                    _slot.BeginStop(_clearOwned);
                }
                catch (Exception exception)
                {
                    RecordTeardownFailure("begin owned-message drain", exception);
                }

                if (token != null)
                {
                    try
                    {
                        _backend.RemoveSubscription(token);
                    }
                    catch (Exception exception)
                    {
                        RecordTeardownFailure("remove subscription", exception);
                    }
                }
            }

            try
            {
                _slot.Stop(_clearOwned);
            }
            catch (Exception exception)
            {
                RecordTeardownFailure("drain owned messages", exception);
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
            ReleaseNodeIfClaimed(releaseNode);
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

        private bool IsActiveGeneration(long callerGeneration)
        {
            if (callerGeneration != SessionGeneration)
                return false;
            try
            {
                return _activeGeneration() == SessionGeneration;
            }
            catch
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
            catch
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
            catch
            {
                generation = 0;
                return false;
            }
        }

        private bool TryClaimNodeReleaseUnderLock()
        {
            if (Volatile.Read(ref _stopping) == 0
                || !_slotCleanupComplete
                || _registrationInFlight
                || _nodeReleaseClaimed)
                return false;
            _nodeReleaseClaimed = true;
            return true;
        }

        private void RollbackToken(IFoxRunRos2NativeSubscriptionToken token)
        {
            if (token == null)
                return;
            try
            {
                _backend.RemoveSubscription(token);
            }
            catch (Exception exception)
            {
                RecordTeardownFailure("rollback subscription", exception);
            }
        }

        private void ReleaseNodeIfClaimed(bool claimed)
        {
            if (!claimed)
                return;
            try
            {
                _backend.ReleaseNodeOwnership();
            }
            catch (Exception exception)
            {
                RecordTeardownFailure("release node", exception);
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
            => exception.GetType().Name + ": " + exception.Message;
    }
}
#endif
