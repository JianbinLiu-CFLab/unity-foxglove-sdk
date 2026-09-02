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
        private const int HandshakeTimeoutMs = 5000;
        private const int MaxQueuedCapacityResponses = 64;
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
        // Serializes listener publication/claiming with Stop so a concurrent
        // Start cannot resurrect a listener while the previous shutdown still
        // owns its sockets and callback continuations.
        private readonly object _lifecycleLock = new object();
        private int _stopInProgress;
        private readonly Dictionary<uint, ClientPublication> _clientPublications =
            new Dictionary<uint, ClientPublication>();
        private int _queuedCapacityResponses;

        private sealed class ClientPublication
        {
            // Serializes the cancellation check with entering user callback
            // code.  Stop may retire an unstarted publication, but once this
            // gate marks CallbackStarted the callback is logically in flight
            // and disconnect is deferred until it returns.
            internal readonly object CallbackGate = new object();
            internal bool Announced;
            internal bool CallbackStarted;
            internal bool CallbackCompleted;
            internal int CallbackThreadId;
            internal bool Cancelled;
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

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

        /// <summary>
        /// Whether a capacity rejection can safely write a plaintext HTTP
        /// response before the transport-specific handshake has been created.
        /// TLS transports override this and close the socket instead.
        /// </summary>
        protected virtual bool SupportsPlaintextCapacityResponse => true;

        /// <summary>
        /// Releases resources published by a derived transport after the base
        /// stop has drained its listener, clients, and handshake continuations.
        /// The callback runs while the lifecycle gate is held and before a new
        /// Start is allowed to claim the transport.
        /// </summary>
        protected virtual void OnStopCompletedUnderLifecycleLock() { }

        /// <summary>
        /// Shared lifecycle gate for derived transports that publish additional
        /// resources (for example the WSS certificate) around Start/Stop.
        /// </summary>
        protected object LifecycleLock => _lifecycleLock;

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
            lock (_lifecycleLock)
            {
                if (Volatile.Read(ref _stopInProgress) != 0)
                    throw new InvalidOperationException("Server stop is still in progress.");
                if (Volatile.Read(ref _listener) != null)
                    throw new InvalidOperationException("Server already started");
                lock (_clientAdmissionLock)
                {
                    if (_clients.Count != 0
                        || _pendingClients.Count != 0
                        || _clientPublications.Count != 0)
                        throw new InvalidOperationException(
                            "Server shutdown is still retaining client resources.");
                }

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
                    // Start the loop against the local listener before publishing
                    // the running marker.  This keeps the task independent from a
                    // partially published field set and makes IsRunning true only
                    // after all shutdown handles are visible.
                    var acceptTask = Task.Run(() => AcceptLoop(listener, cts.Token));
                    Volatile.Write(ref _cts, cts);
                    Volatile.Write(ref _acceptLoopTask, acceptTask);
                    Volatile.Write(ref _listener, listener);
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
        }

        /// <summary>Cancel listener, disconnect all clients, and stop accepting new connections.</summary>
        public virtual void Stop()
        {
            CancellationTokenSource cts = null;
            Task acceptLoopTask = null;
            TcpListener listener = null;
            Task[] publicationTasks = null;
            KeyValuePair<uint, WsConnection>[] clients = null;

            // Claim the lifecycle handles under the same gate used by Start,
            // then release that gate before running user callbacks or waiting
            // for handler tasks.  Start therefore cannot publish a new
            // listener while this stop owns the previous generation, while a
            // callback is still free to call transport APIs without deadlocking
            // the shutdown waiter.
            lock (_lifecycleLock)
            {
                if (Interlocked.Exchange(ref _stopInProgress, 1) != 0)
                    return;

                Interlocked.Exchange(ref _stopping, 1);
                lock (_clientAdmissionLock)
                {
                    // Claim every listener handle and snapshot clients while
                    // the admission gate is held. A handler can therefore
                    // either publish before this point or observe stopping
                    // and remove itself, but cannot publish into a snapshot
                    // that has already been disconnected.
                    cts = _cts;
                    _cts = null;
                    acceptLoopTask = _acceptLoopTask;
                    _acceptLoopTask = null;
                    listener = _listener;
                    _listener = null;
                    // Keep the source contract literal while taking the
                    // snapshot under the admission gate.
                    clients = _clients.ToArray();
                    publicationTasks = SnapshotPublicationTasks();
                }
            }

            try
            {
                try { cts?.Cancel(); } catch { }
                try { listener?.Stop(); } catch { }
                WaitForShutdownTask(acceptLoopTask, StopAcceptLoopWaitMs, "accept loop");

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

                WaitForPublicationTasks(publicationTasks);

                // Give established clients their bounded graceful-close
                // window before hard-closing sockets still in handshake.
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
            }
            finally
            {
                try { cts?.Dispose(); } catch { }
                lock (_lifecycleLock)
                {
                    try
                    {
                        OnStopCompletedUnderLifecycleLock();
                    }
                    catch (Exception ex)
                    {
                        // A derived release hook must not strand the base
                        // lifecycle gate. The listener and client ownership
                        // have already been retired; retain a diagnostic and
                        // always make a later Start eligible to retry.
                        _logger.LogError($"Derived transport stop cleanup failed: {FormatExceptionChain(ex)}");
                    }
                    finally
                    {
                        Volatile.Write(ref _stopInProgress, 0);
                    }
                }
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
        private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (ct.IsCancellationRequested || IsStopping)
                    {
                        try { tcpClient.Dispose(); } catch { }
                        break;
                    }

                    if (!TryReservePendingClient(tcpClient))
                    {
                        QueueRejectedClient(tcpClient);
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
            CancellationTokenSource handshakeCts = null;
            CancellationTokenRegistration handshakeRegistration = default;
            var clientId = 0u;
            var registeredClient = false;
            try
            {
                // Stream ReadTimeout is an inactivity limit.  Pair it with a
                // linked cancellation timer so a peer which drips one byte at
                // a time cannot hold an admission reservation forever.  The
                // callback closes both layers, which also interrupts a
                // synchronous SslStream authentication on secure backends.
                handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(HandshakeTimeoutMs);
                handshakeRegistration = handshakeCts.Token.Register(
                    () => CloseHandshakeResources(tcpClient, stream));

                stream = CreateClientStream(tcpClient, handshakeCts.Token);
                ConfigureStreamTimeouts(stream, HandshakeTimeoutMs, HandshakeTimeoutMs);
                handshakeCts.Token.ThrowIfCancellationRequested();
                if (ct.IsCancellationRequested || IsStopping)
                {
                    CloseUnregisteredClient(tcpClient, stream);
                    stream = null;
                    return;
                }

                var (accepted, _) = _handshakeHandler.Handshake(stream, HasClientCapacityForHandshake);
                handshakeCts.Token.ThrowIfCancellationRequested();
                handshakeRegistration.Dispose();
                handshakeRegistration = default;
                handshakeCts.Dispose();
                handshakeCts = null;
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

                // The active-client entry now owns this admission slot. Release
                // the handshake reservation before entering the connection's
                // long-lived receive loop so established clients are counted
                // exactly once.
                ReleasePendingClient(tcpClient);

                if (ct.IsCancellationRequested || !BeginClientPublication(clientId, conn, ct))
                {
                    RemoveUnannouncedClient(clientId, conn);
                    conn = null;
                    stream = null;
                    return;
                }

                registeredClient = true;
                try
                {
                    // BeginClientPublication has claimed the callback decision
                    // under the admission gate and starts the send loop only
                    // after user notification. Stop takes that same gate before
                    // snapshotting clients, so it cannot publish a canceled
                    // connection or resurrect one after shutdown.
                    ReceiveLoop(clientId, conn, ct);
                }
                finally
                {
                    CompleteClientPublication(clientId);
                }
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
                handshakeRegistration.Dispose();
                handshakeCts?.Dispose();
                ReleasePendingClient(tcpClient);
            }
        }

        /// <summary>Create the stream used by the WebSocket core. Secure backends override this to return SslStream.</summary>
        protected virtual Stream CreateClientStream(TcpClient tcpClient)
        {
            return tcpClient.GetStream();
        }

        /// <summary>Create the stream while a handshake cancellation deadline is active.</summary>
        protected virtual Stream CreateClientStream(TcpClient tcpClient, CancellationToken handshakeCancellation)
        {
            return CreateClientStream(tcpClient);
        }

        private bool HasClientCapacityForHandshake()
        {
            var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
            lock (_clientAdmissionLock)
            {
                // Every handler already owns one pending reservation.  The
                // active set is the only remaining capacity decision here;
                // TryRegisterClient repeats it under this same lock.
                if (!IsStopping && _clients.Count < maxClients)
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

        protected virtual void RejectPendingClient(TcpClient tcpClient)
        {
            Stream stream = null;
            try
            {
                if (SupportsPlaintextCapacityResponse)
                {
                    // This client was rejected at the bounded TCP reservation
                    // gate, before HandleClient can invoke the handshake
                    // handler. Bound the response write so a zero-window peer
                    // cannot hold the accept loop indefinitely.
                    stream = tcpClient?.GetStream();
                    ConfigureStreamTimeouts(stream, HandshakeTimeoutMs, HandshakeTimeoutMs);
                    if (stream != null)
                        WsHandshakeHandler.WriteCapacityResponse(stream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not send WebSocket capacity response: {FormatExceptionChain(ex)}");
            }
            finally
            {
                CloseUnregisteredClient(tcpClient, stream);
            }
        }

        private void QueueRejectedClient(TcpClient tcpClient)
        {
            // Record the admission decision on the accept-loop thread. The
            // response worker may be deliberately blocked by a slow peer, but
            // the rejection metric must still reflect that the connection was
            // refused and must not depend on worker scheduling.
            Interlocked.Increment(ref _totalRejectedClients);
            _logger.LogWarning(
                $"Rejected WebSocket client because active client limit {ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients)} is reached (including pending handshakes).");

            // Keep the asynchronous response backlog bounded as well. A peer
            // can hold a response worker until its write timeout, so an
            // unbounded Task.Run queue would otherwise become a second resource
            // exhaustion path under a rejection flood.
            if (Interlocked.Increment(ref _queuedCapacityResponses) > MaxQueuedCapacityResponses)
            {
                Interlocked.Decrement(ref _queuedCapacityResponses);
                CloseUnregisteredClient(tcpClient, null);
                return;
            }

            try
            {
                // Capacity responses perform network I/O. Run them away from
                // the accept loop so a slow peer cannot stop admission for
                // subsequent connections.
                _ = Task.Run(() => ProcessRejectedClient(tcpClient));
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _queuedCapacityResponses);
                _logger.LogWarning($"Could not schedule WebSocket capacity response: {FormatExceptionChain(ex)}");
                CloseUnregisteredClient(tcpClient, null);
            }
        }

        private void ProcessRejectedClient(TcpClient tcpClient)
        {
            try
            {
                RejectPendingClient(tcpClient);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"WebSocket capacity response worker failed: {FormatExceptionChain(ex)}");
                CloseUnregisteredClient(tcpClient, null);
            }
            finally
            {
                Interlocked.Decrement(ref _queuedCapacityResponses);
            }
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
                _clientPublications[clientId] = new ClientPublication();
                stopped = false;
                return true;
            }
        }

        /// <summary>
        /// Atomically claims the final admission-to-publication transition. Stop
        /// marks the same admission gate before it snapshots clients, so an
        /// unannounced connection is removed instead of publishing after stop.
        /// </summary>
        private bool BeginClientPublication(
            uint clientId,
            WsConnection expectedConnection,
            CancellationToken cancellationToken)
        {
            ClientPublication publication;
            var acceptedCounted = false;
            var disconnectAfterCallback = false;
            lock (_clientAdmissionLock)
            {
                if (IsStopping
                    || !_clients.TryGetValue(clientId, out var current)
                    || !ReferenceEquals(current, expectedConnection)
                    || !_clientPublications.TryGetValue(clientId, out publication)
                    || publication.Cancelled)
                    return false;

                // Claim the publication decision under the admission gate, but
                // invoke user code outside the gate. Announced is deliberately
                // set before the callback so a callback which has been handed to
                // user code has a matching disconnect event.
                publication.Announced = true;
                Interlocked.Increment(ref _totalAcceptedClients);
                acceptedCounted = true;
            }

            // The per-publication gate closes the narrow window between the
            // admission decision and entering user code.  Stop can cancel and
            // remove the publication while CallbackStarted is still false; in
            // that case no connect callback is emitted after cancellation.
            lock (publication.CallbackGate)
            {
                lock (_clientAdmissionLock)
                {
                    if (IsStopping
                        || publication.Cancelled
                        || !_clients.TryGetValue(clientId, out var current)
                        || !ReferenceEquals(current, expectedConnection))
                    {
                        if (acceptedCounted)
                        {
                            Interlocked.Decrement(ref _totalAcceptedClients);
                            acceptedCounted = false;
                        }
                        publication.Cancelled = true;
                        _clients.TryRemove(clientId, out _);
                        _clientPublications.Remove(clientId);
                        publication.Completion.TrySetResult(true);
                        return false;
                    }

                    // Mark the callback as logically in flight while holding
                    // the admission gate.  DisconnectClient observes this bit
                    // and defers removal until the callback has returned.
                    publication.CallbackStarted = true;
                    publication.CallbackThreadId = Thread.CurrentThread.ManagedThreadId;
                }

                try
                {
                    OnClientConnected?.Invoke(clientId);

                    lock (_clientAdmissionLock)
                    {
                        publication.CallbackCompleted = true;
                        publication.CallbackThreadId = 0;
                        if (publication.Cancelled
                            || IsStopping
                            || !_clients.TryGetValue(clientId, out var current)
                            || !ReferenceEquals(current, expectedConnection))
                        {
                            // A concurrent Stop/DisconnectClient marked the
                            // publication cancelled while the callback was
                            // running. Leave the entry in place and finish it
                            // through the normal disconnect path after
                            // releasing the lock, so the disconnect event
                            // cannot overtake OnClientConnected.
                            disconnectAfterCallback = publication.Announced
                                && _clients.TryGetValue(clientId, out current)
                                && ReferenceEquals(current, expectedConnection);
                        }
                        else
                        {
                            expectedConnection.StartSendLoop(
                                () => DisconnectClient(clientId, expectedConnection),
                                cancellationToken);
                            return true;
                        }
                    }

                    if (disconnectAfterCallback)
                        DisconnectClient(clientId, expectedConnection);
                    // BeginClientPublication returns false on the cancellation
                    // path, so HandleClient will not enter the registered-client
                    // finally block that normally completes this publication
                    // task. Complete it here after the paired disconnect has
                    // been delivered; Stop must never wait on a stranded TCS.
                    CompleteClientPublication(clientId);
                    return false;
                }
                catch
                {
                    // The caller has not yet entered its registered-client
                    // finally block. Roll back the admission record here so a
                    // callback or send-loop failure cannot strand a publication
                    // task.
                    lock (_clientAdmissionLock)
                    {
                        if (acceptedCounted)
                            Interlocked.Decrement(ref _totalAcceptedClients);
                        _clients.TryRemove(clientId, out _);
                        publication.Announced = false;
                        publication.CallbackCompleted = true;
                        publication.CallbackThreadId = 0;
                        publication.Cancelled = true;
                        _clientPublications.Remove(clientId);
                        publication.Completion.TrySetResult(true);
                    }
                    throw;
                }
            }
        }

        private void CompleteClientPublication(uint clientId)
        {
            lock (_clientAdmissionLock)
            {
                if (_clientPublications.TryGetValue(clientId, out var publication))
                {
                    _clientPublications.Remove(clientId);
                    publication.Completion.TrySetResult(true);
                }
            }
        }

        private Task[] SnapshotPublicationTasks()
        {
            var tasks = new List<Task>(_clientPublications.Count);
            foreach (var publication in _clientPublications.Values)
            {
                if (!publication.Announced)
                    publication.Cancelled = true;
                tasks.Add(publication.Completion.Task);
            }
            return tasks.ToArray();
        }

        private void WaitForPublicationTasks(Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
                return;

            // A user connect callback is allowed to call Stop reentrantly.  In
            // that case this thread is itself completing one of the tasks and
            // waiting for it would deadlock.  The callback's completion path
            // still performs the deferred disconnect and releases the entry.
            if (IsCurrentThreadPublicationCallback())
            {
                _logger.LogWarning(
                    "Client publication stop wait is reentrant; deferred callback cleanup will complete asynchronously.");
                return;
            }

            try
            {
                if (!Task.WaitAll(tasks, StopDisconnectWaitMs))
                    _logger.LogWarning(
                        $"Client publication callbacks did not complete within {StopDisconnectWaitMs}ms during stop.");
            }
            catch (AggregateException ex)
            {
                _logger.LogError($"Client publication callback error during stop: {FormatExceptionChain(ex)}");
            }
        }

        private bool IsCurrentThreadPublicationCallback()
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            lock (_clientAdmissionLock)
            {
                return _clientPublications.Values.Any(
                    publication => publication.CallbackStarted
                        && !publication.CallbackCompleted
                        && publication.CallbackThreadId == threadId);
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

            if (ex is OperationCanceledException
                || ex is AuthenticationException
                || ex is IOException
                || ex is SocketException)
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

        private static void CloseHandshakeResources(TcpClient tcpClient, Stream stream)
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
            // Do not remove or dispose a connection while its connect callback
            // is still executing.  Stop and receive-loop teardown can race this
            // method; deferring the decision preserves connect-before-disconnect
            // ordering and lets the publication owner finish the paired event.
            if (TryDeferPublicationDisconnect(clientId, conn))
                return;

            if (!TryRemoveClient(clientId, conn, out var announced))
            {
                try { conn?.Dispose(); } catch { }
                return;
            }
            Interlocked.Add(ref _totalDroppedDataFrames, conn.DroppedDataFrames);
            if (announced)
                Interlocked.Increment(ref _totalDisconnectedClients);
            try
            {
                if (announced)
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

        private bool TryDeferPublicationDisconnect(uint clientId, WsConnection expectedConnection)
        {
            lock (_clientAdmissionLock)
            {
                if (!_clients.TryGetValue(clientId, out var current)
                    || !ReferenceEquals(current, expectedConnection)
                    || !_clientPublications.TryGetValue(clientId, out var publication)
                    || !publication.CallbackStarted
                    || publication.CallbackCompleted)
                {
                    return false;
                }

                publication.Cancelled = true;
                return true;
            }
        }

        private void RemoveUnannouncedClient(uint clientId, WsConnection conn)
        {
            if (!TryRemoveClient(clientId, conn, out _))
            {
                CloseUnannouncedClient(conn);
                return;
            }
            Interlocked.Add(ref _totalDroppedDataFrames, conn.DroppedDataFrames);
            CloseUnannouncedClient(conn);
        }

        private bool TryRemoveClient(uint clientId, WsConnection expectedConnection)
            => TryRemoveClient(clientId, expectedConnection, out _);

        private bool TryRemoveClient(
            uint clientId,
            WsConnection expectedConnection,
            out bool announced)
        {
            announced = true;
            lock (_clientAdmissionLock)
            {
                if (!_clients.TryGetValue(clientId, out var currentConnection)
                    || !ReferenceEquals(currentConnection, expectedConnection))
                {
                    return false;
                }
                var removed = _clients.TryRemove(clientId, out _);
                if (_clientPublications.TryGetValue(clientId, out var publication))
                {
                    announced = publication.Announced;
                    publication.Cancelled = true;
                    if (!publication.Announced)
                    {
                        _clientPublications.Remove(clientId);
                        publication.Completion.TrySetResult(true);
                    }
                }

                return removed;
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
