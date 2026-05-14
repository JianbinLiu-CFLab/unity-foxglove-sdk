// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Queues Foxglove playback-control requests from transport threads
// and applies them from the runtime owner tick.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Owns playback-control request buffering so transport threads can enqueue commands
    /// and the runtime owner can apply them from its tick loop.
    /// </summary>
    internal sealed class SessionPlaybackHandler
    {
        internal const int MaxPendingPlaybackControls = 64;

        private readonly Func<IRuntimeContext> _runtimeProvider;
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveLogger _logger;
        private readonly Action _clearQueuedDataAfterSeek;
        private readonly object _playbackControlsLock = new();
        private readonly Queue<PendingPlaybackControl> _pendingPlaybackControls = new();

        public SessionPlaybackHandler(
            Func<IRuntimeContext> runtimeProvider,
            IFoxgloveTransport transport,
            IFoxgloveLogger logger,
            Action clearQueuedDataAfterSeek)
        {
            _runtimeProvider = runtimeProvider ?? throw new ArgumentNullException(nameof(runtimeProvider));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? new ConsoleLogger();
            _clearQueuedDataAfterSeek = clearQueuedDataAfterSeek ?? (() => { });
        }

        public void Clear()
        {
            lock (_playbackControlsLock)
                _pendingPlaybackControls.Clear();
        }

        public bool HandleRequest(uint clientId, byte[] data)
        {
            var runtime = _runtimeProvider();
            if (runtime?.PlaybackEnabled != true) return false;
            if (!BinaryEncoding.TryDecodePlaybackControlRequest(data, out var playbackCommand, out var playbackSpeed,
                    out var playbackHasSeek, out var playbackSeekNs, out var playbackRequestId))
                return false;

            lock (_playbackControlsLock)
            {
                while (_pendingPlaybackControls.Count >= MaxPendingPlaybackControls)
                    _pendingPlaybackControls.Dequeue();
                _pendingPlaybackControls.Enqueue(new PendingPlaybackControl(
                    clientId, playbackCommand, playbackSpeed, playbackHasSeek, playbackSeekNs, playbackRequestId));
            }

            return true;
        }

        public void Drain()
        {
            while (true)
            {
                PendingPlaybackControl request;
                lock (_playbackControlsLock)
                {
                    if (_pendingPlaybackControls.Count == 0)
                        return;
                    request = _pendingPlaybackControls.Dequeue();
                }

                var runtime = _runtimeProvider();
                if (runtime?.PlaybackEnabled != true)
                    continue;

                if (request.HasSeek)
                    FoxgloveReplayTrace.ResetBudget();

                if (FoxgloveReplayTrace.TryEvent(
                    "CONTROL",
                    $"client={request.ClientId} command={request.Command} speed={request.Speed} hasSeek={request.HasSeek} seek={request.SeekNs} requestId={request.RequestId}",
                    out var controlTrace))
                    _logger.LogWarning(controlTrace);

                var state = runtime.ApplyPlaybackControl(
                    request.Command, request.Speed, request.HasSeek, request.SeekNs, request.RequestId);
                if (request.HasSeek)
                    _clearQueuedDataAfterSeek();

                var playbackFrame = BinaryEncoding.EncodePlaybackState(
                    state.Status, state.CurrentTimeNs, state.Speed, state.DidSeek, state.RequestId);
                if (FoxgloveReplayTrace.TryEvent(
                    "STATE",
                    $"targetClient={request.ClientId} status={state.Status} time={state.CurrentTimeNs} speed={state.Speed} didSeek={state.DidSeek} requestId={state.RequestId}",
                    out var stateTrace))
                    _logger.LogWarning(stateTrace);

                // PlaybackState responses that carry requestId are request-correlated.
                // Send them only to the client that issued the PlaybackControl request.
                _transport.SendBinary(request.ClientId, playbackFrame);
            }
        }

        private readonly struct PendingPlaybackControl
        {
            public readonly uint ClientId;
            public readonly byte Command;
            public readonly float Speed;
            public readonly bool HasSeek;
            public readonly ulong SeekNs;
            public readonly string RequestId;

            public PendingPlaybackControl(uint clientId, byte command, float speed, bool hasSeek, ulong seekNs, string requestId)
            {
                ClientId = clientId;
                Command = command;
                Speed = speed;
                HasSeek = hasSeek;
                SeekNs = seekNs;
                RequestId = requestId;
            }
        }
    }
}
