// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
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
        /// <summary>Settled-scrub debounce before panel history is rebuilt, set to 250 ms.</summary>
        internal const ulong ScrubHistoryDebounceNs = 250_000_000UL;
        /// <summary>Maximum paused-scrub panel history window, set to the 30 seconds before the seek target.</summary>
        internal const ulong ScrubHistoryWindowNs = 30_000_000_000UL;
        /// <summary>Maximum settled history messages sent per Unity tick to avoid main-thread stalls.</summary>
        internal const int ScrubHistoryMaxMessagesPerTick = 256;
        /// <summary>Maximum messages retained for one settled history rebuild before transport headroom is applied.</summary>
        internal const int ScrubHistoryMaxMessagesPerRequest = 5000;
        /// <summary>Minimum transport frame headroom preserved while draining settled history messages.</summary>
        internal const int ScrubHistoryQueueReserveFrames = 32;
        /// <summary>Minimum transport byte headroom preserved while draining settled history messages.</summary>
        internal const int ScrubHistoryQueueReserveBytes = 512 * 1024;
        /// <summary>Approximate binary MessageData framing overhead used for replay history byte budgeting.</summary>
        private const int MessageDataFrameOverheadBytes = 32;

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
        private readonly List<McapMessage> _panelHistoryBuffer = new();
        private bool _panelHistoryActive;
        private int _panelHistoryOffset;
        private ulong _panelHistoryParkTimeNs;
        private bool _hasPanelHistoryTime;
        private ulong _lastPanelHistoryTimeNs;
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

        /// <summary>
        /// Creates a replay controller using the provided logger for warnings and
        /// playback diagnostics.
        /// </summary>
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
                    ValidateReplayFileForLoad(filePath);
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
                    _hasPanelHistoryTime = false;
                    _lastPanelHistoryTimeNs = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to load MCAP replay '{filePath}': {ex.Message}");
                    _replayEngine?.Dispose();
                    _replayEngine = null;
                    _summarySchemas = null;
                    _channelTopicMap = null;
                    _replayEnabled = false;
                }
            }
        }

        private static void ValidateReplayFileForLoad(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidDataException("Replay MCAP file path is empty.");

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Replay MCAP file does not exist: {fullPath}", fullPath);

            var info = new FileInfo(fullPath);
            const int minFileBytes =
                McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            if (info.Length < minFileBytes)
                throw new InvalidDataException(
                    $"Replay MCAP file is too small to be finalized: {fullPath} ({info.Length} bytes).");

            var expectedMagic = McapWriter.Magic;
            var actualMagic = new byte[McapWriter.MagicLength];
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReadExactReplayMagic(stream, actualMagic);
            if (!MatchesReplayMagic(actualMagic, expectedMagic))
                throw new InvalidDataException($"Replay MCAP file does not start with MCAP magic: {fullPath}.");

            stream.Seek(-McapWriter.MagicLength, SeekOrigin.End);
            ReadExactReplayMagic(stream, actualMagic);
            if (!MatchesReplayMagic(actualMagic, expectedMagic))
                throw new InvalidDataException(
                    $"Replay MCAP file is not finalized or is truncated (missing trailing magic): {fullPath} ({info.Length} bytes). Stop recording cleanly and select a finalized .mcap file.");
        }

        private static void ReadExactReplayMagic(Stream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("Replay MCAP file ended while reading magic bytes.");
                offset += read;
            }
        }

        private static bool MatchesReplayMagic(byte[] actual, byte[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
                return false;
            for (var i = 0; i < expected.Length; i++)
                if (actual[i] != expected[i])
                    return false;
            return true;
        }

        /// <summary>Dispose the replay engine and disable replay.</summary>
        public void Disable()
        {
            lock (_replayEngineLock)
            {
                _replayEngine?.Dispose();
                _replayEngine = null;
                _replayEnabled = false;
                _summarySchemas = null;
                _channelTopicMap = null;
                _panelHistoryBuffer.Clear();
                _panelHistoryActive = false;
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = 0;
                _hasPanelHistoryTime = false;
                _lastPanelHistoryTimeNs = 0;
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
                PublishMessages(session, messages, nowNs, "Tick", forwardToScene: true);
            }
        }

        /// <summary>
        /// Publish historical messages through <paramref name="timeNs"/> so
        /// Foxglove panels can rebuild time-series views after a paused seek.
        /// The scene uses a separate latest-state snapshot path.
        /// </summary>
        public void PublishSnapshot(FoxgloveSession session, ulong timeNs)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null || session == null) return;
                var startNs = _replayEngine.StartTimeNs;
                var clampedTo = timeNs > _replayEngine.EndTimeNs ? _replayEngine.EndTimeNs : timeNs;
                if (clampedTo < startNs) clampedTo = startNs;

                ulong fromNs;
                if (_hasPanelHistoryTime && clampedTo >= _lastPanelHistoryTimeNs)
                    fromNs = _lastPanelHistoryTimeNs < ulong.MaxValue ? _lastPanelHistoryTimeNs + 1UL : ulong.MaxValue;
                else
                    fromNs = clampedTo > ScrubHistoryWindowNs ? clampedTo - ScrubHistoryWindowNs : startNs;
                if (fromNs < startNs) fromNs = startNs;

                _replayEngine.History(fromNs, clampedTo, _panelHistoryBuffer, ScrubHistoryMaxMessagesPerRequest);
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = clampedTo;
                _panelHistoryActive = true;
                DrainPanelHistoryLocked(session);
            }
        }

        /// <summary>
        /// Sends the next batch of buffered scrub-history messages to the session,
        /// respecting replay queue headroom and per-tick history budgets.
        /// </summary>
        /// <param name="session">Session that receives replay history frames.</param>
        public void DrainPanelHistory(FoxgloveSession session)
        {
            lock (_replayEngineLock)
            {
                DrainPanelHistoryLocked(session);
            }
        }

        /// <summary>
        /// Cancels the current panel-history drain while leaving the last observed
        /// panel request time intact for debounce decisions.
        /// </summary>
        public void CancelPanelHistory()
        {
            lock (_replayEngineLock)
            {
                _panelHistoryBuffer.Clear();
                _panelHistoryActive = false;
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = 0;
            }
        }

        /// <summary>
        /// Clears panel-history progress and debounce state after replay stops or
        /// the active replay source changes.
        /// </summary>
        public void ResetPanelHistoryProgress()
        {
            lock (_replayEngineLock)
            {
                _panelHistoryBuffer.Clear();
                _panelHistoryActive = false;
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = 0;
                _hasPanelHistoryTime = false;
                _lastPanelHistoryTimeNs = 0;
            }
        }

        private void DrainPanelHistoryLocked(FoxgloveSession session)
        {
            if (session == null || !_panelHistoryActive) return;

            var frameBudget = ScrubHistoryMaxMessagesPerTick;
            var byteBudget = int.MaxValue;
            if (session.TryGetReplayQueueHeadroom(
                ScrubHistoryQueueReserveFrames,
                ScrubHistoryQueueReserveBytes,
                out var queueFrameHeadroom,
                out var queueByteHeadroom))
            {
                frameBudget = Math.Min(frameBudget, queueFrameHeadroom);
                byteBudget = queueByteHeadroom;
            }

            if (frameBudget <= 0 || byteBudget <= 0) return;

            var sentFrames = 0;
            var sentBytes = 0;
            while (_panelHistoryOffset < _panelHistoryBuffer.Count && sentFrames < frameBudget)
            {
                var msg = _panelHistoryBuffer[_panelHistoryOffset];
                var estimatedBytes = EstimateMessageDataFrameBytes(msg);
                if (sentBytes + estimatedBytes > byteBudget)
                    break;

                var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                string topic = null;
                _channelTopicMap?.TryGetValue(msg.ChannelId, out topic);
                session.PublishReplay(replayId, msg.Data, msg.LogTime, "History", topic);
                _panelHistoryOffset++;
                sentFrames++;
                sentBytes += estimatedBytes;
            }

            if (_panelHistoryOffset >= _panelHistoryBuffer.Count)
            {
                if (_panelHistoryParkTimeNs > 0)
                {
                    if (FoxgloveReplayTrace.TryTime("History", _panelHistoryParkTimeNs, "data", out var trace))
                        _logger.LogWarning(trace);
                    session.BroadcastReplayBinary(BinaryEncoding.EncodeTime(_panelHistoryParkTimeNs));
                }

                _lastPanelHistoryTimeNs = _panelHistoryParkTimeNs;
                _hasPanelHistoryTime = true;
                _panelHistoryBuffer.Clear();
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = 0;
                _panelHistoryActive = false;
            }
        }

        private static int EstimateMessageDataFrameBytes(McapMessage message)
        {
            return MessageDataFrameOverheadBytes + (message.Data?.Length ?? 0);
        }

        /// <summary>
        /// Apply the latest replay messages at or before <paramref name="timeNs"/>
        /// to local scene listeners without publishing MessageData to Foxglove.
        /// Used by paused seek/scrub so Unity can follow the timeline without
        /// relying on the separate Foxglove panel snapshot stream.
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

        private void PublishMessages(FoxgloveSession session, IReadOnlyList<McapMessage> messages, ulong? broadcastTimeNs, string source, bool forwardToScene)
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
                foreach (var msg in messages)
                {
                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                    string topic = null;
                    _channelTopicMap?.TryGetValue(msg.ChannelId, out topic);
                    session.PublishReplay(replayId, msg.Data, msg.LogTime, source, topic);
                    if (msg.LogTime > latestLogTime) latestLogTime = msg.LogTime;

                    if (forwardToScene && topic != null)
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
