// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Pure C# WebSocket server backend using TcpListener and manual
// RFC 6455 framing. No http.sys dependency — works on all platforms
// without admin rights.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Pure C# WebSocket server backend using TcpListener + manual WebSocket protocol.
    /// No http.sys dependency — works on all platforms without admin rights.
    /// </summary>
    public class ManagedWsBackend : IFoxgloveTransport, IDisposable
    {
        /// <summary>TCP listener bound to the server address and port.</summary>
        private TcpListener _listener;
        /// <summary>Cancellation token source to stop accept/receive loops.</summary>
        private CancellationTokenSource _cts;
        /// <summary>Active WebSocket connections keyed by client ID.</summary>
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        /// <summary>Logger instance for diagnostic output.</summary>
        private readonly IFoxgloveLogger _logger;
        /// <summary>Monotonically increasing counter for assigning client IDs.</summary>
        private int _nextClientId;
        /// <summary>Allowed browser origins for Cross-Site WebSocket Hijacking protection. Empty collection rejects all browser-origin clients.</summary>
        private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);

        public ManagedWsBackend(IFoxgloveLogger logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        /// <summary>Whether the TCP listener is actively accepting connections.</summary>
        public bool IsRunning => _listener != null;

        /// <summary>Fires when a new WebSocket client completes the handshake.</summary>
        public event Action<uint> OnClientConnected;
        /// <summary>Fires when a client disconnects or is forcefully removed.</summary>
        public event Action<uint> OnClientDisconnected;
        /// <summary>Fires when a UTF-8 text message is received from a client.</summary>
        public event Action<uint, string> OnTextReceived;
        /// <summary>Fires when a binary message is received from a client.</summary>
        public event Action<uint, byte[]> OnBinaryReceived;

        /// <summary>Bind the TCP listener to <c>host</c>:<c>port</c> and begin accepting connections.</summary>
        public void Start(string host, int port)
        {
            if (_listener != null)
                throw new InvalidOperationException("Server already started");

            var addr = host switch
            {
                "0.0.0.0" => IPAddress.Any,
                _ => IPAddress.Parse(host)
            };

            _listener = new TcpListener(addr, port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _cts = new CancellationTokenSource();
            _listener.Start();

            _ = Task.Run(() => AcceptLoop(_cts.Token));
        }

        /// <summary>Cancel listener, disconnect all clients, and stop accepting new connections.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            foreach (var (id, conn) in _clients.ToArray())
                DisconnectClient(id, conn);

            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        /// <summary>Send a UTF-8 text frame to a specific client.</summary>
        public void SendText(uint clientId, string json)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            try { conn.SendText(json); }
            catch (IOException) { DisconnectClient(clientId, conn); }
            catch (Exception ex) { _logger.LogError($"SendText error: {ex.Message}"); }
        }

        /// <summary>Send a binary frame to a specific client.</summary>
        public void SendBinary(uint clientId, byte[] data)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            try { conn.SendBinary(data); }
            catch (IOException) { DisconnectClient(clientId, conn); }
            catch (Exception ex) { _logger.LogError($"SendBinary error: {ex.Message}"); }
        }

        /// <summary>Send a UTF-8 text frame to every connected client.</summary>
        public void BroadcastText(string json)
        {
            foreach (var (id, conn) in _clients.ToArray())
            {
                try { conn.SendText(json); }
                catch { DisconnectClient(id, conn); }
            }
        }

        /// <summary>Send a binary frame to every connected client.</summary>
        public void BroadcastBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients.ToArray())
            {
                try { conn.SendBinary(data); }
                catch { DisconnectClient(id, conn); }
            }
        }

        /// <summary>Stop the server and release the cancellation token source.</summary>
        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
        }

        // ── Origin Guard ──

        /// <summary>Snapshot of currently allowed browser origins. Empty means no browser clients are allowed.</summary>
        public IReadOnlyCollection<string> AllowedOrigins
        {
            get { lock (_allowedOrigins) return _allowedOrigins.ToList(); }
        }

        /// <summary>Add an origin to the allowlist (case-insensitive). Example: <c>http://localhost:3000</c>.</summary>
        public void AddAllowedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return;
            lock (_allowedOrigins) _allowedOrigins.Add(origin);
        }

        /// <summary>Remove all origins from the allowlist, blocking all browser clients.</summary>
        public void ClearAllowedOrigins()
        {
            lock (_allowedOrigins) _allowedOrigins.Clear();
        }

        // ── Internal ──

        /// <summary>Continuously accept TCP clients and spawn handler tasks until canceled.</summary>
        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(tcpClient, ct), ct);
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
                catch (Exception) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"Accept error: {ex.Message}");
                }
            }
        }

        /// <summary>Perform WebSocket handshake, register the connection, and enter the receive loop.</summary>
        private void HandleClient(TcpClient tcpClient, CancellationToken ct)
        {
            try
            {
                using (tcpClient)
                {
                    var stream = tcpClient.GetStream();
                    stream.ReadTimeout = 5000;
                    stream.WriteTimeout = 5000;

                    var (accepted, subprotocol) = Handshake(stream);
                    if (!accepted)
                        return;

                    stream.ReadTimeout = Timeout.Infinite;

                    var clientId = (uint)Interlocked.Increment(ref _nextClientId);
                    var conn = new WsConnection(stream);
                    _clients[clientId] = conn;

                    OnClientConnected?.Invoke(clientId);

                    ReceiveLoop(clientId, conn, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Client handler error: {ex.Message}");
            }
        }

        // ── WebSocket Handshake (RFC 6455 §4) ──

        /// <summary>Parse HTTP upgrade request and complete the WebSocket opening handshake per RFC 6455.</summary>
        private (bool accepted, string subprotocol) Handshake(NetworkStream stream)
        {
            var requestLine = ReadLineRaw(stream);
            if (string.IsNullOrEmpty(requestLine))
                return (false, null);

            var parts = requestLine.Split(' ');
            if (parts.Length < 3 || parts[0] != "GET")
                return (false, null);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while (!string.IsNullOrEmpty(line = ReadLineRaw(stream)))
            {
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

            // Origin guard: reject browser clients unless the origin is in the allowlist.
            // file:// origins come from non-browser environments (Electron desktop apps,
            // Foxglove Desktop) — they are not subject to CSWSH, so they are always allowed.
            if (headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin)
                && !origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                bool allowed;
                lock (_allowedOrigins) allowed = _allowedOrigins.Contains(origin);
                if (!allowed)
                {
                    _logger.LogWarning(
                        $"[Foxglove] Rejected WebSocket Origin '{origin}'. Add it to allowed origins to permit browser clients.");
                    var forbid = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\n\r\n");
                    stream.Write(forbid, 0, forbid.Length);
                    return (false, null);
                }
            }

            // Subprotocol negotiation
            string selected = null;
            if (headers.TryGetValue("Sec-WebSocket-Protocol", out var clientProtocols))
            {
                foreach (var cp in clientProtocols.Split(',').Select(p => p.Trim()))
                {
                    foreach (var a in Subprotocol.Accepted)
                    {
                        if (cp.Equals(a, StringComparison.OrdinalIgnoreCase))
                        {
                            selected = a;
                            break;
                        }
                    }
                    if (selected != null) break;
                }
            }

            if (selected == null)
            {
                _logger.LogError("Client connected without accepted subprotocol, closing.");
                var reject = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n");
                stream.Write(reject, 0, reject.Length);
                return (false, null);
            }

            // Compute Sec-WebSocket-Accept
            var acceptKey = ComputeAcceptKey(wsKey);
            var response = new StringBuilder();
            response.Append("HTTP/1.1 101 Switching Protocols\r\n");
            response.Append("Upgrade: websocket\r\n");
            response.Append("Connection: Upgrade\r\n");
            response.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n");
            response.Append($"Sec-WebSocket-Protocol: {selected}\r\n");
            response.Append("\r\n");

            var responseBytes = Encoding.ASCII.GetBytes(response.ToString());
            stream.Write(responseBytes, 0, responseBytes.Length);

            return (true, selected);
        }

        /// <summary>Compute the Sec-WebSocket-Accept response value per RFC 6455 §4.2.2.</summary>
        private static string ComputeAcceptKey(string wsKey)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(wsKey + magic));
            return Convert.ToBase64String(hash);
        }

        /// <summary>Read one line byte-by-byte, avoiding StreamReader buffering that could steal frame data.</summary>
        private static string ReadLineRaw(NetworkStream stream)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var b = stream.ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (b == '\r')
                {
                    var next = stream.ReadByte();
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

        // ── Receive Loop ──

        /// <summary>Continuously read frames, dispatch text/binary/close/ping, until the stream ends or is canceled.</summary>
        private void ReceiveLoop(uint clientId, WsConnection conn, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = conn.ReadFrame();
                    if (frame == null) break;

                    // Reject fragmented text/binary frames (continuation not supported)
                    if (!frame.Fin && (frame.Opcode == WsOpcode.Text || frame.Opcode == WsOpcode.Binary))
                    {
                        conn.SendClose();
                        return;
                    }

                    switch (frame.Opcode)
                    {
                        case WsOpcode.Text:
                            OnTextReceived?.Invoke(clientId, Encoding.UTF8.GetString(frame.Payload));
                            break;
                        case WsOpcode.Binary:
                            OnBinaryReceived?.Invoke(clientId, frame.Payload);
                            break;
                        case WsOpcode.Close:
                            conn.SendClose();
                            return;
                        case WsOpcode.Ping:
                            conn.SendPong(frame.Payload);
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (Exception ex)
            {
                _logger.LogError($"Receive error client {clientId}: {ex.Message}");
            }
            finally
            {
                DisconnectClient(clientId, conn);
            }
        }

        /// <summary>Remove the client from the dictionary, fire the disconnected event, and dispose the connection.</summary>
        private void DisconnectClient(uint clientId, WsConnection conn)
        {
            if (!_clients.TryRemove(clientId, out _)) return;
            OnClientDisconnected?.Invoke(clientId);
            try { conn.Dispose(); } catch { }
        }

        // ── WebSocket Connection ──

        /// <summary>
        /// Per-connection framing layer: send/receive WebSocket frames over a single TCP stream.
        /// Thread-safe for concurrent sends via <c>_sendLock</c>.
        /// </summary>
        private sealed class WsConnection : IDisposable
        {
            /// <summary>Underlying TCP network stream.</summary>
            private readonly NetworkStream _stream;
            /// <summary>Lock to serialize frame writes (RFC 6455 requires no interleaving).</summary>
            private readonly object _sendLock = new object();

            /// <summary>RFC 6455 opcode for text frames.</summary>
            private const byte OpText = 0x1;
            /// <summary>RFC 6455 opcode for binary frames.</summary>
            private const byte OpBinary = 0x2;
            /// <summary>RFC 6455 opcode for close frames.</summary>
            private const byte OpClose = 0x8;
            /// <summary>RFC 6455 opcode for ping frames.</summary>
            private const byte OpPing = 0x9;
            /// <summary>RFC 6455 opcode for pong frames.</summary>
            private const byte OpPong = 0xA;
            /// <summary>FIN bit mask in the first byte of a frame header.</summary>
            private const byte FinBit = 0x80;

            /// <summary>Maximum allowable payload size in bytes (64 MiB).</summary>
            internal const int MaxPayloadBytes = 64 * 1024 * 1024;

            /// <summary>Create a connection on the given network stream.</summary>
            public WsConnection(NetworkStream stream) => _stream = stream;

            /// <summary>Encode the string as UTF-8 and send it in a text frame.</summary>
            public void SendText(string json)
            {
                var payload = Encoding.UTF8.GetBytes(json);
                SendFrame(OpText, payload);
            }

            /// <summary>Send raw bytes in a binary frame.</summary>
            public void SendBinary(byte[] data)
            {
                SendFrame(OpBinary, data);
            }

            /// <summary>Send a close frame with an empty payload to initiate graceful shutdown.</summary>
            public void SendClose()
            {
                try { SendFrame(OpClose, Array.Empty<byte>()); } catch { }
            }

            /// <summary>Echo back a pong frame with the given payload in response to a ping.</summary>
            public void SendPong(byte[] data)
            {
                SendFrame(OpPong, data);
            }

            /// <summary>Build and write a complete WebSocket frame (FIN + opcode + length-prefixed payload).</summary>
            private void SendFrame(byte opcode, byte[] payload)
            {
                lock (_sendLock)
                {
                    var header = new List<byte> { (byte)(FinBit | opcode) };

                    if (payload.Length <= 125)
                    {
                        header.Add((byte)payload.Length);
                    }
                    else if (payload.Length <= 65535)
                    {
                        header.Add(126);
                        header.Add((byte)(payload.Length >> 8));
                        header.Add((byte)payload.Length);
                    }
                    else
                    {
                        header.Add(127);
                        for (var i = 7; i >= 0; i--)
                            header.Add((byte)((ulong)payload.Length >> (i * 8)));
                    }

                    _stream.Write(header.ToArray(), 0, header.Count);
                    _stream.Write(payload, 0, payload.Length);
                    _stream.Flush();
                }
            }

            /// <summary>
            /// Read and unmask a complete WebSocket frame from the stream.
            /// Returns <c>null</c> on stream closure, oversized payload, or protocol error.
            /// </summary>
            public WsFrame ReadFrame()
            {
                var header = new byte[2];
                if (!ReadExact(header, 0, 2)) return null;

                var fin = (header[0] & FinBit) != 0;
                var opcode = header[0] & 0x0F;
                var masked = (header[1] & 0x80) != 0;
                var payloadLen = (int)(header[1] & 0x7F);

                if (payloadLen == 126)
                {
                    var ext = new byte[2];
                    if (!ReadExact(ext, 0, 2)) return null;
                    payloadLen = (ext[0] << 8) | ext[1];
                }
                else if (payloadLen == 127)
                {
                    var ext = new byte[8];
                    if (!ReadExact(ext, 0, 8)) return null;
                    var len64 = (long)(((long)ext[0] << 56) | ((long)ext[1] << 48) | ((long)ext[2] << 40)
                                     | ((long)ext[3] << 32) | ((long)ext[4] << 24) | ((long)ext[5] << 16)
                                     | ((long)ext[6] << 8)  | (long)ext[7]);
                    if (len64 < 0 || len64 > int.MaxValue) return null;
                    payloadLen = (int)len64;
                }

                byte[] mask = null;
                if (masked)
                {
                    mask = new byte[4];
                    if (!ReadExact(mask, 0, 4)) return null;
                }

                if (payloadLen > MaxPayloadBytes)
                    return null;

                var payload = new byte[payloadLen];
                if (payloadLen > 0 && !ReadExact(payload, 0, payloadLen)) return null;

                if (masked && mask != null)
                {
                    for (var i = 0; i < payload.Length; i++)
                        payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                }

                return new WsFrame
                {
                    Fin = fin,
                    Opcode = (byte)opcode,
                    Payload = payload
                };
            }

            /// <summary>Read exactly <c>count</c> bytes into the buffer, returning <c>false</c> if the stream ends early.</summary>
            private bool ReadExact(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    var read = _stream.Read(buffer, offset, count);
                    if (read == 0) return false;
                    offset += read;
                    count -= read;
                }
                return true;
            }

            /// <summary>Close and dispose the underlying network stream.</summary>
            public void Dispose()
            {
                try { _stream.Close(); } catch { }
                try { _stream.Dispose(); } catch { }
            }
        }

        /// <summary>Decoded WebSocket frame: FIN flag, opcode, and unmasked payload.</summary>
        internal sealed class WsFrame
        {
            /// <summary>Whether this is the final fragment of a message.</summary>
            public bool Fin;
            /// <summary>WebSocket opcode (text, binary, close, ping, pong).</summary>
            public byte Opcode;
            /// <summary>Unmasked payload data.</summary>
            public byte[] Payload;
        }
    }

    /// <summary>RFC 6455 WebSocket opcode constants.</summary>
    internal static class WsOpcode
    {
        /// <summary>Text frame opcode (0x1).</summary>
        public const byte Text = 0x1;
        /// <summary>Binary frame opcode (0x2).</summary>
        public const byte Binary = 0x2;
        /// <summary>Close frame opcode (0x8).</summary>
        public const byte Close = 0x8;
        /// <summary>Ping frame opcode (0x9).</summary>
        public const byte Ping = 0x9;
        /// <summary>Pong frame opcode (0xA).</summary>
        public const byte Pong = 0xA;
    }
}
