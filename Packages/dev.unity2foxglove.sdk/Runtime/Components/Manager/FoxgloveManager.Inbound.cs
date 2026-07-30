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
        [FormerlySerializedAs("_defaultFoxRunWireEncoding")]
        [SerializeField, HideInInspector,
         Obsolete("Use directional FoxRun encoding defaults.")]
        private FoxRunEncoding _defaultFoxRunEncoding =
            FoxRunEncoding.Protobuf;

        [SerializeField]
        private FoxRunEncoding
            _defaultFoxRunSubscriptionEncoding =
                FoxRunEncoding.Protobuf;

        [Tooltip(
            "Allow generated Subscribe and PublishAndSubscribe FoxRun members to receive Provider input.")]
        [SerializeField]
        private bool _enableFoxRunInbound = true;

        [Tooltip(
            "Permit non-loopback WebSocket input only when a configured shared token is required at connect time.")]
        [SerializeField]
        private bool
            _allowRemoteFoxRunInboundWithSharedToken;

        [SerializeField, Min(256)]
        private int _foxRunInboundMaxPayloadBytes =
            64 * 1024;

        [Tooltip(
            "Hard per-topic transport-admission ceiling. Excess input is dropped before avoidable decode or copying.")]
        [SerializeField, Min(1)]
        private int
            _foxRunInboundMaxMessagesPerSecondPerTopic =
                60;

        [Tooltip(
            "Default subscription rate inherited by declarations that do not specify a positive Hz.")]
        [FormerlySerializedAs(
            "_foxRunDefaultApplyRateHz")]
        [SerializeField, Min(1)]
        private int _foxRunDefaultSubscribeRateHz = 10;

        public bool EnableFoxRunInbound
        {
            get => _enableFoxRunInbound;
            set => _enableFoxRunInbound = value;
        }

        public int FoxRunSubscriptionMaxPayloadBytes =>
            Math.Max(
                256,
                _foxRunInboundMaxPayloadBytes);

        private int
            ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic =>
                Math.Max(
                    1,
                    _foxRunInboundMaxMessagesPerSecondPerTopic);

        private int ConfiguredFoxRunDefaultSubscribeRateHz =>
            Math.Max(
                1,
                _foxRunDefaultSubscribeRateHz);

        public int
            FoxRunSubscriptionMaxMessagesPerSecondPerTopic =>
                ActiveFoxRunSubscriptionSessionPolicy
                    .SubscriptionsEnabled
                    ? ActiveFoxRunSubscriptionSessionPolicy
                        .TransportAdmissionRateLimitHz
                    : ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic;

        public int DefaultFoxRunSubscriptionRateHz =>
            ConfiguredFoxRunDefaultSubscribeRateHz;

        public FoxRunEncoding
            DefaultFoxRunSubscriptionEncoding
        {
            get => _defaultFoxRunSubscriptionEncoding
                   == (FoxRunEncoding)0
                ? FoxRunEncoding.Protobuf
                : FoxRunEncodingResolver
                    .ValidateProfileDefault(
                        _defaultFoxRunSubscriptionEncoding);
            set => _defaultFoxRunSubscriptionEncoding =
                FoxRunEncodingResolver
                    .ValidateProfileDefault(value);
        }

        public FoxRunTransportId
            DefaultFoxRunSubscribeTransportId =>
                ConfiguredFoxRunSubscribeTransportId;

        public FoxRunEncoding
            ActiveFoxRunSubscriptionEncoding =>
                ActiveFoxRunSubscriptionSessionPolicy
                    .SubscriptionsEnabled
                    ? ActiveFoxRunSubscriptionSessionPolicy
                        .WebSocketEncoding
                    : DefaultFoxRunSubscriptionEncoding;

        public FoxRunTransportId
            ActiveFoxRunSubscribeTransportId =>
                ActiveFoxRunSubscriptionSessionPolicy
                    .SubscriptionsEnabled
                    ? ActiveFoxRunSubscriptionSessionPolicy
                        .DefaultProvider
                    : DefaultFoxRunSubscribeTransportId;

        public FoxRunEncoding ResolveFoxRunEncoding(
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode)
            => FoxRunEncodingResolver.Resolve(
                declaredEncoding,
                mode,
                ActiveFoxRunPublishEncoding,
                ActiveFoxRunSubscriptionEncoding);

        public bool IsFoxRunInboundAuthorized =>
            FoxRunInboundAuthorization
                .IsRemoteInboundPolicyMet(
                    _enableFoxRunInbound,
                    _host,
                    _allowRemoteFoxRunInboundWithSharedToken,
                    ResolveSharedToken(),
                    out _);

        public string
            FoxRunInboundAuthorizationDiagnostic
        {
            get
            {
                FoxRunInboundAuthorization
                    .IsRemoteInboundPolicyMet(
                        _enableFoxRunInbound,
                        _host,
                        _allowRemoteFoxRunInboundWithSharedToken,
                        ResolveSharedToken(),
                        out var diagnostic);
                return diagnostic;
            }
        }

        public static bool IsLoopbackHost(string host)
            => FoxRunInboundAuthorization
                .IsLoopbackHost(host);
    }
}
