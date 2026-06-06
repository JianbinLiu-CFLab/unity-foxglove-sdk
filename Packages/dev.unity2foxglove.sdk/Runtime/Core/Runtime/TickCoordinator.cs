// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: Coordinates runtime tick ordering for services, clocks, replay, and
// external replay cursor ownership.

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
        private const ulong ExternalCursorSeekJumpThresholdNs = 500_000_000UL;
        private readonly object _playbackControlLock = new();
        private readonly ReplaySnapshotStateMachine _replaySnapshots;
        private bool _hasExternalCursorTime;
        private ulong _lastExternalCursorTimeNs;

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
        public void Tick(
            FoxgloveSession session,
            PlaybackClock playbackClock,
            ReplayController replay,
            IFoxgloveClock wallClock,
            ExternalReplayCursorController externalCursor = null)
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
                    if (externalCursor != null && externalCursor.TryDrainLatest(out var cursor))
                    {
                        if (ShouldTreatExternalCursorAsSeek(cursor))
                            ReplaySeekExternalCursor(cursor.TimeNs, replay, playbackClock);
                        else
                            ReplayAdvanceToExternalCursor(cursor.TimeNs, replay, playbackClock);

                        RememberExternalCursor(cursor.TimeNs);
                    }

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

        private bool ShouldTreatExternalCursorAsSeek(ReplayCursorRequest cursor)
        {
            if (!_hasExternalCursorTime || cursor.DidSeek || cursor.TimeNs < _lastExternalCursorTimeNs)
                return true;

            return cursor.TimeNs - _lastExternalCursorTimeNs > ExternalCursorSeekJumpThresholdNs;
        }

        private void ReplaySeekExternalCursor(ulong timeNs, ReplayController replay, PlaybackClock playbackClock)
        {
            playbackClock.Apply(1, 1f, true, timeNs);
            replay.Seek(timeNs);
            replay.Pause();
            QueueReplaySceneSnapshot(timeNs);
        }

        private static void ReplayAdvanceToExternalCursor(
            ulong timeNs,
            ReplayController replay,
            PlaybackClock playbackClock)
        {
            playbackClock.Apply(1, 1f, true, timeNs);
            replay.Play();
            replay.ApplyTickToScene(timeNs, deferCallbacks: true);
            replay.Pause();
        }

        private void RememberExternalCursor(ulong timeNs)
        {
            _lastExternalCursorTimeNs = timeNs;
            _hasExternalCursorTime = true;
        }

        private void ClearExternalCursorState()
        {
            _hasExternalCursorTime = false;
            _lastExternalCursorTimeNs = 0;
        }

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
            if (PlaybackClock.ShouldWarnInvalidSpeed(cmd, speed))
                logger.LogWarning($"Invalid playback speed {speed}; using 1.0.");

            lock (_playbackControlLock)
            {
                playbackClock.Apply(cmd, speed, hasSeek, seekNs);

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
            if (PlaybackClock.ShouldWarnInvalidSpeed(cmd, speed))
                logger.LogWarning($"Invalid playback speed {speed}; using 1.0.");
            lock (_playbackControlLock)
                playbackClock.Apply(cmd, speed, hasSeek, seekNs);
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
                ClearExternalCursorState();
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
                ClearExternalCursorState();
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
                ClearExternalCursorState();
                playbackClock.Pause();
                replay.Pause();
                ClearPendingReplaySnapshot();
            }
        }

        /// <summary>
        /// Queue a panel-history refresh at the current replay time when a
        /// client subscribes after autoplay has already begun. The replay
        /// cursor is left untouched so Unity scene playback continues.
        /// </summary>
        public void RequestReplaySubscriberBackfill(
            ReplayController replay, PlaybackClock playbackClock, IFoxgloveClock wallClock)
        {
            lock (_playbackControlLock)
            {
                if (!replay.IsEnabled || !playbackClock.PlaybackEnabled)
                    return;

                replay.ResetPanelHistoryProgress();
                QueueReplaySnapshot(playbackClock.NowNs, replay, wallClock);
            }
        }

        /// <summary>
        /// Disable replay: clears pending snapshots and disposes the replay engine.
        /// </summary>
        public void DisableReplay(ReplayController replay)
        {
            ClearExternalCursorState();
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
