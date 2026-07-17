// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Immutable generated native subscription metadata and bounded-copy context.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>Immutable metadata for one generated native ROS2 subscription.</summary>
    public sealed class FoxRunRos2GeneratedContract
    {
        /// <summary>
        /// Compatibility constructor for Phase179-B generated callers. Legacy
        /// string metadata is retained for source compatibility but is not
        /// sufficient to activate a native subscription.
        /// </summary>
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            string declaredProvider,
            string ros2Qos)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            DeclaredProvider = Require(declaredProvider, nameof(declaredProvider));
            Ros2Qos = Require(ros2Qos, nameof(ros2Qos));
            Mode = FoxRunMode.PublishOnly;
            SubscriptionProvider = FoxRunSubscriptionProvider.Inherit;
            QosPreset = FoxRunRos2QosPreset.Inherit;
            SupportsRos2Native = false;
            HasCompleteMetadata = false;
        }

        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            FoxRunMode mode,
            FoxRunSubscriptionProvider subscriptionProvider,
            FoxRunRos2QosPreset qosPreset,
            bool supportsRos2Native)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            Mode = mode;
            SubscriptionProvider = subscriptionProvider;
            QosPreset = qosPreset;
            SupportsRos2Native = supportsRos2Native;
            HasCompleteMetadata = true;
            DeclaredProvider = ProviderText(subscriptionProvider);
            Ros2Qos = QosText(qosPreset);
        }

        public string Id { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public string DeclaredProvider { get; }
        public string Ros2Qos { get; }
        public bool HasCompleteMetadata { get; }
        public FoxRunMode Mode { get; }
        public FoxRunSubscriptionProvider SubscriptionProvider { get; }
        public FoxRunRos2QosPreset QosPreset { get; }
        public bool SupportsRos2Native { get; }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Generated ROS2 contract value must not be empty.", name);
            return value;
        }

        private static string ProviderText(FoxRunSubscriptionProvider provider)
        {
            switch (provider)
            {
                case FoxRunSubscriptionProvider.Inherit: return "inherit";
                case FoxRunSubscriptionProvider.FoxgloveWebSocket: return "foxglove-websocket";
                case FoxRunSubscriptionProvider.Ros2Native: return "ros2-native";
                default: return ((int)provider).ToString();
            }
        }

        private static string QosText(FoxRunRos2QosPreset qos)
        {
            switch (qos)
            {
                case FoxRunRos2QosPreset.Inherit: return "inherit";
                case FoxRunRos2QosPreset.Default: return "default";
                case FoxRunRos2QosPreset.Reliable: return "reliable";
                case FoxRunRos2QosPreset.SensorData: return "sensor-data";
                case FoxRunRos2QosPreset.TransientLocal: return "transient-local";
                default: return ((int)qos).ToString();
            }
        }
    }

    /// <summary>
    /// Per-callback managed-copy budget. Counts copied string UTF-16 storage and
    /// sequence element storage; it is intentionally not a DDS/CDR byte size.
    /// </summary>
    public sealed class FoxRunRos2CopyContext
    {
        [ThreadStatic]
        private static FoxRunRos2CopyContext s_cached;

        private long _remainingBytes;
        private bool _rented;

        public FoxRunRos2CopyContext(long maximumBytes)
        {
            Reset(maximumBytes);
        }

        public long RemainingBytes => _remainingBytes;

        public void RequireBytes(long byteCount)
        {
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if (byteCount > _remainingBytes)
                throw new InvalidOperationException("FoxRun ROS2 managed-copy budget exceeded.");
            _remainingBytes -= byteCount;
        }

        internal static FoxRunRos2CopyContext Rent(long maximumBytes)
        {
            var context = s_cached;
            if (context == null)
                context = new FoxRunRos2CopyContext(maximumBytes);
            else
            {
                s_cached = null;
                context.Reset(maximumBytes);
            }
            context._rented = true;
            return context;
        }

        internal void Return()
        {
            if (!_rented)
                return;
            _rented = false;
            if (s_cached == null)
                s_cached = this;
        }

        private void Reset(long maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _remainingBytes = maximumBytes;
        }
    }
}
#endif
