// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: RFC 6455 HTTP upgrade handshake handling for managed WebSocket
// transports, including subprotocol, token, and browser Origin checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>Handles the HTTP upgrade handshake before a connection enters WebSocket framing.</summary>
    internal sealed class WsHandshakeHandler
    {
        private const int MaxHandshakeLineBytes = 8192;
        private const int MaxHandshakeHeaders = 100;

        private readonly ManagedWebSocketOptions _options;
        private readonly IFoxgloveLogger _logger;
        private readonly HashSet<string> _allowedOrigins;
        private readonly object _allowedOriginsLock;

        public WsHandshakeHandler(
            ManagedWebSocketOptions options,
            HashSet<string> allowedOrigins,
            object allowedOriginsLock,
            IFoxgloveLogger logger)
        {
            _options = options ?? new ManagedWebSocketOptions();
            _allowedOrigins = allowedOrigins ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _allowedOriginsLock = allowedOriginsLock ?? _allowedOrigins;
            _logger = logger ?? new ConsoleLogger();
        }

        /// <summary>Parse and complete the opening handshake per RFC 6455.</summary>
        public (bool accepted, string subprotocol) Handshake(Stream stream)
        {
            string requestLine;
            try { requestLine = ReadLineRaw(stream, MaxHandshakeLineBytes); }
            catch (InvalidDataException) { return (false, null); }
            if (string.IsNullOrEmpty(requestLine))
                return (false, null);

            var parts = requestLine.Split(' ');
            if (parts.Length < 3 || parts[0] != "GET")
                return (false, null);
            var requestTarget = parts[1];

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            var headerCount = 0;
            while (true)
            {
                try { line = ReadLineRaw(stream, MaxHandshakeLineBytes); }
                catch (InvalidDataException) { return (false, null); }
                if (string.IsNullOrEmpty(line))
                    break;
                headerCount++;
                if (headerCount > MaxHandshakeHeaders)
                    return (false, null);

                var colon = line.IndexOf(':');
                if (colon > 0)
                    headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            if (!headers.TryGetValue("Connection", out var conn) ||
                conn.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0)
                return (false, null);

            if (!headers.TryGetValue("Upgrade", out var upgrade) ||
                !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                return (false, null);

            if (!headers.TryGetValue("Sec-WebSocket-Key", out var wsKey))
                return (false, null);

            if (!headers.TryGetValue("Sec-WebSocket-Version", out var version)
                || !version.Equals("13", StringComparison.Ordinal))
            {
                WriteResponse(stream, "HTTP/1.1 426 Upgrade Required\r\nSec-WebSocket-Version: 13\r\n\r\n");
                return (false, null);
            }

            // file:// origins come from non-browser environments such as Foxglove Desktop.
            if (headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin)
                && !origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                && !IsOriginAllowed(origin))
            {
                _logger.LogWarning(
                    $"Rejected WebSocket Origin '{origin}'. Add it to allowed origins to permit browser clients.");
                WriteResponse(stream, "HTTP/1.1 403 Forbidden\r\n\r\n");
                return (false, null);
            }

            if (_options.RequireToken)
            {
                var token = ManagedWebSocketOptions.GetQueryParameter(requestTarget, "token");
                if (!_options.IsTokenAccepted(token))
                {
                    _logger.LogWarning(token == null
                        ? "Rejected WebSocket client with missing token."
                        : "Rejected WebSocket client with invalid token.");
                    WriteResponse(stream, "HTTP/1.1 401 Unauthorized\r\n\r\n");
                    return (false, null);
                }
            }

            var selected = SelectSubprotocol(headers);
            if (selected == null)
            {
                _logger.LogError("Client connected without accepted subprotocol, closing.");
                WriteResponse(stream, "HTTP/1.1 400 Bad Request\r\n\r\n");
                return (false, null);
            }

            var acceptKey = ComputeAcceptKey(wsKey);
            var response = new StringBuilder();
            response.Append("HTTP/1.1 101 Switching Protocols\r\n");
            response.Append("Upgrade: websocket\r\n");
            response.Append("Connection: Upgrade\r\n");
            response.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n");
            response.Append($"Sec-WebSocket-Protocol: {selected}\r\n");
            response.Append("\r\n");

            WriteResponse(stream, response.ToString());
            return (true, selected);
        }

        private static string SelectSubprotocol(IReadOnlyDictionary<string, string> headers)
        {
            if (!headers.TryGetValue("Sec-WebSocket-Protocol", out var clientProtocols))
                return null;

            foreach (var cp in clientProtocols.Split(',').Select(p => p.Trim()))
            {
                foreach (var accepted in Subprotocol.Accepted)
                {
                    if (cp.Equals(accepted, StringComparison.OrdinalIgnoreCase))
                        return accepted;
                }
            }

            return null;
        }

        private bool IsOriginAllowed(string origin)
        {
            lock (_allowedOriginsLock) return _allowedOrigins.Contains(origin);
        }

        /// <summary>Compute the Sec-WebSocket-Accept response value per RFC 6455 section 4.2.2.</summary>
        private static string ComputeAcceptKey(string wsKey)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(wsKey + magic));
            return Convert.ToBase64String(hash);
        }

        private static void WriteResponse(Stream stream, string response)
        {
            var bytes = Encoding.ASCII.GetBytes(response);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Read one line byte-by-byte, avoiding StreamReader buffering that could steal frame data.</summary>
        private static string ReadLineRaw(Stream stream, int maxBytes)
        {
            var sb = new StringBuilder();
            var bytesRead = 0;
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                bytesRead++;
                if (bytesRead > maxBytes)
                    throw new InvalidDataException("WebSocket handshake line exceeds maximum length.");
                if (b == '\r')
                {
                    var next = stream.ReadByte();
                    if (next >= 0)
                    {
                        bytesRead++;
                        if (bytesRead > maxBytes)
                            throw new InvalidDataException("WebSocket handshake line exceeds maximum length.");
                    }
                    if (next == '\n') break;
                    if (next >= 0) sb.Append((char)next);
                }
                else if (b == '\n')
                {
                    break;
                }
                else
                {
                    sb.Append((char)b);
                }
            }

            return sb.ToString();
        }
    }
}
