// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Security and resource policy for FoxRun subscription topics.

using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        // Kept as the source for one-to-two migration of scenes serialized before Phase176.
        [SerializeField, HideInInspector, Obsolete("Use directional FoxRun encoding defaults.")]
        private FoxRunWireEncoding _defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf;
        [SerializeField] private FoxRunWireEncoding _defaultFoxRunSubscriptionEncoding = FoxRunWireEncoding.Protobuf;
        [SerializeField] private FoxRunSubscriptionProvider _defaultFoxRunSubscriptionProvider = FoxRunSubscriptionProvider.FoxgloveWebSocket;
        [SerializeField] private FoxRunRos2QosPreset _defaultFoxRunRos2Qos = FoxRunRos2QosPreset.Default;
        [SerializeField, Min(FoxRunWireEncodingPolicyMigration.MinRos2NativeCopyBudgetBytes)]
        private int _foxRunRos2NativeCopyBudgetBytes = FoxRunWireEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes;

        [Tooltip("Allow generated Subscribe and PublishAndSubscribe FoxRun members to receive client-published Protobuf or JSON. Disabled by default.")]
        [SerializeField] private bool _enableFoxRunInbound;
        [Tooltip("Permit non-loopback FoxRun inbound only when a configured shared token is required at WebSocket connect time. This is shared-token authorization, not per-client identity.")]
        [SerializeField] private bool _allowRemoteFoxRunInboundWithSharedToken;
        [SerializeField, Min(256)] private int _foxRunInboundMaxPayloadBytes = 64 * 1024;
        [Tooltip("Hard per-topic transport-admission ceiling shared by Foxglove WebSocket and ROS 2 Native subscriptions. Excess input is dropped before avoidable decode or native deep-copy work.")]
        [SerializeField, Min(1)] private int _foxRunInboundMaxMessagesPerSecondPerTopic = 60;
        [Tooltip("Default subscription rate inherited by subscription declarations that do not specify a positive RateHz.")]
        [FormerlySerializedAs("_foxRunDefaultApplyRateHz")]
        [SerializeField, Min(1)] private int _foxRunDefaultSubscribeRateHz = 10;

        public bool EnableFoxRunInbound
        {
            get => _enableFoxRunInbound;
            set => _enableFoxRunInbound = value;
        }

        public int FoxRunSubscriptionMaxPayloadBytes => Math.Max(256, _foxRunInboundMaxPayloadBytes);
        private int ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic =>
            Math.Max(1, _foxRunInboundMaxMessagesPerSecondPerTopic);
        private int ConfiguredFoxRunDefaultSubscribeRateHz =>
            Math.Max(1, _foxRunDefaultSubscribeRateHz);
        public int FoxRunSubscriptionMaxMessagesPerSecondPerTopic =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.TransportAdmissionRateLimitHz
                : ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic;

        /// <summary>Configured default rate for inherited subscription declarations.</summary>
        public int DefaultFoxRunSubscriptionRateHz => ConfiguredFoxRunDefaultSubscribeRateHz;

        /// <summary>Serialized default used by inherited Subscribe contracts.</summary>
        public FoxRunWireEncoding DefaultFoxRunSubscriptionEncoding
        {
            get => _defaultFoxRunSubscriptionEncoding == FoxRunWireEncoding.Inherit
                ? FoxRunWireEncoding.Protobuf
                : FoxRunWireEncodingResolver.ValidateManagerDefault(_defaultFoxRunSubscriptionEncoding);
            set => _defaultFoxRunSubscriptionEncoding = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
        }

        /// <summary>Serialized default provider used by inherited subscription contracts.</summary>
        public FoxRunSubscriptionProvider DefaultFoxRunSubscriptionProvider
        {
            get => FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(
                _defaultFoxRunSubscriptionProvider);
            set
            {
                _defaultFoxRunSubscriptionProvider =
                    FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(value);
                _foxRunPolicySerializationVersion =
                    FoxRunWireEncodingPolicyMigration.CurrentSerializationVersion;
            }
        }

        /// <summary>Serialized default QoS used by native ROS2 subscription contracts.</summary>
        public FoxRunRos2QosPreset DefaultFoxRunRos2Qos
        {
            get => FoxRunRos2QosResolver.NormalizeManagerDefault(_defaultFoxRunRos2Qos);
            set
            {
                _defaultFoxRunRos2Qos = FoxRunRos2QosResolver.NormalizeManagerDefault(value);
                _foxRunPolicySerializationVersion =
                    FoxRunWireEncodingPolicyMigration.CurrentSerializationVersion;
            }
        }

        /// <summary>Configured copied-data budget for optional native subscriptions.</summary>
        public int FoxRunRos2NativeCopyBudgetBytes
        {
            get => FoxRunWireEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(
                _foxRunRos2NativeCopyBudgetBytes);
            set
            {
                _foxRunRos2NativeCopyBudgetBytes =
                    FoxRunWireEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(value);
                _foxRunPolicySerializationVersion =
                    FoxRunWireEncodingPolicyMigration.CurrentSerializationVersion;
            }
        }

        /// <summary>Effective subscription encoding for the active subscription session.</summary>
        public FoxRunWireEncoding ActiveFoxRunSubscriptionEncoding =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.WebSocketSubscriptionEncoding
                : DefaultFoxRunSubscriptionEncoding;

        /// <summary>Effective provider for the active subscription session.</summary>
        public FoxRunSubscriptionProvider ActiveFoxRunSubscriptionProvider =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.DefaultProvider
                : DefaultFoxRunSubscriptionProvider;

        /// <summary>Effective ROS2 QoS for the active subscription session.</summary>
        public FoxRunRos2QosPreset ActiveFoxRunRos2Qos =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.DefaultRos2Qos
                : DefaultFoxRunRos2Qos;

        /// <summary>Effective native copy budget for the active subscription session.</summary>
        public int ActiveFoxRunRos2NativeCopyBudgetBytes =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.NativeCopyBudgetBytes
                : FoxRunRos2NativeCopyBudgetBytes;

        /// <summary>Compatibility alias for code compiled against the pre-Phase176 input policy.</summary>
        [Obsolete("Use FoxRunSubscriptionMaxPayloadBytes.")]
        public int FoxRunInboundMaxPayloadBytes => FoxRunSubscriptionMaxPayloadBytes;

        /// <summary>Compatibility alias for code compiled against the pre-Phase176 input policy.</summary>
        [Obsolete("Use FoxRunSubscriptionMaxMessagesPerSecondPerTopic.")]
        public int FoxRunInboundMaxMessagesPerSecondPerTopic => FoxRunSubscriptionMaxMessagesPerSecondPerTopic;

        /// <summary>Compatibility alias for the former single FoxRun default.</summary>
        [Obsolete("Use DefaultFoxRunPublishEncoding or DefaultFoxRunSubscriptionEncoding.")]
        public FoxRunWireEncoding DefaultFoxRunWireEncoding
        {
            get => DefaultFoxRunSubscriptionEncoding;
            set
            {
                value = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
                _defaultFoxRunWireEncoding = value;
                DefaultFoxRunPublishEncoding = value;
                DefaultFoxRunSubscriptionEncoding = value;
                _foxRunPolicySerializationVersion = FoxRunWireEncodingPolicyMigration.CurrentSerializationVersion;
            }
        }

        /// <summary>Compatibility alias for the former single active FoxRun default.</summary>
        [Obsolete("Use ActiveFoxRunPublishEncoding or ActiveFoxRunSubscriptionEncoding.")]
        public FoxRunWireEncoding ActiveFoxRunDefaultWireEncoding => ActiveFoxRunSubscriptionEncoding;

        /// <summary>Resolves a generated declaration against the active directional session policy.</summary>
        public FoxRunWireEncoding ResolveFoxRunWireEncoding(FoxRunWireEncoding declaredEncoding, FoxRunFlow mode)
            => FoxRunWireEncodingResolver.Resolve(
                declaredEncoding,
                mode,
                ActiveFoxRunPublishEncoding,
                ActiveFoxRunSubscriptionEncoding);

        /// <summary>Compatibility resolver for older generated input dispatch.</summary>
        [Obsolete("Generated FoxRun code must pass its flow mode.")]
        public FoxRunWireEncoding ResolveFoxRunWireEncoding(FoxRunWireEncoding declaredEncoding)
            => ResolveFoxRunWireEncoding(declaredEncoding, FoxRunFlow.Subscribe);

        public bool IsFoxRunInboundAuthorized
        {
            get
            {
                return FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                    _enableFoxRunInbound,
                    _host,
                    _allowRemoteFoxRunInboundWithSharedToken,
                    ResolveSharedToken(),
                    out _);
            }
        }

        public string FoxRunInboundAuthorizationDiagnostic
        {
            get
            {
                FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                    _enableFoxRunInbound,
                    _host,
                    _allowRemoteFoxRunInboundWithSharedToken,
                    ResolveSharedToken(),
                    out var diagnostic);
                return diagnostic;
            }
        }

        public static bool IsLoopbackHost(string host) => FoxRunInboundAuthorization.IsLoopbackHost(host);
    }
}
