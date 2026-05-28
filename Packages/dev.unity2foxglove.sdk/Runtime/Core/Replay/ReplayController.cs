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
using Unity.FoxgloveSDK.Components;
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
        private const int MaxPendingReplayCallbacks = 8192;
        private const long MaxPendingReplayCallbackPayloadBytes = 64L * 1024L * 1024L;
        private const long ReplayCallbackOverflowWarningIntervalTicks = 5L * 1000L * 1000L * 10L;

        /// <summary>Active replay engine, or null when not replaying.</summary>
        private McapReplayEngine _replayEngine;
        /// <summary>Whether replay has been enabled and successfully loaded.</summary>
        private bool _replayEnabled;
        /// <summary>Schema lookup by ID, built from the MCAP summary.</summary>
        private Dictionary<ushort, McapSchema> _summarySchemas;
        /// <summary>Channel topic lookup by channel ID for forwarding messages.</summary>
        private Dictionary<ushort, string> _channelTopicMap;
        /// <summary>Channel lookup by channel ID for context-rich scene forwarding.</summary>
        private Dictionary<ushort, McapChannel> _channelMap;
        /// <summary>Behavior lookup by channel ID for replay pose ownership arbitration.</summary>
        private Dictionary<ushort, ReplayChannelBehavior> _channelBehaviorMap;
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
        private bool _lastEnableHadSchemaMismatch;
        private bool _lastEnableBlockedBySchemaMismatch;
        private string _lastEnableFailureMessage = string.Empty;
        /// <summary>
        /// Guards the MCAP replay cursor. Playback control requests arrive on
        /// the WebSocket receive thread, while replay ticks run on Unity's
        /// main thread, so cursor mutation must be serialized.
        /// </summary>
        private readonly object _replayEngineLock = new();
        private readonly IFoxgloveLogger _logger;
        private readonly BoundedEventQueue<ReplayCallbackDispatch> _pendingReplayCallbacks =
            new(MaxPendingReplayCallbacks, MaxPendingReplayCallbackPayloadBytes, MeasureReplayCallbackPayloadBytes);
        private long _lastReplayCallbackOverflowWarningTicks;

        /// <summary>Whether replay is enabled and the engine is loaded.</summary>
        public bool IsEnabled => _replayEnabled;
        /// <summary>Whether the most recent replay enable attempt observed a confirmed FoxRun schema mismatch.</summary>
        public bool LastEnableHadSchemaMismatch => _lastEnableHadSchemaMismatch;
        /// <summary>Whether the most recent replay enable attempt was blocked by a confirmed FoxRun schema mismatch.</summary>
        public bool LastEnableBlockedBySchemaMismatch => _lastEnableBlockedBySchemaMismatch;
        /// <summary>Message from the most recent failed replay enable attempt, or an empty string.</summary>
        public string LastEnableFailureMessage => _lastEnableFailureMessage;
        /// <summary>
        /// Active replay engine instance; null when not replaying.
        /// <para>The returned engine is a short-lived snapshot. Do not retain it across runtime ticks.</para>
        /// </summary>
        public McapReplayEngine Engine
        {
            get
            {
                lock (_replayEngineLock)
                    return _replayEngine;
            }
        }

        /// <summary>
        /// Fires when the replay engine outputs a message.
        /// <para>First argument is the topic, second is the raw message data.</para>
        /// </summary>
        public event Action<string, byte[]> OnReplayMessage;

        /// <summary>
        /// Fires when replay data is forwarded with channel, schema, and log-time context.
        /// </summary>
        public event Action<ReplayMessageContext> OnReplayMessageContext;

        /// <summary>
        /// Fires after a replay batch has been forwarded to scene listeners.
        /// </summary>
        public event Action<ReplayBatchContext> OnReplayBatchCompleted;

        /// <summary>Test-only hook to fire a replay message without loading an MCAP file.</summary>
        internal void FireForTests(string topic, byte[] data)
        {
            TryQueueReplayCallback(ReplayCallbackDispatch.ForMessage(new ReplayMessageContext(
                0,
                topic,
                string.Empty,
                string.Empty,
                string.Empty,
                0UL,
                0UL,
                data ?? Array.Empty<byte>())));
            DrainReplayCallbacks();
        }

        /// <summary>Test-only hook to fire a context-rich replay message without loading an MCAP file.</summary>
        internal void FireContextForTests(ReplayMessageContext context)
        {
            TryQueueReplayCallback(ReplayCallbackDispatch.ForMessage(context));
            DrainReplayCallbacks();
        }

        /// <summary>Test-only hook to fire a replay batch boundary without loading an MCAP file.</summary>
        internal void FireBatchCompletedForTests(ReplayBatchContext context)
        {
            TryQueueReplayCallback(ReplayCallbackDispatch.ForBatch(context));
            DrainReplayCallbacks();
        }

        /// <summary>
        /// Creates a replay controller using the provided logger for warnings and
        /// playback diagnostics. Uses the supplied recording state reader for
        /// mutual-exclusion checks and the clock for playback range control.
        /// </summary>
        public ReplayController(IFoxgloveLogger logger, IRecordingStateReader recordingState, IRangePlaybackClock clock)
        {
            _logger = logger;
            _recordingState = recordingState;
            _clock = clock;
        }

        /// <summary>
        /// Creates a replay controller using the provided logger for warnings and
        /// playback diagnostics.
        /// </summary>
        [Obsolete("Use ReplayController(IFoxgloveLogger, IRecordingStateReader, IRangePlaybackClock) instead.")]
        public ReplayController(IFoxgloveLogger logger) : this(logger, null, null) { }

        private readonly IRecordingStateReader _recordingState;
        private readonly IRangePlaybackClock _clock;

        /// <summary>
        /// Load an MCAP file for replay with the default Strict schema identity mode.
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
            lock (_replayEngineLock)
            {
                // Clean any previous replay state to avoid leaking old engine/stream
                Disable();
                _lastEnableHadSchemaMismatch = false;
                _lastEnableBlockedBySchemaMismatch = false;
                _lastEnableFailureMessage = string.Empty;

                if (recordingEnabled)
                {
                    _logger.LogWarning("Recording and Replay cannot both be enabled. Replay disabled.");
                    return;
                }
                try
                {
                    _replayEngine = new McapReplayEngine(_logger);
                    ValidateReplayFileForLoad(filePath);
                    _replayEngine.Load(filePath);
                    var summary = _replayEngine.Summary;
                    if (identityMode != SchemaIdentityMode.Off)
                    {
                        var schemaGuard = ReplaySchemaGuard.Evaluate(_replayEngine);
                        if (schemaGuard.State == FoxRunReplaySchemaGuardState.Mismatch)
                            _lastEnableHadSchemaMismatch = true;

                        if (schemaGuard.IsBlocking && identityMode == SchemaIdentityMode.Strict)
                        {
                            _lastEnableBlockedBySchemaMismatch = true;
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
                    _channelMap = new Dictionary<ushort, McapChannel>();
                    _channelBehaviorMap = new Dictionary<ushort, ReplayChannelBehavior>();
                    var channels = _replayEngine.Channels;
                    if (channels != null)
                        foreach (var c in channels)
                        {
                            _channelTopicMap[c.Id] = c.Topic;
                            _channelMap[c.Id] = c;
                            var s = _summarySchemas != null && _summarySchemas.TryGetValue(c.SchemaId, out var schema) ? schema : null;
                            _channelBehaviorMap[c.Id] = ReplayChannelBehaviorClassifier.ClassifyChannel(
                                c.MessageEncoding,
                                s?.Name,
                                s?.Encoding,
                                c.Topic);
                        }

                    _clock?.EnableRange(_replayEngine.StartTimeNs, _replayEngine.EndTimeNs);
                    _replayEngine.Play();
                    _replayEnabled = true;
                    _hasPanelHistoryTime = false;
                    _lastPanelHistoryTimeNs = 0;
                }
                catch (Exception ex)
                {
                    _lastEnableFailureMessage = ex.Message ?? string.Empty;
                    _logger.LogError($"Failed to load MCAP replay '{filePath}': {ex.Message}");
                    _replayEngine?.Dispose();
                    _replayEngine = null;
                    _summarySchemas = null;
                    _channelTopicMap = null;
                    _channelMap = null;
                    _channelBehaviorMap = null;
                    _replayEnabled = false;
                }
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
                _channelMap = null;
                _channelBehaviorMap = null;
                _panelHistoryBuffer.Clear();
                _panelHistoryActive = false;
                _panelHistoryOffset = 0;
                _panelHistoryParkTimeNs = 0;
                _hasPanelHistoryTime = false;
                _lastPanelHistoryTimeNs = 0;
                _pendingReplayCallbacks.Clear();
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
            => Tick(session, nowNs, deferCallbacks: false);

        /// <summary>
        /// Tick the replay engine with optional deferred scene callback draining.
        /// Runtime ticks defer callbacks until after playback control locks are released.
        /// </summary>
        public void Tick(FoxgloveSession session, ulong nowNs, bool deferCallbacks)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null) return;
                var messages = _replayEngine.Tick(nowNs, _replayTickBuffer);
                if (messages == null || messages.Count == 0) return;
                PublishMessages(session, messages, nowNs, "Tick", forwardToScene: true);
            }

            if (!deferCallbacks)
                DrainReplayCallbacks();
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
            => ApplySnapshotToScene(timeNs, deferCallbacks: false);

        /// <summary>
        /// Apply replay snapshot messages with optional deferred scene callback draining.
        /// </summary>
        public void ApplySnapshotToScene(ulong timeNs, bool deferCallbacks)
        {
            lock (_replayEngineLock)
            {
                if (!_replayEnabled || _replayEngine == null) return;
                var messages = _replayEngine.Snapshot(timeNs, _replaySnapshotBuffer);
                if (messages == null) return;
                foreach (var msg in messages)
                {
                    ForwardReplayMessageToScene(msg);
                }

                FireReplayBatchCompleted(messages, timeNs, "Snapshot");
            }

            if (!deferCallbacks)
                DrainReplayCallbacks();
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
                        ForwardReplayMessageToScene(msg);
                }

                if (forwardToScene)
                    FireReplayBatchCompleted(messages, latestLogTime, source);
            }

            if (!broadcastTimeNs.HasValue && latestLogTime > 0)
            {
                if (FoxgloveReplayTrace.TryTime(source, latestLogTime, "data", out var trace))
                    _logger.LogWarning(trace);
                session.BroadcastReplayBinary(BinaryEncoding.EncodeTime(latestLogTime));
            }
        }

        private void ForwardReplayMessageToScene(McapMessage message)
        {
            var context = CreateReplayMessageContext(message);
            TryQueueReplayCallback(ReplayCallbackDispatch.ForMessage(context));
        }

        private void FireReplayBatchCompleted(IReadOnlyList<McapMessage> messages, ulong batchLogTimeNs, string source)
        {
            if (messages == null || messages.Count == 0)
                return;

            TryQueueReplayCallback(ReplayCallbackDispatch.ForBatch(new ReplayBatchContext(
                batchLogTimeNs,
                _replayEngine?.StartTimeNs ?? 0UL,
                messages.Count,
                source)));
        }

        /// <summary>
        /// Drain replay callbacks outside replay/playback locks so scene listeners
        /// cannot stall cursor mutation or abort the owning replay tick.
        /// </summary>
        public void DrainReplayCallbacks()
        {
            List<ReplayCallbackDispatch> callbacks;
            lock (_replayEngineLock)
            {
                if (_pendingReplayCallbacks.Count == 0)
                    return;

                callbacks = new List<ReplayCallbackDispatch>(_pendingReplayCallbacks.Count);
                while (_pendingReplayCallbacks.TryDequeue(out var callback))
                    callbacks.Add(callback);
            }

            foreach (var callback in callbacks)
            {
                if (callback.IsBatch)
                {
                    InvokeReplayBatchCompleted(callback.BatchContext.Value);
                    continue;
                }

                var context = callback.MessageContext.Value;
                InvokeReplayMessageContext(context);
                InvokeReplayMessage(context.Topic, context.Payload);
            }
        }

        private bool TryQueueReplayCallback(ReplayCallbackDispatch dispatch)
        {
            if (_pendingReplayCallbacks.TryEnqueue(dispatch, out var overflow))
                return true;

            WarnReplayCallbackQueueOverflow(overflow);
            return false;
        }

        private void WarnReplayCallbackQueueOverflow(BoundedEventQueueOverflow overflow)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var previousTicks = System.Threading.Interlocked.Read(ref _lastReplayCallbackOverflowWarningTicks);
            if (nowTicks - previousTicks < ReplayCallbackOverflowWarningIntervalTicks)
                return;

            if (System.Threading.Interlocked.CompareExchange(
                    ref _lastReplayCallbackOverflowWarningTicks,
                    nowTicks,
                    previousTicks) != previousTicks)
                return;

            _logger?.LogWarning(
                "Dropped replay scene callback because the deferred replay callback queue is full. queuedCallbacks="
                + overflow.QueuedFrames
                + " queuedPayloadBytes="
                + overflow.QueuedBytes
                + " rejectedPayloadBytes="
                + overflow.RejectedBytes
                + " droppedCallbacks="
                + overflow.DroppedCount
                + " droppedPayloadBytes="
                + overflow.DroppedBytes
                + " limits="
                + MaxPendingReplayCallbacks
                + "/"
                + MaxPendingReplayCallbackPayloadBytes
                + " bytes.");
        }

        private static int MeasureReplayCallbackPayloadBytes(ReplayCallbackDispatch dispatch)
        {
            if (dispatch.IsBatch || !dispatch.MessageContext.HasValue)
                return 0;

            return dispatch.MessageContext.Value.Payload?.Length ?? 0;
        }

        private void InvokeReplayMessage(string topic, byte[] data)
        {
            var handlers = OnReplayMessage;
            if (handlers == null)
                return;

            foreach (Action<string, byte[]> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(topic, data);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Replay message listener failed: {ex.Message}");
                }
            }
        }

        private void InvokeReplayMessageContext(ReplayMessageContext context)
        {
            var handlers = OnReplayMessageContext;
            if (handlers == null)
                return;

            foreach (Action<ReplayMessageContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Replay message context listener failed: {ex.Message}");
                }
            }
        }

        private void InvokeReplayBatchCompleted(ReplayBatchContext context)
        {
            var handlers = OnReplayBatchCompleted;
            if (handlers == null)
                return;

            foreach (Action<ReplayBatchContext> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(context);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Replay batch listener failed: {ex.Message}");
                }
            }
        }

        private ReplayMessageContext CreateReplayMessageContext(McapMessage message)
        {
            McapChannel channel = null;
            _channelMap?.TryGetValue(message.ChannelId, out channel);
            McapSchema schema = null;
            if (channel != null && _summarySchemas != null)
                _summarySchemas.TryGetValue(channel.SchemaId, out schema);
            var logTimeNs = message.LogTime;
            var replayStartTimeNs = _replayEngine?.StartTimeNs ?? 0UL;

            return new ReplayMessageContext(
                channelId: message.ChannelId,
                topic: channel?.Topic ?? string.Empty,
                messageEncoding: channel?.MessageEncoding ?? string.Empty,
                schemaName: schema?.Name ?? string.Empty,
                schemaEncoding: schema?.Encoding ?? string.Empty,
                logTimeNs: logTimeNs,
                replayStartTimeNs: replayStartTimeNs,
                payload: message.Data);
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

        /// <summary>Return the behavior class loaded for a replay channel id.</summary>
        public ReplayChannelBehavior GetChannelBehavior(ushort channelId)
        {
            lock (_replayEngineLock)
                return _channelBehaviorMap != null && _channelBehaviorMap.TryGetValue(channelId, out var behavior)
                    ? behavior
                    : ReplayChannelBehavior.NotLoaded;
        }

        /// <summary>Dispose the replay engine and all associated resources.</summary>
        public void Dispose() => Disable();

        private readonly struct ReplayCallbackDispatch
        {
            private ReplayCallbackDispatch(ReplayMessageContext? messageContext, ReplayBatchContext? batchContext, bool isBatch)
            {
                MessageContext = messageContext;
                BatchContext = batchContext;
                IsBatch = isBatch;
            }

            public ReplayMessageContext? MessageContext { get; }
            public ReplayBatchContext? BatchContext { get; }
            public bool IsBatch { get; }

            public static ReplayCallbackDispatch ForMessage(ReplayMessageContext context)
                => new ReplayCallbackDispatch(context, null, isBatch: false);

            public static ReplayCallbackDispatch ForBatch(ReplayBatchContext context)
                => new ReplayCallbackDispatch(null, context, isBatch: true);
        }
    }
}
