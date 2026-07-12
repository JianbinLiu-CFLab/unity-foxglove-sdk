// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Security and resource policy for FoxRun inbound control topics.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField] private FoxRunWireEncoding _defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf;
        private FoxRunWireEncoding _activeFoxRunDefaultWireEncoding = FoxRunWireEncoding.Protobuf;
        private bool _hasActiveFoxRunWireEncoding;

        [Header("FoxRun Inbound")]
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

        public int FoxRunInboundMaxPayloadBytes => Math.Max(256, _foxRunInboundMaxPayloadBytes);
        public int FoxRunInboundMaxMessagesPerSecondPerTopic =>
            Math.Max(1, _foxRunInboundMaxMessagesPerSecondPerTopic);

        /// <summary>Serialized default used by generated FoxRun declarations that specify <see cref="FoxRunWireEncoding.Inherit"/>.</summary>
        public FoxRunWireEncoding DefaultFoxRunWireEncoding
        {
            get => _defaultFoxRunWireEncoding == FoxRunWireEncoding.Inherit
                ? FoxRunWireEncoding.Protobuf
                : FoxRunWireEncodingResolver.ValidateManagerDefault(_defaultFoxRunWireEncoding);
            set => _defaultFoxRunWireEncoding = FoxRunWireEncodingResolver.ValidateManagerDefault(value);
        }

        /// <summary>Effective default for the active server session, or the current configuration while stopped.</summary>
        public FoxRunWireEncoding ActiveFoxRunDefaultWireEncoding => _hasActiveFoxRunWireEncoding
            ? _activeFoxRunDefaultWireEncoding
            : DefaultFoxRunWireEncoding;

        /// <summary>Resolves a generated declaration against the active session policy.</summary>
        public FoxRunWireEncoding ResolveFoxRunWireEncoding(FoxRunWireEncoding declaredEncoding)
            => FoxRunWireEncodingResolver.Resolve(declaredEncoding, ActiveFoxRunDefaultWireEncoding);

        internal void CaptureFoxRunWireEncodingForSession()
        {
            _activeFoxRunDefaultWireEncoding = DefaultFoxRunWireEncoding;
            _hasActiveFoxRunWireEncoding = true;
        }

        internal void ClearFoxRunWireEncodingForSession()
        {
            _hasActiveFoxRunWireEncoding = false;
            _activeFoxRunDefaultWireEncoding = FoxRunWireEncoding.Protobuf;
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
