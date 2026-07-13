// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Security and resource policy for FoxRun subscription topics.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        // Kept as the source for one-to-two migration of scenes serialized before Phase176.
        [SerializeField, HideInInspector, Obsolete("Use directional FoxRun encoding defaults.")]
        private FoxRunWireEncoding _defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf;
        [SerializeField] private FoxRunWireEncoding _defaultFoxRunSubscriptionEncoding = FoxRunWireEncoding.Protobuf;
        private FoxRunWireEncoding _activeFoxRunSubscriptionEncoding = FoxRunWireEncoding.Protobuf;
        private bool _hasActiveFoxRunWireEncoding;

        [Tooltip("Allow generated SubscribeOnly and PublishAndSubscribe FoxRun members to receive client-published Protobuf or JSON. Disabled by default.")]
        [SerializeField] private bool _enableFoxRunInbound;
        [Tooltip("Permit non-loopback FoxRun inbound only when a configured shared token is required at WebSocket connect time. This is shared-token authorization, not per-client identity.")]
        [SerializeField] private bool _allowRemoteFoxRunInboundWithSharedToken;
        [SerializeField, Min(256)] private int _foxRunInboundMaxPayloadBytes = 64 * 1024;
        [SerializeField, Min(1)] private int _foxRunInboundMaxMessagesPerSecondPerTopic = 60;

        public bool EnableFoxRunInbound
        {
            get => _enableFoxRunInbound;
            set => _enableFoxRunInbound = value;
        }

        public int FoxRunSubscriptionMaxPayloadBytes => Math.Max(256, _foxRunInboundMaxPayloadBytes);
        public int FoxRunSubscriptionMaxMessagesPerSecondPerTopic =>
            Math.Max(1, _foxRunInboundMaxMessagesPerSecondPerTopic);

        /// <summary>Serialized default used by inherited SubscribeOnly contracts.</summary>
        public FoxRunWireEncoding DefaultFoxRunSubscriptionEncoding
        {
            get => _defaultFoxRunSubscriptionEncoding == FoxRunWireEncoding.Inherit
                ? FoxRunWireEncoding.Protobuf
                : FoxRunWireEncodingResolver.ValidateManagerDefault(_defaultFoxRunSubscriptionEncoding);
            set => _defaultFoxRunSubscriptionEncoding = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
        }

        /// <summary>Effective subscription default for the active server session, or the current configuration while stopped.</summary>
        public FoxRunWireEncoding ActiveFoxRunSubscriptionEncoding => _hasActiveFoxRunWireEncoding
            ? _activeFoxRunSubscriptionEncoding
            : DefaultFoxRunSubscriptionEncoding;

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
        public FoxRunWireEncoding ActiveFoxRunDefaultWireEncoding => _hasActiveFoxRunWireEncoding
            ? _activeFoxRunSubscriptionEncoding
            : DefaultFoxRunSubscriptionEncoding;

        /// <summary>Resolves a generated declaration against the active directional session policy.</summary>
        public FoxRunWireEncoding ResolveFoxRunWireEncoding(FoxRunWireEncoding declaredEncoding, FoxRunMode mode)
            => FoxRunWireEncodingResolver.Resolve(
                declaredEncoding,
                mode,
                ActiveFoxRunPublishEncoding,
                ActiveFoxRunSubscriptionEncoding);

        /// <summary>Compatibility resolver for older generated input dispatch.</summary>
        [Obsolete("Generated FoxRun code must pass its flow mode.")]
        public FoxRunWireEncoding ResolveFoxRunWireEncoding(FoxRunWireEncoding declaredEncoding)
            => ResolveFoxRunWireEncoding(declaredEncoding, FoxRunMode.SubscribeOnly);

        internal void CaptureFoxRunWireEncodingForSession()
        {
            _activeFoxRunPublishEncoding = DefaultFoxRunPublishEncoding;
            _activeFoxRunSubscriptionEncoding = DefaultFoxRunSubscriptionEncoding;
            _hasActiveFoxRunWireEncoding = true;
        }

        internal void ClearFoxRunWireEncodingForSession()
        {
            _hasActiveFoxRunWireEncoding = false;
            _activeFoxRunPublishEncoding = FoxRunWireEncoding.Protobuf;
            _activeFoxRunSubscriptionEncoding = FoxRunWireEncoding.Protobuf;
        }

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
