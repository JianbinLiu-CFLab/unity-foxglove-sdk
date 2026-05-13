// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Pure C# WebSocket server backend using TcpListener and manual
// RFC 6455 framing. No http.sys dependency - works on all platforms
// without admin rights.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
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
    /// No http.sys dependency; works on all platforms without admin rights.
    /// </summary>
    public class ManagedWsBackend : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IFoxgloveTransportStatsProvider, IOriginGuardedFoxgloveTransport, IDisposable
    {
        /// <summary>Per-client send queue frame cap; stale data frames are dropped before this is exceeded.</summary>
        internal const int MaxQueuedFrames = ManagedWebSocketOptions.DefaultMaxQueuedFrames;
        /// <summary>Per-client send queue byte cap.</summary>
        internal const int MaxQueuedBytes = ManagedWebSocketOptions.DefaultMaxQueuedBytes;
        private const int MaxHandshakeLineBytes = 8192;
        private const int MaxHandshakeHeaders = 100;
        private const int CloseDrainTimeoutMs = 250;
        private const int SendLoopCloseTimeoutMs = 1000;

        /// <summary>TCP listener bound to the server address and port.</summary>
        private TcpListener _listener;
        /// <summary>Cancellation token source to stop accept/receive loops.</summary>
        private CancellationTokenSource _cts;
        /// <summary>Active WebSocket connections keyed by client ID.</summary>
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        /// <summary>Shared managed WebSocket options for queue capacity and token gate.</summary>
        private readonly ManagedWebSocketOptions _options;
        /// <summary>Logger instance for diagnostic output.</summary>
        private readonly IFoxgloveLogger _logger;
        /// <summary>Monotonically increasing counter for assigning client IDs.</summary>
        private long _nextClientId;
        /// <summary>Allowed browser origins for Cross-Site WebSocket Hijacking protection. Empty collection rejects all browser-origin clients.</summary>
        private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);

        // Aggregate health counters
        private long _totalAcceptedClients;
        private long _totalDisconnectedClients;
        private long _totalControlOverflowDisconnects;
        private long _totalDroppedDataFrames;

        public ManagedWsBackend(IFoxgloveLogger logger = null)
            : this(new ManagedWebSocketOptions(), logger) { }

        public ManagedWsBackend(ManagedWebSocketOptions options, IFoxgloveLogger logger = null)
        {
            _options = options ?? new ManagedWebSocketOptions();
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
        public virtual void Start(string host, int port)
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
        public virtual void Stop()
        {
            var cts = _cts;
            _cts = null;
            cts?.Cancel();
            foreach (var (id, conn) in _clients.ToArray())
                DisconnectClient(id, conn);

            try { _listener?.Stop(); } catch { }
            _listener = null;
            cts?.Dispose();
        }

        /// <summary>Send a UTF-8 text frame to a specific client.</summary>
        public void SendText(uint clientId, string json)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            HandleEnqueueResult(clientId, conn, conn.SendText(json, FramePriority.Control), "SendText");
        }

        /// <summary>Send a binary frame to a specific client.</summary>
        public void SendBinary(uint clientId, byte[] data)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            HandleEnqueueResult(clientId, conn, conn.SendBinary(data, FramePriority.Control), "SendBinary");
        }

        /// <summary>Send droppable live data to a specific client.</summary>
        public void SendDataBinary(uint clientId, byte[] data)
        {
            if (!_clients.TryGetValue(clientId, out var conn)) return;
            HandleEnqueueResult(clientId, conn, conn.SendBinary(data, FramePriority.Data), "SendDataBinary");
        }

        /// <summary>Send a UTF-8 text frame to every connected client.</summary>
        public void BroadcastText(string json)
        {
            foreach (var (id, conn) in _clients.ToArray())
                HandleEnqueueResult(id, conn, conn.SendText(json, FramePriority.Control), "BroadcastText");
        }

        /// <summary>Send a binary frame to every connected client.</summary>
        public void BroadcastBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients.ToArray())
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Control), "BroadcastBinary");
        }

        /// <summary>Send droppable live data binary frames to every connected client.</summary>
        public void BroadcastDataBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients.ToArray())
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Data), "BroadcastDataBinary");
        }

        /// <summary>Drop queued data frames for all clients while preserving protocol control frames.</summary>
        public void ClearDataQueues()
        {
            foreach (var (_, conn) in _clients.ToArray())
                conn.ClearDataFrames();
        }

        /// <summary>Stop the server and release the cancellation token source.</summary>
        public virtual void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
        }

        // Transport health

        /// <summary>Produce an immutable snapshot of current transport health.</summary>
        public TransportStatsSnapshot GetStatsSnapshot()
        {
            var clientList = new List<TransportClientStats>();
            long totalQueuedFrames = 0;
            long totalQueuedBytes = 0;
            var droppedDisconnected = Interlocked.Read(ref _totalDroppedDataFrames);

            foreach (var kv in _clients.ToArray())
            {
                var cs = kv.Value.GetClientStats(kv.Key);
                clientList.Add(cs);
                totalQueuedFrames += cs.QueuedFrames;
                totalQueuedBytes += cs.QueuedBytes;
            }

            var totalDropped = droppedDisconnected;
            foreach (var cs in clientList)
                totalDropped += cs.DroppedDataFrames;

            return new TransportStatsSnapshot
            {
                Supported = true,
                IsRunning = IsRunning,
                ActiveClientCount = clientList.Count,
                TotalAcceptedClients = Interlocked.Read(ref _totalAcceptedClients),
                TotalDisconnectedClients = Interlocked.Read(ref _totalDisconnectedClients),
                TotalDroppedDataFrames = totalDropped,
                ControlOverflowDisconnects = Interlocked.Read(ref _totalControlOverflowDisconnects),
                TotalQueuedFrames = totalQueuedFrames,
                TotalQueuedBytes = totalQueuedBytes,
                MaxQueuedFramesPerClient = _options.MaxQueuedFramesPerClient,
                MaxQueuedBytesPerClient = _options.MaxQueuedBytesPerClient,
                Clients = clientList.AsReadOnly()
            };
        }

        // Origin guard

        /// <summary>Snapshot of currently allowed browser origins. Empty means no browser clients are allowed.</summary>
        public IReadOnlyCollection<string> AllowedOrigins
        {
            get { lock (_allowedOrigins) return _allowedOrigins.ToList(); }
        }

        /// <summary>Add an origin to the allowlist (case-insensitive). Full page URLs are normalized to their browser Origin.</summary>
        public void AddAllowedOrigin(string origin)
        {
            var normalized = NormalizeAllowedOrigin(origin);
            if (string.IsNullOrEmpty(normalized)) return;
            lock (_allowedOrigins) _allowedOrigins.Add(normalized);
        }

        /// <summary>Remove all origins from the allowlist, blocking all browser clients.</summary>
        public void ClearAllowedOrigins()
        {
            lock (_allowedOrigins) _allowedOrigins.Clear();
        }

        internal static string NormalizeAllowedOrigin(string originOrUrl)
        {
            if (string.IsNullOrWhiteSpace(originOrUrl))
                return null;

            var value = originOrUrl.Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && !string.IsNullOrEmpty(uri.Scheme)
                && !string.IsNullOrEmpty(uri.Host))
            {
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            }

            return value.TrimEnd('/');
        }

        // Internal

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
            WsConnection conn = null;
            Stream stream = null;
            try
            {
                stream = CreateClientStream(tcpClient);
                ConfigureStreamTimeouts(stream, 5000, 5000);

                var (accepted, subprotocol) = Handshake(stream);
                if (!accepted)
                {
                    try { stream?.Close(); } catch { }
                    try { stream?.Dispose(); } catch { }
                    try { tcpClient.Close(); } catch { }
                    try { tcpClient.Dispose(); } catch { }
                    return;
                }

                if (stream.CanTimeout)
                {
                    stream.ReadTimeout = Timeout.Infinite;
                    stream.WriteTimeout = Timeout.Infinite;
                }

                var clientId = AllocateClientId();
                conn = new WsConnection(
                    tcpClient,
                    stream,
                    _options.MaxQueuedFramesPerClient,
                    _options.MaxQueuedBytesPerClient);
                _clients[clientId] = conn;
                conn.StartSendLoop(() => DisconnectClient(clientId, conn), ct);

                Interlocked.Increment(ref _totalAcceptedClients);
                OnClientConnected?.Invoke(clientId);

                ReceiveLoop(clientId, conn, ct);
            }
            catch (Exception ex)
            {
                if (conn == null)
                {
                    try { stream?.Close(); } catch { }
                    try { stream?.Dispose(); } catch { }
                    try { tcpClient.Close(); } catch { }
                    try { tcpClient.Dispose(); } catch { }
                }

                var detail = FormatExceptionChain(ex);
                if (conn == null && IsPreWebSocketHandshakeClientFailure(ex))
                    _logger.LogWarning($"Client disconnected during TLS/WebSocket handshake: {detail}");
                else
                    _logger.LogError($"Client handler error: {detail}");
            }
        }

        /// <summary>Create the stream used by the WebSocket core. Secure backends override this to return SslStream.</summary>
        protected virtual Stream CreateClientStream(TcpClient tcpClient)
        {
            return tcpClient.GetStream();
        }

        private static bool IsPreWebSocketHandshakeClientFailure(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is AuthenticationException || ex is IOException || ex is SocketException)
                return true;

            if (ex is AggregateException aggregate)
                return aggregate.InnerExceptions.Any(IsPreWebSocketHandshakeClientFailure);

            return IsPreWebSocketHandshakeClientFailure(ex.InnerException);
        }

        private static string FormatExceptionChain(Exception ex)
        {
            if (ex == null)
                return string.Empty;

            var sb = new StringBuilder();
            var current = ex;
            while (current != null)
            {
                if (sb.Length > 0)
                    sb.Append(" Inner: ");
                sb.Append(current.GetType().Name);
                sb.Append(": ");
                sb.Append(current.Message);
                current = current.InnerException;
            }

            return sb.ToString();
        }

        private static void ConfigureStreamTimeouts(Stream stream, int readTimeout, int writeTimeout)
        {
            if (stream == null || !stream.CanTimeout)
                return;

            stream.ReadTimeout = readTimeout;
            stream.WriteTimeout = writeTimeout;
        }

        private uint AllocateClientId()
        {
            while (true)
            {
                var next = Interlocked.Increment(ref _nextClientId);
                if (next <= 0 || next > uint.MaxValue)
                    throw new InvalidOperationException("WebSocket client id space exhausted.");

                var clientId = (uint)next;
                if (clientId != 0 && !_clients.ContainsKey(clientId))
                    return clientId;
            }
        }

        // WebSocket handshake (RFC 6455 section 4)

        /// <summary>Parse HTTP upgrade request and complete the WebSocket opening handshake per RFC 6455.</summary>
        private (bool accepted, string subprotocol) Handshake(Stream stream)
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

            // Origin guard: reject browser clients unless the origin is in the allowlist.
            // file:// origins come from non-browser environments (Electron desktop apps,
            // Foxglove Desktop); they are not subject to CSWSH, so they are always allowed.
            if (headers.TryGetValue("Origin", out var origin) && !string.IsNullOrEmpty(origin)
                && !origin.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                bool allowed;
                lock (_allowedOrigins) allowed = _allowedOrigins.Contains(origin);
                if (!allowed)
                {
                    _logger.LogWarning(
                        $"Rejected WebSocket Origin '{origin}'. Add it to allowed origins to permit browser clients.");
                    var forbid = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\n\r\n");
                    stream.Write(forbid, 0, forbid.Length);
                    return (false, null);
                }
            }

            if (_options.RequireToken)
            {
                var token = ManagedWebSocketOptions.GetQueryParameter(requestTarget, "token");
                if (!_options.IsTokenAccepted(token))
                {
                    _logger.LogWarning(token == null
                        ? "Rejected WebSocket client with missing token."
                        : "Rejected WebSocket client with invalid token.");
                    var unauthorized = Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\n\r\n");
                    stream.Write(unauthorized, 0, unauthorized.Length);
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

        /// <summary>Compute the Sec-WebSocket-Accept response value per RFC 6455 section 4.2.2.</summary>
        private static string ComputeAcceptKey(string wsKey)
        {
            const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(wsKey + magic));
            return Convert.ToBase64String(hash);
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

        // Receive loop

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
                        conn.TouchActivity();
                        HandleEnqueueResult(clientId, conn, conn.SendClose(), "SendClose");
                        conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
                        return;
                    }

                    conn.TouchActivity();
                    switch (frame.Opcode)
                    {
                        case WsOpcode.Text:
                            OnTextReceived?.Invoke(clientId, Encoding.UTF8.GetString(frame.Payload));
                            break;
                        case WsOpcode.Binary:
                            OnBinaryReceived?.Invoke(clientId, frame.Payload);
                            break;
                        case WsOpcode.Close:
                            HandleEnqueueResult(clientId, conn, conn.SendClose(), "SendClose");
                            conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
                            return;
                        case WsOpcode.Ping:
                            HandleEnqueueResult(clientId, conn, conn.SendPong(frame.Payload), "SendPong");
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
            Interlocked.Add(ref _totalDroppedDataFrames, conn.DroppedDataFrames);
            Interlocked.Increment(ref _totalDisconnectedClients);
            OnClientDisconnected?.Invoke(clientId);
            try { conn.Dispose(); } catch { }
        }

        private void HandleEnqueueResult(uint clientId, WsConnection conn, EnqueueResult result, string operation)
        {
            if (result.ShouldLogDataDrop)
            {
                _logger.LogWarning(
                    $"Client {clientId} send queue dropped {result.DroppedDataFrames} stale data frame(s); total dropped={result.TotalDroppedDataFrames}.");
            }

            if (result.ShouldDisconnect)
            {
                Interlocked.Increment(ref _totalControlOverflowDisconnects);
                _logger.LogWarning($"Client {clientId} send queue overflowed on control frame during {operation}; disconnecting.");
                DisconnectClient(clientId, conn);
            }
        }

        internal static int WriteFrameHeader(byte opcode, int payloadLength, Span<byte> destination)
        {
            return WsFrameCodec.WriteFrameHeader(opcode, payloadLength, destination);
        }

        // WebSocket connection

        private sealed class WsConnection : IDisposable
        {
            /// <summary>Underlying TCP client owned by this connection after handshake.</summary>
            private readonly TcpClient _tcpClient;
            /// <summary>Underlying plain or TLS stream.</summary>
            private readonly Stream _stream;
            private readonly WsSendQueue _sendQueue;
            private CancellationTokenSource _sendCts;
            private Task _sendTask;
            private int _disposed;

            /// <summary>RFC 6455 opcode for text frames.</summary>
            private const byte OpText = 0x1;
            /// <summary>RFC 6455 opcode for binary frames.</summary>
            private const byte OpBinary = 0x2;
            /// <summary>RFC 6455 opcode for close frames.</summary>
            private const byte OpClose = 0x8;
            /// <summary>RFC 6455 opcode for pong frames.</summary>
            private const byte OpPong = 0xA;

            // Health counters
            private readonly DateTime _connectedAtUtc = DateTime.UtcNow;
            private long _lastActivityMs;
            private long _sentFrames;
            private long _sentBytes;

            /// <summary>Create a connection on the given network stream.</summary>
            public WsConnection(TcpClient tcpClient, Stream stream, int maxQueuedFrames, int maxQueuedBytes)
            {
                _tcpClient = tcpClient;
                _stream = stream;
                _sendQueue = new WsSendQueue(maxQueuedFrames, maxQueuedBytes);
                _lastActivityMs = MonotonicMilliseconds();
            }

            public long DroppedDataFrames => _sendQueue.DroppedDataFramesSnapshot;

            public TransportClientStats GetClientStats(uint clientId)
            {
                var snap = _sendQueue.GetSnapshot();
                var nowTicks = DateTime.UtcNow.Ticks;
                var nowMs = MonotonicMilliseconds();
                return new TransportClientStats
                {
                    ClientId = clientId,
                    ConnectedAtUtc = _connectedAtUtc,
                    ConnectedDurationMs = (long)(new DateTime(nowTicks) - _connectedAtUtc).TotalMilliseconds,
                    LastActivityAgeMs = nowMs - Interlocked.Read(ref _lastActivityMs),
                    QueuedFrames = snap.QueuedFrames,
                    QueuedControlFrames = snap.QueuedControlFrames,
                    QueuedDataFrames = snap.QueuedDataFrames,
                    QueuedBytes = snap.QueuedBytes,
                    DroppedDataFrames = snap.DroppedDataFrames,
                    SentFrames = Interlocked.Read(ref _sentFrames),
                    SentBytes = Interlocked.Read(ref _sentBytes)
                };
            }

            internal void TouchActivity() => Interlocked.Exchange(ref _lastActivityMs, MonotonicMilliseconds());

            private static long MonotonicMilliseconds()
            {
                var ticks = Stopwatch.GetTimestamp();
                var frequency = Stopwatch.Frequency;
                return (ticks / frequency) * 1000 + (ticks % frequency) * 1000 / frequency;
            }

            public void StartSendLoop(Action onSendFailed, CancellationToken parentToken)
            {
                if (_sendTask != null) return;

                _sendCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
                var token = _sendCts.Token;
                _sendTask = Task.Run(() => SendLoop(onSendFailed, token), token);
            }

            /// <summary>Encode the string as UTF-8 and send it in a text frame.</summary>
            public EnqueueResult SendText(string json, FramePriority priority)
            {
                var payload = Encoding.UTF8.GetBytes(json);
                return _sendQueue.Enqueue(new QueuedFrame(OpText, payload, priority));
            }

            /// <summary>Send raw bytes in a binary frame.</summary>
            public EnqueueResult SendBinary(byte[] data, FramePriority priority)
            {
                return _sendQueue.Enqueue(new QueuedFrame(OpBinary, data, priority));
            }

            public int ClearDataFrames()
            {
                return _sendQueue.ClearDataFrames();
            }

            /// <summary>Send a close frame with an empty payload to initiate graceful shutdown.</summary>
            public EnqueueResult SendClose()
            {
                return _sendQueue.Enqueue(new QueuedFrame(OpClose, Array.Empty<byte>(), FramePriority.Control));
            }

            /// <summary>Echo back a pong frame with the given payload in response to a ping.</summary>
            public EnqueueResult SendPong(byte[] data)
            {
                return _sendQueue.Enqueue(new QueuedFrame(OpPong, data, FramePriority.Control));
            }

            public bool WaitForPendingSends(TimeSpan timeout)
            {
                return _sendQueue.WaitUntilEmpty(timeout);
            }

            public bool WaitForSendLoop(TimeSpan timeout)
            {
                var task = _sendTask;
                if (task == null)
                    return true;

                try
                {
                    return task.Wait(timeout);
                }
                catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
                {
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }

            private void SendLoop(Action onSendFailed, CancellationToken ct)
            {
                try
                {
                    while (_sendQueue.WaitToDequeue(ct, out var frame))
                        WriteFrame(frame.Opcode, frame.Payload);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) when (ct.IsCancellationRequested) { }
                catch (IOException)
                {
                    onSendFailed?.Invoke();
                }
                catch
                {
                    onSendFailed?.Invoke();
                }
            }

            /// <summary>Build and write a complete WebSocket frame (FIN + opcode + length-prefixed payload).</summary>
            private void WriteFrame(byte opcode, byte[] payload)
            {
                WsFrameCodec.WriteFrame(_stream, opcode, payload);
                Interlocked.Increment(ref _sentFrames);
                Interlocked.Add(ref _sentBytes, payload.Length);
                TouchActivity();
            }

            /// <summary>
            /// Read and unmask a complete WebSocket frame from the stream.
            /// Returns <c>null</c> on stream closure, oversized payload, or protocol error.
            /// </summary>
            public WsFrame ReadFrame()
            {
                return WsFrameCodec.TryReadFrame(_stream, out var frame) ? frame : null;
            }

            /// <summary>Close and dispose the underlying network stream.</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                _sendQueue.Complete();
                if (!WaitForSendLoop(TimeSpan.FromMilliseconds(SendLoopCloseTimeoutMs)))
                {
                    try { _sendCts?.Cancel(); } catch { }
                    WaitForSendLoop(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
                }
                try { _stream.Close(); } catch { }
                try { _stream.Dispose(); } catch { }
                try { _tcpClient?.Close(); } catch { }
                try { _tcpClient?.Dispose(); } catch { }
                try { _sendCts?.Dispose(); } catch { }
                _sendCts = null;
            }
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
