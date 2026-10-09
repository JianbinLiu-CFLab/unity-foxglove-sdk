// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Foxglove WebSocket session — owns channel/advertise/subscribe
// lifecycle, client messaging dispatch, connection graph, and MCAP recording
// integration. Split into partial classes for readability (Connection,
// Parameters, Services).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Core session that handles Foxglove WebSocket protocol logic:
    /// channel lifecycle, subscription routing, binary message encoding,
    /// client text/binary dispatch, time broadcast, connection graph,
    /// and serverInfo on connect.
    ///
    /// Split across source files for readability:
    /// FoxgloveSession.Connection.cs (subscribe/unsubscribe,
    /// ConnectionGraph, ClientPublish, PlaybackControl, Assets),
    /// FoxgloveSession.Parameters.cs, FoxgloveSession.Services.cs, and
    /// the Phase192 lifecycle, channel, publishing, service, time, and
    /// transport sections.
    /// </summary>
    /// <remarks>Phase192 source sections: FoxgloveSession.Lifecycle.cs, FoxgloveSession.Channels.cs, FoxgloveSession.SchemaChannels.cs, FoxgloveSession.Publishing.cs, FoxgloveSession.ServiceRegistration.cs, FoxgloveSession.TimeAndEncoding.cs, FoxgloveSession.TransportEvents.cs</remarks>
    public partial class FoxgloveSession : IDisposable
    {
        // ── Lifecycle ──

        /// <summary>Start the WebSocket transport on the given host and port.</summary>
        public void Start(string host, int port)
        {
            lock (_encodingConfigurationLock)
            {
                // Freeze before transport start so synchronous callbacks observe final encoding authority.
                _encodingConfigurationFrozen = true;
            }
            _transport.Start(host, port);
        }
        /// <summary>Stop the WebSocket transport.</summary>
        public void Stop() => _transport.Stop();

        /// <summary>Clear all transient session channels, subscriptions, and graph state.</summary>
        public void ClearSession()
        {
            lock (_channelLifecycleLock)
            {
                _channels.Clear();
                _subscriptions.Clear();
                _paramSubs.Clear();
                _services.ClearPendingCalls();
                _graph.Clear();
                _playback.Clear();
                _clientPublish.Clear();
                _timeBroadcaster.Reset();
                _recordingOnlyChannels.Clear();
            }
            lock (_subscriptionBudgetWarnedClientsLock)
                _subscriptionBudgetWarnedClients.Clear();
        }

        /// <summary>Stop the transport and detach all event handlers.</summary>
        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            ExceptionDispatchInfo firstFailure = null;
            TryCleanup(Stop, ref _disposeStopComplete, ref firstFailure);
            TryCleanup(DetachClientConnected, ref _disposeClientConnectedComplete, ref firstFailure);
            TryCleanup(DetachClientDisconnected, ref _disposeClientDisconnectedComplete, ref firstFailure);
            TryCleanup(DetachTextReceived, ref _disposeTextReceivedComplete, ref firstFailure);
            TryCleanup(DetachBinaryReceived, ref _disposeBinaryReceivedComplete, ref firstFailure);

            // Make the session's downstream callback graph inert even when a
            // custom event accessor refused one of the detach attempts. The
            // transport may still retain an inbound handler until a later
            // retry, but it must not retain the recorder or user callbacks.
            Volatile.Write(ref _recorder, null);
            Volatile.Write(ref _mirrorSink, null);
            OnClientMessage = null;
            OnClientMessageWithEncoding = null;

            if (_disposeStopComplete
                && _disposeClientConnectedComplete
                && _disposeClientDisconnectedComplete
                && _disposeTextReceivedComplete
                && _disposeBinaryReceivedComplete)
            {
                Volatile.Write(ref _disposed, 1);
            }

            firstFailure?.Throw();
        }

        private static void TryCleanup(
            Action cleanup,
            ref bool completed,
            ref ExceptionDispatchInfo firstFailure)
        {
            if (completed)
                return;

            try
            {
                cleanup?.Invoke();
                completed = true;
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void DetachClientConnected()
        {
            if (!_clientConnectedSubscribed)
                return;
            _transport.OnClientConnected -= OnClientConnected;
            _clientConnectedSubscribed = false;
        }

        private void DetachClientDisconnected()
        {
            if (!_clientDisconnectedSubscribed)
                return;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _clientDisconnectedSubscribed = false;
        }

        private void DetachTextReceived()
        {
            if (!_textReceivedSubscribed)
                return;
            _transport.OnTextReceived -= OnClientText;
            _textReceivedSubscribed = false;
        }

        private void DetachBinaryReceived()
        {
            if (!_binaryReceivedSubscribed)
                return;
            _transport.OnBinaryReceived -= OnClientBinary;
            _binaryReceivedSubscribed = false;
        }

        private void RollBackConstructorSubscriptions()
        {
            TryRollback(DetachBinaryReceived);
            TryRollback(DetachTextReceived);
            TryRollback(DetachClientDisconnected);
            TryRollback(DetachClientConnected);
        }

        private static void TryRollback(Action rollback)
        {
            try
            {
                rollback?.Invoke();
            }
            catch
            {
                // Preserve the constructor failure as the primary exception.
            }
        }

    }
}
