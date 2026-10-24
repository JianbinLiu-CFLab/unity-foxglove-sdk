// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Application lifecycle, manager policy, and source scanning.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2CustomPublisherHub
    {
        private void OnEnable()
        {
            _stopping = false;
        }

        private void Update()
        {
            _cleanupRetryCooldown -= Time.deltaTime;
            if (_cleanupRetryCooldown <= 0f)
            {
                _cleanupRetryCooldown = ScanIntervalSeconds;
                RetryPendingBindings();
            }

            if (_stopping)
            {
                StopBindings();
                return;
            }

            if (!_providerSessionActive || _manager == null)
            {
                StopBindings();
                return;
            }

            ApplyPublishSessionPolicy(
                _manager.ActiveFoxRunPublishSessionPolicy);

            // Hierarchy notifications deliberately make the cheap shutdown
            // read fail closed without rebuilding the scene cache. Refresh
            // that dirty cache before deciding the window is still unsafe,
            // otherwise one recoverable hierarchy change can stop native
            // publishers for the remainder of Play Mode.
            var bridgeLifecycleIsShuttingDown =
                Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(
                    gameObject.scene)
                && !Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                    gameObject.scene);

            // Output policy is independent from the captured subscription
            // session. A Publish custom endpoint must stay available while
            // subscriptions or the legacy component-output switch are disabled.
            if (ShouldStopFoxRunPublishing(
                    _publishSessionTracker.AllowsPublishing,
                    legacyComponentNativeOutputEnabled: true,
                    bridgeLifecycleIsShuttingDown))
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
            var inheritedQos =
                _provider?.ActivePublishQos
                ?? FoxRunResolvedQos.Default;
            var defaultTargets = publishSessionPolicy == null
                ? FoxRunRos2RouteEndpoint.R2fu
                : FoxRunRos2RouteResolver
                    .FromPublishProviders(
                        publishSessionPolicy
                            .PublishTransportIds);
            var subscriptionPolicy = _manager == null
                ? null
                : _manager.ActiveFoxRunSubscriptionSessionPolicy;
            var defaultSource = subscriptionPolicy != null
                                && subscriptionPolicy.SubscriptionsEnabled
                ? FoxRunRos2RouteResolver
                    .FromSubscribeProvider(
                        subscriptionPolicy.DefaultProvider)
                : FoxRunRos2RouteEndpoint.WebSocket;
            ScanAndReconcile(bus, inheritedQos, defaultSource, defaultTargets);
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
        }

        private void OnPublishSessionChanged(FoxRunPublishSessionPolicy policy)
            => ApplyPublishSessionPolicy(policy);

        private void ApplyPublishSessionPolicy(FoxRunPublishSessionPolicy policy)
        {
            if (!_publishSessionTracker.Observe(policy))
                return;

            StopBindings();
            _scanCooldown = 0f;
            _cleanupRetryCooldown = 0f;
        }

        private void ScanAndReconcile(
            FoxTopicBus bus,
            FoxRunResolvedQos inheritedQos,
            FoxRunRos2RouteEndpoint defaultSource,
            FoxRunRos2RouteEndpoint defaultTargets)
        {
            _scanCompleted = false;
            _nativeDemand.Clear();
            _seen.Clear();
            _existing.Clear();
            for (var index = 0; index < _bindings.Count; index++)
                _existing.Add(_bindings[index].Identity);

            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Array.Sort(behaviours, CompareBehaviours);
            for (var index = 0; index < behaviours.Length; index++)
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
            _observedNativeContractCount = Math.Max(
                _nativeDemand.Count,
                _bindings.Count);
            _scanCompleted = true;
        }

    }
}
#endif
