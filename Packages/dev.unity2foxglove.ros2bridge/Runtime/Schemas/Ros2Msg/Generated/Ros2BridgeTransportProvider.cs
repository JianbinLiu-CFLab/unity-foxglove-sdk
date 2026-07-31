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
    /// <summary>
    /// Optional same-GameObject companion that contributes the ROS 2 Bridge
    /// transport without creating an SDK-to-Bridge assembly dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class Ros2BridgeTransportProvider :
        MonoBehaviour,
        IFoxRunTransportProvider,
        IFoxRunTransportSchemaContributor,
        IFoxRunOrdinaryPayloadMapper,
        IFoxRunMcapDecoderContribution
    {
        public const string ProviderId = "unity2foxglove.ros2bridge";

        private FoxgloveManager _manager;
        [SerializeField] private bool _available = true;
        [SerializeField] private bool _autoConnect = true;
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField, Min(1)] private int _port = 8767;
        [SerializeField, Min(1)] private int _queueCapacity = 1024;
        [SerializeField, Min(1)] private int _reconnectIntervalMs = 1000;
        [SerializeField, Min(1)] private int _sendTimeoutMs = 1000;

        private ulong _activeGeneration;
        private Session _activeSession;
        private readonly Dictionary<
            IFoxRunBridgeGeneratedSubscribeSource,
            GeneratedSourceRegistration> _generatedSources =
                new Dictionary<
                    IFoxRunBridgeGeneratedSubscribeSource,
                    GeneratedSourceRegistration>();
        private readonly Dictionary<
            IFoxRunBridgeGeneratedSubscribeSource,
            string> _generatedSourceFailures =
                new Dictionary<
                    IFoxRunBridgeGeneratedSubscribeSource,
                    string>();
        private float _nextGeneratedSourceScanTime;

        public FoxRunTransportId Id { get; } =
            new FoxRunTransportId(ProviderId);

        public FoxRunTransportCapabilities Capabilities =>
            FoxRunTransportCapabilities.Publish
            | FoxRunTransportCapabilities.Subscribe;

        public FoxRunTransportLifecycleState LifecycleState =>
            !isActiveAndEnabled || !_available
                ? FoxRunTransportLifecycleState.Unavailable
                : _activeGeneration == 0
                    ? FoxRunTransportLifecycleState.Available
                    : FoxRunTransportLifecycleState.Active;

        public string StableMapperId => ProviderId + "/ordinary-cdr-v1";
        public string StableDecoderId => ProviderId + "/mcap-cdr-v1";

        /// <summary>Latest immutable runtime status for samples and inspectors.</summary>
        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
            => _activeSession?.GetStatsSnapshot()
               ?? Ros2BridgeStatsSnapshot.Disabled;

        private void Reset()
        {
            _manager = GetComponent<FoxgloveManager>();
        }

        private void OnEnable()
        {
            ResolveManager();
            _manager?.RegisterFoxRunTransportProvider(this);
        }

        private void OnDisable()
        {
            ClearGeneratedSources();
            _manager?.UnregisterFoxRunTransportProvider(this);
        }

        private void OnDestroy()
        {
            ClearGeneratedSources();
            _manager?.UnregisterFoxRunTransportProvider(this);
        }

        private void Update()
        {
            ResolveManager();
            var session = _activeSession;
            var selected =
                session != null
                && ReferenceEquals(
                    _manager?.ActiveFoxRunTransportSession
                        ?.SubscribeTransport,
                    session);
            if (!selected)
            {
                ClearGeneratedSources();
                return;
            }

            session.PumpInbound(maxFrames: 64);
            if (Time.unscaledTime < _nextGeneratedSourceScanTime)
                return;
            _nextGeneratedSourceScanTime = Time.unscaledTime + 0.5f;
            SynchronizeGeneratedSources(session);
        }

        private void OnValidate()
        {
            _port = Clamp(_port, 1, 65535);
            _queueCapacity = Math.Max(1, _queueCapacity);
            _reconnectIntervalMs = Math.Max(1, _reconnectIntervalMs);
            _sendTimeoutMs = Math.Max(1, _sendTimeoutMs);
        }

        public bool TryCaptureSession(
            ulong generation,
            out IFoxRunTransportSession session,
            out string reason)
        {
            session = null;
            if (!isActiveAndEnabled || !_available)
            {
                reason = "ROS2 Bridge Provider is unavailable.";
                return false;
            }
            if (!_autoConnect)
            {
                reason =
                    "ROS2 Bridge Provider requires Auto Connect; no manual data-session path is configured.";
                return false;
            }

            try
            {
                var runtime = new Ros2BridgeRuntime(
                    NormalizeHost(_host),
                    Clamp(_port, 1, 65535),
                    Math.Max(1, _queueCapacity),
                    Math.Max(1, _reconnectIntervalMs),
                    Math.Max(1, _sendTimeoutMs),
                    sinkFactory: null,
                    retirementOwner: FoxRunTransportRetirementOwner.Shared,
                    providerId: Id,
                    direction: FoxRunTransportDirection.Publish,
                    generation: generation,
                    joinTimeoutMs: Math.Max(
                        1000,
                        Math.Max(1, _sendTimeoutMs) + 250),
                    requiresSubscription: true);
                runtime.Start(enabled: true, autoConnect: _autoConnect);
                var captured = new Session(
                    this,
                    generation,
                    runtime,
                    Math.Max(1, _sendTimeoutMs));
                session = captured;
                _activeSession = captured;
                _activeGeneration = generation;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException)
            {
                reason = Bound(exception.Message);
                return false;
            }
        }

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

        private void ResolveManager()
        {
            if (_manager == null)
                _manager = GetComponent<FoxgloveManager>();
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        private static string NormalizeHost(string host)
            => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

        private static string Bound(string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason)
                ? "ROS2 Bridge Provider failed."
                : reason;
            return reason.Length <= 512 ? reason : reason.Substring(0, 512);
        }

        private static bool TryMapOrdinary(
            in FoxRunOrdinaryPayloadRequest request,
            out FoxRunOrdinaryPayloadContribution contribution,
            out string reason)
        {
            string schemaName;
            byte[] payload;
            if (request.Value is SensorCompressedImageFrame compressedImage)
            {
                if (!MatchesLogicalType<SensorCompressedImageFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(SensorCompressedImageFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorCompressedImage;
                payload = Ros2CdrSensorCompressedImageBuilder.Serialize(
                    compressedImage.UnixNs,
                    compressedImage.FrameId,
                    compressedImage.Data,
                    compressedImage.Format);
            }
            else if (request.Value is SensorCameraInfoFrame cameraInfo)
            {
                if (!MatchesLogicalType<SensorCameraInfoFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(SensorCameraInfoFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorCameraInfo;
                payload = Ros2CdrSensorCameraInfoBuilder.Serialize(
                    cameraInfo.UnixNs,
                    cameraInfo.FrameId,
                    cameraInfo.Width,
                    cameraInfo.Height,
                    cameraInfo.DistortionModel,
                    cameraInfo.D,
                    cameraInfo.K,
                    cameraInfo.R,
                    cameraInfo.P);
            }
            else if (request.Value is PackedPointCloudFrame pointCloud2)
            {
                if (!MatchesLogicalType<PackedPointCloudFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(PackedPointCloudFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorPointCloud2;
                payload = Ros2CdrSensorPointCloud2Builder.Serialize(
                    pointCloud2.UnixNs,
                    pointCloud2.FrameId,
                    pointCloud2.Height,
                    pointCloud2.Width,
                    pointCloud2.Fields,
                    pointCloud2.PointStep,
                    pointCloud2.Data,
                    pointCloud2.IsDense);
            }
            else if (request.Value is IMessage message)
            {
                var descriptorName = message.Descriptor?.FullName ?? string.Empty;
                if (!string.Equals(
                        request.LogicalSchemaName,
                        descriptorName,
                        StringComparison.Ordinal))
                {
                    contribution = default;
                    reason = "Logical schema '"
                             + request.LogicalSchemaName
                             + "' does not match protobuf value '"
                             + descriptorName
                             + "'.";
                    return false;
                }

                if (!TryMapFoxgloveSchema(descriptorName, out schemaName))
                {
                    contribution = default;
                    reason = "ROS2 Bridge has no ordinary mapping for protobuf schema '"
                             + descriptorName
                             + "'.";
                    return false;
                }

                payload = Ros2CdrSerializerRegistry.Serialize(
                    schemaName,
                    message);
            }
            else
            {
                contribution = default;
                reason =
                    "ROS2 Bridge ordinary mapping accepts only supported neutral sensor DTOs or Foxglove protobuf values.";
                return false;
            }

            Ros2CdrPayloadValidator.Validate(payload);
            contribution = new FoxRunOrdinaryPayloadContribution(
                schemaName,
                payload,
                Ros2BridgeMcapCodecs.MessageEncoding,
                Ros2BridgeMcapCodecs.SchemaEncoding);
            reason = string.Empty;
            return true;
        }

        private static bool TryMapFoxgloveSchema(
            string logicalSchemaName,
            out string schemaName)
        {
            switch (logicalSchemaName)
            {
                case "foxglove.FrameTransform":
                    schemaName = Ros2PublisherSchemaNames.FrameTransform;
                    return true;
                case "foxglove.SceneUpdate":
                    schemaName = Ros2PublisherSchemaNames.SceneUpdate;
                    return true;
                case "foxglove.CompressedImage":
                    schemaName = Ros2PublisherSchemaNames.CompressedImage;
                    return true;
                case "foxglove.CameraCalibration":
                    schemaName = Ros2PublisherSchemaNames.CameraCalibration;
                    return true;
                case "foxglove.LaserScan":
                    schemaName = Ros2PublisherSchemaNames.LaserScan;
                    return true;
                case "foxglove.PointCloud":
                    schemaName = Ros2PublisherSchemaNames.PointCloud;
                    return true;
                case "foxglove.CompressedPointCloud":
                    schemaName = Ros2PublisherSchemaNames.CompressedPointCloud;
                    return true;
                default:
                    schemaName = string.Empty;
                    return false;
            }
        }

        private static bool MatchesLogicalType<T>(string logicalSchemaName)
            => string.Equals(
                logicalSchemaName,
                typeof(T).FullName,
                StringComparison.Ordinal);

        private static bool RejectLogicalType(
            string logicalSchemaName,
            Type expectedType,
            out FoxRunOrdinaryPayloadContribution contribution,
            out string reason)
        {
            contribution = default;
            reason = "Logical schema '"
                     + logicalSchemaName
                     + "' does not match value type '"
                     + expectedType.FullName
                     + "'.";
            return false;
        }

        private void ReleaseSession(ulong generation)
        {
            if (_activeGeneration != generation)
                return;
            ClearGeneratedSources();
            _activeSession = null;
            _activeGeneration = 0;
        }

        private void SynchronizeGeneratedSources(Session session)
        {
            var seen = new HashSet<
                IFoxRunBridgeGeneratedSubscribeSource>();
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (!(behaviours[index]
                      is IFoxRunBridgeGeneratedSubscribeSource source))
                {
                    continue;
                }
                seen.Add(source);
                if (_generatedSources.ContainsKey(source))
                    continue;
                GeneratedSourceRegistration registration;
                string reason;
                var registered = false;
                try
                {
                    registered = GeneratedSourceRegistration.TryCreate(
                        session,
                        source,
                        out registration,
                        out reason);
                }
                catch (Exception exception)
                {
                    registration = null;
                    reason = Bound(exception.Message);
                }
                if (registered)
                {
                    _generatedSources.Add(source, registration);
                    _generatedSourceFailures.Remove(source);
                }
                else if (IsHardGeneratedSourceFailure(reason))
                {
                    var bounded = Bound(reason);
                    if (!_generatedSourceFailures.TryGetValue(
                            source,
                            out var previous)
                        || !string.Equals(
                            previous,
                            bounded,
                            StringComparison.Ordinal))
                    {
                        _generatedSourceFailures[source] = bounded;
                        Debug.LogWarning(
                            "[FoxRun] ROS2 Bridge generated subscription rejected: "
                            + bounded);
                    }
                }
            }

            var stale = new List<
                IFoxRunBridgeGeneratedSubscribeSource>();
            foreach (var pair in _generatedSources)
            {
                if (!seen.Contains(pair.Key)
                    || pair.Key is MonoBehaviour behaviour
                    && (behaviour == null
                        || !behaviour.isActiveAndEnabled))
                {
                    stale.Add(pair.Key);
                }
            }
            for (var index = 0; index < stale.Count; index++)
            {
                var source = stale[index];
                if (_generatedSources.TryGetValue(
                        source,
                        out var registration))
                {
                    _generatedSources.Remove(source);
                    _generatedSourceFailures.Remove(source);
                    var cleanupError = Ros2BridgeCleanup.RunAll(
                        1,
                        _ => registration.Dispose());
                    if (cleanupError != null)
                        LogGeneratedSourceCleanupFailure(cleanupError);
                }
            }
            if (_generatedSourceFailures.Count != 0)
            {
                var staleFailures = new List<
                    IFoxRunBridgeGeneratedSubscribeSource>();
                foreach (var source in _generatedSourceFailures.Keys)
                {
                    if (!seen.Contains(source)
                        || source is MonoBehaviour behaviour
                        && (behaviour == null
                            || !behaviour.isActiveAndEnabled))
                    {
                        staleFailures.Add(source);
                    }
                }
                for (var index = 0;
                     index < staleFailures.Count;
                     index++)
                {
                    _generatedSourceFailures.Remove(
                        staleFailures[index]);
                }
            }
        }

        private void ClearGeneratedSources()
        {
            var registrations = new List<GeneratedSourceRegistration>(
                _generatedSources.Values);
            _generatedSources.Clear();
            _generatedSourceFailures.Clear();
            var cleanupError = Ros2BridgeCleanup.RunAll(
                registrations.Count,
                index => registrations[index].Dispose(),
                reverse: true);
            if (cleanupError != null)
                LogGeneratedSourceCleanupFailure(cleanupError);
        }

        private static void LogGeneratedSourceCleanupFailure(
            Exception exception)
            => Debug.LogWarning(
                "[FoxRun] ROS2 Bridge generated subscription cleanup failed: "
                + Bound(exception?.Message));

        private static bool IsHardGeneratedSourceFailure(string reason)
            => !string.IsNullOrWhiteSpace(reason)
               && reason.IndexOf(
                   "not ready",
                   StringComparison.OrdinalIgnoreCase) < 0
               && reason.IndexOf(
                   "unavailable",
                   StringComparison.OrdinalIgnoreCase) < 0;

        private sealed class Session :
            IFoxRunTransportSession,
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
                    if (readiness != Ros2BridgePublisherReadiness.Ready)
                        return FoxRunTransportPublishResult.Unavailable(reason);

                    var frame = Ros2BridgeFrame.CreateOwned(
                        route.Topic,
                        route.LogicalSchemaName,
                        Ros2BridgeMcapCodecs.MessageEncoding,
                        route.LogTimeNs,
                        route.Sequence,
                        payload,
                        qos);
                    return runtime.TryEnqueuePrepared(frame, out reason)
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

        private sealed class GeneratedSourceRegistration :
            IDisposable
        {
            private IFoxRunBridgeGeneratedSubscribeSource _source;
            private readonly ulong _generation;
            private readonly List<
                IFoxRunTransportSubscriptionLease> _leases;
            private readonly List<int> _publishTopicIndexes;

            private GeneratedSourceRegistration(
                IFoxRunBridgeGeneratedSubscribeSource source,
                ulong generation,
                List<IFoxRunTransportSubscriptionLease> leases,
                List<int> publishTopicIndexes)
            {
                _source = source;
                _generation = generation;
                _leases = leases;
                _publishTopicIndexes = publishTopicIndexes;
            }

            internal static bool TryCreate(
                Session session,
                IFoxRunBridgeGeneratedSubscribeSource source,
                out GeneratedSourceRegistration registration,
                out string reason)
            {
                registration = null;
                reason = string.Empty;
                if (session == null || source == null)
                {
                    reason = "The generated Bridge subscription source is unavailable.";
                    return false;
                }
                var leases = new List<
                    IFoxRunTransportSubscriptionLease>();
                var publishTopicIndexes = new List<int>();
                try
                {
                    var bindingCount =
                        source.FoxRunBridge_SubscribeBindingCount;
                    if (bindingCount <= 0)
                    {
                        reason =
                            "The generated Bridge subscription source has no bindings.";
                        return false;
                    }
                    if (checked((ulong)bindingCount)
                        > Protocol.U2R2ProtocolLimits.Default.MaxContracts)
                    {
                        reason =
                            "The generated Bridge subscription source exceeds the contract bound.";
                        return false;
                    }
                    for (var bindingIndex = 0;
                         bindingIndex
                         < bindingCount;
                         bindingIndex++)
                    {
                        if (!source.FoxRunBridge_TryGetSubscribeBinding(
                                bindingIndex,
                                out var binding,
                                out reason))
                        {
                            return false;
                        }
                        if (!IsValidBinding(binding, out reason))
                            return false;
                        var capturedBindingIndex = binding.BindingIndex;
                        var route = new FoxRunTransportSubscribeRoute(
                            binding.StableMemberId,
                            binding.Topic,
                            binding.CanonicalRosType,
                            binding.MaxPayloadBytes,
                            binding.DeliveryPolicy,
                            (payload, receiveTimeNs, sequence) =>
                            {
                                if (!source.FoxRunBridge_TryDecodeAndApply(
                                        capturedBindingIndex,
                                        payload,
                                        ProviderId,
                                        session.Generation,
                                        markRemoteOwned: true,
                                        out var decodeReason))
                                {
                                    throw new InvalidDataException(
                                        Bound(decodeReason));
                                }
                            },
                            binding.MessageEncoding);
                        var result = session.Subscribe(in route);
                        if (result.State
                            != FoxRunTransportRouteResultState.Accepted
                            || result.Lease == null)
                        {
                            reason = result.Reason;
                            return false;
                        }
                        leases.Add(result.Lease);
                        if (binding.PublishTopicIndex >= 0)
                        {
                            publishTopicIndexes.Add(
                                binding.PublishTopicIndex);
                        }
                    }

                    registration = new GeneratedSourceRegistration(
                        source,
                        session.Generation,
                        leases,
                        publishTopicIndexes);
                    leases = null;
                    reason = string.Empty;
                    return true;
                }
                finally
                {
                    if (leases != null)
                    {
                        var cleanupError = Ros2BridgeCleanup.RunAll(
                            leases.Count,
                            index => leases[index]?.Dispose(),
                            reverse: true);
                        if (cleanupError != null)
                            reason = Bound(cleanupError.Message);
                    }
                }
            }

            public void Dispose()
            {
                var source = _source;
                _source = null;
                var first = Ros2BridgeCleanup.RunAll(
                    _leases.Count,
                    index => _leases[index]?.Dispose(),
                    reverse: true);
                _leases.Clear();
                if (source != null)
                {
                    var ownershipError = Ros2BridgeCleanup.RunAll(
                        _publishTopicIndexes.Count,
                        index => source.FoxRunBridge_ReleaseRemoteOwnership(
                            _publishTopicIndexes[index],
                            ProviderId,
                            _generation));
                    first ??= ownershipError;
                }
                _publishTopicIndexes.Clear();
                if (first != null)
                    throw first;
            }

            private static bool IsValidBinding(
                FoxRunBridgeGeneratedSubscribeBinding binding,
                out string reason)
            {
                if (binding.BindingIndex < 0
                    || string.IsNullOrWhiteSpace(binding.StableMemberId)
                    || string.IsNullOrWhiteSpace(binding.Topic)
                    || string.IsNullOrWhiteSpace(binding.CanonicalRosType)
                    || !string.Equals(
                        binding.MessageEncoding,
                        "cdr",
                        StringComparison.Ordinal)
                    || binding.MaxPayloadBytes <= 0
                    || binding.MaxPayloadBytes
                    > Ros2BridgeFrameWriter.MaxPayloadBytes
                    || !IsSha256(binding.SchemaSha256))
                {
                    reason =
                        "The generated Bridge subscription binding is incomplete or invalid.";
                    return false;
                }
                reason = string.Empty;
                return true;
            }

            private static bool IsSha256(string value)
            {
                if (value == null || value.Length != 64)
                    return false;
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    if (character < '0'
                        || character > '9'
                        && (character < 'a' || character > 'f'))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private sealed class CompositeMcapDecoderFactory :
            IStableMcapMessageDecoderFactory
        {
            private readonly System.Collections.Generic.IReadOnlyList<
                IMcapMessageDecoderFactory> _factories;

            internal CompositeMcapDecoderFactory(
                System.Collections.Generic.IReadOnlyList<
                    IMcapMessageDecoderFactory> factories)
            {
                _factories = factories
                             ?? throw new ArgumentNullException(nameof(factories));
            }

            public string StableDecoderId => ProviderId + "/mcap-cdr-v1";

            public IMcapMessageDecoder TryCreate(
                McapSchema schema,
                McapChannel channel)
            {
                for (var i = 0; i < _factories.Count; i++)
                {
                    var decoder = _factories[i]?.TryCreate(schema, channel);
                    if (decoder != null)
                        return decoder;
                }

                return null;
            }
        }
    }
#endif
}
