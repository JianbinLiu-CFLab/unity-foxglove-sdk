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
    internal static class Ros2BridgeOrdinaryLogicalSchema
    {
        internal static bool Matches(
            string logicalSchemaName,
            IMessage message)
        {
            if (message == null)
                return false;

            var clrTypeName = message.GetType().FullName ?? string.Empty;
            var descriptorName = message.Descriptor?.FullName ?? string.Empty;
            return string.Equals(
                       logicalSchemaName,
                       clrTypeName,
                       StringComparison.Ordinal)
                   || string.Equals(
                       logicalSchemaName,
                       descriptorName,
                       StringComparison.Ordinal);
        }
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Optional same-GameObject companion that contributes the ROS 2 Bridge
    /// transport without creating an SDK-to-Bridge assembly dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    /// <remarks>Phase192 source sections: Mapping.cs, LifecycleAndAccounting.cs, Session.cs, GeneratedSourceRegistration.cs</remarks>
    public sealed partial class Ros2BridgeTransportProvider :
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
        private double _nextGeneratedSourceScanTime;

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
            if (!Ros2BridgeGeneratedSourceScanGate.TryAdvance(
                    Time.unscaledTimeAsDouble,
                    ref _nextGeneratedSourceScanTime))
                return;

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

    }
#endif
}
