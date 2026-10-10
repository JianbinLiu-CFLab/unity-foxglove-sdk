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
        private bool BeginClientPublication(
            uint clientId,
            WsConnection expectedConnection)
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
                var stopOwnsConnection = false;
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
                        if (!(IsStopping
                            && _clients.TryGetValue(clientId, out var stoppingConnection)
                            && ReferenceEquals(stoppingConnection, expectedConnection)))
                        {
                            _clients.TryRemove(clientId, out _);
                            _clientPublications.Remove(clientId);
                        }
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
                    if (InvokeClientObservers(OnClientConnected, clientId, "connected"))
                        disconnectAfterCallback = true;

                    List<PendingPublicationFrame> pendingControls = null;
                    var publicationReady = false;
                    lock (_clientAdmissionLock)
                    {
                        publication.CallbackThreadId = 0;
                        if (disconnectAfterCallback
                            || publication.Cancelled
                            || publication.ControlOverflowed
                            || IsStopping
                            || !_clients.TryGetValue(clientId, out var current)
                            || !ReferenceEquals(current, expectedConnection))
                        {
                            publication.CallbackCompleted = true;
                            publication.PendingControlFrames.Clear();
                            publication.PendingControlBytes = 0;
                            // A concurrent Stop/DisconnectClient marked the
                            // publication cancelled while the callback was
                            // running. Leave the entry in place and finish it
                            // through the normal disconnect path after
                            // releasing the lock, so the disconnect event
                            // cannot overtake OnClientConnected.
                            stopOwnsConnection = IsStopping
                                && !publication.StopDisconnectRequested
                                && _clients.TryGetValue(clientId, out current)
                                && ReferenceEquals(current, expectedConnection);
                            disconnectAfterCallback = publication.StopDisconnectRequested
                                || (!stopOwnsConnection
                                    && publication.Announced
                                    && _clients.TryGetValue(clientId, out current)
                                    && ReferenceEquals(current, expectedConnection));
                        }
                        else
                        {
                            pendingControls = TakePendingPublicationControlsLocked(publication);
                            publicationReady = true;
                        }
                    }

                    while (publicationReady)
                    {
                        if (pendingControls != null)
                        {
                            foreach (var pending in pendingControls)
                            {
                                var result = pending.Text
                                    ? expectedConnection.SendTextEncoded(pending.Payload, FramePriority.Control)
                                    : expectedConnection.SendBinary(pending.Payload, FramePriority.Control);
                                HandleEnqueueResult(
                                    clientId,
                                    expectedConnection,
                                    result,
                                    pending.Text ? "FlushPublicationText" : "FlushPublicationBinary");
                                if (result.ShouldDisconnect)
                                    break;
                            }
                        }

                        lock (_clientAdmissionLock)
                        {
                            if (publication.Cancelled
                                || IsStopping
                                || publication.ControlOverflowed
                                || !_clients.TryGetValue(clientId, out var current)
                                || !ReferenceEquals(current, expectedConnection))
                            {
                                publication.CallbackCompleted = true;
                                publication.PendingControlFrames.Clear();
                                publication.PendingControlBytes = 0;
                                stopOwnsConnection = IsStopping
                                    && !publication.StopDisconnectRequested
                                    && _clients.TryGetValue(clientId, out current)
                                    && ReferenceEquals(current, expectedConnection);
                                disconnectAfterCallback = publication.StopDisconnectRequested
                                    || (!stopOwnsConnection
                                        && publication.Announced
                                        && _clients.TryGetValue(clientId, out current)
                                        && ReferenceEquals(current, expectedConnection));
                                publicationReady = false;
                            }
                            else
                            {
                                pendingControls = TakePendingPublicationControlsLocked(publication);
                                if (pendingControls == null)
                                {
                                    publication.CallbackCompleted = true;
                                    publicationReady = false;
                                    return true;
                                }
                            }
                        }
                    }

                    if (stopOwnsConnection)
                    {
                        CompleteClientPublication(clientId);
                        return false;
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

        private ClientHandler[] SnapshotClientHandlers()
        {
            return _clientHandlers.Values.ToArray();
        }

        private void WaitForClientHandlers(ClientHandler[] handlers)
        {
            if (handlers == null || handlers.Length == 0)
                return;

            var currentThreadId = Environment.CurrentManagedThreadId;
            var tasks = handlers
                .Where(handler => Volatile.Read(ref handler.ThreadId) != currentThreadId)
                .Select(handler => handler.Completion.Task)
                .ToArray();
            if (tasks.Length == 0)
            {
                _logger.LogWarning("Client handler stop wait is reentrant; current handler will complete after Stop returns.");
                return;
            }

            try
            {
                if (!Task.WaitAll(tasks, StopClientHandlersWaitMs))
                {
                    _logger.LogWarning(
                        $"Client handlers did not finish within {StopClientHandlersWaitMs}ms during stop; retaining them until completion.");
                }
            }
            catch (AggregateException ex)
            {
                _logger.LogError($"Client handler stop error: {FormatExceptionChain(ex)}");
            }
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

        private static void DrainCapacityRequest(Stream stream)
        {
            if (stream == null || !stream.CanRead)
                return;

            try
            {
                var deadline = Stopwatch.GetTimestamp()
                    + Stopwatch.Frequency * CapacityRequestDrainTimeoutMs / 1000;
                var matched = 0;
                for (var count = 0; count < MaxCapacityRequestBytes; count++)
                {
                    var remainingTicks = deadline - Stopwatch.GetTimestamp();
                    if (remainingTicks <= 0)
                        return;
                    if (stream.CanTimeout)
                    {
                        var remainingMs = (int)Math.Min(
                            CapacityRequestDrainTimeoutMs,
                            Math.Max(1, remainingTicks * 1000 / Stopwatch.Frequency));
                        stream.ReadTimeout = remainingMs;
                    }

                    var value = stream.ReadByte();
                    if (value < 0)
                        return;

                    if (matched == 0 && value == '\r')
                        matched = 1;
                    else if (matched == 1 && value == '\n')
                        matched = 2;
                    else if (matched == 2 && value == '\r')
                        matched = 3;
                    else if (matched == 3 && value == '\n')
                        return;
                    else
                        matched = value == '\r' ? 1 : 0;
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
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
    }
}
