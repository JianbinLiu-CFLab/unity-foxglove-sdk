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
    public class ManagedWsBackend : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IClientDataQueueResettableFoxgloveTransport, IFoxgloveTransportStatsProvider, IOriginGuardedFoxgloveTransport, IDisposable
    {
        private const int CloseDrainTimeoutMs = 250;
        private const int StopAcceptLoopWaitMs = 500;
        private const int StopDisconnectWaitMs = 2000;
        private const int StopForcedCloseWaitMs = 1000;
        private const int StopPendingHandshakeWaitMs = 1000;
        private const int MaxFragmentedMessageBytes = 4 * 1024 * 1024;
        private const int MaxFragmentedMessageFrames = ManagedWebSocketOptions.DefaultMaxQueuedFrames;
        private const ushort ProtocolErrorCloseCode = 1002;
        private const ushort InvalidPayloadDataCloseCode = 1007;
        private const ushort MessageTooBigCloseCode = 1009;

        /// <summary>TCP listener bound to the server address and port.</summary>
        private TcpListener _listener;
        /// <summary>Cancellation token source to stop accept/receive loops.</summary>
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;
        private int _stopping;
        /// <summary>Active WebSocket connections keyed by client ID.</summary>
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        /// <summary>
        /// TCP clients which have been accepted but have not completed the
        /// TLS/HTTP handshake.  The completion source lets Stop wait for the
        /// handler after closing the socket, while the dictionary itself is
        /// the bounded admission reservation.
        /// </summary>
        private readonly ConcurrentDictionary<TcpClient, TaskCompletionSource<bool>> _pendingClients =
            new ConcurrentDictionary<TcpClient, TaskCompletionSource<bool>>();
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
        public bool IsRunning => Volatile.Read(ref _listener) != null;

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
            if (Volatile.Read(ref _listener) != null)
                throw new InvalidOperationException("Server already started");

            var addr = TransportHostResolver.ResolveBindAddress(host);
            var listener = new TcpListener(addr, port);
            CancellationTokenSource cts = null;
            try
            {
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                cts = new CancellationTokenSource();
                listener.Start();
                Interlocked.Exchange(ref _stopping, 0);

                // Publish the fully started resources only after every
                // fallible bind step succeeds.  A failed Start therefore
                // leaves a clean, retryable state for the next attempt.
                Volatile.Write(ref _listener, listener);
                Volatile.Write(ref _cts, cts);
                var acceptTask = Task.Run(() => AcceptLoop(cts.Token));
                Volatile.Write(ref _acceptLoopTask, acceptTask);
            }
            catch
            {
                try { listener.Stop(); } catch { }
                try { cts?.Dispose(); } catch { }
                Volatile.Write(ref _listener, null);
                Volatile.Write(ref _cts, null);
                Volatile.Write(ref _acceptLoopTask, null);
                throw;
            }
        }

        /// <summary>Cancel listener, disconnect all clients, and stop accepting new connections.</summary>
        public virtual void Stop()
        {
            Interlocked.Exchange(ref _stopping, 1);
            var cts = _cts;
            _cts = null;
            cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;

            try
            {
                var acceptLoopTask = _acceptLoopTask;
                _acceptLoopTask = null;
                WaitForShutdownTask(acceptLoopTask, StopAcceptLoopWaitMs, "accept loop");

                var pending = _pendingClients.ToArray();
                foreach (var pair in pending)
                {
                    try { pair.Key.Close(); } catch { }
                    try { pair.Key.Dispose(); } catch { }
                }
                if (pending.Length > 0)
                {
                    try
                    {
                        var pendingTasks = pending.Select(pair => pair.Value.Task).ToArray();
                        if (!Task.WaitAll(pendingTasks, StopPendingHandshakeWaitMs))
                            _logger.LogWarning($"{pending.Length} pending WebSocket handshake(s) did not finish within {StopPendingHandshakeWaitMs}ms during stop.");
                    }
                    catch (AggregateException ex)
                    {
                        _logger.LogError($"Pending WebSocket handshake stop error: {FormatExceptionChain(ex)}");
                    }
                }

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
                            "Client disconnect callbacks are still running after forced stop; continuing shutdown.");
                    }
                }
            }
            finally
            {
                cts?.Dispose();
            }
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
            var payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            foreach (var (id, conn) in _clients)
                HandleEnqueueResult(id, conn, conn.SendTextEncoded(payload, FramePriority.Control), "BroadcastText");
        }

        /// <summary>Send a binary frame to every connected client.</summary>
        public void BroadcastBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients)
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Control), "BroadcastBinary");
        }

        /// <summary>Send droppable live data binary frames to every connected client.</summary>
        public void BroadcastDataBinary(byte[] data)
        {
            foreach (var (id, conn) in _clients)
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Data), "BroadcastDataBinary");
        }

        /// <summary>Drop queued data frames for all clients while preserving protocol control frames.</summary>
        public void ClearDataQueues()
        {
            foreach (var (_, conn) in _clients)
                conn.ClearDataFrames();
        }

        /// <summary>
        /// Drop queued data frames for one connected client while preserving
        /// protocol control frames. The clear is client-scoped, not
        /// channel-scoped, because queued data frames do not retain a channel
        /// index.
        /// </summary>
        public void ClearDataQueue(uint clientId)
        {
            if (_clients.TryGetValue(clientId, out var conn))
                conn.ClearDataFrames();
        }

        /// <summary>Stop the server and release the cancellation token source.</summary>
        public virtual void Dispose()
        {
            Stop();
        }

        // Transport health

        /// <summary>
        /// Produce an immutable snapshot of current transport health.
        /// Drop totals are best-effort under concurrent disconnects: a client
        /// can move from the active set to the retained aggregate during the snapshot.
        /// </summary>
        public TransportStatsSnapshot GetStatsSnapshot()
        {
            var clientList = new List<TransportClientStats>();
            long totalQueuedFrames = 0;
            long totalQueuedBytes = 0;
            long activeDropped = 0;

            foreach (var kv in _clients)
            {
                var cs = kv.Value.GetClientStats(kv.Key);
                clientList.Add(cs);
                totalQueuedFrames += cs.QueuedFrames;
                totalQueuedBytes += cs.QueuedBytes;
                activeDropped += cs.DroppedDataFrames;
            }

            var totalDropped = Interlocked.Read(ref _totalDroppedDataFrames) + activeDropped;

            return new TransportStatsSnapshot
            {
                Supported = true,
                IsRunning = IsRunning,
                ActiveClientCount = clientList.Count,
                PendingClientCount = _pendingClients.Count,
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
            get { lock (_allowedOriginsLock) return _allowedOrigins.ToArray(); }
        }

        /// <summary>
        /// Add an origin to the allowlist (case-insensitive). Full page URLs are
        /// normalized to their browser Origin; local file origins are accepted by
        /// the handshake guard without adding them here.
        /// </summary>
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
                    var listener = _listener;
                    if (listener == null)
                        break;

                    var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (ct.IsCancellationRequested || IsStopping)
                    {
                        try { tcpClient.Dispose(); } catch { }
                        break;
                    }

                    if (!TryReservePendingClient(tcpClient))
                    {
                        RejectPendingClient(tcpClient);
                        continue;
                    }

                    try
                    {
                        _ = Task.Run(() => HandleClient(tcpClient, ct));
                    }
                    catch
                    {
                        ReleasePendingClient(tcpClient);
                        CloseUnregisteredClient(tcpClient, null);
                        throw;
                    }
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
                catch (NullReferenceException) when (ct.IsCancellationRequested || IsStopping) { break; }
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
                if (ct.IsCancellationRequested || IsStopping)
                {
                    CloseUnregisteredClient(tcpClient, stream);
                    stream = null;
                    return;
                }

                var (accepted, _) = _handshakeHandler.Handshake(stream, HasClientCapacityForHandshake);
                if (!accepted)
                {
                    try { stream?.Close(); } catch { }
                    try { stream?.Dispose(); } catch { }
                    try { tcpClient.Close(); } catch { }
                    try { tcpClient.Dispose(); } catch { }
                    return;
                }

                if (ct.IsCancellationRequested || IsStopping)
                {
                    CloseUnregisteredClient(tcpClient, stream);
                    stream = null;
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
                if (!TryRegisterClient(conn, out clientId, out var stopped))
                {
                    if (stopped)
                        CloseUnannouncedClient(conn);
                    else
                        RejectClientAtCapacity(conn);
                    conn = null;
                    stream = null;
                    return;
                }

                if (ct.IsCancellationRequested || IsStopping)
                {
                    RemoveUnannouncedClient(clientId, conn);
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
            finally
            {
                ReleasePendingClient(tcpClient);
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
                // The current handler owns a pending reservation.  Count that
                // reservation for admission, but do not reject the handler
                // which already consumed it.
                if (!IsStopping
                    && (_clients.Count < maxClients
                        || (_pendingClients.Count > 0 && _clients.Count + _pendingClients.Count <= maxClients)))
                    return true;
            }

            Interlocked.Increment(ref _totalRejectedClients);
            _logger.LogWarning($"Rejected WebSocket client because active client limit {maxClients} is reached.");
            return false;
        }

        private bool TryReservePendingClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
                return false;

            lock (_clientAdmissionLock)
            {
                var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
                if (IsStopping || _clients.Count + _pendingClients.Count >= maxClients)
                    return false;

                return _pendingClients.TryAdd(
                    tcpClient,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }

        private void RejectPendingClient(TcpClient tcpClient)
        {
            Interlocked.Increment(ref _totalRejectedClients);
            _logger.LogWarning(
                $"Rejected WebSocket client because active and pending client limit {ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients)} is reached.");
            CloseUnregisteredClient(tcpClient, null);
        }

        private void ReleasePendingClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
                return;

            if (_pendingClients.TryRemove(tcpClient, out var completion))
                completion.TrySetResult(true);
        }

        private bool TryRegisterClient(WsConnection conn, out uint clientId, out bool stopped)
        {
            lock (_clientAdmissionLock)
            {
                stopped = IsStopping;
                if (stopped)
                {
                    clientId = 0;
                    return false;
                }

                var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
                if (_clients.Count >= maxClients)
                {
                    clientId = 0;
                    stopped = false;
                    return false;
                }

                clientId = AllocateClientId();
                _clients[clientId] = conn;
                stopped = false;
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

        private bool IsStopping => Volatile.Read(ref _stopping) != 0;

        private void WaitForShutdownTask(Task task, int timeoutMs, string description)
        {
            if (task == null)
                return;

            try
            {
                if (!task.Wait(timeoutMs))
                    _logger.LogWarning($"WebSocket {description} did not finish within {timeoutMs}ms during stop.");
            }
            catch (AggregateException ex)
            {
                var meaningful = ex.Flatten().InnerExceptions
                    .Where(item => item is not OperationCanceledException
                        && item is not ObjectDisposedException
                        && !(item is NullReferenceException && IsStopping))
                    .ToArray();
                if (meaningful.Length > 0)
                    _logger.LogError($"WebSocket {description} stop error: {FormatExceptionChain(meaningful[0])}");
            }
            catch (ObjectDisposedException) { }
        }

        private static void CloseUnregisteredClient(TcpClient tcpClient, Stream stream)
        {
            try { stream?.Close(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { tcpClient?.Close(); } catch { }
            try { tcpClient?.Dispose(); } catch { }
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
            var fragmentedPayload = new MemoryStream();
            var hasFragmentedPayload = false;
            byte fragmentedOpcode = 0;
            var fragmentedBytes = 0;
            var fragmentedFrames = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = conn.ReadFrame(out var readResult);
                    if (frame == null)
                    {
                        if (readResult == WsFrameReadResult.ProtocolError)
                            CloseProtocolError(clientId, conn, ProtocolErrorCloseCode);
                        else if (readResult == WsFrameReadResult.InvalidPayloadData)
                            CloseProtocolError(clientId, conn, InvalidPayloadDataCloseCode);
                        else if (readResult == WsFrameReadResult.MessageTooBig)
                            CloseProtocolError(clientId, conn, MessageTooBigCloseCode);
                        break;
                    }

                    conn.TouchActivity();
                    switch (frame.Opcode)
                    {
                        case WsOpcode.Text:
                        case WsOpcode.Binary:
                            if (hasFragmentedPayload)
                            {
                                CloseProtocolError(clientId, conn);
                                return;
                            }

                            if (frame.Fin)
                            {
                                if (frame.Opcode == WsOpcode.Text)
                                {
                                    if (!TryDispatchText(clientId, conn, frame.Payload))
                                        return;
                                }
                                else
                                    OnBinaryReceived?.Invoke(clientId, frame.Payload);
                                break;
                            }

                            fragmentedOpcode = frame.Opcode;
                            fragmentedPayload.SetLength(0);
                            hasFragmentedPayload = true;
                            fragmentedFrames = 1;
                            if (!TryAppendFragment(fragmentedPayload, frame.Payload, ref fragmentedBytes))
                            {
                                CloseProtocolError(clientId, conn, MessageTooBigCloseCode);
                                return;
                            }
                            break;

                        case WsOpcode.Continuation:
                            if (!hasFragmentedPayload)
                            {
                                CloseProtocolError(clientId, conn);
                                return;
                            }

                            fragmentedFrames++;
                            if (fragmentedFrames > MaxFragmentedMessageFrames)
                            {
                                CloseProtocolError(clientId, conn, MessageTooBigCloseCode);
                                return;
                            }

                            if (!TryAppendFragment(fragmentedPayload, frame.Payload, ref fragmentedBytes))
                            {
                                CloseProtocolError(clientId, conn, MessageTooBigCloseCode);
                                return;
                            }

                            if (frame.Fin)
                            {
                                var payload = fragmentedPayload.ToArray();
                                fragmentedPayload.SetLength(0);
                                hasFragmentedPayload = false;
                                fragmentedBytes = 0;
                                fragmentedFrames = 0;

                                if (fragmentedOpcode == WsOpcode.Text)
                                {
                                    if (!TryDispatchText(clientId, conn, payload))
                                        return;
                                }
                                else
                                    OnBinaryReceived?.Invoke(clientId, payload);
                                fragmentedOpcode = 0;
                            }
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
                fragmentedPayload.Dispose();
                DisconnectClient(clientId, conn);
            }
        }

        private static bool TryAppendFragment(MemoryStream stream, byte[] payload, ref int totalBytes)
        {
            var length = payload?.Length ?? 0;
            if (length > MaxFragmentedMessageBytes - totalBytes)
                return false;

            if (length > 0)
                stream.Write(payload, 0, length);
            totalBytes += length;
            return true;
        }

        private bool TryDispatchText(uint clientId, WsConnection conn, byte[] payload)
        {
            if (!WsFrameCodec.TryDecodeUtf8(payload, 0, payload?.Length ?? 0, out var text))
            {
                CloseProtocolError(clientId, conn, InvalidPayloadDataCloseCode);
                return false;
            }

            OnTextReceived?.Invoke(clientId, text);
            return true;
        }

        private void CloseProtocolError(
            uint clientId,
            WsConnection conn,
            ushort statusCode = ProtocolErrorCloseCode)
        {
            HandleEnqueueResult(clientId, conn, conn.SendClose(statusCode), "SendClose");
            conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
        }

        /// <summary>Remove the client from the dictionary, fire the disconnected event, and dispose the connection.</summary>
        private void DisconnectClient(uint clientId, WsConnection conn)
        {
            if (!TryRemoveClient(clientId, conn))
            {
                try { conn?.Dispose(); } catch { }
                return;
            }
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

        private void RemoveUnannouncedClient(uint clientId, WsConnection conn)
        {
            if (!TryRemoveClient(clientId, conn))
            {
                CloseUnannouncedClient(conn);
                return;
            }
            Interlocked.Add(ref _totalDroppedDataFrames, conn.DroppedDataFrames);
            CloseUnannouncedClient(conn);
        }

        private bool TryRemoveClient(uint clientId, WsConnection expectedConnection)
        {
            lock (_clientAdmissionLock)
            {
                if (!_clients.TryGetValue(clientId, out var currentConnection)
                    || !ReferenceEquals(currentConnection, expectedConnection))
                {
                    return false;
                }

                return _clients.TryRemove(clientId, out _);
            }
        }

        private static void CloseUnannouncedClient(WsConnection conn)
        {
            try { conn?.Dispose(); } catch { }
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
