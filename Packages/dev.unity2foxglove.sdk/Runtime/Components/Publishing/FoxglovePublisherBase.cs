// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Abstract base for all publisher MonoBehaviour components.
// Provides FoxgloveManager auto-resolution, publish-rate throttling,
// frame ID sanitization, encoding override, and publish helpers.

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

        private FixedRatePublishState _publishRateState;
        private bool _warnedManagerMissing;
        private string _lastEncodingWarningKey;

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
            get
            {
                var labels = new System.Collections.Generic.List<string>(3);
                if (SupportsJsonEncoding) labels.Add("JSON");
                if (SupportsProtobufEncoding) labels.Add("Protobuf");
                if (SupportsRos2Encoding) labels.Add("ROS2");
                if (labels.Count != 0) return string.Join(", ", labels);
                return "none";
            }
        }

        protected virtual void Reset()
        {
            _publishRateSource = PublisherRateSource.UseManagerDefault;
        }

        protected virtual void OnEnable()
        {
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
            var resolution = ResolvePublisherEncoding();
            return ShouldPreparePublishPayload(resolution, resolution.Effective);
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

        /// <summary>Publish a message through the manager. Safe no-op if manager is null.</summary>
        protected void Publish(object message, ulong logTimeNs)
        {
            if (_manager == null) return;

            var resolution = ResolvePublisherEncoding();
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

        private void WarnEncodingMismatch(PublisherEncodingResolution resolution, string attemptedEncoding)
        {
            var key = $"mismatch:{attemptedEncoding}:{resolution.EffectiveLabel}";
            if (_lastEncodingWarningKey == key) return;
            _lastEncodingWarningKey = key;

            Debug.LogWarning(
                $"[Foxglove] {GetType().Name} resolved to {resolution.EffectiveLabel} but attempted to publish {attemptedEncoding}; dropping message.");
        }
    }
}
