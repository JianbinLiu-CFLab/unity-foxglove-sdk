// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: FoxgloveRuntime RecordingAndReplay responsibilities.

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
        // ── Assets ──

        /// <summary>Register a local file system root for fetchAsset under the given URI prefix.</summary>
        public void RegisterAssetRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
        {
            ThrowIfSessionCleanupPending();
            _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);
        }

        /// <summary>Asset registry for fetchAsset capability.</summary>
        public FoxgloveAssetRegistry Assets => _assets;

        // ── Recording (delegated) ──

        /// <summary>Whether recording is enabled.</summary>
        public bool RecordingEnabled => _recording.IsEnabled;

        /// <summary>Enable MCAP recording for the next session start.</summary>
        public void EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, chunkSizeBytes, compression, coordinateMode);
        }

        /// <summary>Enable MCAP recording with explicit output and input coordinate conventions.</summary>
        public void EnableRecording(
            string filePath,
            int chunkSizeBytes,
            string compression,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(
                filePath,
                chunkSizeBytes,
                compression,
                outputCoordinateMode,
                inputCoordinateMode);
        }

        /// <summary>Enable MCAP recording with advanced writer options for the next session start.</summary>
        public void EnableRecording(string filePath, McapWriterOptions options, string coordinateMode = "")
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, options, coordinateMode);
        }

        /// <summary>Enable MCAP recording with paired coordinate conventions.</summary>
        public void EnableRecording(
            string filePath,
            McapWriterOptions options,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, options, outputCoordinateMode, inputCoordinateMode);
        }

        /// <summary>Set the coordinate mode on the recording controller.</summary>
        public void SetRecordingCoordinateMode(string mode)
        {
            ThrowIfSessionCleanupPending();
            _recording.SetCoordinateMode(mode);
        }
        /// <summary>Set paired coordinate conventions on the recording controller.</summary>
        public void SetRecordingCoordinateModes(string outputMode, string inputMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.SetCoordinateModes(outputMode, inputMode);
        }
        /// <summary>Disable recording.</summary>
        public void DisableRecording() => _recording.Disable();

        // ── Playback Control ──

        /// <summary>Enable the playback clock range from start to end nanoseconds.</summary>
        public void EnablePlaybackControl(ulong startNs, ulong endNs)
        {
            ThrowIfSessionCleanupPending();
            _playbackClock.EnableRange(startNs, endNs);
        }
        /// <summary>Whether playback control is enabled.</summary>
        public bool PlaybackEnabled => _tickCoordinator.IsPlaybackEnabled(_playbackClock);
        /// <summary>Get the playback start time in nanoseconds.</summary>
        public ulong GetPlaybackStartNs() => _tickCoordinator.GetPlaybackStartNs(_playbackClock);
        /// <summary>Get the playback end time in nanoseconds.</summary>
        public ulong GetPlaybackEndNs() => _tickCoordinator.GetPlaybackEndNs(_playbackClock);

        /// <summary>Apply a playback command to the clock.</summary>
        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
        {
            ThrowIfSessionCleanupPending();
            _tickCoordinator.ApplyPlaybackCommand(cmd, speed, hasSeek, seekNs, _playbackClock, _logger);
        }

        /// <summary>Get a snapshot of the playback clock state for a response.</summary>
        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => _tickCoordinator.GetPlaybackState(didSeek, requestId, _playbackClock);

        /// <summary>Get the current replay cursor state for the optional loopback cursor endpoint.</summary>
        public ReplayCursorState GetExternalReplayCursorState()
            => ReplayCursorState.FromPlayback(
                ReplayEnabled,
                PlaybackEnabled,
                GetPlaybackState(false, "unity-cursor-state"),
                GetPlaybackStartNs(),
                GetPlaybackEndNs());

        /// <summary>Apply a decoded playback control request on the runtime owner thread.</summary>
        public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
            byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
        {
            ThrowIfSessionCleanupPending();
            return _tickCoordinator.ApplyPlaybackControl(
                cmd, speed, hasSeek, seekNs, requestId,
                _replay, _playbackClock, _wallClock, _logger);
        }

        // ── Replay (delegated) ──

        /// <summary>Whether replay is enabled.</summary>
        public bool ReplayEnabled => _replay.IsEnabled;
        internal bool ReplaySuppressesLivePublishing =>
            _suppressLivePublishersForReplay && ReplayEnabled;

        internal void SetReplayLiveSuppression(bool suppress)
        {
            _suppressLivePublishersForReplay = suppress;
        }
        /// <summary>Whether the last replay enable attempt observed a confirmed FoxRun schema mismatch.</summary>
        public bool ReplayStartHadSchemaMismatch => _replay.LastEnableHadSchemaMismatch;
        /// <summary>Whether the last replay enable attempt was blocked by a confirmed FoxRun schema mismatch.</summary>
        public bool ReplayStartBlockedBySchemaMismatch => _replay.LastEnableBlockedBySchemaMismatch;
        /// <summary>Message from the last failed replay enable attempt, or an empty string.</summary>
        public string ReplayStartFailureMessage => _replay.LastEnableFailureMessage;

        /// <summary>Enable MCAP replay; fails if recording is active.</summary>
        public void EnableReplay(string filePath)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath);
        }
        /// <summary>Enable MCAP replay using the selected schema identity policy.</summary>
        public void EnableReplay(string filePath, SchemaIdentityMode identityMode)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath, identityMode);
        }

        /// <summary>Enable MCAP replay with explicit output and input coordinate conventions.</summary>
        public void EnableReplay(
            string filePath,
            SchemaIdentityMode identityMode,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath, outputCoordinateMode, inputCoordinateMode, identityMode);
        }
        /// <summary>Disable replay and dispose the engine.</summary>
        public void DisableReplay()
            => _tickCoordinator.DisableReplay(_replay);
        /// <summary>Seek replay to the given nanosecond timestamp.</summary>
        public void ReplaySeek(ulong timeNs)
            => _tickCoordinator.ReplaySeek(timeNs, _replay, _wallClock);
        /// <summary>Start or resume replay playback.</summary>
        public void ReplayPlay()
            => _tickCoordinator.ReplayPlay(_replay, _playbackClock);
        /// <summary>Pause replay playback.</summary>
        public void ReplayPause()
            => _tickCoordinator.ReplayPause(_replay, _playbackClock);

        /// <summary>Enable or disable the optional external replay cursor queue.</summary>
        public void SetExternalReplayCursorEnabled(bool enabled)
        {
            ThrowIfSessionCleanupPending();
            _externalReplayCursorController.Enabled = enabled;
            if (!enabled)
                _externalReplayCursorController.Clear();
        }

        /// <summary>
        /// Queue one external replay cursor request for main-thread drain on the next runtime tick.
        /// </summary>
        public ExternalReplayCursorEnqueueResult TryEnqueueExternalReplayCursor(
            ReplayCursorRequest request,
            out string message)
        {
            ThrowIfSessionCleanupPending();
            return _externalReplayCursorController.TryEnqueue(
                request,
                ReplayEnabled,
                GetPlaybackStartNs(),
                GetPlaybackEndNs(),
                out message);
        }

        /// <summary>Request a replay panel-history backfill for newly subscribed clients.</summary>
        public void RequestReplaySubscriberBackfill()
        {
            ThrowIfSessionCleanupPending();
            _tickCoordinator.RequestReplaySubscriberBackfill(_replay, _playbackClock, _wallClock);
        }

        void IClientReplayBackfillContext.RequestReplaySubscriberBackfill(uint clientId)
        {
            ThrowIfSessionCleanupPending();
            _tickCoordinator.RequestReplaySubscriberBackfill(_replay, _playbackClock, _wallClock, clientId);
        }

        void IClientReplayDisconnectContext.CancelReplayForClient(uint clientId)
            => _tickCoordinator.CancelReplayForClient(_replay, clientId);

        /// <summary>Internal: get the list of replay channels for test/runtime introspection.</summary>
        internal IReadOnlyList<McapChannel> GetReplayChannels() => _replay.GetChannels();

        /// <summary>Return the behavior class loaded for a replay channel id.</summary>
        public ReplayChannelBehavior GetReplayChannelBehavior(ushort channelId) => _replay.GetChannelBehavior(channelId);
    }
}
