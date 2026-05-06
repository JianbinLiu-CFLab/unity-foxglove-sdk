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
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        private readonly IFoxgloveLogger _logger;
        private int _nextClientId;

        public ManagedWsBackend(IFoxgloveLogger logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        public bool IsRunning => _listener != null;

        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

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

        public void Stop()
        {
            _cts?.Cancel();
            foreach (var (id, conn) in _clients.ToArray())
                DisconnectClient(id, conn);

            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        public void SendText(uint clientId, string json)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            try { conn.SendText(json); }
            catch (IOException) { DisconnectClient(clientId, conn); }
            catch (Exception ex) { _logger.LogError($"SendText error: {ex.Message}"); }
        }

        public void SendBinary(uint clientId, byte[] data)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            try { conn.SendBinary(data); }
            catch (IOException) { DisconnectClient(clientId, conn); }
            catch (Exception ex) { _logger.LogError($"SendBinary error: {ex.Message}"); }
        }

        public void BroadcastText(string json)
        {
            foreach (var (id, conn) in _clients.ToArray())
            {
                try { conn.SendText(json); }
                catch { DisconnectClient(id, conn); }
            }
        }

        public void BroadcastBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients.ToArray())
            {
                try { conn.SendBinary(data); }
                catch { DisconnectClient(id, conn); }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
        }

        // ── Internal ──

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

        private static string ComputeAcceptKey(string wsKey)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(wsKey + magic));
            return Convert.ToBase64String(hash);
        }

        // Read one line from the stream byte-by-byte to avoid StreamReader buffering.
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

        private void ReceiveLoop(uint clientId, WsConnection conn, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = conn.ReadFrame();
                    if (frame == null) break;

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

        private void DisconnectClient(uint clientId, WsConnection conn)
        {
            if (!_clients.TryRemove(clientId, out _)) return;
            OnClientDisconnected?.Invoke(clientId);
            try { conn.Dispose(); } catch { }
        }

        // ── WebSocket Connection ──

        private sealed class WsConnection : IDisposable
        {
            private readonly NetworkStream _stream;
            private readonly object _sendLock = new object();

            // RFC 6455 opcodes
            private const byte OpText = 0x1;
            private const byte OpBinary = 0x2;
            private const byte OpClose = 0x8;
            private const byte OpPing = 0x9;
            private const byte OpPong = 0xA;
            private const byte FinBit = 0x80;

            public WsConnection(NetworkStream stream) => _stream = stream;

            public void SendText(string json)
            {
                var payload = Encoding.UTF8.GetBytes(json);
                SendFrame(OpText, payload);
            }

            public void SendBinary(byte[] data)
            {
                SendFrame(OpBinary, data);
            }

            public void SendClose()
            {
                try { SendFrame(OpClose, Array.Empty<byte>()); } catch { }
            }

            public void SendPong(byte[] data)
            {
                SendFrame(OpPong, data);
            }

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

            public void Dispose()
            {
                try { _stream.Close(); } catch { }
                try { _stream.Dispose(); } catch { }
            }
        }

        internal sealed class WsFrame
        {
            public bool Fin;
            public byte Opcode;
            public byte[] Payload;
        }
    }

    internal static class WsOpcode
    {
        public const byte Text = 0x1;
        public const byte Binary = 0x2;
        public const byte Close = 0x8;
        public const byte Ping = 0x9;
        public const byte Pong = 0xA;
    }
}
