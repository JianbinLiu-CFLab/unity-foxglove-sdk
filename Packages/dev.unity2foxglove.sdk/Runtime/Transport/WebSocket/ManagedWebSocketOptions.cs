// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Shared managed WebSocket transport options for queue capacity,
// lightweight query-token gating, and handshake diagnostics.

using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Shared options used by both plain and TLS managed WebSocket backends.
    /// </summary>
    public sealed class ManagedWebSocketOptions
    {
        /// <summary>Default per-client send queue frame cap.</summary>
        public const int DefaultMaxQueuedFrames = 256;

        /// <summary>Default per-client send queue byte cap (8 MiB).</summary>
        public const int DefaultMaxQueuedBytes = 8 * 1024 * 1024;

        /// <summary>Default active WebSocket client cap.</summary>
        public const int DefaultMaxClients = 64;

        private string _sharedToken = string.Empty;

        /// <summary>Maximum active WebSocket clients accepted by the managed backend.</summary>
        public int MaxClients { get; set; } = DefaultMaxClients;

        /// <summary>Maximum queued frames per client before drop/disconnect policy applies.</summary>
        public int MaxQueuedFramesPerClient { get; set; } = DefaultMaxQueuedFrames;

        /// <summary>Maximum queued payload bytes per client before drop/disconnect policy applies.</summary>
        public int MaxQueuedBytesPerClient { get; set; } = DefaultMaxQueuedBytes;

        /// <summary>Normalize frame capacity so a misconfigured value cannot disable protocol traffic.</summary>
        internal static int NormalizeMaxQueuedFrames(int value) => Math.Max(1, value);

        /// <summary>Normalize client capacity so zero/negative values fall back to the usable default.</summary>
        internal static int NormalizeMaxClients(int value) =>
            value > 0 ? value : DefaultMaxClients;

        /// <summary>Normalize byte capacity so zero/negative values fall back to the usable default.</summary>
        internal static int NormalizeMaxQueuedBytes(int value) =>
            value > 0 ? value : DefaultMaxQueuedBytes;

        /// <summary>
        /// Whether client disconnects before the WebSocket handshake should be logged.
        /// Browsers and desktop clients can open and cancel TLS probes during normal
        /// reconnects, so this diagnostic is quiet by default.
        /// </summary>
        public bool LogPreHandshakeClientDisconnects { get; set; }

        /// <summary>
        /// Optional shared query token. Empty disables token gating.
        /// Query-token auth is a lightweight local/LAN gate, not user identity.
        /// </summary>
        public string SharedToken
        {
            get => _sharedToken;
            set => _sharedToken = value ?? string.Empty;
        }

        /// <summary>Whether the configured token gate should reject missing or wrong tokens.</summary>
        public bool RequireToken => !string.IsNullOrEmpty(_sharedToken);

        /// <summary>Return true when token gating is disabled or the provided token matches.</summary>
        public bool IsTokenAccepted(string providedToken)
        {
            if (!RequireToken)
                return true;

            if (providedToken == null)
                return false;

            return FixedTimeEqualsUtf8(_sharedToken, providedToken);
        }

        /// <summary>Read one decoded query value from an HTTP request target such as <c>/?token=x</c>.</summary>
        public static string GetQueryParameter(string requestTarget, string name)
        {
            if (string.IsNullOrEmpty(requestTarget) || string.IsNullOrEmpty(name))
                return null;

            var queryIndex = requestTarget.IndexOf('?');
            if (queryIndex < 0 || queryIndex + 1 >= requestTarget.Length)
                return null;

            var end = requestTarget.IndexOf('#', queryIndex + 1);
            var query = end >= 0
                ? requestTarget.Substring(queryIndex + 1, end - queryIndex - 1)
                : requestTarget.Substring(queryIndex + 1);

            foreach (var segment in query.Split('&'))
            {
                if (segment.Length == 0)
                    continue;

                var equals = segment.IndexOf('=');
                var rawKey = equals >= 0 ? segment.Substring(0, equals) : segment;
                var rawValue = equals >= 0 ? segment.Substring(equals + 1) : string.Empty;
                var key = DecodeQueryComponent(rawKey);
                if (string.Equals(key, name, StringComparison.Ordinal))
                    return DecodeQueryComponent(rawValue);
            }

            return null;
        }

        /// <summary>Return a display-safe URL with any <c>token=</c> query value replaced.</summary>
        public static string RedactUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            return Regex.Replace(
                url,
                "([?&]token=)[^&#]*",
                "$1REDACTED",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Compare UTF-8 strings without early exit on the first differing byte.</summary>
        public static bool FixedTimeEqualsUtf8(string expected, string actual)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected ?? string.Empty);
            var actualBytes = Encoding.UTF8.GetBytes(actual ?? string.Empty);
            var max = Math.Max(expectedBytes.Length, actualBytes.Length);
            var diff = expectedBytes.Length ^ actualBytes.Length;

            for (var i = 0; i < max; i++)
            {
                var left = i < expectedBytes.Length ? expectedBytes[i] : (byte)0;
                var right = i < actualBytes.Length ? actualBytes[i] : (byte)0;
                diff |= left ^ right;
            }

            return diff == 0;
        }

        private static string DecodeQueryComponent(string value)
        {
            return Uri.UnescapeDataString((value ?? string.Empty).Replace("+", " "));
        }
    }
}
