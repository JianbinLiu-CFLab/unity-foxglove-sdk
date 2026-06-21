// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Pure security policy for FoxRun inbound control.

using System;

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

        public static bool IsAuthorized(
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
    }
}
