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
                        || _clientHandlers.Count != 0
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
            ClientHandler[] clientHandlers = null;

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
                    clientHandlers = SnapshotClientHandlers();
                    publicationTasks = SnapshotPublicationTasks();
                }
            }

            try
            {
                try { listener?.Stop(); } catch { }
                WaitForShutdownTask(acceptLoopTask, StopAcceptLoopWaitMs, "accept loop");
                DrainCapacityResponseWorkers();

                var disconnects = clients
                    .Select(pair => Task.Run(() => DisconnectClient(pair.Key, pair.Value, initiateGracefulClose: true)))
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

                // Keep established send loops alive until their bounded
                // graceful-close window has completed. The cancellation also
                // stops receive/liveness loops before the remaining handlers
                // are joined below.
                try { cts?.Cancel(); } catch { }
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

                WaitForClientHandlers(clientHandlers);
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
            foreach (var (id, conn) in SnapshotAnnouncedClients(FramePriority.Control, true, payload))
                HandleEnqueueResult(id, conn, conn.SendTextEncoded(payload, FramePriority.Control), "BroadcastText");
        }

        /// <summary>Send a binary frame to every connected client.</summary>
        public void BroadcastBinary(byte[] data)
        {
            foreach (var (id, conn) in SnapshotAnnouncedClients(FramePriority.Control, false, data))
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Control), "BroadcastBinary");
        }

        /// <summary>Send droppable live data binary frames to every connected client.</summary>
        public void BroadcastDataBinary(byte[] data)
        {
            foreach (var (id, conn) in SnapshotAnnouncedClients(FramePriority.Data, false, data))
                HandleEnqueueResult(id, conn, conn.SendBinary(data, FramePriority.Data), "BroadcastDataBinary");
        }

        private KeyValuePair<uint, WsConnection>[] SnapshotAnnouncedClients(
            FramePriority priority,
            bool text,
            byte[] payload)
        {
            lock (_clientAdmissionLock)
            {
                var clients = new List<KeyValuePair<uint, WsConnection>>(_clients.Count);
                foreach (var pair in _clients)
                {
                    if (_clientPublications.TryGetValue(pair.Key, out var publication)
                        && !publication.CallbackCompleted)
                    {
                        if (priority == FramePriority.Control)
                            DeferPublicationControlLocked(publication, text, payload);
                        continue;
                    }

                    clients.Add(pair);
                }

                return clients.ToArray();
            }
        }

        private void DeferPublicationControlLocked(
            ClientPublication publication,
            bool text,
            byte[] payload)
        {
            var bytes = payload ?? Array.Empty<byte>();
            var maxFrames = ManagedWebSocketOptions.NormalizeMaxQueuedFrames(_options.MaxQueuedFramesPerClient);
            var maxBytes = ManagedWebSocketOptions.NormalizeMaxQueuedBytes(_options.MaxQueuedBytesPerClient);
            if (publication.PendingControlFrames.Count >= maxFrames
                || bytes.Length > maxBytes - publication.PendingControlBytes)
            {
                if (!publication.ControlOverflowed)
                {
                    publication.ControlOverflowed = true;
                    Interlocked.Increment(ref _totalControlOverflowDisconnects);
                }
                return;
            }

            publication.PendingControlFrames.Add(
                new PendingPublicationFrame(text, (byte[])bytes.Clone()));
            publication.PendingControlBytes += bytes.Length;
        }

        private static List<PendingPublicationFrame> TakePendingPublicationControlsLocked(
            ClientPublication publication)
        {
            if (publication.PendingControlFrames.Count == 0)
                return null;

            var pending = new List<PendingPublicationFrame>(publication.PendingControlFrames);
            publication.PendingControlFrames.Clear();
            publication.PendingControlBytes = 0;
            return pending;
        }

        /// <summary>Drop queued data frames for all clients while preserving protocol control frames.</summary>
        public void ClearDataQueues()
        {
            foreach (var (_, conn) in _clients)
                RecordDroppedDataFrames(conn.ClearDataFrames());
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
                RecordDroppedDataFrames(conn.ClearDataFrames());
        }

        /// <summary>Stop the server and release the cancellation token source.</summary>
        public virtual void Dispose()
        {
            Stop();
        }

        // Transport health

        /// <summary>
        /// Produce an immutable snapshot of current transport health.
        /// Drop totals are lifetime counters updated at the point each data
        /// frame is rejected or cleared.
        /// </summary>
        public TransportStatsSnapshot GetStatsSnapshot()
        {
            var clientList = new List<TransportClientStats>();
            long totalQueuedFrames = 0;
            long totalQueuedBytes = 0;
            foreach (var kv in _clients)
            {
                var cs = kv.Value.GetClientStats(kv.Key);
                clientList.Add(cs);
                totalQueuedFrames += cs.QueuedFrames;
                totalQueuedBytes += cs.QueuedBytes;
            }

            var totalDropped = Interlocked.Read(ref _totalDroppedDataFrames);

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

    }
}
