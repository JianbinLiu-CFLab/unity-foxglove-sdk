// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Hidden main-thread host for generated native FoxRun subscriptions.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.Profiling;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
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
            catch (Exception exception)
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
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            failure = null;
            try
            {
                return binding.TryApplyLatest(generation);
            }
            catch (Exception exception)
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

        internal FoxRunRos2ApplyRateGate(int rateLimitHz)
        {
            if (rateLimitHz < 1)
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
    }

    internal static class FoxRunRos2ContractActivation
    {
        internal static bool TryResolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            out FoxRunRos2QosPreset qos,
            out string diagnostic)
            => TryResolve(contract, policy, out qos, out _, out diagnostic);

        internal static bool TryResolve(
            FoxRunRos2GeneratedContract contract,
            FoxRunSubscriptionSessionPolicy policy,
            out FoxRunRos2QosPreset qos,
            out FoxRunRos2RegistrationError error,
            out string diagnostic)
        {
            qos = FoxRunRos2QosPreset.Default;
            error = FoxRunRos2RegistrationError.RegistrationRejected;
            if (contract == null)
            {
                diagnostic = "Generated ROS2 contract is missing.";
                return false;
            }
            if (!contract.HasCompleteMetadata)
            {
                diagnostic = "Generated ROS2 contract does not carry complete native metadata.";
                return false;
            }
            if (policy == null || !policy.SubscriptionsEnabled)
            {
                diagnostic = "FoxRun subscriptions are disabled for the captured session.";
                return false;
            }
            if (!Enum.IsDefined(typeof(FoxRunSubscriptionProvider), contract.SubscriptionProvider))
            {
                diagnostic = "Generated ROS2 contract has an invalid provider declaration.";
                return false;
            }
            if (!Enum.IsDefined(typeof(FoxRunRos2QosPreset), contract.QosPreset))
            {
                error = FoxRunRos2RegistrationError.UnsupportedQos;
                diagnostic = "Generated ROS2 contract has an invalid QoS declaration.";
                return false;
            }
            if (!Enum.IsDefined(typeof(FoxRunMode), contract.Mode))
            {
                diagnostic = "Generated ROS2 contract has an invalid mode declaration.";
                return false;
            }
            if (contract.Mode != FoxRunMode.SubscribeOnly)
            {
                diagnostic = "Native ROS2 subscriptions require SubscribeOnly mode.";
                return false;
            }
            if (!contract.SupportsRos2Native)
            {
                error = FoxRunRos2RegistrationError.UnsupportedMessageType;
                diagnostic = "The generated input type has no native ROS2 capability.";
                return false;
            }

            var provider = FoxRunSubscriptionProviderResolver.Resolve(
                contract.SubscriptionProvider,
                policy.DefaultProvider,
                contract.Mode,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: false,
                supportsRos2Native: contract.SupportsRos2Native);
            if (!provider.Success || provider.Provider != FoxRunSubscriptionProvider.Ros2Native)
            {
                error = provider.DiagnosticCode == FoxRunSubscriptionProviderDiagnosticCode.Unsupported
                    ? FoxRunRos2RegistrationError.UnsupportedMessageType
                    : FoxRunRos2RegistrationError.RegistrationRejected;
                diagnostic = provider.Success
                    ? "The captured provider is not native ROS2."
                    : provider.DiagnosticMessage;
                return false;
            }

            var qosResolution = FoxRunRos2QosResolver.Resolve(contract.QosPreset, policy.DefaultRos2Qos);
            if (!qosResolution.Success)
            {
                error = FoxRunRos2RegistrationError.UnsupportedQos;
                diagnostic = qosResolution.DiagnosticMessage;
                return false;
            }

            qos = qosResolution.Preset;
            error = FoxRunRos2RegistrationError.None;
            diagnostic = string.Empty;
            return true;
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

    [DefaultExecutionOrder(-435)]
    [AddComponentMenu("")]
    internal sealed class FoxRunRos2SubscriptionHub : MonoBehaviour
    {
        private const string HubObjectName = "[FoxRun ROS2 Subscription Hub]";
        private const string RetryObjectName = "[FoxRun ROS2 Subscription Bootstrap Retry]";
        private const string NodeName = "unity2foxglove_foxrun_subscriptions";
        private const float ManagerSearchIntervalSeconds = 2f;
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaximumContracts = 4096;
        private const int MaxNodeCreateAttempts = 4;
        private const double NodeCreateRetryCooldownSeconds = 5.0;

        private static readonly ProfilerMarker ScanMarker =
            new ProfilerMarker("Unity2Foxglove.FoxRunRos2Subscription.Scan");
        private static readonly ProfilerMarker DrainMarker =
            new ProfilerMarker("Unity2Foxglove.FoxRunRos2Subscription.Drain");
        private static FoxRunRos2SubscriptionHub _instance;
        private static FoxRunRos2SubscriptionBootstrapRetry _retry;

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
        private float _managerSearchCooldown;
        private float _scanCooldown;
        private bool _stopping;
        private bool _duplicate;

        internal static bool TryGetAcceptanceSnapshot(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
        {
            snapshot = default;
            var instance = _instance;
            if (instance == null
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
            var instance = _instance;
            return instance == null || instance._stopping
                ? Array.Empty<FoxRunRos2SubscriptionDiagnosticSnapshot>()
                : instance._diagnostics.GetSnapshots();
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
            var instance = _instance;
            if (instance == null
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _retry = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge
                && EnsureCreated())
                return;
            EnsureManagedRetry();
        }

        private static void EnsureManagedRetry()
        {
            if (_instance != null || _retry != null)
                return;
            var existing = FindFirstObjectByType<FoxRunRos2SubscriptionBootstrapRetry>();
            if (existing != null)
            {
                _retry = existing;
                return;
            }
            var go = new GameObject(RetryObjectName) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _retry = go.AddComponent<FoxRunRos2SubscriptionBootstrapRetry>();
        }

        internal static bool EnsureCreated()
        {
            if (_instance != null)
                return true;
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge)
                return false;
            var existing = FindFirstObjectByType<FoxRunRos2SubscriptionHub>();
            if (existing != null)
            {
                _instance = existing;
                return true;
            }
            var go = new GameObject(HubObjectName) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FoxRunRos2SubscriptionHub>();
            return true;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                _duplicate = true;
                _stopping = true;
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnEnable()
        {
            if (_duplicate)
            {
                _stopping = true;
                return;
            }
            _stopping = false;
            _activeSession.Deactivate();
            Application.quitting += OnApplicationQuitting;
        }

        private void Update()
        {
            if (_stopping
                || Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene))
            {
                BeginShutdown();
                return;
            }

            ResolveManager();
            if (_manager == null)
                return;
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

        private void ResolveManager()
        {
            if (_manager != null)
                return;
            _managerSearchCooldown -= Time.deltaTime;
            if (_managerSearchCooldown > 0f)
                return;
            _managerSearchCooldown = ManagerSearchIntervalSeconds;
            SetManager(FindFirstObjectByType<FoxgloveManager>());
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
            if (_policy != null && _policy.SubscriptionsEnabled)
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
                if (FoxRunRos2SourceDiscovery.TryGet(behaviour, out var nativeSource))
                {
                    _sources.Add(new SourceCandidate(behaviour, nativeSource));
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
                    source.Native.FoxRunRos2RegisterSubscriptions(registrar);
                }
                catch (Exception exception)
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
            for (var i = 0; i < _stale.Count; i++)
            {
                var stale = _stale[i];
                stale.Stop();
                _bindings.Remove(stale);
                _existingBindings.Remove(stale.Identity);
                _diagnostics.Remove(stale.Identity);
            }
            _diagnostics.RemoveExcept(_seenEndpoints);
            _bindings.Sort((left, right) => left.Key.CompareTo(right.Key));
            SampleDiagnostics();
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
            catch (Exception exception)
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
            Func<T, bool> clearIfOwned)
            where T : ROS2.Message, new()
        {
            var identity = source.InstanceId + "|" + contract.Id;
            _seenEndpoints.Add(identity);
            if (_existingBindings.Contains(identity) || _bindings.Count >= MaximumContracts)
                return;

            if (!FoxRunRos2ContractActivation.TryResolve(
                    contract, _policy, out var qos, out var activationError,
                    out var activationDiagnostic))
            {
                RecordUnsupported(identity, contract, activationError, activationDiagnostic);
                return;
            }
            if (!TryEnsureNodeOwner(out var owner))
            {
                RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                return;
            }

            var generation = CheckedGeneration(_policy.SessionGeneration);
            var backend = owner.AcquireBackend();
            FoxRunRos2SubscriptionBinding<T> binding = null;
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
                    qos);
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
                    _policy.MainThreadApplyRateLimitHz);
                _bindings.Add(hosted);
                _existingBindings.Add(identity);
            }
            catch
            {
                if (binding != null)
                    binding.Stop();
                else
                    backend.ReleaseNodeOwnership();
                throw;
            }
        }

        private long ActiveGeneration()
            => _activeSession.ReadGeneration();

        private void RecordUnsupported(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunRos2RegistrationError error,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, contract.QosPreset, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Unsupported,
                error, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordWaiting(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunRos2QosPreset qosPreset,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, qosPreset, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.WaitingForRuntime,
                FoxRunRos2RegistrationError.RuntimeUnavailable, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordFailed(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            Exception exception)
        {
            UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, contract.QosPreset, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                FoxRunRos2PublicDiagnostic.Describe(FoxRunRos2RegistrationError.BackendFailure),
                0, 0, 0, 0, 0, 0, 0, 0, 0));
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
            for (var i = 0; i < _bindings.Count; i++)
                _bindings[i].Stop();
            _bindings.Clear();
            _stale.Clear();
            _sources.Clear();
            _seenSources.Clear();
            _seenEndpoints.Clear();
            _existingBindings.Clear();
            _diagnostics.Clear();
            _runtimeDiagnosticContext = FoxRunRos2RuntimeDiagnosticContext.Unknown;
            var owner = _nodeOwner;
            _nodeOwner = null;
            if (owner != null)
            {
                try
                {
                    owner.ReleaseHostOwnership();
                }
                catch (Exception exception)
                {
                    WarnHostOnce(
                        "release-node|" + exception.GetType().Name,
                        "Native ROS2 FoxRun node removal failed: "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TeardownFailure));
                }
            }
            _ros2Unity = null;
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
            if (_instance == this)
                _instance = null;
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
                Action<T> apply, Func<T, bool> clearIfOwned)
                where T : ROS2.Message, new()
                => FoxRunRos2RegistrationIsolation.TryRun(
                    () => _hub.AddBinding(_source, contract, copy, dispose, apply, clearIfOwned),
                    exception => _hub.RecordFailed(
                        _source.InstanceId + "|" + contract.Id,
                        contract,
                        exception));
        }

        private readonly struct SourceCandidate
        {
            internal SourceCandidate(
                MonoBehaviour behaviour,
                IFoxRunRos2SubscriptionSource native)
            {
                Behaviour = behaviour;
                Native = native;
                TypeName = behaviour.GetType().FullName ?? string.Empty;
                InstanceId = behaviour.GetInstanceID();
                Key = new FoxRunRos2DiscoveryKey(TypeName, InstanceId, string.Empty, string.Empty);
            }

            internal MonoBehaviour Behaviour { get; }
            internal IFoxRunRos2SubscriptionSource Native { get; }
            internal string TypeName { get; }
            internal int InstanceId { get; }
            internal FoxRunRos2DiscoveryKey Key { get; }
        }

        private sealed class HostedBinding
        {
            private readonly MonoBehaviour _source;
            private readonly int _sourceInstanceId;
            private readonly FoxRunRos2ApplyRateGate _rateGate;

            internal HostedBinding(MonoBehaviour source, int sourceInstanceId, string identity,
                FoxRunRos2DiscoveryKey key, IFoxRunRos2HostBinding binding, int rateLimitHz)
            {
                _source = source;
                _sourceInstanceId = sourceInstanceId;
                Identity = identity;
                Key = key;
                Binding = binding;
                _rateGate = new FoxRunRos2ApplyRateGate(Math.Max(1, rateLimitHz));
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
                if (!FoxRunRos2ApplyIsolation.TryRun(Binding, generation, out failure))
                    return false;
                _rateGate.MarkApplied(nowSeconds);
                return true;
            }

            internal void Stop() => Binding.Stop();
        }
    }

    [AddComponentMenu("")]
    internal sealed class FoxRunRos2SubscriptionBootstrapRetry : MonoBehaviour
    {
        private readonly FoxRunRos2BootstrapRetryState _state = new FoxRunRos2BootstrapRetryState();

        private void Update()
        {
            var shuttingDown = Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene);
            if (!_state.ShouldCreateHost(
                    Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge,
                    shuttingDown))
                return;
            if (FoxRunRos2SubscriptionHub.EnsureCreated())
                Destroy(gameObject);
            else
                _state.RecordCreateFailed();
        }

        private void OnDestroy()
        {
            // Static retry state is reset on the next subsystem registration;
            // EnsureManagedRetry can also rediscover a live retry object.
        }
    }
}
#endif
