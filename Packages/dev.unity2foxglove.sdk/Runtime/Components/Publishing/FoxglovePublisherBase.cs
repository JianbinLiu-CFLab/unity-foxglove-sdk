// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Abstract base for all publisher MonoBehaviour components.
// Provides FoxgloveManager auto-resolution, publish-rate throttling,
// frame ID sanitization, encoding override, and publish helpers.

using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Util;
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

        [Header("ROS2 Bridge")]
        [Tooltip("Mirror this publisher's ROS2 CDR payload to the optional local ROS2 Bridge sidecar.")]
        [SerializeField] protected Ros2BridgeOutputOverride _ros2BridgeOutput = Ros2BridgeOutputOverride.UseManager;
        [Tooltip("Optional absolute ROS2 Bridge topic. Leave empty to use manager namespace plus this publisher topic.")]
        [SerializeField] protected string _ros2BridgeTopicOverride = "";

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
        private int _lastBridgeFallbackWarningKey;
        private string _lastBridgeWarningKey;
        private string _lastPublishTopicWarningKey;
        private string _lastRos2BridgeTopicWarningKey;

        protected FoxgloveManager Manager => _manager;
        protected abstract string SchemaName { get; }
        protected ulong CurrentLogTimeNs => _manager?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

        /// <summary>
        /// True when this publisher can serialize JSON-compatible Foxglove messages.
        /// </summary>
        public virtual bool SupportsJsonEncoding => true;

        /// <summary>
        /// True when this publisher can serialize protobuf payload bytes.
        /// </summary>
        public virtual bool SupportsProtobufEncoding => false;

        /// <summary>
        /// True when this publisher can serialize ROS 2 CDR payload bytes.
        /// </summary>
        public virtual bool SupportsRos2Encoding => false;

        /// <summary>
        /// ROS 2 .msg schema name used when <see cref="SupportsRos2Encoding"/> is true.
        /// </summary>
        protected virtual string Ros2SchemaName => "";

        /// <summary>
        /// Return true when a fallback is the intentional product encoding for
        /// this publisher mode and should not be surfaced as a warning.
        /// </summary>
        protected virtual bool IsExpectedEncodingFallback(PublisherEncodingResolution resolution) => false;

        /// <summary>
        /// True when this publisher can mirror a ROS 2 CDR payload to ROS2 Bridge.
        /// </summary>
        public virtual bool SupportsRos2BridgeOutput => SupportsRos2Encoding;

        /// <summary>
        /// ROS 2 .msg schema name used for ROS2 Bridge output.
        /// </summary>
        protected virtual string Ros2BridgeSchemaName => Ros2SchemaName;

        /// <summary>
        /// Resolved effective encoding for this publisher.
        /// Reads global default, override permission, publisher override, and capabilities.
        /// </summary>
        public PublisherEffectiveEncoding EffectiveEncoding => EncodingResolution.Effective;

        /// <summary>
        /// Full encoding resolution used by Inspector UI and publish helpers.
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
        /// Publisher ROS2 Bridge override selected in the Inspector.
        /// </summary>
        public Ros2BridgeOutputOverride Ros2BridgeOutput => _ros2BridgeOutput;

        /// <summary>
        /// Full ROS2 Bridge output resolution used by Inspector UI and publish helpers.
        /// </summary>
        public Ros2BridgeOutputResolution BridgeOutputResolution => ResolveRos2BridgeOutput();

        /// <summary>Publisher-local ROS2 Bridge topic override.</summary>
        public string Ros2BridgeTopicOverride => _ros2BridgeTopicOverride;

        /// <summary>Resolved ROS2 Bridge topic after manager namespace and publisher override are applied.</summary>
        public string EffectiveRos2BridgeTopic
        {
            get
            {
                if (_manager != null && _manager.TryResolveRos2BridgeTopic(_topic, _ros2BridgeTopicOverride, out var effectiveTopic, out _))
                    return effectiveTopic;

                return Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(
                    string.Empty,
                    _topic,
                    _ros2BridgeTopicOverride,
                    out effectiveTopic,
                    out _)
                    ? effectiveTopic
                    : "";
            }
        }

        /// <summary>Resolved ROS2 Bridge QoS profile.</summary>
        public Ros2BridgeQosProfile EffectiveRos2BridgeQos =>
            _manager != null ? _manager.ResolveRos2BridgeQos() : Ros2BridgeQosProfile.ReliableDefault;

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
            get { return BuildSupportedEncodingSummary(); }
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
            _warnedManagerMissing = false;
            _lastEncodingFallbackWarningKey = 0;
            _lastEncodingMismatchWarningKey = 0;
            _lastBridgeFallbackWarningKey = 0;
            _lastBridgeWarningKey = null;
            _lastPublishTopicWarningKey = null;
            _lastRos2BridgeTopicWarningKey = null;
            ResolveManager();
        }

        protected virtual void OnDisable() { }

        protected virtual void OnValidate()
        {
            InvalidatePublishRateCache();
        }

        protected void ResolveManager()
        {
            if (_manager != null) return;

            var managers = FindObjectsByType<FoxgloveManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _manager = ResolveManagerFromCandidates(managers);

            if (_manager == null && _warnIfManagerMissing && !_warnedManagerMissing)
            {
                var hasAmbiguousManagers = managers.Length > 1;
                Debug.LogWarning(hasAmbiguousManagers
                    ? $"[Foxglove] {GetType().Name}: Multiple FoxgloveManager instances found; assign Manager explicitly."
                    : $"[Foxglove] {GetType().Name}: No FoxgloveManager found in scene.");
                _warnedManagerMissing = true;
            }
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
        /// Return whether any enabled output path needs this publisher to prepare
        /// payload data, and return both the web-socket and bridge resolutions.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload(
            out PublisherEncodingResolution encodingResolution,
            out Ros2BridgeOutputResolution bridgeResolution)
        {
            return ShouldPrepareAnyPublishPayload(out _, out _, out encodingResolution, out bridgeResolution);
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for any output path and
        /// return which paths are actually enabled, as well as the resolved output resolutions.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload(
            out bool shouldPrepareWebSocket,
            out bool shouldPrepareRos2Bridge,
            out PublisherEncodingResolution encodingResolution,
            out Ros2BridgeOutputResolution bridgeResolution)
        {
            shouldPrepareWebSocket = TryPreparePublishPayload(out encodingResolution);
            shouldPrepareRos2Bridge = ShouldPrepareRos2BridgePayload(out bridgeResolution);
            return shouldPrepareWebSocket || shouldPrepareRos2Bridge;
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
            if (_manager == null) return false;
            if (!ValidateConfiguredTopic("publish")) return false;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return false;
            if (resolution.Effective != attemptedEncoding)
            {
                WarnEncodingMismatch(resolution, PublisherEncodingPolicy.ToDisplayEncoding(attemptedEncoding));
                return false;
            }

            if (attemptedEncoding == PublisherEffectiveEncoding.Ros2)
            {
                if (string.IsNullOrWhiteSpace(Ros2SchemaName))
                {
                    WarnEncodingMismatch(resolution, "ROS2");
                    return false;
                }

                return _manager.TryPrepareRos2Publish(_topic, Ros2SchemaName, out _, requireDemand: true);
            }

            var wireEncoding = PublisherEncodingPolicy.ToProtocolEncoding(attemptedEncoding);
            return _manager.TryPrepareSchemaPublish(_topic, SchemaName, wireEncoding, out _, requireDemand: true);
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for ROS2 Bridge output.
        /// The bridge path is independent from Foxglove WebSocket demand.
        /// </summary>
        protected bool ShouldPrepareRos2BridgePayload()
        {
            return ShouldPrepareRos2BridgePayload(out _);
        }

        protected bool ShouldPrepareRos2BridgePayload(out Ros2BridgeOutputResolution resolution)
        {
            resolution = ResolveRos2BridgeOutput();
            return ShouldPrepareRos2BridgePayload(resolution);
        }

        private bool ShouldPrepareRos2BridgePayload(Ros2BridgeOutputResolution resolution)
        {
            if (_manager == null) return false;
            if (!ValidateConfiguredTopic("ROS2 Bridge publish")) return false;

            WarnIfRos2BridgeFallback(resolution);
            if (!resolution.IsEnabled)
                return false;

            if (string.IsNullOrWhiteSpace(Ros2BridgeSchemaName))
            {
                WarnRos2BridgeSkipped("missing-schema", "ROS2 Bridge schema name is missing.");
                return false;
            }

            if (!_manager.TryPrepareRos2BridgePublish(_topic, _ros2BridgeTopicOverride, Ros2BridgeSchemaName, out _, out _, out var reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    WarnRos2BridgeSkipped("prepare:" + reason, reason);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Return whether any enabled output path needs this publisher to prepare payload data.
        /// </summary>
        protected bool ShouldPrepareAnyPublishPayload()
        {
            return ShouldPrepareAnyPublishPayload(out _, out _);
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
            if (_manager == null) return;
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
            if (_manager == null) return;
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

        /// <summary>Publish ROS 2 CDR bytes through the manager. Safe no-op if manager is null.</summary>
        protected void PublishRos2(byte[] payload, ulong logTimeNs)
        {
            var resolution = ResolvePublisherEncoding();
            PublishRos2(payload, logTimeNs, resolution);
        }

        /// <summary>Publish ROS 2 CDR bytes through the manager using an already resolved encoding. Safe no-op if manager is null.</summary>
        protected void PublishRos2(byte[] payload, ulong logTimeNs, PublisherEncodingResolution resolution)
        {
            if (_manager == null) return;
            if (!ValidateConfiguredTopic("publish")) return;

            WarnIfEncodingFallback(resolution);
            if (!resolution.IsSupported) return;
            if (resolution.Effective != PublisherEffectiveEncoding.Ros2)
            {
                WarnEncodingMismatch(resolution, "ROS2");
                return;
            }

            if (string.IsNullOrWhiteSpace(Ros2SchemaName))
            {
                WarnEncodingMismatch(resolution, "ROS2");
                return;
            }

            _manager.PublishRos2(_topic, Ros2SchemaName, payload, logTimeNs);
        }

        /// <summary>Mirror ROS 2 CDR bytes to ROS2 Bridge. Safe no-op if manager is null or disabled.</summary>
        protected void PublishRos2Bridge(byte[] payload, ulong logTimeNs)
        {
            var resolution = ResolveRos2BridgeOutput();
            PublishRos2Bridge(payload, logTimeNs, resolution);
        }

        /// <summary>Mirror ROS 2 CDR bytes to ROS2 Bridge using an already resolved output resolution.</summary>
        protected void PublishRos2Bridge(byte[] payload, ulong logTimeNs, Ros2BridgeOutputResolution resolution)
        {
            if (_manager == null) return;
            if (!ValidateConfiguredTopic("ROS2 Bridge publish")) return;

            WarnIfRos2BridgeFallback(resolution);
            if (!resolution.IsEnabled) return;

            if (string.IsNullOrWhiteSpace(Ros2BridgeSchemaName))
            {
                WarnRos2BridgeSkipped("missing-schema", "ROS2 Bridge schema name is missing.");
                return;
            }

            _manager.PublishRos2BridgeCdr(_topic, _ros2BridgeTopicOverride, Ros2BridgeSchemaName, payload, logTimeNs);
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
                SupportsRos2Encoding);
        }

        protected virtual Ros2BridgeOutputResolution ResolveRos2BridgeOutput()
        {
            var managerEnabled = _manager != null && _manager.Ros2BridgeEnabled;
            var managerDefaultEnabled = _manager != null && _manager.DefaultRos2BridgeOutputEnabled;
            var allowPublisherOverride = _manager == null || _manager.AllowPublisherRos2BridgeOverride;
            return Ros2BridgeOutputPolicy.Resolve(
                managerEnabled,
                managerDefaultEnabled,
                allowPublisherOverride,
                _ros2BridgeOutput,
                SupportsRos2BridgeOutput);
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

        private string BuildSupportedEncodingSummary()
        {
            var json = SupportsJsonEncoding;
            var protobuf = SupportsProtobufEncoding;
            var ros2 = SupportsRos2Encoding;

            if (json && protobuf && ros2) return "JSON, Protobuf, ROS2";
            if (json && protobuf) return "JSON, Protobuf";
            if (json && ros2) return "JSON, ROS2";
            if (protobuf && ros2) return "Protobuf, ROS2";
            if (json) return "JSON";
            if (protobuf) return "Protobuf";
            if (ros2) return "ROS2";
            return "none";
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
                Debug.LogWarning($"[Foxglove] {GetType().Name} does not support JSON, Protobuf, or ROS2; dropping messages.");
                return;
            }

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} does not support {resolution.RequestedLabel}; publishing {resolution.EffectiveLabel}.");
        }

        private bool ValidateConfiguredTopic(string operation)
        {
            if (HasValidPublisherTopic(_topic))
                return true;

            ref var lastTopicWarningKey = ref GetTopicWarningKey(operation);
            var key = "invalid-topic:" + operation;
            if (lastTopicWarningKey != key)
            {
                lastTopicWarningKey = key;
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

        private ref string GetTopicWarningKey(string operation)
        {
            if (string.Equals(operation, "ROS2 Bridge publish", System.StringComparison.Ordinal))
                return ref _lastRos2BridgeTopicWarningKey;

            return ref _lastPublishTopicWarningKey;
        }

        private void WarnIfRos2BridgeFallback(Ros2BridgeOutputResolution resolution)
        {
            if (!resolution.FellBack) return;

            var key = BridgeWarningKey(resolution.Requested, resolution.Effective);
            if (_lastBridgeFallbackWarningKey == key) return;
            _lastBridgeFallbackWarningKey = key;

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} does not support ROS2 Bridge output; bridge publishing is disabled for this publisher.");
        }

        private void WarnRos2BridgeSkipped(string key, string reason)
        {
            key = "skip:" + key;
            if (_lastBridgeWarningKey == key) return;
            _lastBridgeWarningKey = key;

            Debug.LogWarning($"[Foxglove] {GetType().Name} ROS2 Bridge publish skipped: {reason}");
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
            if (string.Equals(attemptedEncoding, "ROS2", System.StringComparison.Ordinal))
                return 3;
            return 4;
        }

        private static int BridgeWarningKey(Ros2BridgeEffectiveOutput requested, Ros2BridgeEffectiveOutput effective)
            => (((int)requested + 1) << 8) | ((int)effective + 1);
    }
}
