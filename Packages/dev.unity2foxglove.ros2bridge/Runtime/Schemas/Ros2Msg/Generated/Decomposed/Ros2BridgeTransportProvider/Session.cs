// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Manager-local FoxRun transport Provider for the ROS 2 sidecar.

using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity2Foxglove.Ros2Bridge
{

#if UNITY_5_3_OR_NEWER
    public sealed partial class Ros2BridgeTransportProvider
    {
        private sealed class Session :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource,
            IFoxRunGeneratedTransportSession,
            IFoxRunTransportSchemaContributor,
            IFoxRunOrdinaryPayloadMapper,
            IFoxRunMcapDecoderContribution
        {
            private Ros2BridgeTransportProvider _owner;
            private Ros2BridgeRuntime _runtime;
            private Ros2BridgeGeneratedSubscriptionRuntime
                _subscriptions;
            private readonly int _sendTimeoutMs;

            internal Session(
                Ros2BridgeTransportProvider owner,
                ulong generation,
                Ros2BridgeRuntime runtime,
                int sendTimeoutMs)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Generation = generation;
                _runtime = runtime
                           ?? throw new ArgumentNullException(nameof(runtime));
                _subscriptions =
                    new Ros2BridgeGeneratedSubscriptionRuntime(
                        runtime,
                        Id,
                        generation);
                _sendTimeoutMs = sendTimeoutMs;
            }

            public FoxRunTransportId Id { get; } =
                new FoxRunTransportId(ProviderId);

            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe;

            public ulong Generation { get; }
            public string StableMapperId => ProviderId + "/ordinary-cdr-v1";
            public string StableDecoderId => ProviderId + "/mcap-cdr-v1";

            public bool TryResolveSchema(
                in FoxRunTransportSchemaRequest request,
                out FoxRunTransportSchemaContribution contribution,
                out string reason)
            {
                if (!FoxgloveRos2MsgSchemaCatalog.TryGet(
                        request.LogicalSchemaName,
                        out var schema))
                {
                    contribution = default;
                    reason = "No bundled ROS 2 schema matches '"
                             + request.LogicalSchemaName
                             + "'.";
                    return false;
                }

                contribution = new FoxRunTransportSchemaContribution(
                    ProviderId + "/" + schema.SchemaName,
                    schema.SchemaName,
                    Ros2BridgeMcapCodecs.SchemaEncoding,
                    System.Text.Encoding.UTF8.GetBytes(schema.Content));
                reason = string.Empty;
                return true;
            }

            public bool TryMap(
                in FoxRunOrdinaryPayloadRequest request,
                out FoxRunOrdinaryPayloadContribution contribution,
                out string reason)
            {
                try
                {
                    return TryMapOrdinary(
                        in request,
                        out contribution,
                        out reason);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NotSupportedException)
                {
                    contribution = default;
                    reason = Bound(exception.Message);
                    return false;
                }
            }

            public IMcapMessageDecoderFactory CreateFactory()
                => new CompositeMcapDecoderFactory(
                    Ros2BridgeMcapCodecs.CreateFactories());

            internal Ros2BridgeStatsSnapshot GetStatsSnapshot()
                => _runtime?.GetStatsSnapshot()
                   ?? Ros2BridgeStatsSnapshot.Disabled;

            public FoxRunTransportStatusSnapshot CaptureStatus(
                FoxRunTransportCapabilities selectedDirections)
            {
                var runtime = _runtime;
                var subscriptions = _subscriptions;
                return Ros2BridgeTransportStatusMapper.Create(
                    Generation,
                    selectedDirections,
                    runtime?.LifecycleState
                    ?? Ros2BridgeRuntimeLifecycleState.Stopped,
                    runtime?.GetStatsSnapshot()
                    ?? Ros2BridgeStatsSnapshot.Disabled,
                    runtime?.HasInboundPipeline ?? false,
                    runtime?.GetPublisherObservationSnapshot()
                    ?? Ros2BridgePublisherObservationSnapshot.Empty,
                    subscriptions?.GetObservationSnapshot()
                    ?? Ros2BridgeSubscriptionObservationSnapshot.Empty,
                    runtime?.GetInboundStatsSnapshot());
            }

            public FoxRunTransportPublishResult PublishGenerated(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                if (request.Source
                    is IFoxRunBridgeGeneratedPublishSource bridgeSource)
                {
                    try
                    {
                        if (!bridgeSource.FoxRunBridge_TryBuildPublish(
                                request.TopicIndex,
                                request.LogTimeNs,
                                out var route,
                                out var reason))
                        {
                            return FoxRunTransportPublishResult.Rejected(
                                Bound(reason));
                        }
                        if (!string.Equals(
                                route.Topic,
                                request.Topic,
                                StringComparison.Ordinal)
                            || route.LogTimeNs != request.LogTimeNs)
                        {
                            return FoxRunTransportPublishResult.Rejected(
                                "Generated Bridge route does not match the captured topic and timestamp.");
                        }

                        return Publish(in route);
                    }
                    catch (Exception exception)
                    {
                        return FoxRunTransportPublishResult.Failed(
                            Bound(exception.Message));
                    }
                }

                return PublishGeneratedOrdinary(in request);
            }

            private FoxRunTransportPublishResult
                PublishGeneratedOrdinary(
                    in FoxRunGeneratedTransportPublishRequest request)
            {
                IFoxRunGeneratedMemberAccess selected = null;
                for (var index = 0;
                     index
                     < request.Source.FoxRunTransport_MemberCount;
                     index++)
                {
                    var candidate =
                        request.Source.FoxRunTransport_GetMember(
                            index);
                    if (candidate == null
                        || !candidate.CanRead
                        || (candidate.Flow != FoxRunFlow.Publish
                            && candidate.Flow
                            != FoxRunFlow.PublishAndSubscribe)
                        || !string.Equals(
                            candidate.Topic,
                            request.Topic,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (selected != null)
                    {
                        return FoxRunTransportPublishResult.Rejected(
                            "ROS2 Bridge ordinary mapping requires exactly one generated member per topic.");
                    }
                    selected = candidate;
                }

                if (selected == null)
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "The generated source has no readable Bridge member for this topic.");
                }

                object value;
                try
                {
                    value = selected.ReadBoxed();
                }
                catch (Exception exception)
                {
                    return FoxRunTransportPublishResult.Failed(
                        Bound(exception.Message));
                }
                if (value == null)
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "ROS2 Bridge cannot map a null generated value.");
                }

                var ordinary =
                    new FoxRunOrdinaryPayloadRequest(
                        selected.StableMemberId,
                        request.Topic,
                        selected.LogicalSchemaName,
                        value,
                        request.LogTimeNs,
                        request.Source
                            .FoxRunTransport_GetCaptureSequence(
                                request.TopicIndex),
                        selected.DeliveryPolicy);
                if (!TryMap(
                        in ordinary,
                        out var contribution,
                        out var reason))
                {
                    return FoxRunTransportPublishResult.Rejected(
                        Bound(reason));
                }

                var route = new FoxRunTransportPublishRoute(
                    ordinary.StablePublisherId,
                    ordinary.Topic,
                    contribution.LogicalSchemaName,
                    contribution.Payload,
                    ordinary.LogTimeNs,
                    ordinary.Sequence,
                    ordinary.DeliveryPolicy,
                    contribution.MessageEncoding,
                    contribution.SchemaEncoding);
                return Publish(in route);
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
            {
                var runtime = _runtime;
                if (runtime == null)
                {
                    return FoxRunTransportPublishResult.Unavailable(
                        "ROS2 Bridge session has ended.");
                }
                if (!string.Equals(
                        route.MessageEncoding,
                        Ros2BridgeMcapCodecs.MessageEncoding,
                        StringComparison.Ordinal))
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "ROS2 Bridge accepts only exact 'cdr' payloads.");
                }
                if (string.IsNullOrWhiteSpace(route.LogicalSchemaName))
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "ROS2 Bridge requires a canonical schema name.");
                }
                if (!string.IsNullOrEmpty(route.SchemaEncoding)
                    && !string.Equals(
                        route.SchemaEncoding,
                        Ros2BridgeMcapCodecs.SchemaEncoding,
                        StringComparison.Ordinal))
                {
                    return FoxRunTransportPublishResult.Rejected(
                        "ROS2 Bridge schema encoding must be exactly 'ros2msg'.");
                }

                try
                {
                    var payload = route.Payload.ToArray();
                    Ros2CdrPayloadValidator.Validate(payload);
                    var qos = ResolveQos(route.DeliveryPolicy);
                    var readiness = runtime.PreparePublisher(
                        route.Topic,
                        route.LogicalSchemaName,
                        qos,
                        out var reason);
                    if (readiness == Ros2BridgePublisherReadiness.Rejected)
                        return FoxRunTransportPublishResult.Rejected(reason);

                    var frame = Ros2BridgeFrame.CreateOwned(
                        route.Topic,
                        route.LogicalSchemaName,
                        Ros2BridgeMcapCodecs.MessageEncoding,
                        route.LogTimeNs,
                        route.Sequence,
                        payload,
                        qos);
                    return runtime.TryEnqueue(frame, out reason)
                        ? FoxRunTransportPublishResult.Accepted()
                        : FoxRunTransportPublishResult.Unavailable(reason);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    || exception is InvalidOperationException)
                {
                    return FoxRunTransportPublishResult.Rejected(
                        Bound(exception.Message));
                }
                catch (Exception exception)
                {
                    return FoxRunTransportPublishResult.Failed(
                        Bound(exception.Message));
                }
            }

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
            {
                var subscriptions = _subscriptions;
                return subscriptions == null
                    ? FoxRunTransportSubscribeResult.Unavailable(
                        "ROS2 Bridge session has ended.")
                    : subscriptions.Subscribe(in route);
            }

            internal int PumpInbound(int maxFrames)
                => _subscriptions?.Pump(maxFrames) ?? 0;

            public void Dispose()
            {
                var runtime = _runtime;
                _runtime = null;
                var subscriptions = _subscriptions;
                _subscriptions = null;
                var owner = _owner;
                _owner = null;
                try
                {
                    subscriptions?.Dispose();
                }
                finally
                {
                    try
                    {
                        runtime?.Dispose();
                    }
                    finally
                    {
                        owner?.ReleaseSession(Generation);
                    }
                }
            }

            private static FoxRunResolvedQos ResolveQos(
                FoxRunDeliveryPolicy policy)
            {
                if (policy.Equals(FoxRunDeliveryPolicy.ProviderDefault))
                    return FoxRunResolvedQos.Default;

                var baseline = FoxRunResolvedQos.Default;
                var reliability = policy.Reliability switch
                {
                    FoxRunDeliveryReliability.Reliable =>
                        FoxRunQosReliability.Reliable,
                    FoxRunDeliveryReliability.BestEffort =>
                        FoxRunQosReliability.BestEffort,
                    FoxRunDeliveryReliability.SystemDefault =>
                        FoxRunQosReliability.SystemDefault,
                    _ => baseline.Reliability
                };
                var durability = policy.Durability switch
                {
                    FoxRunDeliveryDurability.TransientLocal =>
                        FoxRunQosDurability.TransientLocal,
                    FoxRunDeliveryDurability.SystemDefault =>
                        FoxRunQosDurability.SystemDefault,
                    _ => baseline.Durability
                };
                var history = policy.History switch
                {
                    FoxRunDeliveryHistory.KeepAll => FoxRunQosHistory.KeepAll,
                    FoxRunDeliveryHistory.SystemDefault =>
                        FoxRunQosHistory.SystemDefault,
                    _ => baseline.History
                };
                var depth = history == FoxRunQosHistory.KeepLast
                    ? policy.History == FoxRunDeliveryHistory.KeepLast
                        ? Math.Max(1, policy.Depth)
                        : baseline.Depth
                    : 0;
                var profile =
                    reliability == FoxRunQosReliability.SystemDefault
                    && durability == FoxRunQosDurability.SystemDefault
                    && history == FoxRunQosHistory.SystemDefault
                        ? FoxRunQosProfile.SystemDefault
                        : FoxRunQosProfile.Default;
                return new FoxRunResolvedQos(
                    profile,
                    reliability,
                    durability,
                    history,
                    depth);
            }
        }

    }
#endif
}
