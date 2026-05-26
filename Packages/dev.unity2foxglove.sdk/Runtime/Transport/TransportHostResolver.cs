// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Shared host binding normalization for managed WebSocket listeners.

using System;
using System.Net;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>Resolve configured bind hosts consistently across local transport listeners.</summary>
    internal static class TransportHostResolver
    {
        public static IPAddress ResolveBindAddress(string host)
        {
            if (string.IsNullOrWhiteSpace(host)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
                return IPAddress.Loopback;

            if (string.Equals(host, "::1", StringComparison.Ordinal))
                return IPAddress.IPv6Loopback;

            if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal)
                || string.Equals(host, "*", StringComparison.Ordinal))
                return IPAddress.Any;

            if (string.Equals(host, "::", StringComparison.Ordinal))
                return IPAddress.IPv6Any;

            if (IPAddress.TryParse(host, out var address))
                return address;

            throw new FormatException($"Unsupported bind host '{host}'. Use an IP address, localhost, 0.0.0.0, *, or ::.");
        }
    }
}
