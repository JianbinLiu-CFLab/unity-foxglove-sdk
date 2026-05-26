// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Pure C# WebSocket server backend using TcpListener and manual
// RFC 6455 framing. No http.sys dependency - works on all platforms
// without admin rights.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Pure C# WebSocket server backend using TcpListener + manual WebSocket protocol.
    /// No http.sys dependency; works on all platforms without admin rights.
    /// </summary>
    public class ManagedWsBackend : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IFoxgloveTransportStatsProvider, IOriginGuardedFoxgloveTransport, IDisposable
    {
        private const int CloseDrainTimeoutMs = 250;
        private const int StopDisconnectWaitMs = 2000;
        private const int StopForcedCloseWaitMs = 1000;

        /// <summary>TCP listener bound to the server address and port.</summary>
        private TcpListener _listener;
        /// <summary>Cancellation token source to stop accept/receive loops.</summary>
        private CancellationTokenSource _cts;
        /// <summary>Active WebSocket connections keyed by client ID.</summary>
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        /// <summary>Shared managed WebSocket options for queue capacity and token gate.</summary>
        private readonly ManagedWebSocketOptions _options;
        private readonly WsHandshakeHandler _handshakeHandler;
        /// <summary>Logger instance for diagnostic output.</summary>
        private readonly IFoxgloveLogger _logger;
        /// <summary>Monotonically increasing counter for assigning client IDs.</summary>
        private long _nextClientId;
        /// <summary>Allowed browser origins for Cross-Site WebSocket Hijacking protection. Empty collection rejects all browser-origin clients.</summary>
        private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _allowedOriginsLock = new object();
        private readonly object _clientAdmissionLock = new object();

        // Aggregate health counters
        private long _totalAcceptedClients;
        private long _totalRejectedClients;
        private long _totalDisconnectedClients;
        private long _totalControlOverflowDisconnects;
        private long _totalDroppedDataFrames;

        public ManagedWsBackend(IFoxgloveLogger logger = null)
            : this(new ManagedWebSocketOptions(), logger) { }

        public ManagedWsBackend(ManagedWebSocketOptions options, IFoxgloveLogger logger = null)
        {
            _options = options ?? new ManagedWebSocketOptions();
            _logger = logger ?? new ConsoleLogger();
            _handshakeHandler = new WsHandshakeHandler(_options, _allowedOrigins, _allowedOriginsLock, _logger);
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

            var addr = TransportHostResolver.ResolveBindAddress(host);

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
            try { _listener?.Stop(); } catch { }
            _listener = null;

            var clients = _clients.ToArray();
            var disconnects = clients
                .Select(pair => Task.Run(() => DisconnectClient(pair.Key, pair.Value)))
                .ToArray();
            if (disconnects.Length > 0)
            {
                bool completed = false;
                try { completed = Task.WaitAll(disconnects, StopDisconnectWaitMs); }
                catch (AggregateException ex) { _logger.LogError($"Client disconnect error during stop: {FormatExceptionChain(ex)}"); }

                if (!completed)
                {
                    _logger.LogWarning(
                        $"Client disconnect did not complete within {StopDisconnectWaitMs}ms during stop; forcing network close for remaining clients.");
                    foreach (var pair in clients)
                    {
                        try { pair.Value.Dispose(); } catch { }
                    }

                    try { completed = Task.WaitAll(disconnects, StopForcedCloseWaitMs); }
                    catch (AggregateException ex) { _logger.LogError($"Client disconnect error during forced stop: {FormatExceptionChain(ex)}"); }
                }

                if (!completed)
                {
                    _logger.LogWarning(
                        "Client disconnect callbacks are still running after forced stop; deferring cancellation token disposal.");
                    return;
                }
            }

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
                TotalRejectedClients = Interlocked.Read(ref _totalRejectedClients),
                TotalDisconnectedClients = Interlocked.Read(ref _totalDisconnectedClients),
                TotalDroppedDataFrames = totalDropped,
                ControlOverflowDisconnects = Interlocked.Read(ref _totalControlOverflowDisconnects),
                TotalQueuedFrames = totalQueuedFrames,
                TotalQueuedBytes = totalQueuedBytes,
                MaxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients),
                MaxQueuedFramesPerClient = ManagedWebSocketOptions.NormalizeMaxQueuedFrames(_options.MaxQueuedFramesPerClient),
                MaxQueuedBytesPerClient = ManagedWebSocketOptions.NormalizeMaxQueuedBytes(_options.MaxQueuedBytesPerClient),
                Clients = clientList.AsReadOnly()
            };
        }

        // Origin guard

        /// <summary>Snapshot of currently allowed browser origins. Empty means no browser clients are allowed.</summary>
        public IReadOnlyCollection<string> AllowedOrigins
        {
            get { lock (_allowedOriginsLock) return _allowedOrigins.ToList(); }
        }

        /// <summary>Add an origin to the allowlist (case-insensitive). Full page URLs are normalized to their browser Origin.</summary>
        public void AddAllowedOrigin(string origin)
        {
            var normalized = NormalizeAllowedOrigin(origin);
            if (string.IsNullOrEmpty(normalized)) return;
            lock (_allowedOriginsLock) _allowedOrigins.Add(normalized);
        }

        /// <summary>Remove all origins from the allowlist, blocking all browser clients.</summary>
        public void ClearAllowedOrigins()
        {
            lock (_allowedOriginsLock) _allowedOrigins.Clear();
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
            var clientId = 0u;
            var registeredClient = false;
            try
            {
                stream = CreateClientStream(tcpClient);
                ConfigureStreamTimeouts(stream, 5000, 5000);

                var (accepted, _) = _handshakeHandler.Handshake(stream, HasClientCapacityForHandshake);
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

                conn = new WsConnection(
                    tcpClient,
                    stream,
                    _options.MaxQueuedFramesPerClient,
                    _options.MaxQueuedBytesPerClient);
                if (!TryRegisterClient(conn, out clientId))
                {
                    RejectClientAtCapacity(conn);
                    conn = null;
                    stream = null;
                    return;
                }

                registeredClient = true;
                conn.StartSendLoop(() => DisconnectClient(clientId, conn), ct);

                Interlocked.Increment(ref _totalAcceptedClients);
                OnClientConnected?.Invoke(clientId);

                ReceiveLoop(clientId, conn, ct);
            }
            catch (Exception ex)
            {
                if (registeredClient && conn != null)
                {
                    try { DisconnectClient(clientId, conn); } catch { }
                }
                else
                {
                    try { stream?.Close(); } catch { }
                    try { stream?.Dispose(); } catch { }
                    try { tcpClient.Close(); } catch { }
                    try { tcpClient.Dispose(); } catch { }
                }

                var detail = FormatExceptionChain(ex);
                if (conn == null && IsPreWebSocketHandshakeClientFailure(ex))
                {
                    if (_options.LogPreHandshakeClientDisconnects)
                        _logger.LogWarning($"Client disconnected during TLS/WebSocket handshake: {detail}");
                }
                else
                {
                    _logger.LogError($"Client handler error: {detail}");
                }
            }
        }

        /// <summary>Create the stream used by the WebSocket core. Secure backends override this to return SslStream.</summary>
        protected virtual Stream CreateClientStream(TcpClient tcpClient)
        {
            return tcpClient.GetStream();
        }

        private bool HasClientCapacityForHandshake()
        {
            var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
            lock (_clientAdmissionLock)
            {
                if (_clients.Count < maxClients)
                    return true;
            }

            Interlocked.Increment(ref _totalRejectedClients);
            _logger.LogWarning($"Rejected WebSocket client because active client limit {maxClients} is reached.");
            return false;
        }

        private bool TryRegisterClient(WsConnection conn, out uint clientId)
        {
            lock (_clientAdmissionLock)
            {
                var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
                if (_clients.Count >= maxClients)
                {
                    clientId = 0;
                    return false;
                }

                clientId = AllocateClientId();
                _clients[clientId] = conn;
                return true;
            }
        }

        private void RejectClientAtCapacity(WsConnection conn)
        {
            Interlocked.Increment(ref _totalRejectedClients);
            _logger.LogWarning(
                $"Rejected WebSocket client because active client limit {ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients)} is reached.");
            try { conn?.Dispose(); } catch { }
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
            try
            {
                OnClientDisconnected?.Invoke(clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Client disconnected handler error: {FormatExceptionChain(ex)}");
            }
            finally
            {
                try { conn.Dispose(); } catch { }
            }
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

    }
}
