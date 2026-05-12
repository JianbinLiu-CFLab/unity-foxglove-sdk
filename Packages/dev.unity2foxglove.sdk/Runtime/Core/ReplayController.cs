// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Manages MCAP replay lifecycle — loads an .mcap file, registers
// replay channels on the session, and ticks the replay engine each frame
// to emit messages in log-time order. Forwards replay data to listeners.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Manages the MCAP replay lifecycle. Loads an .mcap file, registers
    /// channels on the session (with replay ID prefix), and ticks the
    /// replay engine each frame. Tracks schema/channel topic maps for
    /// metadata forwarding and coordinate-mode warn-on-mismatch.
    /// </summary>
    public class ReplayController : IDisposable
    {
        /// <summary>Active replay engine, or null when not replaying.</summary>
        private McapReplayEngine _replayEngine;
        /// <summary>Whether replay has been enabled and successfully loaded.</summary>
        private bool _replayEnabled;
        /// <summary>Schema lookup by ID, built from the MCAP summary.</summary>
        private Dictionary<ushort, McapSchema> _summarySchemas;
        /// <summary>Channel topic lookup by channel ID for forwarding messages.</summary>
        private Dictionary<ushort, string> _channelTopicMap;
        /// <summary>Reusable replay tick output buffer to avoid per-frame list allocations.</summary>
        private readonly List<McapMessage> _replayTickBuffer = new();
        /// <summary>Reusable paused-seek snapshot buffer to avoid per-request list allocations.</summary>
        private readonly List<McapMessage> _replaySnapshotBuffer = new();
        /// <summary>
        /// Guards the MCAP replay cursor. Playback control requests arrive on
        /// the WebSocket receive thread, while replay ticks run on Unity's
        /// main thread, so cursor mutation must be serialized.
        /// </summary>
        private readonly object _replayEngineLock = new();
        private readonly IFoxgloveLogger _logger;

        /// <summary>Whether replay is enabled and the engine is loaded.</summary>
        public bool IsEnabled => _replayEnabled;
        /// <summary>Active replay engine instance; null when not replaying.</summary>
        public McapReplayEngine Engine => _replayEngine;

        /// <summary>
        /// Fires when the replay engine outputs a message.
        /// <para>First argument is the topic, second is the raw message data.</para>
        /// </summary>
        public event Action<string, byte[]> OnReplayMessage;

        /// <summary>Test-only hook to fire a replay message without loading an MCAP file.</summary>
        internal void FireForTests(string topic, byte[] data) => OnReplayMessage?.Invoke(topic, data);

        public ReplayController(IFoxgloveLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Load an MCAP file for replay.
        /// <para>Disables any previous replay state first. If recording is active,
        /// replay is declined with a warning. Sets the playback clock range, then starts playback.</para>
        /// </summary>
        public void Enable(string filePath, PlaybackClock playbackClock, bool recordingEnabled, string currentCoordinateMode = "")
        {
            lock (_replayEngineLock)
            {
                // Clean any previous replay state to avoid leaking old engine/stream
                Disable();

                if (recordingEnabled)
                {
                    _logger.LogWarning("Recording and Replay cannot both be enabled. Replay disabled.");
                    return;
                }
                try
                {
                    _replayEngine = new McapReplayEngine();
                    _replayEngine.Load(filePath);
                    var summary = _replayEngine.Summary;

                    if (summary?.Schemas != null)
                    {
                        _summarySchemas = new Dictionary<ushort, McapSchema>();
                        foreach (var s in summary.Schemas)
                            _summarySchemas[s.Id] = s;
                    }

                    if (summary?.Channels != null)
                    {
                        foreach (var ch in summary.Channels)
                        {
                            if (ch.Metadata != null && ch.Metadata.TryGetValue("coordinate_mode", out var mcapMode)
                                && !string.IsNullOrEmpty(mcapMode))
                            {
                                if (mcapMode != currentCoordinateMode)
                                    _logger.LogWarning($"MCAP '{Path.GetFileName(filePath)}' was recorded with coordinate_mode '{mcapMode}', " +
                                        $"but current mode is '{currentCoordinateMode}'. Mismatch may cause incorrect object transforms.");
                                break;
                            }
                        }
                    }

                    _channelTopicMap = new Dictionary<ushort, string>();
                    var channels = _replayEngine.Channels;
                    if (channels != null)
                        foreach (var c in channels)
                            _channelTopicMap[c.Id] = c.Topic;

                    playbackClock?.EnableRange(_replayEngine.StartTimeNs, _replayEngine.EndTimeNs);
                    _replayEngine.Play();
                    _replayEnabled = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to load MCAP replay: {ex.Message}");
                    _replayEngine?.Dispose();
                    _replayEngine = null;
                    _replayEnabled = false;
                }
            }
        }

        /// <summary>Dispose the replay engine and disable replay.</summary>
        public void Disable()
        {
            lock (_replayEngineLock)
            {
                _replayEngine?.Dispose();
                _replayEngine = null;
                _replayEnabled = false;
            }
        }

        /// <summary>Register replay channels on the session with replay ID prefix.</summary>
        public void RegisterChannels(FoxgloveSession session)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null || !_replayEngine.IsLoaded) return;
                var channels = _replayEngine.Channels;
                if (channels == null) return;
                foreach (var ch in channels)
                {
                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | ch.Id);
                    var schema = _summarySchemas != null && _summarySchemas.TryGetValue(ch.SchemaId, out var s) ? s : null;
                    session.RegisterChannel(new AdvertiseChannel
                    {
                        Id = replayId,
                        Topic = ch.Topic,
                        Encoding = ch.MessageEncoding,
                        SchemaName = schema?.Name ?? "",
                        SchemaEncoding = schema?.Encoding ?? "",
                        Schema = EncodeSchemaForAdvertise(schema)
                    });
                }
            }
        }

        private static string EncodeSchemaForAdvertise(McapSchema schema)
        {
            if (schema?.Data == null || schema.Data.Length == 0) return "";
            if (string.Equals(schema.Encoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return Convert.ToBase64String(schema.Data);
            return System.Text.Encoding.UTF8.GetString(schema.Data);
        }

        /// <summary>
        /// Tick the replay engine, publishing messages whose log time is on or before <c>nowNs</c>.
        /// <para>Broadcasts replay time before message frames so seek-induced time jumps are observed before data.</para>
        /// </summary>
        public void Tick(FoxgloveSession session, ulong nowNs)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null) return;
                var messages = _replayEngine.Tick(nowNs, _replayTickBuffer);
                if (messages == null || messages.Count == 0) return;
                PublishMessages(session, messages, nowNs, "Tick");
            }
        }

        /// <summary>
        /// Publish the latest message at or before <paramref name="timeNs"/> for
        /// each replay channel. This refreshes Foxglove panels after paused
        /// seek/pause commands where normal ticking is intentionally stopped.
        /// </summary>
        public void PublishSnapshot(FoxgloveSession session, ulong timeNs)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null || session == null) return;
                var messages = _replayEngine.Snapshot(timeNs, _replaySnapshotBuffer);
                PublishMessages(session, messages, timeNs, "Snapshot");
            }
        }

        /// <summary>
        /// Apply the latest replay messages at or before <paramref name="timeNs"/>
        /// to local scene listeners without publishing MessageData to Foxglove.
        /// Used by paused seek/scrub so Unity can follow the timeline while the
        /// WebSocket playback stream stays in the official paused state.
        /// </summary>
        public void ApplySnapshotToScene(ulong timeNs)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null) return;
                var messages = _replayEngine.Snapshot(timeNs, _replaySnapshotBuffer);
                if (messages == null) return;
                foreach (var msg in messages)
                {
                    if (_channelTopicMap != null && _channelTopicMap.TryGetValue(msg.ChannelId, out var topic))
                        OnReplayMessage?.Invoke(topic, msg.Data);
                }
            }
        }

        private void PublishMessages(FoxgloveSession session, IReadOnlyList<McapMessage> messages, ulong? broadcastTimeNs, string source)
        {
            if (session == null) return;
            if (broadcastTimeNs.HasValue && broadcastTimeNs.Value > 0)
            {
                var frame = BinaryEncoding.EncodeTime(broadcastTimeNs.Value);
                if (FoxgloveReplayTrace.TryTime(source, broadcastTimeNs.Value, "data", out var trace))
                    _logger.LogWarning(trace);
                session.BroadcastReplayBinary(frame);
            }

            ulong latestLogTime = 0;
            if (messages != null)
            {
                var stampSnapshotAtSeekTime =
                    string.Equals(source, "Snapshot", StringComparison.Ordinal)
                    && broadcastTimeNs.HasValue;
                foreach (var msg in messages)
                {
                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                    string topic = null;
                    _channelTopicMap?.TryGetValue(msg.ChannelId, out topic);
                    var outgoingLogTime = stampSnapshotAtSeekTime ? broadcastTimeNs.Value : msg.LogTime;
                    session.PublishReplay(replayId, msg.Data, outgoingLogTime, source, topic);
                    if (msg.LogTime > latestLogTime) latestLogTime = msg.LogTime;

                    if (topic != null)
                        OnReplayMessage?.Invoke(topic, msg.Data);
                }
            }

            if (!broadcastTimeNs.HasValue && latestLogTime > 0)
            {
                if (FoxgloveReplayTrace.TryTime(source, latestLogTime, "data", out var trace))
                    _logger.LogWarning(trace);
                session.BroadcastReplayBinary(BinaryEncoding.EncodeTime(latestLogTime));
            }
        }

        /// <summary>Seek the replay engine to the given nanosecond timestamp.</summary>
        public void Seek(ulong timeNs)
        {
            lock (_replayEngineLock)
                _replayEngine?.Seek(timeNs);
        }
        /// <summary>Start or resume playback of the replay.</summary>
        public void Play()
        {
            lock (_replayEngineLock)
                _replayEngine?.Play();
        }
        /// <summary>Pause replay playback without disposing.</summary>
        public void Pause()
        {
            lock (_replayEngineLock)
                _replayEngine?.Pause();
        }
        /// <summary>Get the list of channels from the loaded MCAP file.</summary>
        public IReadOnlyList<McapChannel> GetChannels()
        {
            lock (_replayEngineLock)
                return _replayEngine?.Channels;
        }

        /// <summary>Dispose the replay engine and all associated resources.</summary>
        public void Dispose() => Disable();
    }
}
