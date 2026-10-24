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
        string Topic { get; }
        bool IsStopped { get; }
        bool CleanupPending { get; }
        bool CleanupRetryExhausted { get; }
        bool CleanupRetryFatal { get; }
        void Stop();
        bool TryRetryCleanup();
        bool TryForceRetryCleanup();
        FoxRunTransportPublishResult PublishGenerated(
            IFoxRunGeneratedMemberAccess member,
            ulong logTimeNs);
    }

    /// <summary>
    /// Independent output-demand hub for generated custom DTO publishers.
    /// It owns no node itself: every endpoint lease is acquired from the custom
    /// transport host so input and output converge on one node lifecycle.
    /// </summary>
    [DefaultExecutionOrder(-434)]
    [AddComponentMenu("")]
    internal sealed partial class FoxRunRos2CustomPublisherHub : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaximumBindings = 4096;

        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _bindings =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly List<IFoxRunRos2CustomPublisherHostedBinding> _stale =
            new List<IFoxRunRos2CustomPublisherHostedBinding>();
        private readonly HashSet<string> _existing = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _nativeDemand = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _terminalRegistrationFailures =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new HashSet<string>(StringComparer.Ordinal);
        private readonly FoxRunRos2CustomPublisherSessionTracker _publishSessionTracker =
            new FoxRunRos2CustomPublisherSessionTracker();
        private FoxgloveManager _manager;
        private FoxRunRos2TransportProvider _provider;
        private float _scanCooldown;
        private float _cleanupRetryCooldown;
        private bool _stopping;
        private bool _providerSessionActive;
        private bool _scanCompleted;
        private int _observedNativeContractCount;

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
                StopBindings();
            _scanCooldown = 0f;
            _cleanupRetryCooldown = 0f;
        }

        /// <summary>
        /// Reports whether a started typed binding exists for the captured
        /// source component and topic. Topics without one are not R2FU output.
        /// </summary>
        internal bool OwnsGeneratedTopic(
            in FoxRunGeneratedTransportPublishRequest request)
        {
            if (_stopping || !_providerSessionActive)
                return false;
            var source = request.Source as UnityEngine.Object;
            return source != null
                   && FindGeneratedBinding(
                       _bindings,
                       source.GetInstanceID(),
                       request.Topic) != null;
        }

        /// <summary>
        /// Publishes one captured generated topic through the typed binding
        /// registered for the same source component and topic.
        /// </summary>
        internal FoxRunTransportPublishResult PublishGenerated(
            in FoxRunGeneratedTransportPublishRequest request)
        {
            if (_stopping || !_providerSessionActive)
            {
                return FoxRunTransportPublishResult.Unavailable(
                    "The R2FU custom publisher hub is stopped.");
            }

            var source = request.Source as UnityEngine.Object;
            if (source == null)
            {
                return FoxRunTransportPublishResult.Rejected(
                    "The generated source is not a live Unity component.");
            }

            return PublishGenerated(
                _bindings,
                source.GetInstanceID(),
                in request);
        }

        internal static IFoxRunRos2CustomPublisherHostedBinding FindGeneratedBinding(
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
            int sourceInstanceId,
            string topic)
        {
            var count = bindings == null ? 0 : bindings.Count;
            for (var index = 0; index < count; index++)
            {
                var binding = bindings[index];
                if (binding != null
                    && !binding.IsStopped
                    && binding.SourceInstanceId == sourceInstanceId
                    && string.Equals(
                        binding.Topic,
                        topic,
                        StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            return null;
        }

        internal static FoxRunTransportPublishResult PublishGenerated(
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
            int sourceInstanceId,
            in FoxRunGeneratedTransportPublishRequest request)
        {
            var selected = FindGeneratedBinding(
                bindings,
                sourceInstanceId,
                request.Topic);
            if (selected == null)
            {
                return FoxRunTransportPublishResult.Unavailable(
                    "The R2FU custom publisher for this topic is not ready.");
            }

            IFoxRunGeneratedMemberAccess member = null;
            var memberCount = request.Source.FoxRunTransport_MemberCount;
            for (var index = 0; index < memberCount; index++)
            {
                var candidate = request.Source.FoxRunTransport_GetMember(index);
                if (candidate == null
                    || !candidate.CanRead
                    || (candidate.Flow != FoxRunFlow.Publish
                        && candidate.Flow != FoxRunFlow.PublishAndSubscribe)
                    || !string.Equals(
                        candidate.Topic,
                        request.Topic,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (member != null)
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "R2FU custom publishing requires exactly one generated member per topic.");
                }
                member = candidate;
            }

            if (member == null)
            {
                return FoxRunTransportPublishResult.Rejected(
                    "The generated source has no readable R2FU member for this topic.");
            }

            return selected.PublishGenerated(member, request.LogTimeNs);
        }

        internal FoxRunTransportDirectionStatus CaptureTransportStatus()
        {
            var ready = 0;
            for (var index = 0; index < _bindings.Count; index++)
                if (!_bindings[index].IsStopped)
                    ready++;
            var failed = _bindings.Count - ready;
            var observed = Math.Max(_observedNativeContractCount, _bindings.Count);
            return BuildTransportStatus(
                _providerSessionActive,
                _stopping,
                _scanCompleted,
                observed,
                ready,
                failed);
        }

        internal static FoxRunTransportDirectionStatus BuildTransportStatus(
            bool sessionActive,
            bool stopping,
            bool scanCompleted,
            int observedContracts,
            int readyContracts,
            int failedContracts)
        {
            if (!sessionActive || stopping)
            {
                return new FoxRunTransportDirectionStatus(
                    FoxRunTransportDirection.Publish,
                    selected: true,
                    FoxRunTransportObservedState.Stopped,
                    0,
                    0,
                    0);
            }

            var pending = observedContracts - readyContracts - failedContracts;
            var state = failedContracts != 0
                ? readyContracts == 0
                    ? FoxRunTransportObservedState.Failed
                    : FoxRunTransportObservedState.Degraded
                : !scanCompleted || pending != 0
                    ? readyContracts == 0
                        ? FoxRunTransportObservedState.Starting
                        : FoxRunTransportObservedState.Degraded
                    : FoxRunTransportObservedState.Ready;
            FoxRunTransportDiagnostic? diagnostic = failedContracts != 0
                ? new FoxRunTransportDiagnostic(
                    "R2FU002",
                    "One or more native publisher bindings stopped.")
                : !scanCompleted || pending != 0
                    ? new FoxRunTransportDiagnostic(
                        "R2FU001",
                        "Native publisher bindings are waiting for runtime readiness.")
                    : (FoxRunTransportDiagnostic?)null;
            return new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Publish,
                selected: true,
                state,
                observedContracts,
                readyContracts,
                failedContracts,
                diagnostic);
        }

    }
}
#endif
