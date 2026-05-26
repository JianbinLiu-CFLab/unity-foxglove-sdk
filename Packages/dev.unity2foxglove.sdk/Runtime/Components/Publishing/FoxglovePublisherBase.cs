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

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Base class for all Foxglove publisher components.
    /// Handles manager resolution, FPS throttling, frame ID sanitization,
    /// encoding override policy, and publish helpers.
    /// </summary>
    public abstract class FoxglovePublisherBase : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] protected FoxgloveManager _manager;
        [SerializeField] protected string _topic = "";
        [SerializeField] protected PublisherRateSource _publishRateSource = PublisherRateSource.OverrideLocal;
        [SerializeField] protected float _publishRateHz = 10f;
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
        private bool _warnedManagerMissing;
        private string _lastEncodingWarningKey;
        private string _lastBridgeWarningKey;
        private string _lastTopicWarningKey;

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
            _warnedManagerMissing = false;
            ResolveManager();
        }

        protected virtual void OnDisable() { }

        protected void ResolveManager()
        {
            if (_manager != null) return;

            _manager = FindFirstObjectByType<FoxgloveManager>();

            if (_manager == null && _warnIfManagerMissing && !_warnedManagerMissing)
            {
                Debug.LogWarning($"[Foxglove] {GetType().Name}: No FoxgloveManager found in scene.");
                _warnedManagerMissing = true;
            }
        }

        /// <summary>True if enough time has elapsed since last publish.</summary>
        protected bool ShouldPublishNow()
        {
            return FixedRatePublishScheduler.ShouldPublish(
                Time.unscaledTimeAsDouble,
                EffectivePublishRateHz,
                ref _publishRateState,
                nonPositivePublishesEveryFrame: true);
        }

        /// <summary>Replace spaces with underscores. Use fallback if empty.</summary>
        protected static string SanitizeFrameId(string raw, string fallback)
        {
            var sanitized = string.IsNullOrEmpty(raw) ? fallback : raw;
            return sanitized.Replace(' ', '_');
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for its effective encoding.
        /// </summary>
        protected bool ShouldPreparePublishPayload()
        {
            return TryPreparePublishPayload(out _);
        }

        /// <summary>
        /// Return whether this publisher should prepare payload data for an attempted encoding.
        /// </summary>
        protected bool ShouldPreparePublishPayload(PublisherEffectiveEncoding effectiveEncoding)
        {
            var resolution = ResolvePublisherEncoding();
            return ShouldPreparePublishPayload(resolution, effectiveEncoding);
        }

        protected bool TryPreparePublishPayload(out PublisherEncodingResolution resolution)
        {
            resolution = ResolvePublisherEncoding();
            return ShouldPreparePublishPayload(resolution, resolution.Effective);
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
            if (_manager == null) return false;
            if (!ValidateConfiguredTopic("ROS2 Bridge publish")) return false;

            var resolution = ResolveRos2BridgeOutput();
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
            return ShouldPreparePublishPayload() || ShouldPrepareRos2BridgePayload();
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
            if (_manager == null) return;
            if (!ValidateConfiguredTopic("publish")) return;

            var resolution = ResolvePublisherEncoding();
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
            if (_manager == null) return;
            if (!ValidateConfiguredTopic("publish")) return;

            var resolution = ResolvePublisherEncoding();
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
            if (_manager == null) return;
            if (!ValidateConfiguredTopic("ROS2 Bridge publish")) return;

            var resolution = ResolveRos2BridgeOutput();
            WarnIfRos2BridgeFallback(resolution);
            if (!resolution.IsEnabled) return;

            if (string.IsNullOrWhiteSpace(Ros2BridgeSchemaName))
            {
                WarnRos2BridgeSkipped("missing-schema", "ROS2 Bridge schema name is missing.");
                return;
            }

            _manager.PublishRos2BridgeCdr(_topic, _ros2BridgeTopicOverride, Ros2BridgeSchemaName, payload, logTimeNs);
        }

        private PublisherEncodingResolution ResolvePublisherEncoding()
        {
            var managerDefault = _manager != null ? _manager.DefaultPublisherEncoding : GlobalEncoding.Protobuf;
            var allowPublisherOverride = _manager == null || _manager.AllowPublisherOverride;
            return PublisherEncodingPolicy.Resolve(
                managerDefault,
                allowPublisherOverride,
                _encodingOverride,
                SupportsJsonEncoding,
                SupportsProtobufEncoding,
                SupportsRos2Encoding);
        }

        private Ros2BridgeOutputResolution ResolveRos2BridgeOutput()
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
                manager = FindFirstObjectByType<FoxgloveManager>();
#endif

            return PublisherRatePolicy.Resolve(
                _publishRateSource,
                manager != null ? manager.DefaultPublishRateHz : _publishRateHz,
                _publishRateHz,
                manager != null);
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

            var key = $"fallback:{resolution.RequestedLabel}:{resolution.EffectiveLabel}";
            if (_lastEncodingWarningKey == key) return;
            _lastEncodingWarningKey = key;

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

            var key = "invalid-topic:" + operation;
            if (_lastTopicWarningKey != key)
            {
                _lastTopicWarningKey = key;
                Debug.LogWarning(
                    $"[Foxglove] {GetType().Name} cannot {operation}: Topic is empty. Configure a non-empty topic before publishing.");
            }

            return false;
        }

        private void WarnEncodingMismatch(PublisherEncodingResolution resolution, string attemptedEncoding)
        {
            var key = $"mismatch:{attemptedEncoding}:{resolution.EffectiveLabel}";
            if (_lastEncodingWarningKey == key) return;
            _lastEncodingWarningKey = key;

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} resolved to {resolution.EffectiveLabel} but attempted to publish {attemptedEncoding}; dropping message.");
        }

        private void WarnIfRos2BridgeFallback(Ros2BridgeOutputResolution resolution)
        {
            if (!resolution.FellBack) return;

            var key = $"fallback:{resolution.RequestedLabel}:{resolution.EffectiveLabel}";
            if (_lastBridgeWarningKey == key) return;
            _lastBridgeWarningKey = key;

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
    }
}
