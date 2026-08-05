// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Abstract base for all publisher MonoBehaviour components.
// Provides FoxgloveManager auto-resolution, publish-rate throttling,
// frame ID sanitization, encoding override, and publish helpers.

using Unity.FoxgloveSDK.Util;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_2020_3_OR_NEWER
using Unity.Profiling;
#endif

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Base class for all Foxglove publisher components.
    /// Handles manager resolution, FPS throttling, frame ID sanitization,
    /// encoding override policy, and publish helpers.
    /// </summary>
    public abstract class FoxglovePublisherBase : MonoBehaviour
    {
#if UNITY_2020_3_OR_NEWER
        private static readonly ProfilerMarker PublisherTickMarker = new ProfilerMarker("FoxglovePublisher.Tick");
#endif

        [Header("General")]
        [SerializeField] protected FoxgloveManager _manager;
        [SerializeField] protected string _topic = "";
        [SerializeField] protected PublisherRateSource _publishRateSource = PublisherRateSource.UseManagerDefault;
        [SerializeField] protected float _publishRateHz = 10f;
        [Tooltip("When true, this publisher sends data on each scheduled tick. Disable to pause publishing without removing the component.")]
        [SerializeField] protected bool _publishOnEnable = true;
        [SerializeField] protected bool _warnIfManagerMissing = true;

        [Header("Encoding")]
        [Tooltip("Override the global default encoding for this publisher.")]
        [SerializeField] protected PublisherEncodingOverride _encodingOverride = PublisherEncodingOverride.UseManager;
        [System.NonSerialized]
        private string _ordinaryTransportPublisherId;

        private FixedRatePublishState _publishRateState;
        private FixedRatePublishState _publishRateStateFixed;
        private bool _warnedManagerMissing;
        private bool _publishRateCacheValid;
        private PublisherRateSource _cachedPublishRateSource;
        private float _cachedLocalPublishRateHz;
        private float _cachedManagerPublishRateHz;
        private bool _cachedPublishRateHasManager;
        private float _cachedPublishRateHz;
        private int _lastEncodingFallbackWarningKey;
        private int _lastEncodingMismatchWarningKey;
        private string _lastPublishTopicWarningKey;
        private string _lastOrdinaryTransportWarningKey;
        private string _supportedEncodingSummaryCache;
        private bool _managerWasResolved;
        private double _nextManagerResolveTime;
        private ulong _ordinaryTransportSequence;

        protected FoxgloveManager Manager => _manager;
        protected abstract string SchemaName { get; }
        protected ulong CurrentLogTimeNs
        {
            get
            {
                if (_manager == null)
                    return Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

                return _manager.NowNs;
            }
        }

        /// <summary>
        /// True when this publisher can serialize JSON-compatible Foxglove messages.
        /// </summary>
        public virtual bool SupportsJsonEncoding => true;

        /// <summary>
        /// True when this publisher can serialize protobuf payload bytes.
        /// </summary>
        public virtual bool SupportsProtobufEncoding => false;

        /// <summary>
        /// True when this publisher can serialize pre-encoded MessagePack payload bytes.
        /// </summary>
        public virtual bool SupportsMsgPackEncoding => false;

        /// <summary>
        /// Return true when a fallback is the intentional product encoding for
        /// this publisher mode and should not be surfaced as a warning.
        /// </summary>
        protected virtual bool IsExpectedEncodingFallback(PublisherEncodingResolution resolution) => false;

        /// <summary>
        /// Resolved effective encoding for this publisher.
        /// Reads global default, override permission, publisher override, and capabilities.
        /// This property resolves on each access; cache the value locally before
        /// using it more than once in a hot path.
        /// </summary>
        public PublisherEffectiveEncoding EffectiveEncoding => EncodingResolution.Effective;

        /// <summary>
        /// Full encoding resolution used by Inspector UI and publish helpers.
        /// This property resolves on each access; cache the value locally before
        /// using it more than once in a hot path.
        /// </summary>
        public PublisherEncodingResolution EncodingResolution => ResolvePublisherEncoding();

        /// <summary>
        /// Publisher override selected in the Inspector.
        /// </summary>
        public PublisherEncodingOverride EncodingOverride => _encodingOverride;

        /// <summary>
        /// Configured Foxglove topic for this publisher.
        /// </summary>
        public string Topic => _topic;

        /// <summary>
        /// True when the configured topic can be advertised to Foxglove.
        /// </summary>
        public bool HasValidTopic => HasValidPublisherTopic(_topic);

        /// <summary>
        /// Return whether a publisher topic is valid for channel registration.
        /// </summary>
        public static bool HasValidPublisherTopic(string topic)
            => !string.IsNullOrWhiteSpace(topic);

        /// <summary>
        /// Source used to resolve this publisher's effective publish rate.
        /// </summary>
        public PublisherRateSource PublishRateSource => _publishRateSource;

        /// <summary>
        /// Publisher-local publish rate used when local override is selected
        /// or when no manager is available.
        /// </summary>
        public float LocalPublishRateHz => _publishRateHz;

        /// <summary>
        /// Resolved publish rate after applying manager default and local
        /// override policy.
        /// </summary>
        public float EffectivePublishRateHz => ResolvePublishRateHz();

        /// <summary>
        /// Manager explicitly assigned to this publisher, if any.
        /// </summary>
        public FoxgloveManager ConfiguredManager => _manager;

        /// <summary>
        /// Human-readable capability summary for custom Inspectors.
        /// </summary>
        public string SupportedEncodingSummary
        {
            get { return _supportedEncodingSummaryCache ??= BuildSupportedEncodingSummary(); }
        }

        protected virtual void Reset()
        {
            _publishRateSource = PublisherRateSource.UseManagerDefault;
        }

        protected virtual void OnEnable()
        {
            // Re-enable starts a fresh cadence window, so the first scheduled
            // tick can publish immediately instead of waiting one full period.
            _publishRateState = default;
            _publishRateStateFixed = default;
            InvalidatePublishRateCache();
            InvalidateSupportedEncodingSummaryCache();
            _warnedManagerMissing = false;
            _lastEncodingFallbackWarningKey = 0;
            _lastEncodingMismatchWarningKey = 0;
            _lastPublishTopicWarningKey = null;
            _lastOrdinaryTransportWarningKey = null;
            ResolveManager();
        }

        protected virtual void OnDisable() { }

        protected virtual void OnValidate()
        {
            InvalidatePublishRateCache();
            InvalidateSupportedEncodingSummaryCache();
        }

        protected void ResolveManager()
        {
            if (_manager != null)
            {
                _managerWasResolved = true;
                return;
            }

            var managers = FindObjectsByType<FoxgloveManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _manager = ResolveManagerFromCandidates(managers);
            if (_manager != null)
            {
                _managerWasResolved = true;
                _warnedManagerMissing = false;
                return;
            }

            if (_manager == null && _warnIfManagerMissing && !_warnedManagerMissing)
            {
                var hasAmbiguousManagers = managers.Length > 1;
                Debug.LogWarning(hasAmbiguousManagers
                    ? $"[Foxglove] {GetType().Name}: Multiple FoxgloveManager instances found; assign Manager explicitly."
                    : $"[Foxglove] {GetType().Name}: No FoxgloveManager found in scene.");
                _warnedManagerMissing = true;
            }
        }

        protected bool EnsureManagerAvailable()
        {
            if (_manager != null)
                return true;

            if (_managerWasResolved)
            {
                _managerWasResolved = false;
                _warnedManagerMissing = false;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextManagerResolveTime)
                return false;

            _nextManagerResolveTime = now + 1.0;
            ResolveManager();
            return _manager != null;
        }

        private FoxgloveManager ResolveManagerFromCandidates(FoxgloveManager[] managers)
        {
            if (managers == null || managers.Length == 0)
            {
                return null;
            }

            var publisherScene = gameObject.scene;
            FoxgloveManager sameSceneManager = null;
            var sameSceneCount = 0;
            foreach (var candidate in managers)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (publisherScene.IsValid()
                    && candidate.gameObject.scene.IsValid()
                    && candidate.gameObject.scene.handle == publisherScene.handle)
                {
                    sameSceneManager = candidate;
                    sameSceneCount++;
                }
            }

            if (sameSceneCount == 1)
            {
                return sameSceneManager;
            }

            return managers.Length == 1 ? managers[0] : null;
        }

        /// <summary>True if enough time has elapsed since last publish.</summary>
        protected bool ShouldPublishNow()
        {
            if (!_publishOnEnable)
                return false;

#if UNITY_2020_3_OR_NEWER
            using (PublisherTickMarker.Auto())
            {
#endif
            return FixedRatePublishScheduler.ShouldPublish(
                Time.unscaledTimeAsDouble,
                ResolveCachedPublishRateHz(),
                ref _publishRateState,
                nonPositivePublishesEveryFrame: true);
#if UNITY_2020_3_OR_NEWER
            }
#endif
        }

        /// <summary>
        /// True if enough fixed-time scheduler time has elapsed since last publish.
        /// This is drift-only for physics-clock publishers; WebSocket arrival cadence
        /// still follows when the publisher enqueues payloads.
        /// </summary>
        protected bool ShouldPublishNowFixed()
        {
            if (!_publishOnEnable)
                return false;

#if UNITY_2020_3_OR_NEWER
            using (PublisherTickMarker.Auto())
            {
#endif
            return FixedRatePublishScheduler.ShouldPublish(
                Time.fixedTimeAsDouble,
                ResolveCachedPublishRateHz(),
                ref _publishRateStateFixed,
                nonPositivePublishesEveryFrame: true);
#if UNITY_2020_3_OR_NEWER
            }
#endif
        }

        /// <summary>Replace spaces with underscores. Use fallback if empty.</summary>
        protected static string SanitizeFrameId(string raw, string fallback)
        {
            var sanitized = string.IsNullOrEmpty(raw) ? fallback : raw;
            return sanitized.Contains(' ') ? sanitized.Replace(' ', '_') : sanitized;
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for its effective encoding.
        /// </summary>
        protected bool ShouldPreparePublishPayload()
        {
            return TryPreparePublishPayload(out _);
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for the web
        /// socket output path using a pre-resolved encoding and return it to
        /// callers that need to reuse the same resolution.
        /// </summary>
        protected bool TryPreparePublishPayload(out PublisherEncodingResolution resolution)
        {
            resolution = ResolvePublisherEncoding();
            return ShouldPreparePublishPayload(resolution, resolution.Effective);
        }

        /// <summary>
        /// Return whether any enabled output path needs this publisher to
        /// prepare payload data.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload(
            out PublisherEncodingResolution encodingResolution)
        {
            return ShouldPrepareAnyPublishPayload(
                out _,
                out _,
                out encodingResolution);
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for any output path and
        /// return which paths are actually enabled, as well as the resolved output resolutions.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload(
            out bool shouldPrepareWebSocket,
            out bool shouldPrepareOrdinaryTransport,
            out PublisherEncodingResolution encodingResolution)
        {
            shouldPrepareWebSocket = TryPreparePublishPayload(out encodingResolution);
            shouldPrepareOrdinaryTransport =
                ShouldPrepareOrdinaryTransportPayload();
            return shouldPrepareWebSocket
                   || shouldPrepareOrdinaryTransport;
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for an attempted encoding.
        /// </summary>
        protected bool ShouldPreparePublishPayload(PublisherEffectiveEncoding effectiveEncoding)
        {
            var resolution = ResolvePublisherEncoding();
            return ShouldPreparePublishPayload(resolution, effectiveEncoding);
        }

        private bool ShouldPreparePublishPayload(
            PublisherEncodingResolution resolution,
            PublisherEffectiveEncoding attemptedEncoding)
        {
            if (!EnsureManagerAvailable()) return false;
            if (!ValidateConfiguredTopic("publish")) return false;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return false;
            if (resolution.Effective != attemptedEncoding)
            {
                WarnEncodingMismatch(resolution, PublisherEncodingPolicy.ToDisplayEncoding(attemptedEncoding));
                return false;
            }

            if (attemptedEncoding == PublisherEffectiveEncoding.MsgPack)
            {
                return _manager.TryPrepareMsgPackPublish(_topic, out _, requireDemand: true);
            }

            var wireEncoding = PublisherEncodingPolicy.ToProtocolEncoding(attemptedEncoding);
            return _manager.TryPrepareSchemaPublish(_topic, SchemaName, wireEncoding, out _, requireDemand: true);
        }

        /// <summary>
        /// Return whether any selected optional Provider needs an ordinary
        /// publisher value. The wire mapping remains Provider-owned.
        /// </summary>
        protected bool ShouldPrepareOrdinaryTransportPayload()
        {
            if (!EnsureManagerAvailable()) return false;
            if (!ValidateConfiguredTopic("Provider publish")) return false;
            return _manager.HasOrdinaryTransportDemand;
        }

        /// <summary>
        /// Return whether any enabled output path needs this publisher to prepare payload data.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload()
        {
            return ShouldPrepareAnyPublishPayload(out _);
        }

        /// <summary>Publish a message through the manager. Safe no-op if manager is null.</summary>
        protected void Publish(object message, ulong logTimeNs)
        {
            var resolution = ResolvePublisherEncoding();
            Publish(message, logTimeNs, resolution);
        }

        /// <summary>Publish a message using a previously resolved encoding. Safe no-op if manager is null.</summary>
        protected void Publish(object message, ulong logTimeNs, PublisherEncodingResolution resolution)
        {
            if (!EnsureManagerAvailable()) return;
            if (!ValidateConfiguredTopic("publish")) return;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return;
            if (resolution.Effective != PublisherEffectiveEncoding.Json)
            {
                WarnEncodingMismatch(resolution, "JSON");
                return;
            }

            _manager.PublishJson(_topic, SchemaName, message, logTimeNs);
        }

        /// <summary>Publish protobuf bytes through the manager. Safe no-op if manager is null.</summary>
        protected void PublishProto(byte[] payload, ulong logTimeNs)
        {
            var resolution = ResolvePublisherEncoding();
            PublishProto(payload, logTimeNs, resolution);
        }

        /// <summary>Publish protobuf bytes through the manager using an already resolved encoding. Safe no-op if manager is null.</summary>
        protected void PublishProto(byte[] payload, ulong logTimeNs, PublisherEncodingResolution resolution)
        {
            if (!EnsureManagerAvailable()) return;
            if (!ValidateConfiguredTopic("publish")) return;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return;
            if (resolution.Effective != PublisherEffectiveEncoding.Protobuf)
            {
                WarnEncodingMismatch(resolution, "Protobuf");
                return;
            }

            _manager.PublishProto(_topic, SchemaName, payload, logTimeNs);
        }

        /// <summary>Publish MessagePack bytes through the manager. Safe no-op if manager is null.</summary>
        protected void PublishMsgPack(byte[] payload, ulong logTimeNs)
        {
            var resolution = ResolvePublisherEncoding();
            PublishMsgPack(payload, logTimeNs, resolution);
        }

        /// <summary>Publish MessagePack bytes through the manager using an already resolved encoding. Safe no-op if manager is null.</summary>
        protected void PublishMsgPack(byte[] payload, ulong logTimeNs, PublisherEncodingResolution resolution)
        {
            if (!EnsureManagerAvailable()) return;
            if (!ValidateConfiguredTopic("publish")) return;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return;
            if (resolution.Effective != PublisherEffectiveEncoding.MsgPack)
            {
                WarnEncodingMismatch(resolution, "MsgPack");
                return;
            }

            _manager.PublishMsgPack(_topic, payload, logTimeNs);
        }

        /// <summary>
        /// Publish one already captured logical value through every selected
        /// ordinary-payload Provider.
        /// </summary>
        protected FoxRunOrdinaryTransportFanoutResult PublishOrdinaryTransport(
            object value,
            string logicalSchemaName,
            ulong logTimeNs)
        {
            if (!EnsureManagerAvailable()
                || !ValidateConfiguredTopic("Provider publish"))
            {
                return default;
            }
            if (value == null)
                throw new System.ArgumentNullException(nameof(value));
            if (_ordinaryTransportSequence == ulong.MaxValue)
                throw new System.InvalidOperationException(
                    "Ordinary Provider sequence is exhausted.");

            var request = new FoxRunOrdinaryPayloadRequest(
                EnsureOrdinaryTransportPublisherId(),
                _topic,
                logicalSchemaName,
                value,
                logTimeNs,
                ++_ordinaryTransportSequence,
                FoxRunDeliveryPolicy.ProviderDefault);
            var result = _manager.PublishOrdinaryTransports(in request);
            if (result.Matched > 0
                && result.Accepted == 0
                && result.Failed + result.Rejected > 0)
            {
                var key = logicalSchemaName + ":"
                          + result.Rejected + ":"
                          + result.Failed;
                if (!string.Equals(
                        _lastOrdinaryTransportWarningKey,
                        key,
                        System.StringComparison.Ordinal))
                {
                    _lastOrdinaryTransportWarningKey = key;
                    Debug.LogWarning(
                        $"[Foxglove] {GetType().Name} Provider fanout rejected "
                        + $"logical schema '{logicalSchemaName}'.");
                }
            }
            return result;
        }

        private string EnsureOrdinaryTransportPublisherId()
        {
            if (string.IsNullOrWhiteSpace(_ordinaryTransportPublisherId))
            {
                _ordinaryTransportPublisherId =
                    "ordinary-" + System.Guid.NewGuid().ToString("N");
            }

            return _ordinaryTransportPublisherId;
        }

        protected virtual PublisherEncodingResolution ResolvePublisherEncoding()
        {
            var managerDefault = _manager != null ? _manager.DefaultPublisherEncoding : GlobalEncoding.Json;
            var allowPublisherOverride = _manager == null || _manager.AllowPublisherOverride;
            return PublisherEncodingPolicy.Resolve(
                managerDefault,
                allowPublisherOverride,
                _encodingOverride,
                SupportsJsonEncoding,
                SupportsProtobufEncoding,
                SupportsMsgPackEncoding);
        }

        private float ResolvePublishRateHz()
        {
            var manager = _manager;
#if UNITY_EDITOR
            if (manager == null && !Application.isPlaying)
                manager = FindAnyObjectByType<FoxgloveManager>();
#endif

            return PublisherRatePolicy.Resolve(
                _publishRateSource,
                manager != null ? manager.DefaultPublishRateHz : _publishRateHz,
                _publishRateHz,
                manager != null);
        }

        private float ResolveCachedPublishRateHz()
        {
            var manager = _manager;
#if UNITY_EDITOR
            if (manager == null && !Application.isPlaying)
                manager = FindAnyObjectByType<FoxgloveManager>();
#endif
            var hasManager = manager != null;
            var managerRateHz = hasManager ? manager.DefaultPublishRateHz : _publishRateHz;

            if (!_publishRateCacheValid
                || _cachedPublishRateSource != _publishRateSource
                || _cachedLocalPublishRateHz != _publishRateHz
                || _cachedManagerPublishRateHz != managerRateHz
                || _cachedPublishRateHasManager != hasManager)
            {
                _cachedPublishRateHz = PublisherRatePolicy.Resolve(
                    _publishRateSource,
                    managerRateHz,
                    _publishRateHz,
                    hasManager);
                _cachedPublishRateSource = _publishRateSource;
                _cachedLocalPublishRateHz = _publishRateHz;
                _cachedManagerPublishRateHz = managerRateHz;
                _cachedPublishRateHasManager = hasManager;
                _publishRateCacheValid = true;
            }

            return _cachedPublishRateHz;
        }

        private void InvalidatePublishRateCache()
        {
            _publishRateCacheValid = false;
        }

        private void InvalidateSupportedEncodingSummaryCache()
        {
            _supportedEncodingSummaryCache = null;
        }

        private string BuildSupportedEncodingSummary()
        {
            var labels = new List<string>(4);
            if (SupportsJsonEncoding) labels.Add("JSON");
            if (SupportsProtobufEncoding) labels.Add("Protobuf");
            if (SupportsMsgPackEncoding) labels.Add("MsgPack");
            return labels.Count == 0 ? "none" : string.Join(", ", labels);
        }

        private void WarnIfEncodingFallback(PublisherEncodingResolution resolution)
        {
            if (!resolution.FellBack) return;
            if (resolution.IsSupported && IsExpectedEncodingFallback(resolution)) return;

            var key = EncodingWarningKey(resolution.Requested, resolution.Effective);
            if (_lastEncodingFallbackWarningKey == key) return;
            _lastEncodingFallbackWarningKey = key;

            if (resolution.Effective == PublisherEffectiveEncoding.Unsupported)
            {
                Debug.LogWarning($"[Foxglove] {GetType().Name} does not support JSON, Protobuf, or MsgPack; dropping messages.");
                return;
            }

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} does not support {resolution.RequestedLabel}; publishing {resolution.EffectiveLabel}.");
        }

        private bool ValidateConfiguredTopic(string operation)
        {
            if (HasValidPublisherTopic(_topic))
                return true;

            var key = "invalid-topic";
            if (_lastPublishTopicWarningKey != key)
            {
                _lastPublishTopicWarningKey = key;
                Debug.LogWarning(
                    $"[Foxglove] {GetType().Name} cannot {operation}: Topic is empty. Configure a non-empty topic before publishing.");
            }

            return false;
        }

        private void WarnEncodingMismatch(PublisherEncodingResolution resolution, string attemptedEncoding)
        {
            var key = EncodingWarningKey(AttemptedEncodingWarningKey(attemptedEncoding), resolution.Effective);
            if (_lastEncodingMismatchWarningKey == key) return;
            _lastEncodingMismatchWarningKey = key;

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} resolved to {resolution.EffectiveLabel} but attempted to publish {attemptedEncoding}; dropping message.");
        }

        private static int EncodingWarningKey(PublisherEffectiveEncoding requested, PublisherEffectiveEncoding effective)
            => (((int)requested + 1) << 8) | ((int)effective + 1);

        private static int EncodingWarningKey(int attemptedEncoding, PublisherEffectiveEncoding effective)
            => (attemptedEncoding << 8) | ((int)effective + 1);

        private static int AttemptedEncodingWarningKey(string attemptedEncoding)
        {
            if (string.Equals(attemptedEncoding, "JSON", System.StringComparison.Ordinal))
                return 1;
            if (string.Equals(attemptedEncoding, "Protobuf", System.StringComparison.Ordinal))
                return 2;
            if (string.Equals(attemptedEncoding, "MsgPack", System.StringComparison.Ordinal))
                return 3;
            return 4;
        }

    }
}
