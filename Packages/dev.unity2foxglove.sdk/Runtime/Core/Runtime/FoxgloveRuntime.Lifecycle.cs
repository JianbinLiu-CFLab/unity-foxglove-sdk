// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: FoxgloveRuntime Lifecycle responsibilities.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using static Unity.FoxgloveSDK.Transport.TransportStatsSnapshot;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveRuntime
    {
        /// <summary>
        /// Start the WebSocket server. Creates a new FoxgloveSession,
        /// attaches recording/replay controllers, and wires replay
        /// message forwarding. Protobuf encoding is enabled automatically
        /// when the proto assembly is available.
        /// </summary>
        public void Start(string name, string host = "127.0.0.1", int port = 8765)
            => StartWithSessionSetup(name, host, port, null);

        /// <summary>
        /// Starts a session after allowing an owning bridge to attach its
        /// callbacks to the freshly constructed session.  The setup callback
        /// runs before the transport listener starts, closing the small window
        /// in which a fast client could otherwise be consumed without reaching
        /// the owner.  This overload is internal so the public runtime API
        /// remains source compatible.
        /// </summary>
        internal void StartWithSessionSetup(
            string name,
            string host,
            int port,
            Action<FoxgloveSession> beforeTransportStart)
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
                throw new ObjectDisposedException(nameof(FoxgloveRuntime));
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            // Do not overlap a new session with a retired session or an attached
            // forwarder whose cleanup failed. Best-effort replay-history cleanup
            // may report a failure without poisoning the next session epoch.
            if (_sessionPendingCleanup != null || !_stopCleanup.IsReadyForStart)
            {
                ExceptionDispatchInfo cleanupFailure = null;
                RunStopCleanup(ref cleanupFailure);
                if (!_stopCleanup.IsReadyForStart)
                {
                    if (cleanupFailure != null)
                        cleanupFailure.Throw();
                    throw new InvalidOperationException(
                        "The previous runtime session still owns cleanup resources.");
                }
                if (cleanupFailure != null)
                    _logger.LogWarning(
                        $"Ignoring non-critical Stop cleanup failure before restart: {cleanupFailure.SourceException.Message}");
            }

            FoxgloveSession session = null;
            try
            {
                // Begin the cleanup epoch before the factory subscribes the
                // transport. Factory failures therefore enter the same rollback
                // state as transport Start failures.
                _stopCleanup.Reset();
                _stopCleanupComplete = false;
                session = SessionFactory.Create(
                    name,
                    _transport, _playbackClock, _schemaRegistry, _logger,
                    _parameters, _services, _recording,
                    _protobufSchemasRegistered, _additionalMessageEncodings,
                    this,
                    Volatile.Read(ref _liveWebSocketChannelFilter),
                    Volatile.Read(ref _mcapRecordingChannelFilter),
                    Volatile.Read(ref _mirrorSink));
                beforeTransportStart?.Invoke(session);
                _session = session;
                _tickCoordinator.UpdateReplaySnapshotCapacity(ResolveReplaySnapshotCapacity(_transport));
                session.Start(host, port);
                ClearReplaySuppressionWarnings();
                _replayOrchestrator.Attach(_replay, session);
                _stopped = false;
            }
            catch (Exception)
            {
                // Run every cleanup step independently. The original Start
                // failure remains primary; cleanup failures are retained in
                // the per-step state for a later Stop/Dispose retry.
                ExceptionDispatchInfo cleanupFailure = null;
                RunStopCleanup(ref cleanupFailure, session);
                if (cleanupFailure != null)
                    _logger.LogWarning(
                        $"Startup cleanup was incomplete; preserving the original Start exception: {cleanupFailure.SourceException.Message}");
                throw;
            }
        }

        /// <summary>Fires when the replay engine forwards a message (e.g. for UI update).</summary>
        public event Action<string, byte[]> OnReplayMessage
        {
            add => _replayOrchestrator.OnReplayMessage += value;
            remove => _replayOrchestrator.OnReplayMessage -= value;
        }

        /// <summary>Fires when replay data is forwarded with channel, schema, and log-time context.</summary>
        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add => _replayOrchestrator.OnReplayMessageContext += value;
            remove => _replayOrchestrator.OnReplayMessageContext -= value;
        }

        /// <summary>Fires after a replay batch has been forwarded to scene listeners.</summary>
        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add => _replayOrchestrator.OnReplayBatchCompleted += value;
            remove => _replayOrchestrator.OnReplayBatchCompleted -= value;
        }

        /// <summary>Test-only hook to fire replay without loading an MCAP file.</summary>
        internal void FireReplayForTests(string topic, byte[] data)
            => _replay.FireForTests(topic, data);

        /// <summary>Test-only hook to fire context-rich replay without loading an MCAP file.</summary>
        internal void FireReplayContextForTests(ReplayMessageContext context)
            => _replay.FireContextForTests(context);

        /// <summary>
        /// Stop the server, detach recording/replay, and dispose the session.
        /// </summary>
        public void Stop()
        {
            if (_stopped && _session == null)
            {
                if (_sessionPendingCleanup == null && _stopCleanup.IsResourceCleanupComplete)
                    return;
            }

            ExceptionDispatchInfo firstFailure = null;
            RunStopCleanup(ref firstFailure);
            firstFailure?.Throw();
        }

        /// <summary>
        /// Runs each Stop action once per cleanup epoch and retains only failed
        /// steps for later retry. The active session is retired before any
        /// callback can observe it as running.
        /// </summary>
        private void RunStopCleanup(
            ref ExceptionDispatchInfo firstFailure,
            FoxgloveSession startupSession = null)
        {
            _stopped = true;
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySuppressionWarnings,
                ClearReplaySuppressionWarnings,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySnapshot,
                _tickCoordinator.ClearPendingReplaySnapshot,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySceneSnapshot,
                _tickCoordinator.ClearPendingReplaySceneSnapshot,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplayPanelHistory,
                _replay.CancelPanelHistory,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplayOrchestrator,
                () => _replayOrchestrator.Detach(_replay),
                ref firstFailure);

            var session = _session ?? _sessionPendingCleanup ?? startupSession;
            _session = null;
            if (session == null)
                _stopCleanup.MarkComplete(RuntimeStopCleanupStep.Session);
            else
                _sessionPendingCleanup = session;

            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.Recording,
                _recording.DetachFromSession,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.Session,
                () => session?.Dispose(),
                ref firstFailure);
            if (_stopCleanup.IsCompleted(RuntimeStopCleanupStep.Session))
                _sessionPendingCleanup = null;

            // Resource ownership is represented by the required per-step
            // latches, not by one failure result from the current invocation.
            _stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;
        }
    }
}
