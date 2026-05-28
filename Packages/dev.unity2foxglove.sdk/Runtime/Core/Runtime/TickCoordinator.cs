// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime

using System;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Coordinates per-frame tick dispatch: drains service calls, advances the
    /// playback clock, and routes replay seek/play/pause/disable operations
    /// through a shared snapshot state machine under a single lock.
    /// </summary>
    internal class TickCoordinator
    {
        private readonly object _playbackControlLock = new();
        private readonly ReplaySnapshotStateMachine _replaySnapshots;

        /// <summary>
        /// Creates a <see cref="TickCoordinator"/> backed by the given snapshot
        /// state machine.
        /// </summary>
        public TickCoordinator(ReplaySnapshotStateMachine snapshots) { _replaySnapshots = snapshots; }

        /// <summary>
        /// Per-frame tick: drains pending service/playback-control calls, advances
        /// the clock, and dispatches replay work (scene snapshot, panel snapshot,
        /// drain callbacks) when replay is active.
        /// </summary>
        public void Tick(FoxgloveSession session, PlaybackClock playbackClock, ReplayController replay, IFoxgloveClock wallClock)
        {
            if (session == null) return;
            session.DrainPlaybackControls();
            session.DrainServiceCalls();
            var broadcastLiveTime = false;
            lock (_playbackControlLock)
            {
                playbackClock.Tick();

                if (replay.IsEnabled)
                {
                    // Replay work intentionally stays inside _playbackControlLock.
                    // Seek/play/pause mutate the same snapshot scheduler, and
                    // releasing the lock here could publish a stale pre-seek
                    // snapshot after a newer playback control request.
                    if (TryConsumeReplaySceneSnapshot(out var sceneSnapshotTimeNs, wallClock))
                        replay.ApplySnapshotToScene(sceneSnapshotTimeNs, deferCallbacks: true);
                    if (TryConsumeReplaySnapshot(out var snapshotTimeNs, wallClock))
                        replay.PublishSnapshot(session, snapshotTimeNs);
                    else
                        replay.DrainPanelHistory(session);
                    replay.Tick(session, playbackClock.NowNs, deferCallbacks: true);
                }
                else
                    broadcastLiveTime = true;
            }

            if (broadcastLiveTime)
                session.BroadcastTime();
            replay.DrainReplayCallbacks();
        }

        private void QueueReplaySnapshot(ulong timeNs, ReplayController replay, IFoxgloveClock wallClock)
        {
            replay.CancelPanelHistory();
            _replaySnapshots.RequestPanelSnapshot(
                timeNs,
                wallClock.NowNs + ReplayController.ScrubHistoryDebounceNs);
        }

        private bool TryConsumeReplaySnapshot(out ulong timeNs, IFoxgloveClock wallClock)
            => _replaySnapshots.TryConsumePanelSnapshot(wallClock.NowNs, out timeNs);

        private void QueueReplaySceneSnapshot(ulong timeNs)
            => _replaySnapshots.RequestSceneSnapshot(timeNs);

        private bool TryConsumeReplaySceneSnapshot(out ulong timeNs, IFoxgloveClock wallClock)
            => _replaySnapshots.TryConsumeSceneSnapshot(out timeNs);

        /// <summary>
        /// Applies a decoded playback-control request (play/pause + optional seek)
        /// to the clock and replay controller, and returns the resulting playback
        /// state snapshot.
        /// </summary>
        public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
            byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId,
            ReplayController replay, PlaybackClock playbackClock, IFoxgloveClock wallClock,
            IFoxgloveLogger logger)
        {
            var normalizedSpeed = PlaybackClock.NormalizeSpeed(speed);
            if (normalizedSpeed != speed)
                logger.LogWarning($"Invalid playback speed {speed}; using 1.0.");

            lock (_playbackControlLock)
            {
                playbackClock.Apply(cmd, normalizedSpeed, hasSeek, seekNs);

                if (hasSeek)
                {
                    replay.Seek(seekNs);
                    QueueReplaySceneSnapshot(seekNs);
                }

                if (cmd == 0)
                {
                    ClearPendingReplaySnapshot();
                    replay.ResetPanelHistoryProgress();
                    replay.Play();
                }
                else if (cmd == 1)
                {
                    replay.Pause();
                    ClearPendingReplaySnapshot();
                }

                if (hasSeek && cmd == 1)
                    QueueReplaySnapshot(seekNs, replay, wallClock);

                return playbackClock.ToState(hasSeek, requestId);
            }
        }

        /// <summary>
        /// Applies a playback command (play/pause speed change) to the clock
        /// without touching the replay controller.
        /// </summary>
        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs,
            PlaybackClock playbackClock, IFoxgloveLogger logger)
        {
            var normalizedSpeed = PlaybackClock.NormalizeSpeed(speed);
            if (normalizedSpeed != speed)
                logger.LogWarning($"Invalid playback speed {speed}; using 1.0.");
            lock (_playbackControlLock)
                playbackClock.Apply(cmd, normalizedSpeed, hasSeek, seekNs);
        }

        /// <summary>
        /// Returns a snapshot of the playback clock state for a client-requested
        /// state response.
        /// </summary>
        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId,
            PlaybackClock playbackClock)
        {
            lock (_playbackControlLock)
                return playbackClock.ToState(didSeek, requestId);
        }

        /// <summary>
        /// Seek the replay controller to the given timestamp, queueing both a
        /// scene snapshot and a panel snapshot.
        /// </summary>
        public void ReplaySeek(ulong timeNs, ReplayController replay, IFoxgloveClock wallClock)
        {
            lock (_playbackControlLock)
            {
                replay.Seek(timeNs);
                QueueReplaySceneSnapshot(timeNs);
                QueueReplaySnapshot(timeNs, replay, wallClock);
            }
        }

        /// <summary>
        /// Resume replay playback, clearing any pending snapshots and advancing
        /// the playback clock.
        /// </summary>
        public void ReplayPlay(ReplayController replay, PlaybackClock playbackClock)
        {
            lock (_playbackControlLock)
            {
                ClearPendingReplaySnapshot();
                ClearPendingReplaySceneSnapshot();
                replay.ResetPanelHistoryProgress();
                playbackClock.Play();
                replay.Play();
            }
        }

        /// <summary>
        /// Pause replay playback and clear any pending panel snapshot so stale
        /// data is not published on next tick.
        /// </summary>
        public void ReplayPause(ReplayController replay, PlaybackClock playbackClock)
        {
            lock (_playbackControlLock)
            {
                playbackClock.Pause();
                replay.Pause();
                ClearPendingReplaySnapshot();
            }
        }

        /// <summary>
        /// Disable replay: clears pending snapshots and disposes the replay engine.
        /// </summary>
        public void DisableReplay(ReplayController replay)
        {
            ClearPendingReplaySnapshot();
            ClearPendingReplaySceneSnapshot();
            replay.Disable();
        }

        /// <summary>
        /// Clears the pending panel snapshot request if one is queued.
        /// </summary>
        public void ClearPendingReplaySnapshot()
            => _replaySnapshots.ClearPanelSnapshot();

        /// <summary>
        /// Clears the pending scene snapshot request if one is queued.
        /// </summary>
        public void ClearPendingReplaySceneSnapshot()
            => _replaySnapshots.ClearSceneSnapshot();
    }
}
