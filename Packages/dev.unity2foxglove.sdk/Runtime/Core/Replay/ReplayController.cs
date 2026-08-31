// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Manages MCAP replay lifecycle — loads an .mcap file, registers
// replay channels on the session, and ticks the replay engine each frame
// to emit messages in log-time order. Forwards replay data to listeners.

using System;
using System.Collections.Generic;
using System.Threading;
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
    public partial class ReplayController : IDisposable
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
        /// <summary>Combined channel/schema/topic lookup for per-message replay hot paths.</summary>
        private Dictionary<ushort, ReplayChannelContext> _channelContextMap;
        /// <summary>Behavior lookup by channel ID for replay pose ownership arbitration.</summary>
        private Dictionary<ushort, ReplayChannelBehavior> _channelBehaviorMap;
        /// <summary>Reusable replay tick output buffer to avoid per-frame list allocations.</summary>
        private readonly List<McapMessage> _replayTickBuffer = new();
        /// <summary>Reusable paused-seek snapshot buffer to avoid per-request list allocations.</summary>
        private readonly List<McapMessage> _replaySnapshotBuffer = new();
        private readonly ReplayPanelHistoryBuffer _panelHistory = new();
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
        private readonly List<ReplayCallbackDispatch> _drainBuffer = new();
        private readonly object _replayCallbackDrainGate = new();
        private long _lastReplayCallbackOverflowWarningTicks;
        private ulong _replaySessionId;
        private readonly object _replayHandlersGate = new();
        private bool _isDrainingReplayCallbacks;
        // Monotonic fence that invalidates callbacks transferred to a drain when replay stops.
        private long _replayCallbackGeneration;

        /// <summary>Whether replay is enabled and the engine is loaded.</summary>
        public bool IsEnabled => Volatile.Read(ref _replayEnabled);
        /// <summary>Whether the most recent replay enable attempt observed a confirmed FoxRun schema mismatch.</summary>
        public bool LastEnableHadSchemaMismatch => Volatile.Read(ref _lastEnableHadSchemaMismatch);
        /// <summary>Whether the most recent replay enable attempt was blocked by a confirmed FoxRun schema mismatch.</summary>
        public bool LastEnableBlockedBySchemaMismatch => Volatile.Read(ref _lastEnableBlockedBySchemaMismatch);
        /// <summary>Message from the most recent failed replay enable attempt, or an empty string.</summary>
        public string LastEnableFailureMessage => Volatile.Read(ref _lastEnableFailureMessage);
        /// <summary>
        /// Active replay engine instance; null when not replaying.
        /// <para>The returned engine is a short-lived snapshot. Do not retain it across runtime ticks.</para>
        /// <para>The engine may be disposed immediately after this property returns if replay is disabled.</para>
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
        private Action<string, byte[]>[] _replayMessageHandlers = Array.Empty<Action<string, byte[]>>();
        private Action<ReplayMessageContext>[] _replayMessageContextHandlers = Array.Empty<Action<ReplayMessageContext>>();
        private Action<ReplayBatchContext>[] _replayBatchCompletedHandlers = Array.Empty<Action<ReplayBatchContext>>();

        public event Action<string, byte[]> OnReplayMessage
        {
            add { AddHandler(ref _replayMessageHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayMessageHandlers, _replayHandlersGate, value); }
        }

        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add { AddHandler(ref _replayMessageContextHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayMessageContextHandlers, _replayHandlersGate, value); }
        }

        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add { AddHandler(ref _replayBatchCompletedHandlers, _replayHandlersGate, value); }
            remove { RemoveHandler(ref _replayBatchCompletedHandlers, _replayHandlersGate, value); }
        }

        private static void AddHandler<T>(ref T[] cache, object handlersGate, T handler) where T : Delegate
        {
            lock (handlersGate)
            {
                cache = ToTypedHandlerArray<T>(
                    Delegate.Combine(Delegate.Combine((Delegate[])(object)cache), handler));
            }
        }

        private static void RemoveHandler<T>(ref T[] cache, object handlersGate, T handler) where T : Delegate
        {
            lock (handlersGate)
            {
                cache = ToTypedHandlerArray<T>(
                    Delegate.Remove(Delegate.Combine((Delegate[])(object)cache), handler));
            }
        }

        // LINQ-free conversion: ReplayController must not import System.Linq (see 134-3K-2).
        private static T[] ToTypedHandlerArray<T>(Delegate combined) where T : Delegate
        {
            if (combined == null)
                return Array.Empty<T>();

            var invocationList = combined.GetInvocationList();
            var result = new T[invocationList.Length];
            for (var i = 0; i < invocationList.Length; i++)
                result[i] = (T)invocationList[i];
            return result;
        }

        /// <summary>Test-only hook to fire a replay message without loading an MCAP file.</summary>
        internal void FireForTests(string topic, byte[] data)
            => FireForTests(topic, data, replaySessionId: 0UL);

        /// <summary>Test-only hook to fire a replay message for a specific replay session.</summary>
        internal void FireForTests(string topic, byte[] data, ulong replaySessionId)
        {
            TryQueueReplayCallback(ReplayCallbackDispatch.ForMessage(new ReplayMessageContext(
                0,
                topic,
                string.Empty,
                string.Empty,
                string.Empty,
                0UL,
                0UL,
                data ?? Array.Empty<byte>(),
                replaySessionId: replaySessionId)));
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

        /// <summary>Register replay channels on the session with replay ID prefix.</summary>
        public void RegisterChannels(FoxgloveSession session)
        {
            lock (_replayEngineLock)
            {
                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null || !_replayEngine.IsLoaded) return;
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
                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null) return;
                var messages = _replayEngine.Tick(nowNs, _replayTickBuffer);
                if (messages == null || messages.Count == 0) return;
                PublishMessages(session, messages, nowNs, "Tick", forwardToScene: true);
            }

            if (!deferCallbacks)
                DrainReplayCallbacks();
        }

        /// <summary>
        /// Advance replay messages through <paramref name="timeNs"/> for Unity
        /// scene listeners only. This is used when Foxglove Remote files owns
        /// the data timeline; Foxglove already reads MCAP bytes directly, so
        /// Unity should not publish replay MessageData back over WebSocket.
        /// </summary>
        public void ApplyTickToScene(ulong timeNs)
            => ApplyTickToScene(timeNs, deferCallbacks: false);

        /// <summary>
        /// Advance replay messages for scene listeners with optional deferred
        /// callback draining.
        /// </summary>
        public void ApplyTickToScene(ulong timeNs, bool deferCallbacks)
        {
            lock (_replayEngineLock)
            {
                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null) return;
                // External-clock following: the scene MUST reach the Foxglove cursor
                // time every frame, or it lags further behind on every tick. The
                // engine's per-tick cap protects the *live* main-thread playback path
                // from bursts; it must not throttle external-cursor advance, or dense
                // (100Hz+ multi-topic) recordings accumulate unbounded delay. Per-frame
                // render-rate intervals are small; large jumps use the seek path, so an
                // uncapped drain here stays cheap.
                var savedCap = _replayEngine.MaxMessagesPerTick;
                _replayEngine.MaxMessagesPerTick = 0;
                try
                {
                    var messages = _replayEngine.Tick(timeNs, _replayTickBuffer);
                    if (messages == null || messages.Count == 0) return;
                    var expectedSceneCallbacks = 0;
                    var queuedSceneCallbacks = 0;
                    foreach (var msg in messages)
                        if (TryGetReplayTopic(msg.ChannelId, out _))
                        {
                            expectedSceneCallbacks++;
                            if (ForwardReplayMessageToScene(msg))
                                queuedSceneCallbacks++;
                        }
                    FireReplayBatchCompleted(
                        messages,
                        messages[messages.Count - 1].LogTime,
                        "ExternalCursor",
                        expectedSceneCallbacks,
                        queuedSceneCallbacks);
                }
                finally
                {
                    _replayEngine.MaxMessagesPerTick = savedCap;
                }
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
                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null || session == null) return;
                var startNs = _replayEngine.StartTimeNs;
                var clampedTo = timeNs > _replayEngine.EndTimeNs ? _replayEngine.EndTimeNs : timeNs;
                if (clampedTo < startNs) clampedTo = startNs;

                var fromNs = _panelHistory.GetHistoryFromTime(startNs, clampedTo, ScrubHistoryWindowNs);
                _replayEngine.History(fromNs, clampedTo, _panelHistory.Buffer, ScrubHistoryMaxMessagesPerRequest);
                _panelHistory.BeginDrain(clampedTo);
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
                _panelHistory.CancelDrain();
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
                _panelHistory.ResetDebounce();
            }
        }

        private void DrainPanelHistoryLocked(FoxgloveSession session)
        {
            _panelHistory.DrainLocked(
                session,
                _channelTopicMap,
                _logger,
                ScrubHistoryMaxMessagesPerTick,
                ScrubHistoryQueueReserveFrames,
                ScrubHistoryQueueReserveBytes);
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
                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null) return;
                var messages = _replayEngine.Snapshot(timeNs, _replaySnapshotBuffer);
                if (messages == null) return;
                var queuedSceneCallbacks = 0;
                foreach (var msg in messages)
                    if (ForwardReplayMessageToScene(msg))
                        queuedSceneCallbacks++;

                FireReplayBatchCompleted(
                    messages,
                    timeNs,
                    "Snapshot",
                    messages.Count,
                    queuedSceneCallbacks);
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
            var expectedSceneCallbacks = 0;
            var queuedSceneCallbacks = 0;
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                    TryGetReplayTopic(msg.ChannelId, out var topic);
                    session.PublishReplay(replayId, msg.Data, msg.LogTime, source, topic);
                    if (msg.LogTime > latestLogTime) latestLogTime = msg.LogTime;

                    if (forwardToScene && topic != null)
                    {
                        expectedSceneCallbacks++;
                        if (ForwardReplayMessageToScene(msg))
                            queuedSceneCallbacks++;
                    }
                }

                if (forwardToScene)
                    FireReplayBatchCompleted(
                        messages,
                        latestLogTime,
                        source,
                        expectedSceneCallbacks,
                        queuedSceneCallbacks);
            }

            if (!broadcastTimeNs.HasValue && latestLogTime > 0)
            {
                if (FoxgloveReplayTrace.TryTime(source, latestLogTime, "data", out var trace))
                    _logger.LogWarning(trace);
                session.BroadcastReplayBinary(BinaryEncoding.EncodeTime(latestLogTime));
            }
        }

        private bool ForwardReplayMessageToScene(McapMessage message)
        {
            var context = CreateReplayMessageContext(message);
            return TryQueueReplayCallback(ReplayCallbackDispatch.ForMessage(context));
        }

        private bool TryGetReplayTopic(ushort channelId, out string topic)
        {
            if (_channelContextMap != null && _channelContextMap.TryGetValue(channelId, out var replayContext))
            {
                topic = replayContext.Topic;
                return topic != null;
            }

            if (_channelTopicMap != null && _channelTopicMap.TryGetValue(channelId, out topic))
                return topic != null;

            topic = null;
            return false;
        }

        private void FireReplayBatchCompleted(
            IReadOnlyList<McapMessage> messages,
            ulong batchLogTimeNs,
            string source,
            int expectedMessageCount,
            int queuedMessageCount)
        {
            if (messages == null || expectedMessageCount <= 0)
                return;

            if (queuedMessageCount != expectedMessageCount)
            {
                _logger?.LogWarning(
                    "Skipped replay batch completion because scene callback admission was incomplete. expected="
                    + expectedMessageCount
                    + " queued="
                    + queuedMessageCount
                    + " source="
                    + source);
                return;
            }

            TryQueueReplayCallback(ReplayCallbackDispatch.ForBatch(new ReplayBatchContext(
                batchLogTimeNs,
                _replayEngine?.StartTimeNs ?? 0UL,
                expectedMessageCount,
                source,
                replaySessionId: _replaySessionId)));
        }

        /// <summary>
        /// Drain replay callbacks outside replay/playback locks so scene listeners
        /// cannot stall cursor mutation or abort the owning replay tick.
        /// </summary>
        public void DrainReplayCallbacks()
        {
            lock (_replayCallbackDrainGate)
            {
                if (_isDrainingReplayCallbacks)
                    return;

                _isDrainingReplayCallbacks = true;
            }

            try
            {
                while (true)
                {
                    _drainBuffer.Clear();
                    lock (_replayEngineLock)
                    {
                        if (_pendingReplayCallbacks.Count == 0)
                            break;

                        while (_pendingReplayCallbacks.TryDequeue(out var callback))
                            _drainBuffer.Add(callback);
                    }

                    for (var i = 0; i < _drainBuffer.Count; i++)
                    {
                        var callback = _drainBuffer[i];
                        if (!IsReplayCallbackCurrent(callback.Generation))
                            continue;

                        if (callback.IsBatch)
                        {
                            InvokeReplayBatchCompleted(callback.BatchContext.Value, callback.Generation);
                            continue;
                        }

                        var context = callback.MessageContext.Value;
                        InvokeReplayMessageContext(context, callback.Generation);
                        if (IsReplayCallbackCurrent(callback.Generation))
                            InvokeReplayMessage(context.Topic, context.Payload, callback.Generation);
                    }
                }
            }
            finally
            {
                lock (_replayCallbackDrainGate)
                {
                    _drainBuffer.Clear();
                    _isDrainingReplayCallbacks = false;
                }
            }
        }

        private bool TryQueueReplayCallback(ReplayCallbackDispatch dispatch)
        {
            var stamped = dispatch.WithGeneration(Interlocked.Read(ref _replayCallbackGeneration));
            if (_pendingReplayCallbacks.TryEnqueue(stamped, out var overflow))
                return true;

            WarnReplayCallbackQueueOverflow(overflow);
            return false;
        }

        private void WarnReplayCallbackQueueOverflow(BoundedEventQueueOverflow overflow)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var previousTicks = System.Threading.Interlocked.Read(ref _lastReplayCallbackOverflowWarningTicks);
            var elapsedTicks = nowTicks >= previousTicks ? nowTicks - previousTicks : ReplayCallbackOverflowWarningIntervalTicks;
            if (elapsedTicks < ReplayCallbackOverflowWarningIntervalTicks)
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

        private bool IsReplayCallbackCurrent(long generation)
            => Interlocked.Read(ref _replayCallbackGeneration) == generation;

        private void InvokeReplayMessage(string topic, byte[] data, long generation)
        {
            var handlers = _replayMessageHandlers;
            foreach (var handler in handlers)
            {
                if (!IsReplayCallbackCurrent(generation))
                    break;
                try { handler(topic, data); }
                catch (Exception ex) { _logger?.LogWarning($"Replay message listener failed: {ex.Message}"); }
            }
        }

        private void InvokeReplayMessageContext(ReplayMessageContext context, long generation)
        {
            var handlers = _replayMessageContextHandlers;
            foreach (var handler in handlers)
            {
                if (!IsReplayCallbackCurrent(generation))
                    break;
                try { handler(context); }
                catch (Exception ex) { _logger?.LogWarning($"Replay message context listener failed: {ex.Message}"); }
            }
        }

        private void InvokeReplayBatchCompleted(ReplayBatchContext context, long generation)
        {
            var handlers = _replayBatchCompletedHandlers;
            foreach (var handler in handlers)
            {
                if (!IsReplayCallbackCurrent(generation))
                    break;
                try { handler(context); }
                catch (Exception ex) { _logger?.LogWarning($"Replay batch listener failed: {ex.Message}"); }
            }
        }

        private ReplayMessageContext CreateReplayMessageContext(McapMessage message)
        {
            var channelContext = default(ReplayChannelContext);
            var hasChannelContext = _channelContextMap != null
                && _channelContextMap.TryGetValue(message.ChannelId, out channelContext);
            var logTimeNs = message.LogTime;
            var replayStartTimeNs = _replayEngine?.StartTimeNs ?? 0UL;

            return new ReplayMessageContext(
                channelId: message.ChannelId,
                topic: hasChannelContext ? channelContext.Topic : string.Empty,
                messageEncoding: hasChannelContext ? channelContext.MessageEncoding : string.Empty,
                schemaName: hasChannelContext ? channelContext.SchemaName : string.Empty,
                schemaEncoding: hasChannelContext ? channelContext.SchemaEncoding : string.Empty,
                logTimeNs: logTimeNs,
                replayStartTimeNs: replayStartTimeNs,
                payload: message.Data,
                replaySessionId: _replaySessionId);
        }

        private static ulong NextReplaySessionId(ulong previous)
        {
            unchecked
            {
                var next = previous + 1UL;
                return next == 0UL ? 1UL : next;
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

        /// <summary>Return the behavior class loaded for a replay channel id.</summary>
        public ReplayChannelBehavior GetChannelBehavior(ushort channelId)
        {
            lock (_replayEngineLock)
                return _channelBehaviorMap != null && _channelBehaviorMap.TryGetValue(channelId, out var behavior)
                    ? behavior
                    : ReplayChannelBehavior.NotLoaded;
        }

    }
}
