// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread custom DTO native-output registration over FoxTopicBus.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal interface IFoxRunRos2CustomPublisherHostedBinding
    {
        string Identity { get; }
        int SourceInstanceId { get; }
        bool IsStopped { get; }
        void Stop();
    }

    /// <summary>
    /// Independent output-demand hub for generated custom DTO publishers.
    /// It owns no node itself: every endpoint lease is acquired from the custom
    /// transport host so input and output converge on one node lifecycle.
    /// </summary>
    [DefaultExecutionOrder(-434)]
    [AddComponentMenu("")]
    internal sealed class FoxRunRos2CustomPublisherHub : MonoBehaviour
    {
        private const string HubObjectName = "[FoxRun ROS2 Custom Publisher Hub]";
        private const float ScanIntervalSeconds = 0.5f;
        private const float ManagerSearchIntervalSeconds = 0.5f;
        private const int MaximumBindings = 4096;

        private static FoxRunRos2CustomPublisherHub _instance;
        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _bindings =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _stale =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new HashSet<string>(StringComparer.Ordinal);
        private readonly FoxRunRos2CustomPublisherSessionTracker _publishSessionTracker =
            new FoxRunRos2CustomPublisherSessionTracker();
        private FoxgloveManager _manager;
        private float _scanCooldown;
        private float _managerSearchCooldown;
        private bool _stopping;
        private bool _duplicate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;
            var existing = FindFirstObjectByType<FoxRunRos2CustomPublisherHub>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject(HubObjectName) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FoxRunRos2CustomPublisherHub>();
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
            if (!_duplicate)
                _stopping = false;
        }

        private void Update()
        {
            if (_stopping)
            {
                StopBindings();
                return;
            }

            ResolveManager();
            ApplyPublishSessionPolicy(
                _manager == null
                    ? null
                    : _manager.ActiveFoxRunPublishSessionPolicy);

            // Output policy is independent from the captured subscription
            // session. A Publish custom endpoint must stay available while
            // subscriptions or the legacy component-output switch are disabled.
            if (ShouldStopFoxRunPublishing(
                    _publishSessionTracker.AllowsPublishing,
                    _manager == null || _manager.Ros2NativeEnabled,
                    Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(
                        gameObject.scene)))
            {
                StopBindings();
                return;
            }

            _scanCooldown -= Time.deltaTime;
            if (_scanCooldown > 0f)
                return;
            _scanCooldown = ScanIntervalSeconds;

            if (!FoxgloveLogHub.TryGetTopicBus(out var bus))
                return;

            var publishSessionPolicy = _publishSessionTracker.Current;
            var inheritedQos = publishSessionPolicy == null
                ? FoxRunResolvedQos.Default
                : publishSessionPolicy.NativeRos2Qos;
            var defaultTargets = publishSessionPolicy == null
                ? FoxRunEndpoint.Ros2Native
                : publishSessionPolicy.DefaultTargets;
            var subscriptionPolicy = _manager == null
                ? null
                : _manager.ActiveFoxRunSubscriptionSessionPolicy;
            var defaultSource = subscriptionPolicy != null
                                && subscriptionPolicy.SubscriptionsEnabled
                ? subscriptionPolicy.DefaultSource
                : FoxRunEndpoint.Foxglove;
            ScanAndReconcile(bus, inheritedQos, defaultSource, defaultTargets);
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

            if (_manager != null)
                _manager.FoxRunPublishSessionChanged -= OnPublishSessionChanged;

            StopBindings();
            _manager = manager;
            if (_manager != null)
            {
                _manager.FoxRunPublishSessionChanged += OnPublishSessionChanged;
                ApplyPublishSessionPolicy(_manager.ActiveFoxRunPublishSessionPolicy);
            }
            else
            {
                ApplyPublishSessionPolicy(null);
            }

            _managerSearchCooldown = 0f;
        }

        private void OnPublishSessionChanged(FoxRunPublishSessionPolicy policy)
            => ApplyPublishSessionPolicy(policy);

        private void ApplyPublishSessionPolicy(FoxRunPublishSessionPolicy policy)
        {
            if (!_publishSessionTracker.Observe(policy))
                return;

            StopBindings();
            _scanCooldown = 0f;
        }

        private void ScanAndReconcile(
            FoxTopicBus bus,
            FoxRunResolvedQos inheritedQos,
            FoxRunEndpoint defaultSource,
            FoxRunEndpoint defaultTargets)
        {
            _seen.Clear();
            _existing.Clear();
            for (var index = 0; index < _bindings.Count; index++)
                _existing.Add(_bindings[index].Identity);

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Array.Sort(behaviours, CompareBehaviours);
            for (var index = 0; index < behaviours.Length && _bindings.Count < MaximumBindings; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour is not IFoxRunRos2CustomPublisherSource source
                    || !behaviour.isActiveAndEnabled)
                    continue;

                try
                {
                    source.FoxRunRos2RegisterCustomPublishers(
                        new CollectingRegistrar(
                            this,
                            behaviour,
                            bus,
                            inheritedQos,
                            defaultSource,
                            defaultTargets));
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    WarnOnce(
                        behaviour.GetInstanceID() + "|" + exception.GetType().FullName,
                        "Custom native ROS2 publisher registration failed: " + exception.GetType().Name);
                }
            }

            _stale.Clear();
            for (var index = 0; index < _bindings.Count; index++)
            {
                var binding = _bindings[index];
                if (binding.IsStopped || !_seen.Contains(binding.Identity))
                    _stale.Add(binding);
            }
            StopStaleBindings(
                _bindings,
                _stale,
                _existing,
                exception => WarnOnce(
                    "stale|" + exception.GetType().FullName + "|" + exception.Message,
                    "Custom native ROS2 publisher teardown failed: "
                    + exception.GetType().Name));
        }

        private void AddBinding<TDto, TEnvelope>(
            MonoBehaviour source,
            FoxTopicBus bus,
            FoxRunResolvedQos inheritedQos,
            FoxRunEndpoint defaultSource,
            FoxRunEndpoint defaultTargets,
            FoxRunRos2CustomPublisherContract contract,
            Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
            Action<TEnvelope> dispose)
            where TEnvelope : ROS2.Message, new()
        {
            if (source == null || contract == null)
                return;

            var identity = source.GetInstanceID() + "|" + contract.Id;
            _seen.Add(identity);
            if (_existing.Contains(identity) || _bindings.Count >= MaximumBindings)
                return;
            if (!contract.SupportsNativeOutput)
            {
                WarnOnce(identity + "|contract", "Custom native ROS2 publisher contract is invalid.");
                return;
            }

            var qosResolution = contract.ResolveQos(
                inheritedQos);
            if (!qosResolution.Success)
            {
                WarnOnce(
                    identity + "|qos|" + qosResolution.DiagnosticCode,
                    qosResolution.DiagnosticMessage);
                return;
            }

            if (!ShouldRegisterNativePublisher(
                    contract,
                    defaultSource,
                    defaultTargets,
                    out var topologyResolution))
            {
                if (!topologyResolution.Success)
                {
                    WarnOnce(
                        identity + "|topology|" + topologyResolution.DiagnosticCode,
                        topologyResolution.DiagnosticMessage);
                }
                return;
            }

            if (!TryGetAcceptedSourceOrigin(
                    source as IFoxgloveLogSource,
                    bus,
                    contract.Topic,
                    out var sourceOrigin))
            {
                // The main FoxRun hub is the sole topic-writer admission
                // authority.  A rejected duplicate must never create a native
                // endpoint merely because this independent scanner can see it.
                return;
            }

            var readiness = EvaluateReadiness(contract);
            if (!readiness.IsReady)
            {
                WarnOnce(
                    identity + "|" + readiness.Code,
                    FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.TypesupportUnavailable));
                return;
            }

            if (!FoxRunRos2CustomNativeTransportHost.TryAcquirePublisherBackend(out var backend))
            {
                WarnOnce(
                    identity + "|runtime",
                    FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.RuntimeUnavailable));
                return;
            }

            var origin = FoxRunRos2CustomOriginRegistry.BeginPublisher(identity, sourceOrigin);
            FoxRunRos2CustomPublisherBinding<TDto, TEnvelope> binding = null;
            try
            {
                binding = new FoxRunRos2CustomPublisherBinding<TDto, TEnvelope>(
                    contract,
                    bus,
                    backend,
                    qosResolution.Qos,
                    map,
                    dispose,
                    origin,
                    new FoxRunRos2CustomSequenceSource(),
                    () => EvaluateReadiness(contract),
                    () => FoxRunRos2CustomOriginRegistry.EndPublisher(identity, origin));
                var result = binding.TryStart();
                if (!result.Succeeded)
                {
                    binding.Stop();
                    var failureKind = result.FailureKind;
                    WarnOnce(
                        identity + "|" + result.Error + "|" + failureKind,
                        string.IsNullOrEmpty(failureKind)
                            ? result.Diagnostic
                            : result.Diagnostic + " [failureKind=" + failureKind + "]");
                    return;
                }

                _bindings.Add(new HostedBinding<TDto, TEnvelope>(identity, source.GetInstanceID(), binding));
                _existing.Add(identity);
            }
            catch (Exception exception)
            {
                CleanupFailedStartupAndRethrow(
                    exception,
                    binding == null ? null : (Action)binding.Stop,
                    binding == null
                        ? () => FoxRunRos2CustomOriginRegistry.EndPublisher(
                            identity,
                            origin)
                        : null,
                    binding == null
                        ? backend.ReleaseNodeOwnership
                        : null);
                throw;
            }
        }

        private static FoxRunRos2CustomTypesupportReadiness EvaluateReadiness(
            FoxRunRos2CustomPublisherContract contract)
            => FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                contract.BaseRuntimePackageId,
                contract.InterfaceDigest,
                Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));

        internal static bool TryGetAcceptedSourceOrigin(
            IFoxgloveLogSource source,
            FoxTopicBus bus,
            string topic,
            out string origin)
        {
            origin = string.Empty;
            if (source == null
                || bus == null
                || string.IsNullOrWhiteSpace(topic)
                || source is not IFoxgloveTopicContractSource contractSource)
            {
                return false;
            }

            origin = contractSource.FoxgloveLog_Origin ?? string.Empty;
            if (string.IsNullOrWhiteSpace(origin))
                return false;
            for (var index = 0; index < source.FoxgloveLog_TopicCount; index++)
            {
                var candidate = contractSource.FoxgloveLog_GetContract(index);
                if (candidate != null
                    && string.Equals(candidate.Topic, topic, StringComparison.Ordinal)
                    && bus.IsRegistered(candidate, origin))
                {
                    return true;
                }
            }

            origin = string.Empty;
            return false;
        }

        private void StopBindings()
        {
            try
            {
                StopAllBindings(
                    _bindings,
                    exception => WarnOnce(
                        "stop|" + exception.GetType().FullName + "|" + exception.Message,
                        "Custom native ROS2 publisher teardown failed: "
                        + exception.GetType().Name));
            }
            finally
            {
                _bindings.Clear();
                _stale.Clear();
                _existing.Clear();
                _seen.Clear();
            }
        }

        internal static void StopAllBindings(
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
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
                        // Diagnostics must not interrupt the remaining teardown.
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception exception)
                {
                    // Finish the remaining mandatory endpoint cleanup, then
                    // preserve the first fatal exception and its stack.
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
        }

        internal static void StopStaleBindings(
            IList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> stale,
            ISet<string> existing,
            Action<Exception> reportFailure)
        {
            if (stale == null)
                return;

            ExceptionDispatchInfo fatal = null;
            for (var index = 0; index < stale.Count; index++)
            {
                var binding = stale[index];
                try
                {
                    binding?.Stop();
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
                        // Diagnostics must not interrupt mandatory bookkeeping.
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
                finally
                {
                    if (binding != null)
                    {
                        bindings?.Remove(binding);
                        existing?.Remove(binding.Identity);
                    }
                }
            }

            fatal?.Throw();
        }

        internal static void CleanupFailedStartupAndRethrow(
            Exception primaryException,
            Action stopBinding,
            Action endPublisher,
            Action releaseNode)
        {
            if (primaryException == null)
                throw new ArgumentNullException(nameof(primaryException));

            var primary = ExceptionDispatchInfo.Capture(primaryException);
            if (stopBinding != null)
            {
                try
                {
                    stopBinding();
                }
                catch (Exception)
                {
                    // The startup failure remains primary.
                }
            }
            else
            {
                try
                {
                    endPublisher?.Invoke();
                }
                catch (Exception)
                {
                    // Continue to the independently owned node release.
                }

                try
                {
                    releaseNode?.Invoke();
                }
                catch (Exception)
                {
                    // The startup failure remains primary.
                }
            }

            primary.Throw();
        }

        internal static bool ShouldStopFoxRunPublishing(
            bool publishSessionAllows,
            bool legacyComponentNativeOutputEnabled,
            bool bridgeLifecycleIsShuttingDown)
        {
            // The legacy switch owns component publishers only. FoxRun demand
            // is frozen in its directional publish session and explicit
            // contracts, so the switch must not disable these endpoints.
            _ = legacyComponentNativeOutputEnabled;
            return !publishSessionAllows || bridgeLifecycleIsShuttingDown;
        }

        internal static bool ShouldRegisterNativePublisher(
            FoxRunRos2CustomPublisherContract contract,
            FoxRunEndpoint defaultSource,
            FoxRunEndpoint defaultTargets,
            out FoxRunEndpointResolution resolution)
        {
            if (contract == null)
            {
                resolution = default;
                return false;
            }

            resolution = contract.ResolveTopology(defaultSource, defaultTargets);
            return resolution.Success
                   && (resolution.Topology.Targets & FoxRunEndpoint.Ros2Native) != 0;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warnings.Add(key))
                Debug.LogWarning("[FoxRun ROS2] " + message);
        }

        internal static void StopForNativeRuntimeShutdown()
        {
            var instance = _instance;
            if (instance == null)
                return;
            instance._stopping = true;
            instance.SetManager(null);
            instance.StopBindings();
        }

        private static int CompareBehaviours(MonoBehaviour left, MonoBehaviour right)
        {
            var leftName = left == null ? string.Empty : left.GetType().FullName ?? string.Empty;
            var rightName = right == null ? string.Empty : right.GetType().FullName ?? string.Empty;
            var result = string.CompareOrdinal(leftName, rightName);
            return result != 0
                ? result
                : (left?.GetInstanceID() ?? 0).CompareTo(right?.GetInstanceID() ?? 0);
        }

        private void OnApplicationQuit()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
        }

        private void OnDisable()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
        }

        private void OnDestroy()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
            if (_instance == this)
                _instance = null;
        }

        private sealed class CollectingRegistrar : IFoxRunRos2CustomPublisherRegistrar
        {
            private readonly FoxRunRos2CustomPublisherHub _hub;
            private readonly MonoBehaviour _source;
            private readonly FoxTopicBus _bus;
            private readonly FoxRunResolvedQos _inheritedQos;
            private readonly FoxRunEndpoint _defaultSource;
            private readonly FoxRunEndpoint _defaultTargets;

            internal CollectingRegistrar(
                FoxRunRos2CustomPublisherHub hub,
                MonoBehaviour source,
                FoxTopicBus bus,
                FoxRunResolvedQos inheritedQos,
                FoxRunEndpoint defaultSource,
                FoxRunEndpoint defaultTargets)
            {
                _hub = hub;
                _source = source;
                _bus = bus;
                _inheritedQos = inheritedQos;
                _defaultSource = defaultSource;
                _defaultTargets = defaultTargets;
            }

            public void Register<TDto, TEnvelope>(
                FoxRunRos2CustomPublisherContract contract,
                Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
                Action<TEnvelope> dispose)
                where TEnvelope : ROS2.Message, new()
                => _hub.AddBinding(
                    _source,
                    _bus,
                    _inheritedQos,
                    _defaultSource,
                    _defaultTargets,
                    contract,
                    map,
                    dispose);
        }

        private sealed class HostedBinding<TDto, TEnvelope> : IFoxRunRos2CustomPublisherHostedBinding
            where TEnvelope : ROS2.Message, new()
        {
            private readonly FoxRunRos2CustomPublisherBinding<TDto, TEnvelope> _binding;

            internal HostedBinding(
                string identity,
                int sourceInstanceId,
                FoxRunRos2CustomPublisherBinding<TDto, TEnvelope> binding)
            {
                Identity = identity;
                SourceInstanceId = sourceInstanceId;
                _binding = binding;
            }

            public string Identity { get; }
            public int SourceInstanceId { get; }
            public bool IsStopped => _binding.IsStopped;
            public void Stop() => _binding.Stop();
        }
    }

    /// <summary>
    /// Tracks immutable Manager publish-session identity. Reference comparison
    /// deliberately catches Manager replacement even when generation and QoS
    /// values happen to match.
    /// </summary>
    internal sealed class FoxRunRos2CustomPublisherSessionTracker
    {
        private FoxRunPublishSessionPolicy _observed;

        internal FoxRunPublishSessionPolicy Current => _observed;

        internal bool AllowsPublishing
            => _observed == null || _observed.SessionActive;

        internal bool Observe(FoxRunPublishSessionPolicy current)
        {
            if (ReferenceEquals(_observed, current))
                return false;

            _observed = current;
            return true;
        }
    }
}
#endif
