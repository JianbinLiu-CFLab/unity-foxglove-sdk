// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    public partial class ReplayController
    {
        /// <summary>
        /// Load an MCAP file for replay with the selected schema identity mode.
        /// Strict blocks schema mismatches, Warn reports them and continues, and Off
        /// skips schema identity comparison. The default mode is Strict.
        /// Recording-state and coordinate-mode values are read from the injected
        /// <see cref="IRecordingStateReader"/>.
        /// </summary>
        public void Enable(string filePath, SchemaIdentityMode identityMode = SchemaIdentityMode.Strict)
        {
            var recordingEnabled = _recordingState != null && _recordingState.IsEnabled;
            var coordinateMode = _recordingState?.CoordinateMode ?? "";
            EnableCore(filePath, recordingEnabled, coordinateMode, identityMode);
        }

        /// <summary>
        /// Load an MCAP file for replay with externally supplied playback-clock,
        /// recording-state, and coordinate-mode values.
        /// </summary>
        [Obsolete("Use Enable(string, SchemaIdentityMode) — recording state and clock are now supplied through the constructor.")]
        public void Enable(
            string filePath,
            PlaybackClock playbackClock,
            bool recordingEnabled,
            string currentCoordinateMode = "",
            SchemaIdentityMode identityMode = SchemaIdentityMode.Strict)
        {
            EnableCore(filePath, recordingEnabled, currentCoordinateMode, identityMode);
        }

        private void EnableCore(
            string filePath,
            bool recordingEnabled,
            string currentCoordinateMode,
            SchemaIdentityMode identityMode)
        {
            McapReplayEngine loadedEngine = null;
            ulong replayStartTimeNs = 0UL;
            ulong replayEndTimeNs = 0UL;

            try
            {
                Volatile.Write(ref _lastEnableHadSchemaMismatch, false);
                Volatile.Write(ref _lastEnableBlockedBySchemaMismatch, false);
                Volatile.Write(ref _lastEnableFailureMessage, string.Empty);

                if (!recordingEnabled)
                    ReplayFileValidator.ValidateReplayFileForLoad(filePath);

                lock (_replayEngineLock)
                {
                    // Clean any previous replay state to avoid leaking old engine/stream
                    Disable();

                    if (recordingEnabled)
                    {
                        const string message = "Recording and Replay cannot both be enabled. Replay disabled.";
                        Volatile.Write(ref _lastEnableFailureMessage, message);
                        _logger.LogWarning(message);
                        return;
                    }

                    _replayEngine = new McapReplayEngine(_logger);
                    loadedEngine = _replayEngine;
                    _replayEngine.Load(filePath);
                    var summary = _replayEngine.Summary;
                    if (identityMode != SchemaIdentityMode.Off)
                    {
                        var schemaGuard = ReplaySchemaGuard.Evaluate(_replayEngine);
                        if (schemaGuard.State == FoxRunReplaySchemaGuardState.Mismatch)
                            Volatile.Write(ref _lastEnableHadSchemaMismatch, true);

                        if (schemaGuard.IsBlocking && identityMode == SchemaIdentityMode.Strict)
                        {
                            Volatile.Write(ref _lastEnableBlockedBySchemaMismatch, true);
                            throw new InvalidDataException(schemaGuard.Message);
                        }

                        if (schemaGuard.State != FoxRunReplaySchemaGuardState.Match)
                        {
                            if (schemaGuard.State == FoxRunReplaySchemaGuardState.Mismatch
                                && identityMode == SchemaIdentityMode.Warn)
                                _logger.LogWarning(CreateWarnModeSchemaMismatchMessage(schemaGuard));
                            else
                                _logger.LogWarning(schemaGuard.Message);
                        }
                    }

                    if (summary?.Schemas != null)
                    {
                        _summarySchemas = new Dictionary<ushort, McapSchema>();
                        foreach (var s in summary.Schemas)
                            _summarySchemas[s.Id] = s;
                    }

                    if (summary?.Channels != null)
                    {
                        var modeWarning = ReplayCoordinateModeGuard.FindMismatch(
                            summary.Channels, currentCoordinateMode, filePath);
                        if (modeWarning != null)
                            _logger.LogWarning(modeWarning);
                    }

                    _channelTopicMap = new Dictionary<ushort, string>();
                    _channelContextMap = new Dictionary<ushort, ReplayChannelContext>();
                    _channelBehaviorMap = new Dictionary<ushort, ReplayChannelBehavior>();
                    var channels = _replayEngine.Channels;
                    if (channels != null)
                        foreach (var c in channels)
                        {
                            _channelTopicMap[c.Id] = c.Topic;
                            var s = _summarySchemas != null && _summarySchemas.TryGetValue(c.SchemaId, out var schema) ? schema : null;
                            _channelContextMap[c.Id] = new ReplayChannelContext(c, s);
                            _channelBehaviorMap[c.Id] = ReplayChannelBehaviorClassifier.ClassifyChannel(
                                c.MessageEncoding,
                                s?.Name,
                                s?.Encoding,
                                c.Topic);
                        }

                    replayStartTimeNs = _replayEngine.StartTimeNs;
                    replayEndTimeNs = _replayEngine.EndTimeNs;
                }

                _clock?.EnableRange(replayStartTimeNs, replayEndTimeNs);

                lock (_replayEngineLock)
                {
                    if (!ReferenceEquals(_replayEngine, loadedEngine))
                        return;

                    _replaySessionId = NextReplaySessionId(_replaySessionId);
                    _replayEngine.Play();
                    Volatile.Write(ref _replayEnabled, true);
                    _panelHistory.ResetDebounce();
                }
            }
            catch (Exception ex)
            {
                lock (_replayEngineLock)
                {
                    Volatile.Write(ref _lastEnableFailureMessage, ex.Message ?? string.Empty);
                    if (ReferenceEquals(_replayEngine, loadedEngine))
                    {
                        _replayEngine?.Dispose();
                        _replayEngine = null;
                        _summarySchemas = null;
                        _channelTopicMap = null;
                        _channelContextMap = null;
                        _channelBehaviorMap = null;
                        Volatile.Write(ref _replayEnabled, false);
                    }
                }

                _logger.LogError($"Failed to load MCAP replay '{filePath}': {ex.Message}");
            }
        }

        private static string CreateWarnModeSchemaMismatchMessage(FoxRunReplaySchemaGuardResult result)
        {
            return "FoxRun replay schema mismatch.\n" +
                   "Recorded: " + ShortHash(result.RecordedGlobalManifestHash) + "\n" +
                   "Current:  " + ShortHash(result.CurrentGlobalManifestHash) + "\n" +
                   "Warn mode: replay will continue.";
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "<missing>";

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        /// <summary>Dispose the replay engine and disable replay.</summary>
        public void Disable()
        {
            lock (_replayEngineLock)
            {
                _replayEngine?.Dispose();
                _replayEngine = null;
                Volatile.Write(ref _replayEnabled, false);
                _summarySchemas = null;
                _channelTopicMap = null;
                _channelContextMap = null;
                _channelBehaviorMap = null;
                _panelHistory.ResetDebounce();
                _pendingReplayCallbacks.Clear();
            }
        }

        /// <summary>Dispose the replay engine and all associated resources.</summary>
        public void Dispose() => Disable();
    }
}
