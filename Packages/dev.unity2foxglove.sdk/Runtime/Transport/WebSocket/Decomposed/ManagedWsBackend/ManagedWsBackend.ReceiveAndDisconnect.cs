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
        private void ReceiveLoop(uint clientId, WsConnection conn, CancellationToken ct)
        {
            var fragmentedPayload = new MemoryStream();
            var hasFragmentedPayload = false;
            byte fragmentedOpcode = 0;
            var fragmentedBytes = 0;
            var fragmentedFrames = 0;
            var maxFragmentedMessageFrames = ManagedWebSocketOptions.NormalizeMaxFragmentedMessageFrames(
                _options.MaxFragmentedMessageFrames);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = conn.ReadFrame(
                        out var readResult,
                        ManagedWebSocketOptions.NormalizeEstablishedIdleTimeoutMs(_options.EstablishedIdleTimeoutMs));
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

                    if (conn.IsClosing
                        && frame.Opcode != WsOpcode.Close
                        && frame.Opcode != WsOpcode.Ping)
                    {
                        continue;
                    }

                    conn.TouchInboundActivity();
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
                                    InvokeBinaryObservers(clientId, frame.Payload);
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
                            if (fragmentedFrames > maxFragmentedMessageFrames)
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
                                    InvokeBinaryObservers(clientId, payload);
                                fragmentedOpcode = 0;
                            }
                            break;

                        case WsOpcode.Close:
                            conn.BeginClosing();
                            conn.MarkCloseReceived();
                            if (!conn.CloseFrameSent)
                            {
                                HandleEnqueueResult(clientId, conn, conn.SendClose(), "SendClose");
                                conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
                            }
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

            InvokeTextObservers(clientId, text);
            return true;
        }

        private void CloseProtocolError(
            uint clientId,
            WsConnection conn,
            ushort statusCode = ProtocolErrorCloseCode)
        {
            conn.BeginClosing();
            HandleEnqueueResult(clientId, conn, conn.SendClose(statusCode), "SendClose");
            conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
        }

        /// <summary>Remove the client from the dictionary, fire the disconnected event, and dispose the connection.</summary>
        private void DisconnectClient(uint clientId, WsConnection conn, bool initiateGracefulClose = false)
        {
            if (initiateGracefulClose && conn != null)
            {
                conn.BeginClosing();
                if (!conn.CloseFrameSent)
                {
                    var closeResult = conn.SendClose(GoingAwayCloseCode);
                    RecordDroppedDataFrames(closeResult.DroppedDataFrames);
                    if (closeResult.ShouldLogDataDrop)
                    {
                        _logger.LogWarning(
                            $"Client {clientId} send queue dropped {closeResult.DroppedDataFrames} stale data frame(s) while stopping; total dropped={closeResult.TotalDroppedDataFrames}.");
                    }
                }
                conn.WaitForPendingSends(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
                if (!conn.WaitForCloseReceived(TimeSpan.FromMilliseconds(CloseHandshakeTimeoutMs)))
                {
                    _logger.LogWarning(
                        $"Client {clientId} did not complete the WebSocket close handshake within {CloseHandshakeTimeoutMs}ms; forcing close.");
                }
            }

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
            if (announced)
                Interlocked.Increment(ref _totalDisconnectedClients);
            try
            {
                if (announced)
                    InvokeClientObservers(OnClientDisconnected, clientId, "disconnected");
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


        private bool InvokeClientObservers(Action<uint> observers, uint clientId, string eventName)
        {
            if (observers == null) return false;
            var hadError = false;
            foreach (Action<uint> observer in observers.GetInvocationList())
            {
                try { observer(clientId); }
                catch (Exception ex) { hadError = true; _logger.LogError($"Client {eventName} handler error: {FormatExceptionChain(ex)}"); }
            }
            return hadError;
        }

        private void InvokeTextObservers(uint clientId, string text)
        {
            if (OnTextReceived == null) return;
            foreach (Action<uint, string> observer in OnTextReceived.GetInvocationList())
            {
                try { observer(clientId, text); }
                catch (Exception ex) { _logger.LogError($"Client text handler error: {FormatExceptionChain(ex)}"); }
            }
        }

        private void InvokeBinaryObservers(uint clientId, byte[] payload)
        {
            if (OnBinaryReceived == null) return;
            foreach (Action<uint, byte[]> observer in OnBinaryReceived.GetInvocationList())
            {
                try { observer(clientId, payload); }
                catch (Exception ex) { _logger.LogError($"Client binary handler error: {FormatExceptionChain(ex)}"); }
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
                publication.StopDisconnectRequested = true;
                return true;
            }
        }

        private void RemoveUnannouncedClient(uint clientId, WsConnection conn)
        {
            if (IsStopping)
            {
                lock (_clientAdmissionLock)
                {
                    if (_clients.TryGetValue(clientId, out var current)
                        && ReferenceEquals(current, conn))
                    {
                        // Stop owns the registered connection snapshot and
                        // must send its graceful close before disposal. Mark
                        // the publication complete without closing the socket;
                        // Stop will remove and dispose it after the close drain.
                        if (_clientPublications.TryGetValue(clientId, out var publication))
                        {
                            publication.Cancelled = true;
                            publication.Completion.TrySetResult(true);
                        }
                        return;
                    }
                }
            }

            if (!TryRemoveClient(clientId, conn, out _))
            {
                CloseUnannouncedClient(conn);
                return;
            }
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

        internal void RecordLivenessEnqueueResult(EnqueueResult result)
        {
            RecordDroppedDataFrames(result.DroppedDataFrames);
        }

        private void HandleEnqueueResult(uint clientId, WsConnection conn, EnqueueResult result, string operation)
        {
            RecordDroppedDataFrames(result.DroppedDataFrames);
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

        private void RecordDroppedDataFrames(int count)
        {
            if (count > 0)
                Interlocked.Add(ref _totalDroppedDataFrames, count);
        }
    }
}
