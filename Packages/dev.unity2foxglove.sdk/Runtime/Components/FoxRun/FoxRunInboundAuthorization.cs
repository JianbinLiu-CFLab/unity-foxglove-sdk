// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Pure security policy for FoxRun inbound control.

using System;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Components
{
    public static class FoxRunInboundAuthorization
    {
        public static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;
            host = host.Trim();
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(host, "::1", StringComparison.Ordinal)
                   || string.Equals(host, "[::1]", StringComparison.Ordinal)
                   || host.StartsWith("127.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks whether the local inbound policy allows FoxRun client-publish traffic for the configured endpoint.
        /// </summary>
        /// <remarks>
        /// This policy gate does not inspect a presented client token. Remote WebSocket authentication is enforced by the
        /// managed WebSocket handshake before client-publish messages reach FoxRun.
        /// </remarks>
        public static bool IsRemoteInboundPolicyMet(
            bool enabled,
            string host,
            bool allowRemoteWithSharedToken,
            string sharedToken,
            out string diagnostic)
        {
            if (!enabled)
            {
                diagnostic = "FoxRun inbound is disabled.";
                return false;
            }
            if (IsLoopbackHost(host))
            {
                diagnostic = string.Empty;
                return true;
            }
            if (!allowRemoteWithSharedToken)
            {
                diagnostic = "FoxRun inbound rejected: the server is not loopback-bound and remote inbound is not explicitly enabled.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(sharedToken))
            {
                diagnostic = "FoxRun inbound rejected: remote inbound requires a configured shared token.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// Performs a full one-shot authorization check when a remote caller's token is available at this layer.
        /// </summary>
        public static bool IsAuthorized(
            bool enabled,
            string host,
            bool allowRemoteWithSharedToken,
            string sharedToken,
            string incomingToken,
            out string diagnostic)
        {
            if (!IsRemoteInboundPolicyMet(enabled, host, allowRemoteWithSharedToken, sharedToken, out diagnostic))
                return false;

            if (IsLoopbackHost(host))
                return true;

            if (!ManagedWebSocketOptions.FixedTimeEqualsUtf8(sharedToken, incomingToken))
            {
                diagnostic = "FoxRun inbound rejected: remote inbound token did not match the configured shared token.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// Backward-compatible policy check. Prefer <see cref="IsRemoteInboundPolicyMet"/> for code that does not have
        /// access to the incoming token at this layer.
        /// </summary>
        public static bool IsAuthorized(
            bool enabled,
            string host,
            bool allowRemoteWithSharedToken,
            string sharedToken,
            out string diagnostic)
            => IsRemoteInboundPolicyMet(enabled, host, allowRemoteWithSharedToken, sharedToken, out diagnostic);
    }
}
