// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Hidden main-thread host for generated native FoxRun subscriptions.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.Profiling;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal interface IFoxRunRos2SubscriptionHostedCleanup
    {
        bool CleanupComplete { get; }
        void Stop();
    }

    internal interface IFoxRunRos2DeferredCleanupStatus
    {
        bool CleanupComplete { get; }
    }

    internal sealed class FoxRunRos2HostCleanupQueue
    {
        private readonly int _hostThreadId;
        private readonly SynchronizationContext _hostContext;
        private readonly Action<Exception> _reportFailure;
        private readonly ConcurrentQueue<Action> _pending =
            new ConcurrentQueue<Action>();
        private int _hostDrainPosted;

        internal FoxRunRos2HostCleanupQueue(int hostThreadId)
            : this(hostThreadId, SynchronizationContext.Current, null)
        {
        }

        internal FoxRunRos2HostCleanupQueue(
            int hostThreadId,
            SynchronizationContext hostContext,
            Action<Exception> reportFailure)
        {
            if (hostThreadId <= 0)
                throw new ArgumentOutOfRangeException(nameof(hostThreadId));
            _hostThreadId = hostThreadId;
            _hostContext = hostContext;
            _reportFailure = reportFailure;
        }

        internal void Dispatch(Action cleanup)
        {
            if (cleanup == null)
                throw new ArgumentNullException(nameof(cleanup));
            if (Thread.CurrentThread.ManagedThreadId == _hostThreadId)
            {
                cleanup();
                return;
            }
            _pending.Enqueue(cleanup);
            ScheduleHostDrain();
        }

        internal int Drain(Action<Exception> reportFailure)
        {
            if (Thread.CurrentThread.ManagedThreadId != _hostThreadId)
            {
                throw new InvalidOperationException(
                    "Deferred ROS2 stream cleanup must drain on the Unity host thread.");
            }

            var drained = 0;
            ExceptionDispatchInfo fatal = null;
            while (_pending.TryDequeue(out var cleanup))
            {
                drained++;
                try
                {
                    cleanup();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    try
                    {
                        reportFailure?.Invoke(exception);
                    }
                    catch (Exception reportException) when (
                        FoxRunRos2NativeExceptionPolicy.IsRecoverable(reportException))
                    {
                        // Diagnostics cannot interrupt remaining cleanup.
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }
            fatal?.Throw();
            return drained;
        }

        internal bool DrainUntil(
            Func<bool> cleanupComplete,
            TimeSpan timeout,
            Action<Exception> reportFailure)
        {
            if (cleanupComplete == null)
                throw new ArgumentNullException(nameof(cleanupComplete));
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                Drain(reportFailure);
                if (cleanupComplete())
                    return true;
                if (stopwatch.Elapsed >= timeout)
                {
                    Drain(reportFailure);
                    return cleanupComplete();
                }
                Thread.Sleep(1);
            }
        }

        private void ScheduleHostDrain()
        {
            if (_hostContext == null
                || Interlocked.CompareExchange(ref _hostDrainPosted, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _hostContext.Post(_ => DrainFromHostContext(), null);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _hostDrainPosted, 0);
                ReportFailure(exception);
            }
        }

        private void DrainFromHostContext()
        {
            Interlocked.Exchange(ref _hostDrainPosted, 0);
            try
            {
                Drain(_reportFailure);
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
            if (!_pending.IsEmpty)
                ScheduleHostDrain();
        }

        private void ReportFailure(Exception exception)
        {
            try
            {
                _reportFailure?.Invoke(exception);
            }
            catch (Exception)
            {
                // A diagnostic callback cannot strand remaining cleanup.
            }
        }
    }

    internal static class FoxRunRos2RegistrationIsolation
    {
        internal static bool TryRun(Action registration, Action<Exception> onFailure)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));
            if (onFailure == null)
                throw new ArgumentNullException(nameof(onFailure));
            try
            {
                registration();
                return true;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                onFailure(exception);
                return false;
            }
        }
    }

    internal sealed class FoxRunRos2BootstrapRetryState
    {
        internal bool HasCreatedHost { get; private set; }

        internal bool ShouldCreateHost(bool canBootstrap, bool shuttingDown)
        {
            if (HasCreatedHost || shuttingDown || !canBootstrap)
                return false;
            HasCreatedHost = true;
            return true;
        }

        internal void RecordCreateFailed() => HasCreatedHost = false;
    }

    /// <summary>
    /// Managed-only session generation captured by executor bindings. Native
    /// callbacks read this state without retaining or reacquiring the Unity host.
    /// </summary>
    internal sealed class FoxRunRos2ActiveSessionState
    {
        private long _generation = -1;
        private int _active;

        internal void Activate(long generation)
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            Interlocked.Exchange(ref _generation, generation);
            Volatile.Write(ref _active, 1);
        }

        internal void Deactivate()
        {
            Volatile.Write(ref _active, 0);
            Interlocked.Exchange(ref _generation, -1);
        }

        internal long ReadGeneration()
        {
            if (Volatile.Read(ref _active) == 0)
                return -1;
            var generation = Interlocked.Read(ref _generation);
            return Volatile.Read(ref _active) != 0 ? generation : -1;
        }
    }

    /// <summary>
    /// Main-thread registration guard that captures only a Scene value and the
    /// managed session token. It deliberately does not retain the Unity host.
    /// </summary>
    internal sealed class FoxRunRos2NativeRuntimeAdmission
    {
        private readonly FoxRunRos2ActiveSessionState _activeSession;
        private readonly UnityEngine.SceneManagement.Scene _ownerScene;

        internal FoxRunRos2NativeRuntimeAdmission(
            FoxRunRos2ActiveSessionState activeSession,
            UnityEngine.SceneManagement.Scene ownerScene)
        {
            _activeSession = activeSession ?? throw new ArgumentNullException(nameof(activeSession));
            _ownerScene = ownerScene;
        }

        internal bool CanUseNativeRuntimeNow()
            => _activeSession.ReadGeneration() >= 0
               && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                   _ownerScene);
    }

    internal static class FoxRunRos2ApplyIsolation
    {
        internal static bool TryRun(
            IFoxRunRos2HostBinding binding,
            long generation,
            out Exception failure)
            => TryRun(
                binding,
                generation,
                (double)System.Diagnostics.Stopwatch.GetTimestamp()
                / System.Diagnostics.Stopwatch.Frequency,
                out failure);

        internal static bool TryRun(
            IFoxRunRos2HostBinding binding,
            long generation,
            double nowSeconds,
            out Exception failure)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            failure = null;
            try
            {
                return binding is IFoxRunRos2TimedHostBinding timed
                    ? timed.TryApplyLatest(generation, nowSeconds)
                    : binding.TryApplyLatest(generation);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                binding.RecordApplyFailure(exception);
                failure = exception;
                return false;
            }
        }
    }

    internal sealed class FoxRunRos2ApplyRateGate
    {
        private readonly double _periodSeconds;
        private double _nextAllowedAt = double.NegativeInfinity;

        internal FoxRunRos2ApplyRateGate(double rateLimitHz)
        {
            if (double.IsNaN(rateLimitHz)
                || double.IsInfinity(rateLimitHz)
                || rateLimitHz <= 0d)
                throw new ArgumentOutOfRangeException(nameof(rateLimitHz));
            _periodSeconds = 1.0 / rateLimitHz;
        }

        internal bool TryAcquire(double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds))
                return false;
            if (nowSeconds + 1e-12 < _nextAllowedAt)
                return false;
            _nextAllowedAt = nowSeconds + _periodSeconds;
            return true;
        }

        internal bool TryExecute(double nowSeconds, Func<bool> apply)
        {
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds)
                || nowSeconds + 1e-12 < _nextAllowedAt)
                return false;
            if (!apply())
                return false;
            _nextAllowedAt = nowSeconds + _periodSeconds;
            return true;
        }

        internal bool IsAllowed(double nowSeconds)
            => !double.IsNaN(nowSeconds)
               && !double.IsInfinity(nowSeconds)
               && nowSeconds + 1e-12 >= _nextAllowedAt;

        internal void MarkApplied(double nowSeconds)
        {
            if (!IsAllowed(nowSeconds))
                throw new InvalidOperationException("Apply-rate permit is not currently available.");
            _nextAllowedAt = nowSeconds + _periodSeconds;
        }
    }

    internal sealed class FoxRunRos2BoundedRetryGate
    {
        private readonly int _maximumBurstAttempts;
        private readonly double _cooldownSeconds;
        private int _failuresInBurst;
        private double _retryAt = double.NegativeInfinity;

        internal FoxRunRos2BoundedRetryGate(int maximumBurstAttempts, double cooldownSeconds)
        {
            if (maximumBurstAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumBurstAttempts));
            if (cooldownSeconds < 0 || double.IsNaN(cooldownSeconds) || double.IsInfinity(cooldownSeconds))
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
            _maximumBurstAttempts = maximumBurstAttempts;
            _cooldownSeconds = cooldownSeconds;
        }

        internal bool TryBegin(double nowSeconds)
            => !double.IsNaN(nowSeconds)
               && !double.IsInfinity(nowSeconds)
               && nowSeconds >= _retryAt;

        internal void RecordFailure(double nowSeconds)
        {
            _failuresInBurst++;
            if (_failuresInBurst < _maximumBurstAttempts)
                return;
            _failuresInBurst = 0;
            _retryAt = nowSeconds + _cooldownSeconds;
        }

        internal void RecordSuccess()
        {
            _failuresInBurst = 0;
            _retryAt = double.NegativeInfinity;
        }
    }

    internal readonly struct FoxRunRos2DiscoveryKey : IComparable<FoxRunRos2DiscoveryKey>
    {
        internal FoxRunRos2DiscoveryKey(
            string sourceType,
            int sourceInstanceId,
            string topic,
            string member)
        {
            SourceType = sourceType ?? string.Empty;
            SourceInstanceId = sourceInstanceId;
            Topic = topic ?? string.Empty;
            Member = member ?? string.Empty;
        }

        internal string SourceType { get; }
        internal int SourceInstanceId { get; }
        internal string Topic { get; }
        internal string Member { get; }

        public int CompareTo(FoxRunRos2DiscoveryKey other)
        {
            var order = string.CompareOrdinal(SourceType, other.SourceType);
            if (order != 0)
                return order;
            order = SourceInstanceId.CompareTo(other.SourceInstanceId);
            if (order != 0)
                return order;
            order = string.CompareOrdinal(Topic, other.Topic);
            return order != 0 ? order : string.CompareOrdinal(Member, other.Member);
        }

        public override string ToString()
            => SourceType + "|" + SourceInstanceId + "|" + Topic + "|" + Member;
    }

    internal static class FoxRunRos2SourceDiscovery
    {
        internal static bool TryGet(
            MonoBehaviour behaviour,
            out IFoxRunRos2SubscriptionSource source)
        {
            source = behaviour as IFoxRunRos2SubscriptionSource;
            return behaviour != null && behaviour.isActiveAndEnabled && source != null;
        }

        internal static bool TryGetCustom(
            MonoBehaviour behaviour,
            out IFoxRunRos2CustomSubscriptionSource source)
        {
            source = behaviour as IFoxRunRos2CustomSubscriptionSource;
            return behaviour != null && behaviour.isActiveAndEnabled && source != null;
        }
    }

    internal enum FoxRunRos2ContractActivationDisposition
    {
        Rejected = 0,
        NotApplicable = 1,
        Active = 2
    }

    internal static class FoxRunRos2ContractActivation
    {
        internal static bool TryResolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            out FoxRunResolvedQos qos,
            out string diagnostic)
            => Resolve(
                   contract,
                   policy,
                   out qos,
                   out _,
                   out diagnostic)
               == FoxRunRos2ContractActivationDisposition.Active;

        internal static bool TryResolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            out FoxRunResolvedQos qos,
            out FoxRunRos2RegistrationError error,
            out string diagnostic)
            => Resolve(contract, policy, out qos, out error, out diagnostic)
               == FoxRunRos2ContractActivationDisposition.Active;

        internal static FoxRunRos2ContractActivationDisposition Resolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            out FoxRunResolvedQos qos,
            out FoxRunRos2RegistrationError error,
            out string diagnostic)
        {
            qos = FoxRunResolvedQos.Default;
            error = FoxRunRos2RegistrationError.RegistrationRejected;
            if (contract == null)
            {
                diagnostic = "Generated ROS2 contract is missing.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (!contract.HasCompleteMetadata)
            {
                diagnostic = "Generated ROS2 contract does not carry complete native metadata.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface
                && !contract.HasCompleteCustomMetadata)
            {
                diagnostic = "Generated custom ROS2 contract does not carry complete interface metadata.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (policy == null || !policy.SubscriptionsEnabled)
            {
                diagnostic = "FoxRun subscriptions are disabled for the captured session.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (contract.Source != 0
                && !Enum.IsDefined(typeof(FoxRunEndpoint), contract.Source))
            {
                diagnostic = "Generated ROS2 contract has an invalid Source declaration.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (!Enum.IsDefined(typeof(FoxRunFlow), contract.Mode))
            {
                diagnostic = "Generated ROS2 contract has an invalid mode declaration.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (!Enum.IsDefined(typeof(FoxRunPolicy), contract.Policy)
                || float.IsNaN(contract.Hz)
                || float.IsInfinity(contract.Hz)
                || contract.Hz < 0f
                || float.IsNaN(contract.HeartbeatIntervalSeconds)
                || float.IsInfinity(contract.HeartbeatIntervalSeconds)
                || contract.HeartbeatIntervalSeconds < 0f
                || (contract.HasExplicitHz && contract.Hz <= 0f)
                || (contract.Policy == FoxRunPolicy.Trigger && contract.HasExplicitHz))
            {
                diagnostic = "Generated ROS2 contract has invalid update-policy metadata.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            var permitsNativePublishAndSubscribe =
                contract.Mode == FoxRunFlow.PublishAndSubscribe
                && (contract.ContractKind == FoxRunRos2GeneratedContractKind.PackagedMessage
                    || contract.HasCompleteCustomMetadata);
            if (contract.Mode != FoxRunFlow.Subscribe
                && !permitsNativePublishAndSubscribe)
            {
                diagnostic = "Native ROS2 subscriptions require Subscribe mode.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }

            var topology = FoxRunEndpointResolver.Resolve(
                contract.Mode,
                contract.Source,
                hasExplicitSource: contract.Source != 0,
                declaredTargets: 0,
                hasExplicitTargets: false,
                contract.DeclaredSubscriptionEncoding,
                hasExplicitEncoding: contract.DeclaredSubscriptionEncoding != 0,
                defaultSource: policy.DefaultSource,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: policy.FoxgloveEncoding);
            if (!topology.Success)
            {
                error = FoxRunRos2RegistrationError.RegistrationRejected;
                diagnostic = topology.DiagnosticMessage;
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (topology.Topology.Source != FoxRunEndpoint.Ros2Native)
            {
                error = FoxRunRos2RegistrationError.None;
                diagnostic = "The captured provider is not native ROS2.";
                return FoxRunRos2ContractActivationDisposition.NotApplicable;
            }
            if (!contract.SupportsRos2Native)
            {
                error = FoxRunRos2RegistrationError.UnsupportedMessageType;
                diagnostic = "The generated input type has no native ROS2 capability.";
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }

            var qosResolution = contract.ResolveQos(policy.DefaultRos2Qos);
            if (!qosResolution.Success)
            {
                error = FoxRunRos2RegistrationError.UnsupportedQos;
                diagnostic = qosResolution.DiagnosticMessage;
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }

            qos = qosResolution.Qos;
            error = FoxRunRos2RegistrationError.None;
            diagnostic = string.Empty;
            return FoxRunRos2ContractActivationDisposition.Active;
        }

    }

    internal interface IFoxRunRos2HostBinding
    {
        FoxRunRos2GeneratedContract Contract { get; }
        string ContractId { get; }
        long SessionGeneration { get; }
        FoxRunRos2SubscriptionBindingState State { get; }
        FoxRunRos2RegistrationResult TryRegister();
        bool TryApplyLatest(long activeSessionGeneration);
        void RecordApplyFailure(Exception exception);
        bool TryGetSnapshot(long activeSessionGeneration, out FoxRunRos2SubscriptionBindingSnapshot snapshot);
        FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot);
        bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot);
        bool EndAcceptanceAttempt(long epoch);
        bool TryCompleteAcceptanceAttempt(
            long epoch,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot);
        void Stop();
    }

    internal interface IFoxRunRos2TimedHostBinding
    {
        bool TryApplyLatest(long activeSessionGeneration, double nowSeconds);
    }

    [DefaultExecutionOrder(-435)]
    [AddComponentMenu("")]
    internal sealed class FoxRunRos2SubscriptionHub : MonoBehaviour
    {
        private const string NodeName = "unity2foxglove_foxrun_subscriptions";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaximumContracts = 4096;
        private const int MaxNodeCreateAttempts = 4;
        private const double NodeCreateRetryCooldownSeconds = 5.0;
        private const int DeferredCleanupDrainTimeoutMilliseconds = 1000;

        private static readonly ProfilerMarker ScanMarker =
            new ProfilerMarker("Unity2Foxglove.FoxRunRos2Subscription.Scan");
        private static readonly ProfilerMarker DrainMarker =
            new ProfilerMarker("Unity2Foxglove.FoxRunRos2Subscription.Drain");
        private readonly List<HostedBinding> _bindings = new List<HostedBinding>();
        private readonly List<HostedBinding> _stale = new List<HostedBinding>();
        private readonly List<SourceCandidate> _sources = new List<SourceCandidate>();
        private readonly HashSet<int> _seenSources = new HashSet<int>();
        private readonly HashSet<string> _seenEndpoints = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _existingBindings = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _hostWarnings = new HashSet<string>(StringComparer.Ordinal);
        private readonly FoxRunRos2SubscriptionDiagnostics _diagnostics =
            new FoxRunRos2SubscriptionDiagnostics();
        private readonly FoxRunRos2BoundedRetryGate _nodeRetry =
            new FoxRunRos2BoundedRetryGate(MaxNodeCreateAttempts, NodeCreateRetryCooldownSeconds);
        private readonly FoxRunRos2ActiveSessionState _activeSession =
            new FoxRunRos2ActiveSessionState();

        private FoxgloveManager _manager;
        private FoxRunSubscriptionSessionPolicy _policy;
        private FoxRunRos2RuntimeDiagnosticContext _runtimeDiagnosticContext =
            FoxRunRos2RuntimeDiagnosticContext.Unknown;
        private ROS2.ROS2UnityComponent _ros2Unity;
        private Ros2ForUnityFoxRunNodeOwner _nodeOwner;
        private FoxRunRos2HostCleanupQueue _hostCleanupQueue;
        private int _hostThreadId;
        private float _scanCooldown;
        private bool _stopping;
        private bool _providerSessionActive;

        internal static bool TryGetAcceptanceSnapshot(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
        {
            snapshot = default;
            if (!TryFindOwnedHub(source, out var instance)
                || source == null
                || string.IsNullOrEmpty(topic)
                || instance._stopping)
                return false;

            var generation = instance._activeSession.ReadGeneration();
            if (generation < 0)
                return false;
            var sourceInstanceId = source.GetInstanceID();
            for (var i = 0; i < instance._bindings.Count; i++)
            {
                var hosted = instance._bindings[i];
                if (hosted.SourceInstanceId != sourceInstanceId
                    || !string.Equals(hosted.Key.Topic, topic, StringComparison.Ordinal)
                    || !hosted.Binding.TryGetSnapshot(generation, out var bindingSnapshot))
                    continue;
                snapshot = new FoxRunRos2SubscriptionAcceptanceSnapshot(topic, bindingSnapshot);
                return true;
            }
            return false;
        }

        internal static FoxRunRos2SubscriptionDiagnosticSnapshot[] GetDiagnosticSnapshots()
        {
            var instances = FindObjectsByType<FoxRunRos2SubscriptionHub>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var snapshots = new List<FoxRunRos2SubscriptionDiagnosticSnapshot>();
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                if (instance == null || instance._stopping)
                    continue;
                snapshots.AddRange(instance._diagnostics.GetSnapshots());
            }
            return snapshots.ToArray();
        }

        internal static FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (!TryFindAcceptanceBinding(source, topic, out var binding))
            {
                snapshot = default;
                return FoxRunRos2AcceptanceArmStatus.EndpointUnavailable;
            }
            return binding.ArmAcceptanceAttempt(out snapshot);
        }

        internal static bool TryGetAcceptanceAttempt(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (!TryFindAcceptanceBinding(source, topic, out var binding))
            {
                snapshot = default;
                return false;
            }
            return binding.TryGetAcceptanceAttempt(out snapshot);
        }

        internal static bool EndAcceptanceAttempt(
            MonoBehaviour source,
            string topic,
            long epoch)
            => TryFindAcceptanceBinding(source, topic, out var binding)
               && binding.EndAcceptanceAttempt(epoch);

        internal static bool TryCompleteAcceptanceAttempt(
            MonoBehaviour source,
            string topic,
            long epoch,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (!TryFindAcceptanceBinding(source, topic, out var binding))
            {
                snapshot = default;
                return false;
            }
            return binding.TryCompleteAcceptanceAttempt(epoch, out snapshot);
        }

        private static bool TryFindAcceptanceBinding(
            MonoBehaviour source,
            string topic,
            out IFoxRunRos2HostBinding binding)
        {
            binding = null;
            if (!TryFindOwnedHub(source, out var instance)
                || source == null
                || string.IsNullOrEmpty(topic)
                || instance._stopping)
                return false;
            var sourceInstanceId = source.GetInstanceID();
            for (var i = 0; i < instance._bindings.Count; i++)
            {
                var hosted = instance._bindings[i];
                if (hosted.SourceInstanceId == sourceInstanceId
                    && string.Equals(hosted.Key.Topic, topic, StringComparison.Ordinal))
                {
                    binding = hosted.Binding;
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindOwnedHub(
            MonoBehaviour source,
            out FoxRunRos2SubscriptionHub hub)
        {
            hub = null;
            if (source == null)
                return false;
            var instances = FindObjectsByType<FoxRunRos2SubscriptionHub>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var sourceScene = source.gameObject.scene;
            for (var i = 0; i < instances.Length; i++)
            {
                var candidate = instances[i];
                if (candidate == null
                    || candidate._stopping
                    || !candidate._providerSessionActive
                    || candidate.gameObject.scene.handle != sourceScene.handle)
                {
                    continue;
                }
                hub = candidate;
                return true;
            }
            return false;
        }

        internal void BindProviderOwner(FoxgloveManager manager)
            => SetManager(manager);

        internal void SetProviderSessionActive(bool active)
        {
            if (_providerSessionActive == active)
                return;
            _providerSessionActive = active;
            if (!active)
            {
                _activeSession.Deactivate();
                StopBindingsAndNode();
            }
            else if (_policy != null && _policy.SubscriptionsEnabled)
            {
                _activeSession.Activate(CheckedGeneration(_policy.SessionGeneration));
            }
            _scanCooldown = 0f;
        }

        private void Awake()
        {
            EnsureHostCleanupQueue();
        }

        private void OnEnable()
        {
            EnsureHostCleanupQueue();
            _stopping = false;
            _activeSession.Deactivate();
            Application.quitting += OnApplicationQuitting;
        }

        private void Update()
        {
            EnsureHostCleanupQueue();
            DrainPendingHostCleanup();
            if (_stopping)
            {
                BeginShutdown();
                return;
            }

            if (!_providerSessionActive || _manager == null)
            {
                StopBindingsAndNode();
                return;
            }

            // A hierarchy or scene notification deliberately marks the shared
            // gate dirty before its next refresh. Do not turn that recoverable
            // window into a permanent host shutdown: refreshing the bootstrap
            // gate may prove that the active user scene is stable again.
            if (Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene)
                && !Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge)
            {
                PauseForLifecycleWindow();
                return;
            }

            var active = _manager.ActiveFoxRunSubscriptionSessionPolicy;
            if (!ReferenceEquals(active, _policy))
                ApplySessionPolicy(active);
            if (_policy == null || !_policy.SubscriptionsEnabled)
                return;

            _scanCooldown -= Time.deltaTime;
            if (_scanCooldown <= 0f)
            {
                _scanCooldown = ScanIntervalSeconds;
                using (ScanMarker.Auto())
                    ScanAndReconcile();
            }

            using (DrainMarker.Auto())
                DrainBindings(Time.realtimeSinceStartupAsDouble);
        }

        private FoxRunRos2HostCleanupQueue EnsureHostCleanupQueue()
        {
            var queue = _hostCleanupQueue;
            if (queue != null)
                return queue;

            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_hostThreadId != 0 && _hostThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The deferred ROS2 cleanup queue must be initialized on the Unity host thread.");
            }

            _hostThreadId = currentThreadId;
            queue = new FoxRunRos2HostCleanupQueue(
                currentThreadId,
                SynchronizationContext.Current,
                exception => WarnHostOnce(
                    "deferred-cleanup|" + exception.GetType().Name,
                    "Deferred native ROS2 stream cleanup failed: "
                    + FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.TeardownFailure)));
            _hostCleanupQueue = queue;
            return queue;
        }

        private void PauseForLifecycleWindow()
        {
            StopBindingsAndNode();
            _scanCooldown = 0f;
        }

        private void SetManager(FoxgloveManager manager)
        {
            if (ReferenceEquals(_manager, manager))
                return;
            _activeSession.Deactivate();
            if (_manager != null)
                _manager.FoxRunSubscriptionSessionChanged -= OnSessionChanged;
            StopBindingsAndNode();
            _manager = manager;
            if (_manager != null)
            {
                _manager.FoxRunSubscriptionSessionChanged += OnSessionChanged;
                ApplySessionPolicy(_manager.ActiveFoxRunSubscriptionSessionPolicy);
            }
            else
            {
                _policy = null;
            }
        }

        private void OnSessionChanged(FoxRunSubscriptionSessionPolicy policy)
            => ApplySessionPolicy(policy);

        private void ApplySessionPolicy(FoxRunSubscriptionSessionPolicy policy)
        {
            if (ReferenceEquals(_policy, policy))
                return;
            _activeSession.Deactivate();
            StopBindingsAndNode();
            _policy = policy;
            if (_providerSessionActive
                && _policy != null
                && _policy.SubscriptionsEnabled)
                _activeSession.Activate(CheckedGeneration(_policy.SessionGeneration));
            _scanCooldown = 0f;
            _nodeRetry.RecordSuccess();
        }

        private void ScanAndReconcile()
        {
            if (_policy == null || !_policy.SubscriptionsEnabled)
                return;
            _sources.Clear();
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length && _sources.Count < MaximumContracts; i++)
            {
                var behaviour = behaviours[i];
                var hasNative = FoxRunRos2SourceDiscovery.TryGet(behaviour, out var nativeSource);
                var hasCustom = FoxRunRos2SourceDiscovery.TryGetCustom(behaviour, out var customSource);
                if (hasNative || hasCustom)
                {
                    _sources.Add(new SourceCandidate(behaviour, nativeSource, customSource));
                }
            }
            _sources.Sort((left, right) => left.Key.CompareTo(right.Key));

            _seenSources.Clear();
            _seenEndpoints.Clear();
            _existingBindings.Clear();
            for (var i = 0; i < _bindings.Count; i++)
                _existingBindings.Add(_bindings[i].Identity);
            for (var i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                _seenSources.Add(source.InstanceId);
                try
                {
                    var registrar = new CollectingRegistrar(this, source);
                    source.Native?.FoxRunRos2RegisterSubscriptions(registrar);
                    source.Custom?.FoxRunRos2RegisterCustomSubscriptions(registrar);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    WarnHostOnce(
                        source.Key + ": " + exception.GetType().Name,
                        "Native ROS2 source registration failed for " + source.Key + ": "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.BackendFailure));
                }
            }

            _stale.Clear();
            for (var i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (!binding.IsCurrent(_policy.SessionGeneration, _seenSources, _seenEndpoints))
                    _stale.Add(binding);
                else if (binding.Binding.State == FoxRunRos2SubscriptionBindingState.WaitingForRuntime
                         && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                             gameObject.scene))
                    binding.Binding.TryRegister();
            }
            ExceptionDispatchInfo staleFatal = null;
            var staleCleanupComplete = false;
            try
            {
                StopHostedBindingsAndDrainDeferredCleanup(
                    _stale,
                    _hostCleanupQueue,
                    TimeSpan.FromMilliseconds(DeferredCleanupDrainTimeoutMilliseconds),
                    exception => WarnHostOnce(
                        "stale-binding|" + exception.GetType().Name,
                        "Native ROS2 subscription teardown failed: "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TeardownFailure)),
                    out staleCleanupComplete);
            }
            catch (Exception exception)
            {
                staleFatal = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (staleCleanupComplete)
                {
                    for (var i = 0; i < _stale.Count; i++)
                    {
                        var stale = _stale[i];
                        _bindings.Remove(stale);
                        _existingBindings.Remove(stale.Identity);
                        _diagnostics.Remove(stale.Identity);
                    }
                }
                else
                {
                    WarnHostOnce(
                        "stale-binding|deferred-cleanup-timeout",
                        "Native ROS2 subscription teardown remains pending after the bounded host cleanup window.");
                }
            }
            _diagnostics.RemoveExcept(_seenEndpoints);
            _bindings.Sort((left, right) => left.Key.CompareTo(right.Key));
            SampleDiagnostics();
            staleFatal?.Throw();
        }

        private void DrainBindings(double nowSeconds)
        {
            if (_policy == null)
                return;
            var generation = CheckedGeneration(_policy.SessionGeneration);
            for (var i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].TryDrain(nowSeconds, generation, out _);
            }
            SampleDiagnostics();
        }

        private void SampleDiagnostics()
        {
            if (_policy == null)
                return;
            var generation = CheckedGeneration(_policy.SessionGeneration);
            for (var i = 0; i < _bindings.Count; i++)
            {
                if (!_bindings[i].Binding.TryGetSnapshot(generation, out var snapshot))
                    continue;
                _diagnostics.Update(
                    _bindings[i].Identity,
                    snapshot,
                    _runtimeDiagnosticContext);
                if (_diagnostics.ShouldLog(_bindings[i].Identity, snapshot))
                    Debug.LogWarning("[FoxRun ROS2] " + snapshot.ContractId + ": " + snapshot.Diagnostic);
            }
        }

        private bool TryEnsureNodeOwner(out Ros2ForUnityFoxRunNodeOwner owner)
        {
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene))
            {
                owner = null;
                return false;
            }
            owner = _nodeOwner;
            if (owner != null)
                return true;
            var now = Time.realtimeSinceStartupAsDouble;
            if (!_nodeRetry.TryBegin(now))
                return false;

            try
            {
                _ros2Unity = GetComponent<ROS2.ROS2UnityComponent>()
                             ?? gameObject.AddComponent<ROS2.ROS2UnityComponent>();
                if (!_ros2Unity.Ok())
                {
                    _nodeRetry.RecordFailure(now);
                    return false;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                var node = _ros2Unity.CreateNode(NodeName);
                if (node == null)
                {
                    _nodeRetry.RecordFailure(now);
                    return false;
                }
                var admission = new FoxRunRos2NativeRuntimeAdmission(
                    _activeSession,
                    gameObject.scene);
                owner = new Ros2ForUnityFoxRunNodeOwner(
                    new Ros2ForUnityFoxRunR2fuNodeDriver(_ros2Unity, node),
                    admission.CanUseNativeRuntimeNow);
                _nodeOwner = owner;
                _nodeRetry.RecordSuccess();
                return true;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                _nodeRetry.RecordFailure(now);
                WarnHostOnce(
                    "node|" + exception.GetType().Name,
                    "Native ROS2 FoxRun node is unavailable: "
                    + FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.RuntimeUnavailable));
                return false;
            }
        }

        private void AddBinding<T>(SourceCandidate source, FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose, Action<T> apply,
            Func<T, bool> clearIfOwned, Func<T, T, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null, Func<bool> canApply = null)
            where T : ROS2.Message, new()
        {
            var identity = source.InstanceId + "|" + contract.Id;
            _seenEndpoints.Add(identity);
            if (_existingBindings.Contains(identity) || _bindings.Count >= MaximumContracts)
                return;

            var activation = FoxRunRos2ContractActivation.Resolve(
                contract,
                _policy,
                out var qos,
                out var activationError,
                out var activationDiagnostic);
            if (activation == FoxRunRos2ContractActivationDisposition.NotApplicable)
                return;
            if (activation != FoxRunRos2ContractActivationDisposition.Active)
            {
                RecordUnsupported(identity, contract, activationError, activationDiagnostic);
                return;
            }
            IFoxRunRos2NativeBackend backend;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var readiness = FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                    contract.BaseRuntimePackageId,
                    contract.InterfaceDigest,
                    Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                if (!readiness.IsReady)
                {
                    RecordUnsupported(
                        identity,
                        contract,
                        FoxRunRos2RegistrationError.TypesupportUnavailable,
                        FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TypesupportUnavailable));
                    return;
                }
                if (!FoxRunRos2CustomNativeTransportHost.TryAcquireSubscriptionBackend(out backend))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
            }
            else
            {
                if (!TryEnsureNodeOwner(out var owner))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                backend = owner.AcquireBackend();
            }

            var generation = CheckedGeneration(_policy.SessionGeneration);
            FoxRunRos2SubscriptionBinding<T> binding = null;
            Func<T, bool> dropBeforeApply = null;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var sourceOrigin =
                    (source.Behaviour as IFoxgloveTopicContractSource)?.FoxgloveLog_Origin;
                // The callback has already deep-copied this envelope when the
                // predicate runs. Do not construct/apply a DTO for a message
                // emitted by this exact active Unity publisher origin.
                dropBeforeApply = owned => contract.TryGetCustomEnvelopeOrigin(
                        owned,
                        out var origin)
                    && IsSelfOrigin(identity, origin, sourceOrigin);
            }
            try
            {
                binding = new FoxRunRos2SubscriptionBinding<T>(
                    contract,
                    generation,
                    _activeSession.ReadGeneration,
                    _policy.NativeCopyBudgetBytes,
                    copy,
                    dispose,
                    apply,
                    clearIfOwned,
                    backend,
                    qos,
                    qosFactory: null,
                    dropBeforeApply: dropBeforeApply,
                    valuesEqual: valuesEqual,
                    consumeTrigger: consumeTrigger,
                    canApply: canApply,
                    transportAdmissionRateLimitHz: _policy.TransportAdmissionRateLimitHz);
                binding.WaitForRuntime();
                if (Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                        gameObject.scene))
                    binding.TryRegister();
                var hosted = new HostedBinding(
                    source.Behaviour,
                    source.InstanceId,
                    identity,
                    new FoxRunRos2DiscoveryKey(source.TypeName, source.InstanceId, contract.Topic, contract.MemberName),
                    binding,
                    EffectiveSubscribeRateHz(
                        contract,
                        _policy.DefaultSubscribeRateHz,
                        _policy.TransportAdmissionRateLimitHz));
                _bindings.Add(hosted);
                _existingBindings.Add(identity);
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                try
                {
                    if (binding != null)
                        binding.Stop();
                }
                catch (Exception)
                {
                    // Preserve the startup failure while completing cleanup.
                }
                if (binding == null)
                {
                    try
                    {
                        backend.ReleaseNodeOwnership();
                    }
                    catch (Exception)
                    {
                        // Preserve the startup failure.
                    }
                }
                primary.Throw();
                throw;
            }
        }

        private void AddStreamBinding<TTransport, TSample>(
            SourceCandidate source,
            FoxRunRos2GeneratedContract contract,
            Func<bool> tryAdmitInput,
            Func<TTransport, FoxRunRos2CopyContext, TSample> materializeOwned,
            Action<TSample> transferOwned,
            Action clearOwned)
            where TTransport : ROS2.Message, new()
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            var identity = source.InstanceId + "|" + contract.Id;
            _seenEndpoints.Add(identity);
            if (_existingBindings.Contains(identity) || _bindings.Count >= MaximumContracts)
                return;

            if (tryAdmitInput == null)
            {
                throw new InvalidOperationException(
                    "FoxRunStream field '" + contract.MemberName
                    + "' is null when the native subscription session is captured.");
            }
            if (materializeOwned == null)
                throw new ArgumentNullException(nameof(materializeOwned));
            if (transferOwned == null)
                throw new ArgumentNullException(nameof(transferOwned));
            if (clearOwned == null)
                throw new ArgumentNullException(nameof(clearOwned));

            var activation = FoxRunRos2ContractActivation.Resolve(
                contract,
                _policy,
                out var qos,
                out var activationError,
                out var activationDiagnostic);
            if (activation == FoxRunRos2ContractActivationDisposition.NotApplicable)
                return;
            if (activation != FoxRunRos2ContractActivationDisposition.Active)
            {
                RecordUnsupported(identity, contract, activationError, activationDiagnostic);
                return;
            }

            IFoxRunRos2NativeBackend backend;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var readiness = FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                    contract.BaseRuntimePackageId,
                    contract.InterfaceDigest,
                    Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                if (!readiness.IsReady)
                {
                    RecordUnsupported(
                        identity,
                        contract,
                        FoxRunRos2RegistrationError.TypesupportUnavailable,
                        FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TypesupportUnavailable));
                    return;
                }
                if (!FoxRunRos2CustomNativeTransportHost.TryAcquireSubscriptionBackend(out backend))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
            }
            else
            {
                if (!TryEnsureNodeOwner(out var owner))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                backend = owner.AcquireBackend();
            }

            var generation = CheckedGeneration(_policy.SessionGeneration);
            FoxRunRos2StreamSubscriptionBinding<TTransport, TSample> binding = null;
            Func<TTransport, bool> dropBorrowed = null;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var sourceOrigin =
                    (source.Behaviour as IFoxgloveTopicContractSource)?.FoxgloveLog_Origin;
                dropBorrowed = borrowed => contract.TryGetCustomEnvelopeOrigin(
                        borrowed,
                        out var origin)
                    && IsSelfOrigin(identity, origin, sourceOrigin);
            }

            try
            {
                binding = new FoxRunRos2StreamSubscriptionBinding<TTransport, TSample>(
                    contract,
                    generation,
                    _activeSession.ReadGeneration,
                    _policy.NativeCopyBudgetBytes,
                    tryAdmitInput,
                    materializeOwned,
                    transferOwned,
                    clearOwned,
                    DispatchCleanupToHostThread,
                    backend,
                    qos,
                    qosFactory: null,
                    dropBorrowed: dropBorrowed);
                binding.WaitForRuntime();
                if (Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                        gameObject.scene))
                    binding.TryRegister();
                _bindings.Add(new HostedBinding(
                    source.Behaviour,
                    source.InstanceId,
                    identity,
                    new FoxRunRos2DiscoveryKey(
                        source.TypeName,
                        source.InstanceId,
                        contract.Topic,
                        contract.MemberName),
                    binding,
                    1d));
                _existingBindings.Add(identity);
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                try
                {
                    if (binding != null)
                        binding.Stop();
                    else
                        backend.ReleaseNodeOwnership();
                }
                catch
                {
                    // Preserve the startup failure.
                }
                primary.Throw();
                throw;
            }
        }

        private static double EffectiveSubscribeRateHz(
            FoxRunRos2GeneratedContract contract,
            int managerDefaultSubscribeRateHz,
            int transportAdmissionRateLimitHz)
            => Math.Min(
                contract.HasExplicitHz
               && !float.IsNaN(contract.Hz)
               && !float.IsInfinity(contract.Hz)
               && contract.Hz > 0f
                ? contract.Hz
                : Math.Max(1, managerDefaultSubscribeRateHz),
                Math.Max(1, transportAdmissionRateLimitHz));

        private long ActiveGeneration()
            => _activeSession.ReadGeneration();

        private void DispatchCleanupToHostThread(Action cleanup)
        {
            var queue = _hostCleanupQueue;
            if (queue == null)
            {
                throw new InvalidOperationException(
                    "The Unity host cleanup queue is unavailable for deferred ROS2 stream cleanup.");
            }
            queue.Dispatch(cleanup);
        }

        private void DrainPendingHostCleanup()
        {
            _hostCleanupQueue?.Drain(exception => WarnHostOnce(
                "deferred-cleanup|" + exception.GetType().Name,
                "Deferred native ROS2 stream cleanup failed: "
                + FoxRunRos2PublicDiagnostic.Describe(
                    FoxRunRos2RegistrationError.TeardownFailure)));
        }

        private void RecordUnsupported(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunRos2RegistrationError error,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, FoxRunResolvedQos.Default, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Unsupported,
                error, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordWaiting(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunResolvedQos qos,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, qos, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.WaitingForRuntime,
                FoxRunRos2RegistrationError.RuntimeUnavailable, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordFailed(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            Exception exception)
        {
            UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, FoxRunResolvedQos.Default, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                FoxRunRos2PublicDiagnostic.Describe(FoxRunRos2RegistrationError.BackendFailure),
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        private void UpdateDiagnostic(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            _diagnostics.Update(endpointIdentity, snapshot, _runtimeDiagnosticContext);
            if (_diagnostics.ShouldLog(endpointIdentity, snapshot))
                Debug.LogWarning("[FoxRun ROS2] " + snapshot.ContractId + ": " + snapshot.Diagnostic);
        }

        private static long CheckedGeneration(ulong generation)
        {
            if (generation > long.MaxValue)
                throw new InvalidOperationException("FoxRun subscription generation exceeds the native host range.");
            return (long)generation;
        }

        private void StopBindingsAndNode()
        {
            ExceptionDispatchInfo fatal = null;
            var cleanupComplete = false;
            var owner = _nodeOwner;
            _nodeOwner = null;
            var cleanupQueue = EnsureHostCleanupQueue();
            try
            {
                StopHostedBindingsAndDrainDeferredCleanupThenReleaseHost(
                    _bindings,
                    cleanupQueue,
                    TimeSpan.FromMilliseconds(DeferredCleanupDrainTimeoutMilliseconds),
                    exception => WarnHostOnce(
                        "stop-binding|" + exception.GetType().Name,
                        "Native ROS2 subscription teardown failed: "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TeardownFailure)),
                    () => owner?.ReleaseHostOwnership(),
                    out cleanupComplete);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            if (!cleanupComplete)
            {
                WarnHostOnce(
                    "stop-binding|deferred-cleanup-timeout",
                    "Native ROS2 subscription teardown remains pending after the bounded host cleanup window.");
            }
            _bindings.Clear();
            _stale.Clear();
            _sources.Clear();
            _seenSources.Clear();
            _seenEndpoints.Clear();
            _existingBindings.Clear();
            _diagnostics.Clear();
            _runtimeDiagnosticContext = FoxRunRos2RuntimeDiagnosticContext.Unknown;
            _ros2Unity = null;
            fatal?.Throw();
        }

        internal static void StopHostedBindings(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            Action<Exception> reportFailure)
        {
            if (bindings == null)
                return;

            ExceptionDispatchInfo fatal = null;
            for (var index = 0; index < bindings.Count; index++)
            {
                try
                {
                    bindings[index]?.Stop();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    try
                    {
                        reportFailure?.Invoke(exception);
                    }
                    catch (Exception reportException) when (
                        FoxRunRos2NativeExceptionPolicy.IsRecoverable(reportException))
                    {
                        // Diagnostics cannot interrupt remaining teardown.
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
        }

        internal static void StopHostedBindingsAndDrainDeferredCleanup(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            FoxRunRos2HostCleanupQueue cleanupQueue,
            TimeSpan timeout,
            Action<Exception> reportFailure,
            out bool cleanupComplete)
        {
            if (cleanupQueue == null)
                throw new ArgumentNullException(nameof(cleanupQueue));

            cleanupComplete = false;
            ExceptionDispatchInfo fatal = null;
            try
            {
                StopHostedBindings(bindings, reportFailure);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                cleanupComplete = cleanupQueue.DrainUntil(
                    () => HostedCleanupIsComplete(bindings),
                    timeout,
                    reportFailure);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
                cleanupComplete = HostedCleanupIsComplete(bindings);
            }

            fatal?.Throw();
        }

        internal static void StopHostedBindingsAndDrainDeferredCleanupThenReleaseHost(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            FoxRunRos2HostCleanupQueue cleanupQueue,
            TimeSpan timeout,
            Action<Exception> reportFailure,
            Action releaseHostOwnership,
            out bool cleanupComplete)
        {
            if (releaseHostOwnership == null)
                throw new ArgumentNullException(nameof(releaseHostOwnership));

            cleanupComplete = false;
            ExceptionDispatchInfo fatal = null;
            try
            {
                StopHostedBindingsAndDrainDeferredCleanup(
                    bindings,
                    cleanupQueue,
                    timeout,
                    reportFailure,
                    out cleanupComplete);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                releaseHostOwnership();
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                try
                {
                    reportFailure?.Invoke(exception);
                }
                catch (Exception reportException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(reportException);
                }
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
        }

        private static bool HostedCleanupIsComplete(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings)
        {
            if (bindings == null)
                return true;
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding != null && !binding.CleanupComplete)
                    return false;
            }
            return true;
        }

        private void BeginShutdown()
        {
            if (!_stopping)
                _stopping = true;
            _activeSession.Deactivate();
            SetManager(null);
            StopBindingsAndNode();
            Application.quitting -= OnApplicationQuitting;
        }

        internal static void StopForNativeRuntimeShutdown()
        {
            var instances = FindObjectsByType<FoxRunRos2SubscriptionHub>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                if (instance == null)
                    continue;
                instance.BeginShutdown();
            }
        }

        private void WarnHostOnce(string key, string message)
        {
            if (_hostWarnings.Add(key))
                Debug.LogWarning("[FoxRun ROS2] " + message);
        }

        private void OnApplicationQuitting() => BeginShutdown();

        private void OnApplicationQuit() => BeginShutdown();

        private void OnDisable()
        {
            _stopping = true;
            BeginShutdown();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            Application.quitting -= OnApplicationQuitting;
        }

        private sealed class CollectingRegistrar : IFoxRunRos2SubscriptionRegistrar
        {
            private readonly FoxRunRos2SubscriptionHub _hub;
            private readonly SourceCandidate _source;

            internal CollectingRegistrar(FoxRunRos2SubscriptionHub hub, SourceCandidate source)
            {
                _hub = hub;
                _source = source;
            }

            public void Register<T>(FoxRunRos2GeneratedContract contract,
                Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
                Action<T> apply, Func<T, bool> clearIfOwned,
                Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
                Func<bool> canApply)
                where T : ROS2.Message, new()
                => FoxRunRos2RegistrationIsolation.TryRun(
                    () => _hub.AddBinding(
                        _source,
                        contract,
                        copy,
                        dispose,
                        apply,
                        clearIfOwned,
                        valuesEqual,
                        consumeTrigger,
                        canApply),
                    exception => _hub.RecordFailed(
                        _source.InstanceId + "|" + contract.Id,
                        contract,
                        exception));

            public void RegisterStream<TTransport, TSample>(
                FoxRunRos2GeneratedContract contract,
                Func<bool> tryAdmitInput,
                Func<TTransport, FoxRunRos2CopyContext, TSample> materializeOwned,
                Action<TSample> transferOwned,
                Action clearOwned)
                where TTransport : ROS2.Message, new()
                => FoxRunRos2RegistrationIsolation.TryRun(
                    () => _hub.AddStreamBinding(
                        _source,
                        contract,
                        tryAdmitInput,
                        materializeOwned,
                        transferOwned,
                        clearOwned),
                    exception => _hub.RecordFailed(
                        _source.InstanceId + "|" + contract.Id,
                        contract,
                        exception));
        }

        internal static bool IsSelfOrigin(
            string endpointIdentity,
            string candidateOrigin,
            string generatedSourceOrigin)
        {
            if (string.IsNullOrWhiteSpace(candidateOrigin))
                return false;

            return (!string.IsNullOrWhiteSpace(generatedSourceOrigin)
                    && string.Equals(
                        candidateOrigin,
                        generatedSourceOrigin,
                        StringComparison.Ordinal))
                   || FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(
                       endpointIdentity,
                       candidateOrigin);
        }

        private readonly struct SourceCandidate
        {
            internal SourceCandidate(
                MonoBehaviour behaviour,
                IFoxRunRos2SubscriptionSource native,
                IFoxRunRos2CustomSubscriptionSource custom)
            {
                Behaviour = behaviour;
                Native = native;
                Custom = custom;
                TypeName = behaviour.GetType().FullName ?? string.Empty;
                InstanceId = behaviour.GetInstanceID();
                Key = new FoxRunRos2DiscoveryKey(TypeName, InstanceId, string.Empty, string.Empty);
            }

            internal MonoBehaviour Behaviour { get; }
            internal IFoxRunRos2SubscriptionSource Native { get; }
            internal IFoxRunRos2CustomSubscriptionSource Custom { get; }
            internal string TypeName { get; }
            internal int InstanceId { get; }
            internal FoxRunRos2DiscoveryKey Key { get; }
        }

        private sealed class HostedBinding : IFoxRunRos2SubscriptionHostedCleanup
        {
            private readonly MonoBehaviour _source;
            private readonly int _sourceInstanceId;
            private readonly FoxRunRos2ApplyRateGate _rateGate;

            internal HostedBinding(MonoBehaviour source, int sourceInstanceId, string identity,
                FoxRunRos2DiscoveryKey key, IFoxRunRos2HostBinding binding, double rateLimitHz)
            {
                _source = source;
                _sourceInstanceId = sourceInstanceId;
                Identity = identity;
                Key = key;
                Binding = binding;
                _rateGate = new FoxRunRos2ApplyRateGate(Math.Max(1d, rateLimitHz));
            }

            internal string Identity { get; }
            internal int SourceInstanceId => _sourceInstanceId;
            internal FoxRunRos2DiscoveryKey Key { get; }
            internal IFoxRunRos2HostBinding Binding { get; }

            internal bool IsCurrent(
                ulong generation,
                HashSet<int> seenSources,
                HashSet<string> seenEndpoints)
                => _source != null
                   && _source.isActiveAndEnabled
                   && seenSources.Contains(_sourceInstanceId)
                   && seenEndpoints.Contains(Identity)
                   && generation <= long.MaxValue
                   && Binding.SessionGeneration == (long)generation;

            internal bool TryDrain(double nowSeconds, long generation, out Exception failure)
            {
                failure = null;
                if (!_rateGate.IsAllowed(nowSeconds))
                    return false;
                if (!FoxRunRos2ApplyIsolation.TryRun(Binding, generation, nowSeconds, out failure))
                    return false;
                _rateGate.MarkApplied(nowSeconds);
                return true;
            }

            internal void Stop() => Binding.Stop();

            internal bool CleanupComplete
                => !(Binding is IFoxRunRos2DeferredCleanupStatus deferred)
                   || deferred.CleanupComplete;

            bool IFoxRunRos2SubscriptionHostedCleanup.CleanupComplete
                => CleanupComplete;

            void IFoxRunRos2SubscriptionHostedCleanup.Stop()
                => Stop();
        }
    }

}
#endif
