// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Abstract base for all publisher MonoBehaviour components.
// Provides FoxgloveManager auto-resolution, publish-rate throttling,
// frame ID sanitization, encoding override, and publish helpers.

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
        [SerializeField] protected float _publishRateHz = 10f;
        [SerializeField] protected bool _publishOnEnable = true;
        [SerializeField] protected bool _warnIfManagerMissing = true;

        [Header("Encoding")]
        [Tooltip("Override the global default encoding for this publisher.")]
        [SerializeField] protected PublisherEncodingOverride _encodingOverride = PublisherEncodingOverride.UseManager;

        private float _lastPublishTime = float.NegativeInfinity;
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
                if (SupportsJsonEncoding && SupportsProtobufEncoding) return "json, protobuf";
                if (SupportsJsonEncoding) return "json";
                if (SupportsProtobufEncoding) return "protobuf";
                return "none";
            }
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
            if (_publishRateHz <= 0) return true;

            var interval = 1f / _publishRateHz;
            var now = Time.unscaledTime;
            if (now - _lastPublishTime >= interval)
            {
                _lastPublishTime = now;
                return true;
            }
            return false;
        }

        /// <summary>Replace spaces with underscores. Use fallback if empty.</summary>
        protected static string SanitizeFrameId(string raw, string fallback)
        {
            var sanitized = string.IsNullOrEmpty(raw) ? fallback : raw;
            return sanitized.Replace(' ', '_');
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
                WarnEncodingMismatch(resolution, "json");
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
                WarnEncodingMismatch(resolution, "protobuf");
                return;
            }

            _manager.PublishProto(_topic, SchemaName, payload, logTimeNs);
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
                SupportsProtobufEncoding);
        }

        private void WarnIfEncodingFallback(PublisherEncodingResolution resolution)
        {
            if (!resolution.FellBack) return;

            var key = $"fallback:{resolution.RequestedLabel}:{resolution.EffectiveLabel}";
            if (_lastEncodingWarningKey == key) return;
            _lastEncodingWarningKey = key;

            if (resolution.Effective == PublisherEffectiveEncoding.Unsupported)
            {
                Debug.LogWarning($"[Foxglove] {GetType().Name} does not support json or protobuf; dropping messages.");
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
