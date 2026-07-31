// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Manager-local neutral FoxRun transport selection and session capture.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.FoxgloveSDK.IO;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField]
        private string[] _foxRunPublishTransportIds =
        {
            FoxgloveWebSocketTransport.Id
        };

        [SerializeField]
        private string _foxRunSubscribeTransportId =
            FoxgloveWebSocketTransport.Id;

        private readonly FoxRunTransportProviderRegistry
            _foxRunTransportProviderRegistry =
                new FoxRunTransportProviderRegistry();

        private BuiltInFoxgloveTransportProvider _builtInFoxgloveTransportProvider;
        private FoxRunTransportSessionSnapshot _activeFoxRunTransportSession;
        private FoxRunTransportSessionCaptureError?
            _lastFoxRunTransportSessionCaptureError;
        private bool _startServerAfterTransportCapture;

        /// <summary>Frozen neutral Provider sessions for the current Manager lifetime.</summary>
        public FoxRunTransportSessionSnapshot ActiveFoxRunTransportSession =>
            _activeFoxRunTransportSession;

        /// <summary>
        /// Current observed Provider states. With no captured session this is
        /// empty; configured capture failures remain explicit through
        /// <see cref="LastFoxRunTransportSessionCaptureError"/>.
        /// </summary>
        public IReadOnlyList<FoxRunTransportStatusSnapshot>
            CaptureFoxRunTransportStatuses()
            => _activeFoxRunTransportSession?.CaptureStatuses()
               ?? Array.Empty<FoxRunTransportStatusSnapshot>();

        public IReadOnlyList<FoxRunTransportRetirementInfo>
            CaptureRetiredFoxRunTransportWorkers()
            => FoxRunTransportRetirementOwner.Shared.CaptureRetired();

        public IReadOnlyList<FoxRunTransportRetirementExitInfo>
            CaptureFoxRunTransportWorkerFinalExits()
            => FoxRunTransportRetirementOwner.Shared.CaptureFinalExits();

        public FoxRunTransportSessionCaptureError?
            LastFoxRunTransportSessionCaptureError =>
                _lastFoxRunTransportSessionCaptureError;

        /// <summary>Canonical configured publish IDs for the next Manager session.</summary>
        public IReadOnlyList<FoxRunTransportId> ConfiguredFoxRunPublishTransportIds =>
            new FoxRunTransportSelection(
                    _foxRunPublishTransportIds
                    ?? Array.Empty<string>(),
                    subscriptionsEnabled: false,
                    subscribeTransportId: null)
                .PublishTransportIds;

        /// <summary>Configured subscribe ID for the next Manager session.</summary>
        public FoxRunTransportId ConfiguredFoxRunSubscribeTransportId =>
            string.IsNullOrWhiteSpace(_foxRunSubscribeTransportId)
                ? default
                : new FoxRunTransportId(_foxRunSubscribeTransportId);

        /// <summary>
        /// Register one same-GameObject Provider. Duplicate instances claiming
        /// the same ID remain conflicted until only one is registered.
        /// </summary>
        public FoxRunTransportRegistrationResult RegisterFoxRunTransportProvider(
            IFoxRunTransportProvider provider)
        {
            if (provider is Component component
                && !ReferenceEquals(component.gameObject, gameObject))
            {
                throw new InvalidOperationException(
                    "FoxRun transport Providers must register with the Manager on the same GameObject.");
            }

            return _foxRunTransportProviderRegistry.Register(provider);
        }

        /// <summary>Idempotently remove one Provider from next-session selection.</summary>
        public bool UnregisterFoxRunTransportProvider(
            IFoxRunTransportProvider provider)
            => _foxRunTransportProviderRegistry.Unregister(provider);

        /// <summary>
        /// Replaces the next-session neutral selection. Active snapshots are
        /// immutable and are not recaptured.
        /// </summary>
        public void ConfigureFoxRunTransports(
            IEnumerable<string> publishTransportIds,
            bool subscriptionsEnabled,
            string subscribeTransportId)
        {
            var selection = new FoxRunTransportSelection(
                publishTransportIds,
                subscriptionsEnabled,
                subscribeTransportId);
            var publish = new string[selection.PublishTransportIds.Count];
            for (var i = 0; i < publish.Length; i++)
                publish[i] = selection.PublishTransportIds[i].Value;
            _foxRunPublishTransportIds = publish;
            _foxRunSubscribeTransportId = selection.SubscribeTransportId?.Value
                                          ?? string.Empty;
            _enableFoxRunInbound = subscriptionsEnabled;
        }

        internal bool BeginFoxRunTransportSessionIfNeeded()
        {
            if (_activeFoxRunTransportSession != null)
                return true;

            EnsureBuiltInFoxgloveProviderRegistered();
            RegisterSameObjectTransportProviders();

            var generation = Math.Max(
                _foxRunPublishSessionState.Current.SessionGeneration,
                _foxRunSubscriptionSessionState.Current.SessionGeneration);
            if (generation == ulong.MaxValue)
                throw new InvalidOperationException(
                    "FoxRun transport session generation is exhausted.");

            if (!TryCreateCapturedTransportSelection(
                    out var selection,
                    out var selectionReason))
            {
                var configuredId = default(FoxRunTransportId);
                FoxRunTransportId.TryCreate(
                    _foxRunSubscribeTransportId,
                    out configuredId);
                return ReportFoxRunTransportSessionCaptureFailure(
                    new FoxRunTransportSessionCaptureError(
                        FoxRunTransportSessionCaptureFailure.Missing,
                        configuredId,
                        "Configured transport selection is invalid: "
                        + selectionReason));
            }

            if (!_foxRunTransportProviderRegistry.TryCaptureSession(
                    selection,
                    generation + 1UL,
                    out _activeFoxRunTransportSession,
                    out var failure))
            {
                return ReportFoxRunTransportSessionCaptureFailure(
                    failure);
            }

            _lastFoxRunTransportSessionCaptureError = null;
            return true;
        }

        internal void EndFoxRunTransportSession()
        {
            var snapshot = _activeFoxRunTransportSession;
            _activeFoxRunTransportSession = null;
            snapshot?.Dispose();
        }

        internal FoxRunTransportPublishResult PublishFoxRunTransport(
            string providerId,
            in FoxRunTransportPublishRoute route)
        {
            FoxRunTransportId id;
            try
            {
                id = new FoxRunTransportId(providerId);
            }
            catch (ArgumentException ex)
            {
                return FoxRunTransportPublishResult.Rejected(ex.Message);
            }

            var snapshot = _activeFoxRunTransportSession;
            if (snapshot == null
                || !snapshot.TryGetPublishTransport(id, out var session))
            {
                return FoxRunTransportPublishResult.Unavailable(
                    "The selected Provider is not present in the frozen transport session.");
            }

            try
            {
                return session.Publish(in route);
            }
            catch (Exception ex)
            {
                return FoxRunTransportPublishResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Maps and publishes one ordinary publisher value through a selected,
        /// frozen Provider session. No optional package type crosses this API.
        /// </summary>
        public FoxRunTransportPublishResult PublishOrdinaryTransport(
            string providerId,
            in FoxRunOrdinaryPayloadRequest request)
        {
            FoxRunTransportId id;
            try
            {
                id = new FoxRunTransportId(providerId);
            }
            catch (ArgumentException exception)
            {
                return FoxRunTransportPublishResult.Rejected(exception.Message);
            }

            var snapshot = _activeFoxRunTransportSession;
            if (snapshot == null
                || !snapshot.TryGetPublishTransport(id, out var session))
            {
                return FoxRunTransportPublishResult.Unavailable(
                    "The selected Provider is not present in the frozen transport session.");
            }
            if (!(session is IFoxRunOrdinaryPayloadMapper mapper))
            {
                return FoxRunTransportPublishResult.Rejected(
                    "The selected Provider does not map ordinary publisher payloads.");
            }

            try
            {
                if (!mapper.TryMap(
                        in request,
                        out var contribution,
                        out var reason))
                {
                    return FoxRunTransportPublishResult.Rejected(reason);
                }

                var route = new FoxRunTransportPublishRoute(
                    request.StablePublisherId,
                    request.Topic,
                    contribution.LogicalSchemaName,
                    contribution.Payload,
                    request.LogTimeNs,
                    request.Sequence,
                    request.DeliveryPolicy,
                    contribution.MessageEncoding,
                    contribution.SchemaEncoding);
                return session.Publish(in route);
            }
            catch (Exception exception)
            {
                return FoxRunTransportPublishResult.Failed(exception.Message);
            }
        }

        /// <summary>
        /// True when the frozen session contains at least one optional
        /// Provider capable of mapping ordinary publisher values.
        /// </summary>
        public bool HasOrdinaryTransportDemand
        {
            get
            {
                var sessions =
                    _activeFoxRunTransportSession?.PublishTransports;
                if (sessions == null)
                    return false;
                for (var index = 0; index < sessions.Count; index++)
                    if (sessions[index] is IFoxRunOrdinaryPayloadMapper)
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Fan out one captured ordinary value to every selected Provider
        /// mapper in the frozen session. The value is captured once by the
        /// publisher; each Provider owns only its wire mapping.
        /// </summary>
        public FoxRunOrdinaryTransportFanoutResult PublishOrdinaryTransports(
            in FoxRunOrdinaryPayloadRequest request)
        {
            var sessions =
                _activeFoxRunTransportSession?.PublishTransports;
            return FoxRunOrdinaryTransportFanout.Publish(
                sessions,
                in request);
        }

        /// <summary>
        /// Fan out one captured generated topic to each selected Provider
        /// which owns a physical emitter for that generated source.
        /// </summary>
        public FoxRunGeneratedTransportFanoutResult
            PublishGeneratedTransports(
                IFoxRunGeneratedTransportSource source,
                int topicIndex,
                string topic,
                IReadOnlyList<string> explicitTransportIds,
                ulong logTimeNs,
                string suppressedTransportId = "",
                ulong suppressedGeneration = 0)
        {
            var request =
                new FoxRunGeneratedTransportPublishRequest(
                    source,
                    topicIndex,
                    topic,
                    logTimeNs);
            return FoxRunGeneratedTransportFanout.Publish(
                _activeFoxRunTransportSession?.PublishTransports,
                explicitTransportIds,
                _activeFoxRunTransportSession?.PublishTransportIds,
                in request,
                suppressedTransportId,
                suppressedGeneration);
        }

        internal bool IsActiveFoxRunPublishTransport(
            string transportId,
            ulong generation)
        {
            if (generation == 0
                || string.IsNullOrWhiteSpace(transportId))
            {
                return false;
            }
            FoxRunTransportId id;
            try
            {
                id = new FoxRunTransportId(transportId);
            }
            catch (ArgumentException)
            {
                return false;
            }
            return _activeFoxRunTransportSession != null
                   && _activeFoxRunTransportSession.TryGetPublishTransport(
                       id,
                       out var session)
                   && session.Generation == generation;
        }

        /// <summary>Resolve one schema from a frozen Provider session.</summary>
        public bool TryResolveTransportSchema(
            string providerId,
            in FoxRunTransportSchemaRequest request,
            out FoxRunTransportSchemaContribution contribution,
            out string reason)
        {
            contribution = default;
            reason = string.Empty;
            FoxRunTransportId id;
            try
            {
                id = new FoxRunTransportId(providerId);
            }
            catch (ArgumentException exception)
            {
                reason = exception.Message;
                return false;
            }

            var snapshot = _activeFoxRunTransportSession;
            if (snapshot == null
                || !snapshot.TryGetPublishTransport(id, out var session))
            {
                reason =
                    "The selected Provider is not present in the frozen transport session.";
                return false;
            }
            if (!(session is IFoxRunTransportSchemaContributor contributor))
            {
                reason = "The selected Provider does not contribute schemas.";
                return false;
            }
            return contributor.TryResolveSchema(
                in request,
                out contribution,
                out reason);
        }

        /// <summary>
        /// Builds one immutable-by-convention decode-options snapshot. Caller
        /// factories retain order and precede Provider and SDK built-ins.
        /// </summary>
        public McapDecodeOptions CreateMcapDecodeOptionsSnapshot(
            McapDecodeOptions callerOptions = null)
        {
            callerOptions ??= new McapDecodeOptions();
            var snapshot = new McapDecodeOptions
            {
                UseBuiltInDecoders = callerOptions.UseBuiltInDecoders,
                FailurePolicy = callerOptions.FailurePolicy,
                DecoderFactories = new List<IMcapMessageDecoderFactory>()
            };
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            AppendStableFactories(
                callerOptions.DecoderFactories,
                snapshot.DecoderFactories,
                stableIds);

            var sessions = _activeFoxRunTransportSession?.PublishTransports;
            if (sessions == null)
                return snapshot;
            for (var index = 0; index < sessions.Count; index++)
            {
                if (!(sessions[index] is IFoxRunMcapDecoderContribution contribution))
                    continue;
                var factory = contribution.CreateFactory();
                AppendStableFactories(
                    new[] { factory },
                    snapshot.DecoderFactories,
                    stableIds);
            }
            return snapshot;
        }

        private static void AppendStableFactories(
            IEnumerable<IMcapMessageDecoderFactory> source,
            ICollection<IMcapMessageDecoderFactory> destination,
            ISet<string> stableIds)
        {
            if (source == null)
                return;
            foreach (var factory in source)
            {
                if (factory == null)
                {
                    throw new ArgumentException(
                        "MCAP decoder factory snapshots reject null entries.",
                        nameof(source));
                }
                if (!(factory is IStableMcapMessageDecoderFactory stable)
                    || string.IsNullOrWhiteSpace(stable.StableDecoderId))
                {
                    throw new ArgumentException(
                        "MCAP decoder factories require a non-empty stable decoder ID.",
                        nameof(source));
                }
                if (!stableIds.Add(stable.StableDecoderId))
                {
                    throw new ArgumentException(
                        "Duplicate MCAP decoder factory ID: "
                        + stable.StableDecoderId,
                        nameof(source));
                }
                destination.Add(factory);
            }
        }

        private bool TryCreateCapturedTransportSelection(
            out FoxRunTransportSelection selection,
            out string reason)
        {
            var configured = _foxRunPublishTransportIds
                             ?? Array.Empty<string>();
            var explicitIds =
                FoxRunSchemaInfoRegistry
                    .GetExplicitPublishTransportIds();
            if (explicitIds.Count == 0)
            {
                return FoxRunTransportSelection.TryCreate(
                    configured,
                    _enableFoxRunInbound,
                    _enableFoxRunInbound
                        ? _foxRunSubscribeTransportId
                        : null,
                    out selection,
                    out reason);
            }

            var union = new HashSet<string>(
                configured,
                StringComparer.Ordinal);
            for (var index = 0;
                 index < explicitIds.Count;
                 index++)
            {
                union.Add(explicitIds[index]);
            }

            var publish = new string[union.Count];
            union.CopyTo(publish);
            Array.Sort(publish, StringComparer.Ordinal);
            return FoxRunTransportSelection.TryCreate(
                publish,
                _enableFoxRunInbound,
                _enableFoxRunInbound
                    ? _foxRunSubscribeTransportId
                    : null,
                out selection,
                out reason);
        }

        private bool ReportFoxRunTransportSessionCaptureFailure(
            FoxRunTransportSessionCaptureError failure)
        {
            var shouldReport =
                !_lastFoxRunTransportSessionCaptureError.HasValue
                || _lastFoxRunTransportSessionCaptureError.Value.Code
                    != failure.Code
                || _lastFoxRunTransportSessionCaptureError.Value.TransportId
                    != failure.TransportId
                || !string.Equals(
                    _lastFoxRunTransportSessionCaptureError.Value.Reason,
                    failure.Reason,
                    StringComparison.Ordinal);
            _lastFoxRunTransportSessionCaptureError = failure;
            if (shouldReport)
            {
                Debug.LogWarning(
                    "[FoxRun] Transport session capture failed closed for '"
                    + (failure.TransportId.Value ?? "<unconfigured>")
                    + "': "
                    + failure.Reason);
            }
            return false;
        }

        private void EnsureBuiltInFoxgloveProviderRegistered()
        {
            _builtInFoxgloveTransportProvider ??=
                new BuiltInFoxgloveTransportProvider(this);
            _foxRunTransportProviderRegistry.Register(
                _builtInFoxgloveTransportProvider);
        }

        private void RegisterSameObjectTransportProviders()
        {
            var components = GetComponents<MonoBehaviour>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                    continue;
                if (!components[i].isActiveAndEnabled)
                    continue;
                if (components[i] is IFoxRunTransportProvider provider)
                    RegisterFoxRunTransportProvider(provider);
            }
        }

        private sealed class BuiltInFoxgloveTransportProvider :
            IFoxRunTransportProvider
        {
            private readonly FoxgloveManager _manager;

            internal BuiltInFoxgloveTransportProvider(FoxgloveManager manager)
            {
                _manager = manager;
            }

            public FoxRunTransportId Id => FoxgloveWebSocketTransport.TransportId;
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe;
            public FoxRunTransportLifecycleState LifecycleState =>
                FoxRunTransportLifecycleState.Available;

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                session = new BuiltInFoxgloveTransportSession(_manager, generation);
                reason = string.Empty;
                return true;
            }
        }

        private sealed class BuiltInFoxgloveTransportSession :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource
        {
            private FoxgloveManager _manager;

            internal BuiltInFoxgloveTransportSession(
                FoxgloveManager manager,
                ulong generation)
            {
                _manager = manager;
                Generation = generation;
            }

            public FoxRunTransportId Id => FoxgloveWebSocketTransport.TransportId;
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe;
            public ulong Generation { get; }

            public FoxRunTransportStatusSnapshot CaptureStatus(
                FoxRunTransportCapabilities selectedDirections)
            {
                var manager = _manager;
                var state = manager != null && manager.IsRunning
                    ? FoxRunTransportObservedState.Ready
                    : FoxRunTransportObservedState.Stopped;
                var publishSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Publish) != 0;
                var subscribeSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Subscribe) != 0;
                return new FoxRunTransportStatusSnapshot(
                    Id,
                    Generation,
                    publishSelected
                        ? new FoxRunTransportDirectionStatus(
                            FoxRunTransportDirection.Publish,
                            selected: true,
                            state,
                            0,
                            0,
                            0)
                        : FoxRunTransportDirectionStatus.Unselected(
                            FoxRunTransportDirection.Publish),
                    subscribeSelected
                        ? new FoxRunTransportDirectionStatus(
                            FoxRunTransportDirection.Subscribe,
                            selected: true,
                            state,
                            0,
                            0,
                            0)
                        : FoxRunTransportDirectionStatus.Unselected(
                            FoxRunTransportDirection.Subscribe));
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
            {
                var manager = _manager;
                if (manager == null)
                    return FoxRunTransportPublishResult.Unavailable(
                        "Foxglove transport session has ended.");

                var payload = ExactArray(route.Payload);
                try
                {
                    switch (route.MessageEncoding)
                    {
                        case "json":
                            manager.PublishFoxRunJsonBytes(
                                route.Topic,
                                route.LogicalSchemaName,
                                payload,
                                route.LogTimeNs);
                            return FoxRunTransportPublishResult.Accepted();
                        case "protobuf":
                            manager.PublishProto(
                                route.Topic,
                                route.LogicalSchemaName,
                                payload,
                                route.LogTimeNs);
                            return FoxRunTransportPublishResult.Accepted();
                        case "msgpack":
                            manager.PublishFoxRunMessagePackBytes(
                                route.Topic,
                                payload,
                                route.LogTimeNs);
                            return FoxRunTransportPublishResult.Accepted();
                        default:
                            return FoxRunTransportPublishResult.Rejected(
                                "Foxglove transport requires json, protobuf, or msgpack encoding.");
                    }
                }
                catch (Exception ex)
                {
                    return FoxRunTransportPublishResult.Failed(ex.Message);
                }
            }

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Unavailable(
                    "Generated Foxglove subscription bindings own inbound registration.");

            public void Dispose()
            {
                _manager = null;
            }

            private static byte[] ExactArray(ReadOnlyMemory<byte> payload)
            {
                if (MemoryMarshal.TryGetArray(payload, out var segment)
                    && segment.Array != null
                    && segment.Offset == 0
                    && segment.Count == segment.Array.Length)
                {
                    return segment.Array;
                }

                return payload.ToArray();
            }
        }
    }
}
