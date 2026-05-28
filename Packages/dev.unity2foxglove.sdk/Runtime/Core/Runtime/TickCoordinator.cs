// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime

using System;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    internal class TickCoordinator
    {
        private readonly object _playbackControlLock = new();
        private readonly ReplaySnapshotStateMachine _replaySnapshots;

        public TickCoordinator(ReplaySnapshotStateMachine snapshots) { _replaySnapshots = snapshots; }

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

        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs,
            PlaybackClock playbackClock, IFoxgloveLogger logger)
        {
            var normalizedSpeed = PlaybackClock.NormalizeSpeed(speed);
            if (normalizedSpeed != speed)
                logger.LogWarning($"Invalid playback speed {speed}; using 1.0.");
            lock (_playbackControlLock)
                playbackClock.Apply(cmd, normalizedSpeed, hasSeek, seekNs);
        }

        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId,
            PlaybackClock playbackClock)
        {
            lock (_playbackControlLock)
                return playbackClock.ToState(didSeek, requestId);
        }

        public void ReplaySeek(ulong timeNs, ReplayController replay, IFoxgloveClock wallClock)
        {
            lock (_playbackControlLock)
            {
                replay.Seek(timeNs);
                QueueReplaySceneSnapshot(timeNs);
                QueueReplaySnapshot(timeNs, replay, wallClock);
            }
        }

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

        public void ReplayPause(ReplayController replay, PlaybackClock playbackClock)
        {
            lock (_playbackControlLock)
            {
                playbackClock.Pause();
                replay.Pause();
                ClearPendingReplaySnapshot();
            }
        }

        public void DisableReplay(ReplayController replay)
        {
            ClearPendingReplaySnapshot();
            ClearPendingReplaySceneSnapshot();
            replay.Disable();
        }

        public void ClearPendingReplaySnapshot()
            => _replaySnapshots.ClearPanelSnapshot();

        public void ClearPendingReplaySceneSnapshot()
            => _replaySnapshots.ClearSceneSnapshot();
    }
}
