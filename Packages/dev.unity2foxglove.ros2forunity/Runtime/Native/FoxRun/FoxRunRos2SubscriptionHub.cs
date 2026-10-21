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
                   FoxRunResolvedQos.Default,
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
            => Resolve(
                   contract,
                   policy,
                   FoxRunResolvedQos.Default,
                   out qos,
                   out error,
                   out diagnostic)
               == FoxRunRos2ContractActivationDisposition.Active;

        internal static FoxRunRos2ContractActivationDisposition Resolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            FoxRunResolvedQos inheritedQos,
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
                && !Enum.IsDefined(
                    typeof(FoxRunRos2RouteEndpoint),
                    contract.Source))
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

            var topology = FoxRunRos2RouteResolver.Resolve(
                contract.Mode,
                contract.Source,
                hasExplicitSource: contract.Source != 0,
                declaredTargets: 0,
                hasExplicitTargets: false,
                defaultSource:
                    FoxRunRos2RouteResolver
                        .FromSubscribeProvider(
                            policy.DefaultProvider),
                defaultTargets:
                    FoxRunRos2RouteEndpoint.WebSocket,
                hasExplicitWebSocketEncoding:
                    contract.DeclaredSubscriptionEncoding
                    != 0);
            if (!topology.Success)
            {
                error = FoxRunRos2RegistrationError.RegistrationRejected;
                diagnostic = topology.DiagnosticMessage;
                return FoxRunRos2ContractActivationDisposition.Rejected;
            }
            if (topology.Route.Source
                != FoxRunRos2RouteEndpoint.R2fu)
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

            var qosResolution =
                contract.ResolveQos(inheritedQos);
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
        bool CanRetryRegistration { get; }
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

    internal static class FoxRunRos2SubscriptionAdmission
    {
        internal static int RetainDeterministicPrefix<T>(
            List<T> candidates,
            int maximum,
            Comparison<T> comparison)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));

            candidates.Sort(comparison);
            var overflow = Math.Max(0, candidates.Count - maximum);
            if (overflow > 0)
                candidates.RemoveRange(maximum, overflow);
            return overflow;
        }
    }

    [DefaultExecutionOrder(-435)]
    [AddComponentMenu("")]
    internal sealed partial class FoxRunRos2SubscriptionHub : MonoBehaviour
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
        private FoxRunRos2TransportProvider _provider;
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

        internal void BindProviderOwner(
            FoxgloveManager manager,
            FoxRunRos2TransportProvider provider)
        {
            _provider = provider;
            SetManager(manager);
        }

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

        internal FoxRunTransportDirectionStatus CaptureTransportStatus()
        {
            if (!_providerSessionActive || _stopping)
            {
                return new FoxRunTransportDirectionStatus(
                    FoxRunTransportDirection.Subscribe,
                    selected: true,
                    FoxRunTransportObservedState.Stopped,
                    0,
                    0,
                    0);
            }

            var ready = 0;
            var pending = 0;
            var failed = 0;
            for (var index = 0; index < _bindings.Count; index++)
            {
                switch (_bindings[index].Binding.State)
                {
                    case FoxRunRos2SubscriptionBindingState.Ready:
                    case FoxRunRos2SubscriptionBindingState.Receiving:
                        ready++;
                        break;
                    case FoxRunRos2SubscriptionBindingState.Unsupported:
                    case FoxRunRos2SubscriptionBindingState.Failed:
                        failed++;
                        break;
                    default:
                        pending++;
                        break;
                }
            }

            var state = failed != 0
                ? ready == 0
                    ? FoxRunTransportObservedState.Failed
                    : FoxRunTransportObservedState.Degraded
                : pending != 0
                    ? ready == 0
                        ? FoxRunTransportObservedState.Starting
                        : FoxRunTransportObservedState.Degraded
                    : FoxRunTransportObservedState.Ready;
            FoxRunTransportDiagnostic? diagnostic = failed != 0
                ? new FoxRunTransportDiagnostic(
                    "R2FU004",
                    "One or more native subscription bindings failed.")
                : pending != 0
                    ? new FoxRunTransportDiagnostic(
                        "R2FU003",
                        "Native subscription bindings are waiting for runtime readiness.")
                    : (FoxRunTransportDiagnostic?)null;
            return new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Subscribe,
                selected: true,
                state,
                _bindings.Count,
                ready,
                failed,
                diagnostic);
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

    }

}
#endif
