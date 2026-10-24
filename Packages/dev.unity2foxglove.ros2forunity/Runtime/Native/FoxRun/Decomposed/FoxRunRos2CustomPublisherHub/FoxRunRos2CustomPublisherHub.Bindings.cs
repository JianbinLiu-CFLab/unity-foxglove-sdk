// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Registrar and hosted binding implementations.

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
        private sealed class CollectingRegistrar : IFoxRunRos2CustomPublisherRegistrar
        {
            private readonly FoxRunRos2CustomPublisherHub _hub;
            private readonly MonoBehaviour _source;
            private readonly FoxTopicBus _bus;
            private readonly FoxRunResolvedQos _inheritedQos;
            private readonly FoxRunRos2RouteEndpoint _defaultSource;
            private readonly FoxRunRos2RouteEndpoint _defaultTargets;

            internal CollectingRegistrar(
                FoxRunRos2CustomPublisherHub hub,
                MonoBehaviour source,
                FoxTopicBus bus,
                FoxRunResolvedQos inheritedQos,
                FoxRunRos2RouteEndpoint defaultSource,
                FoxRunRos2RouteEndpoint defaultTargets)
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

        internal sealed class HostedBinding<TDto, TEnvelope> : IFoxRunRos2CustomPublisherHostedBinding
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
            public string Topic => _binding.Topic;
            public bool IsStopped => _binding.IsStopped;
            public bool CleanupPending => _binding.CleanupPending;
            public bool CleanupRetryExhausted => _binding.CleanupRetryExhausted;
            public bool CleanupRetryFatal => _binding.CleanupRetryFatal;
            public void Stop() => _binding.Stop();
            public bool TryRetryCleanup() => _binding.TryRetryCleanup();
            public bool TryForceRetryCleanup() => _binding.TryForceRetryCleanup();

            public FoxRunTransportPublishResult PublishGenerated(
                IFoxRunGeneratedMemberAccess member,
                ulong logTimeNs)
            {
                if (!(member is FoxRunGeneratedMemberAccess<TDto> typed))
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "The generated member type does not match the R2FU custom publisher DTO.");
                }

                return _binding.TryPublishCaptured(typed.Read(), logTimeNs)
                    ? FoxRunTransportPublishResult.Accepted()
                    : FoxRunTransportPublishResult.Failed(
                        "The R2FU custom publisher did not publish the captured DTO.");
            }
        }
    }
}
#endif
