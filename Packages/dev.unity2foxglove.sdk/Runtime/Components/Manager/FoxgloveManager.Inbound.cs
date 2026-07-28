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
        [FormerlySerializedAs("_defaultFoxRunWireEncoding")]
        [SerializeField, HideInInspector, Obsolete("Use directional FoxRun encoding defaults.")]
        private FoxRunEncoding _defaultFoxRunEncoding = FoxRunEncoding.Protobuf;
        [SerializeField] private FoxRunEncoding _defaultFoxRunSubscriptionEncoding = FoxRunEncoding.Protobuf;
        [FormerlySerializedAs("_defaultFoxRunEndpoint")]
        [FormerlySerializedAs("_defaultFoxRunSubscriptionProvider")]
        [SerializeField] private FoxRunEndpoint _defaultFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove;
        [FormerlySerializedAs("_defaultFoxRunRos2Qos")]
        [SerializeField, HideInInspector] private int _legacyDefaultFoxRunRos2Qos = 1;
        [SerializeField] private FoxRunQosProfileSettings _defaultFoxRunNativeSubscribeQos = new();
        [SerializeField, Min(FoxRunEncodingPolicyMigration.MinRos2NativeCopyBudgetBytes)]
        private int _foxRunRos2NativeCopyBudgetBytes = FoxRunEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes;

        [Tooltip("Allow generated Subscribe and PublishAndSubscribe FoxRun members to receive client-published Protobuf or JSON. Enabled by default for the ordinary Unity and Foxglove workflow.")]
        [SerializeField] private bool _enableFoxRunInbound = true;
        [Tooltip("Permit non-loopback FoxRun inbound only when a configured shared token is required at WebSocket connect time. This is shared-token authorization, not per-client identity.")]
        [SerializeField] private bool _allowRemoteFoxRunInboundWithSharedToken;
        [SerializeField, Min(256)] private int _foxRunInboundMaxPayloadBytes = 64 * 1024;
        [Tooltip("Hard per-topic transport-admission ceiling shared by Foxglove WebSocket and ROS 2 Native subscriptions. Excess input is dropped before avoidable decode or native deep-copy work.")]
        [SerializeField, Min(1)] private int _foxRunInboundMaxMessagesPerSecondPerTopic = 60;
        [Tooltip("Default subscription rate inherited by subscription declarations that do not specify a positive Hz.")]
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
        public FoxRunEncoding DefaultFoxRunSubscriptionEncoding
        {
            get => _defaultFoxRunSubscriptionEncoding == (FoxRunEncoding)0
                ? FoxRunEncoding.Protobuf
                : FoxRunEncodingResolver.ValidateProfileDefault(_defaultFoxRunSubscriptionEncoding);
            set => _defaultFoxRunSubscriptionEncoding = FoxRunEncodingResolver.ValidateProfileDefault(value);
        }

        /// <summary>Serialized default source used by inherited Subscribe contracts.</summary>
        public FoxRunEndpoint DefaultFoxRunSubscriptionSource
        {
            get => FoxRunEndpointResolver.ValidateProfileSource(
                _defaultFoxRunSubscriptionSource);
            set
            {
                _defaultFoxRunSubscriptionSource =
                    FoxRunEndpointResolver.ValidateProfileSource(value);
                _foxRunPolicySerializationVersion =
                    FoxRunEncodingPolicyMigration.QosProfileSerializationVersion;
            }
        }

        /// <summary>Resolved default QoS used by native ROS2 subscription contracts.</summary>
        public FoxRunResolvedQos DefaultFoxRunNativeSubscribeQos
        {
            get
            {
                _defaultFoxRunNativeSubscribeQos ??= new FoxRunQosProfileSettings();
                return _defaultFoxRunNativeSubscribeQos.Resolve();
            }
        }

        /// <summary>Configured copied-data budget for optional native subscriptions.</summary>
        public int FoxRunRos2NativeCopyBudgetBytes
        {
            get => FoxRunEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(
                _foxRunRos2NativeCopyBudgetBytes);
            set
            {
                _foxRunRos2NativeCopyBudgetBytes =
                    FoxRunEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(value);
                _foxRunPolicySerializationVersion =
                    FoxRunEncodingPolicyMigration.QosProfileSerializationVersion;
            }
        }

        /// <summary>Effective subscription encoding for the active subscription session.</summary>
        public FoxRunEncoding ActiveFoxRunSubscriptionEncoding =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.FoxgloveEncoding
                : DefaultFoxRunSubscriptionEncoding;

        /// <summary>Effective source for the active subscription session.</summary>
        public FoxRunEndpoint ActiveFoxRunSubscriptionSource =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.DefaultSource
                : DefaultFoxRunSubscriptionSource;

        /// <summary>Effective ROS2 QoS for the active subscription session.</summary>
        public FoxRunResolvedQos ActiveFoxRunRos2Qos =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.DefaultRos2Qos
                : DefaultFoxRunNativeSubscribeQos;

        /// <summary>Effective native copy budget for the active subscription session.</summary>
        public int ActiveFoxRunRos2NativeCopyBudgetBytes =>
            ActiveFoxRunSubscriptionSessionPolicy.SubscriptionsEnabled
                ? ActiveFoxRunSubscriptionSessionPolicy.NativeCopyBudgetBytes
                : FoxRunRos2NativeCopyBudgetBytes;

        /// <summary>Resolves a generated declaration against the active directional session policy.</summary>
        public FoxRunEncoding ResolveFoxRunEncoding(FoxRunEncoding declaredEncoding, FoxRunFlow mode)
            => FoxRunEncodingResolver.Resolve(
                declaredEncoding,
                mode,
                ActiveFoxRunPublishEncoding,
                ActiveFoxRunSubscriptionEncoding);

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
