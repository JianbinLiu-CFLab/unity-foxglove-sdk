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
using System.Diagnostics;
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
    /// <remarks>Phase192 source sections: ManagedWsBackend.LifecycleAndMessaging.cs, ManagedWsBackend.AdmissionAndHandshake.cs, ManagedWsBackend.PublicationAndShutdown.cs, ManagedWsBackend.ReceiveAndDisconnect.cs</remarks>
    public partial class ManagedWsBackend : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IClientDataQueueResettableFoxgloveTransport, IFoxgloveTransportStatsProvider, IOriginGuardedFoxgloveTransport, IFoxgloveTransportCapacityProvider, IDisposable
    {
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

                    if (!TryReservePendingClient(tcpClient, out var handler))
                    {
                        QueueRejectedClient(tcpClient);
                        continue;
                    }

                    try
                    {
                        _ = Task.Run(() => RunClientHandler(handler, ct));
                    }
                    catch
                    {
                        _clientHandlers.TryRemove(tcpClient, out _);
                        handler.Completion.TrySetResult(true);
                        ReleasePendingClient(tcpClient);
                        CloseUnregisteredClient(tcpClient, null);
                        throw;
                    }
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
                catch (NullReferenceException) when (ct.IsCancellationRequested || IsStopping) { break; }
                catch (Exception) when (ct.IsCancellationRequested || IsStopping) { break; }
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

                // Established connections must not inherit the handshake's
                // inactivity deadline. Frame progress is bounded by
                // WsConnection after the first frame byte arrives, while the
                // liveness monitor sends pings and waits for peer activity.
                ConfigureStreamTimeouts(stream, Timeout.Infinite, Timeout.Infinite);

                conn = new WsConnection(
                    tcpClient,
                    stream,
                    _options.MaxQueuedFramesPerClient,
                    _options.MaxQueuedBytesPerClient,
                    _options.MaxInboundFrameBytes);
                if (!TryRegisterClient(conn, ct, out clientId, out var stopped))
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

                if (ct.IsCancellationRequested || !BeginClientPublication(clientId, conn))
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
                    conn.StartLivenessMonitor(
                        ManagedWebSocketOptions.NormalizeEstablishedIdleTimeoutMs(_options.EstablishedIdleTimeoutMs),
                        () => DisconnectClient(clientId, conn),
                        RecordLivenessEnqueueResult,
                        ct);
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

        private bool TryReservePendingClient(TcpClient tcpClient, out ClientHandler handler)
        {
            handler = null;
            if (tcpClient == null)
                return false;

            lock (_clientAdmissionLock)
            {
                var maxClients = ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);
                if (IsStopping || _clients.Count + _pendingClients.Count >= maxClients)
                    return false;

                if (!_pendingClients.TryAdd(
                    tcpClient,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)))
                    return false;

                handler = new ClientHandler(tcpClient);
                _clientHandlers[tcpClient] = handler;
                return true;
            }
        }

        private void RunClientHandler(ClientHandler handler, CancellationToken ct)
        {
            Volatile.Write(ref handler.ThreadId, Environment.CurrentManagedThreadId);
            try
            {
                HandleClient(handler.TcpClient, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError($"WebSocket client handler failed: {FormatExceptionChain(ex)}");
            }
            finally
            {
                _clientHandlers.TryRemove(handler.TcpClient, out _);
                handler.Completion.TrySetResult(true);
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
                    {
                        DrainCapacityRequest(stream);
                        WsHandshakeHandler.WriteCapacityResponse(stream);
                        stream.Flush();
                    }
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
                Task worker = null;
                worker = Task.Run(() => ProcessRejectedClient(tcpClient));
                _capacityResponseWorkers[worker] = tcpClient;
                _ = worker.ContinueWith(
                    completed => _capacityResponseWorkers.TryRemove(completed, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
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

        private void DrainCapacityResponseWorkers()
        {
            var workers = _capacityResponseWorkers.ToArray();
            foreach (var pair in workers)
            {
                try { pair.Value?.Close(); } catch { }
                try { pair.Value?.Dispose(); } catch { }
            }

            if (workers.Length == 0)
                return;

            try
            {
                if (!Task.WaitAll(workers.Select(pair => pair.Key).ToArray(), StopPendingHandshakeWaitMs))
                    _logger.LogWarning(
                        $"Capacity response workers did not finish within {StopPendingHandshakeWaitMs}ms during stop.");
            }
            catch (AggregateException ex)
            {
                _logger.LogError($"Capacity response stop error: {FormatExceptionChain(ex)}");
            }
        }

        private void ReleasePendingClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
                return;

            lock (_clientAdmissionLock)
            {
                if (_pendingClients.TryRemove(tcpClient, out var completion))
                    completion.TrySetResult(true);
            }
        }

        private bool TryRegisterClient(
            WsConnection conn,
            CancellationToken cancellationToken,
            out uint clientId,
            out bool stopped)
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

                var allocatedClientId = AllocateClientId();
                conn.StartSendLoop(
                    () => DisconnectClient(allocatedClientId, conn),
                    cancellationToken);
                clientId = allocatedClientId;
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
    }
}
