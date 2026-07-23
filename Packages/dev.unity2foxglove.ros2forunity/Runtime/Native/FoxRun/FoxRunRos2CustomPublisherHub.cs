// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread custom DTO native-output registration over FoxTopicBus.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
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
        private const int MaximumBindings = 4096;

        private static FoxRunRos2CustomPublisherHub _instance;
        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _bindings =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _stale =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new HashSet<string>(StringComparer.Ordinal);
        private float _scanCooldown;
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

            // Output policy is independent from the captured subscription
            // session. A Publish custom endpoint must stay available while
            // subscriptions are disabled.
            if (!Ros2NativeOutputPolicy.Enabled
                || Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene))
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

            ScanAndReconcile(bus);
        }

        private void ScanAndReconcile(FoxTopicBus bus)
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
                        new CollectingRegistrar(this, behaviour, bus));
                }
                catch (Exception exception)
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
            for (var index = 0; index < _stale.Count; index++)
            {
                var binding = _stale[index];
                binding.Stop();
                _bindings.Remove(binding);
                _existing.Remove(binding.Identity);
            }
        }

        private void AddBinding<TDto, TEnvelope>(
            MonoBehaviour source,
            FoxTopicBus bus,
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

            var origin = FoxRunRos2CustomOriginRegistry.BeginPublisher(identity);
            FoxRunRos2CustomPublisherBinding<TDto, TEnvelope> binding = null;
            try
            {
                binding = new FoxRunRos2CustomPublisherBinding<TDto, TEnvelope>(
                    contract,
                    bus,
                    backend,
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
            catch (Exception)
            {
                if (binding != null)
                    binding.Stop();
                else
                {
                    FoxRunRos2CustomOriginRegistry.EndPublisher(identity, origin);
                    backend.ReleaseNodeOwnership();
                }
                throw;
            }
        }

        private static FoxRunRos2CustomTypesupportReadiness EvaluateReadiness(
            FoxRunRos2CustomPublisherContract contract)
            => FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                contract.BaseRuntimePackageId,
                contract.InterfaceDigest,
                Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));

        private void StopBindings()
        {
            for (var index = 0; index < _bindings.Count; index++)
                _bindings[index].Stop();
            _bindings.Clear();
            _stale.Clear();
            _existing.Clear();
            _seen.Clear();
        }

        private void WarnOnce(string key, string message)
        {
            if (_warnings.Add(key))
                Debug.LogWarning("[FoxRun ROS2] " + message);
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
            StopBindings();
        }

        private void OnDisable()
        {
            _stopping = true;
            StopBindings();
        }

        private void OnDestroy()
        {
            _stopping = true;
            StopBindings();
            if (_instance == this)
                _instance = null;
        }

        private sealed class CollectingRegistrar : IFoxRunRos2CustomPublisherRegistrar
        {
            private readonly FoxRunRos2CustomPublisherHub _hub;
            private readonly MonoBehaviour _source;
            private readonly FoxTopicBus _bus;

            internal CollectingRegistrar(
                FoxRunRos2CustomPublisherHub hub,
                MonoBehaviour source,
                FoxTopicBus bus)
            {
                _hub = hub;
                _source = source;
                _bus = bus;
            }

            public void Register<TDto, TEnvelope>(
                FoxRunRos2CustomPublisherContract contract,
                Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
                Action<TEnvelope> dispose)
                where TEnvelope : ROS2.Message, new()
                => _hub.AddBinding(_source, _bus, contract, map, dispose);
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
}
#endif
