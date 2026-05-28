// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Build Foxglove hosted-app URLs for WebSocket data sources.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Builds hosted Foxglove app URLs using the same query shape as the
    /// official Foxglove SDK app URL helpers.
    /// </summary>
    public static class FoxgloveAppUrl
    {
        public const string HostedWebBaseUrl = "https://app.foxglove.dev";

        public static string BuildHostedWebSocketUrl(
            string host,
            int port,
            bool secure,
            string layoutId = null,
            bool openInDesktop = false,
            string token = null,
            bool redactToken = false)
        {
            var endpoint = BuildWebSocketEndpoint(host, port, secure, token, redactToken);
            var sb = new StringBuilder(HostedWebBaseUrl);
            sb.Append("?ds=foxglove-websocket");
            sb.Append("&ds.url=").Append(Uri.EscapeDataString(endpoint));

            if (!string.IsNullOrWhiteSpace(layoutId))
                sb.Append("&layoutId=").Append(Uri.EscapeDataString(layoutId.Trim()));

            if (openInDesktop)
                sb.Append("&openIn=desktop");

            return sb.ToString();
        }

        public static string BuildWebSocketEndpoint(
            string host,
            int port,
            bool secure,
            string token = null,
            bool redactToken = false)
        {
            var connectHost = FormatHostForUrl(NormalizeConnectHost(host));
            var sb = new StringBuilder(secure ? "wss://" : "ws://");
            sb.Append(connectHost).Append(':').Append(port);

            if (!string.IsNullOrEmpty(token))
            {
                var tokenValue = redactToken ? "REDACTED" : token;
                sb.Append("?token=").Append(Uri.EscapeDataString(tokenValue));
            }

            return sb.ToString();
        }

        private static string NormalizeConnectHost(string host)
        {
            var value = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            if (value == "0.0.0.0" || value == "*" || value == "::")
                return "127.0.0.1";
            return value;
        }

        private static string FormatHostForUrl(string host)
        {
            if (host.Length > 1 && host[0] == '[' && host[host.Length - 1] == ']')
                return host;
            return host.Contains(":") ? $"[{host}]" : host;
        }
    }
}
