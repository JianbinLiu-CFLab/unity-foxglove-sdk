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
        [Header("FoxRun Inbound")]
        [Tooltip("Allow generated SubscribeOnly and PublishAndSubscribe FoxRun members to receive client-published JSON. Disabled by default.")]
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

        public bool IsFoxRunInboundAuthorized
        {
            get
            {
                return FoxRunInboundAuthorization.IsAuthorized(
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
                FoxRunInboundAuthorization.IsAuthorized(
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
