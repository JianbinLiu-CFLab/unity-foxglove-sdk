// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Optional publish-cadence diagnostics for FoxgloveManager.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [Tooltip("When enabled, logs aggregated per-topic publish cadence summaries for diagnosing Foxglove arrival-rate wobble.")]
        [SerializeField] private bool _publishCadenceDiagnosticsEnabled;
        [Tooltip("Seconds between publish cadence diagnostic summaries.")]
        [SerializeField, Min(0.5f)] private float _publishCadenceDiagnosticsSummaryIntervalSeconds = 5f;
        [Tooltip("When enabled, logs main-thread frame stalls so cadence gaps can be correlated with Unity Editor state.")]
        [SerializeField] private bool _frameStallDiagnosticsEnabled;
        [Tooltip("Main-thread frame time threshold, in milliseconds, before a frame-stall diagnostic is logged.")]
        [SerializeField, Min(10f)] private float _frameStallDiagnosticsThresholdMs = 200f;
        [Tooltip("When enabled, frame-stall diagnostics include FoxgloveManager Update sub-stage timings.")]
        [SerializeField] private bool _frameStallStageTimingDiagnosticsEnabled;

        private readonly PublishCadenceDiagnostics _publishCadenceDiagnostics = new();
        private const double FrameStallEditorAssetRefreshCooldownSeconds = 1.0d;
        private static double s_lastEditorAssetRefreshTime = double.NegativeInfinity;
        private double _nextPublishCadenceDiagnosticsSummaryTime;
        private double _lastFrameStallDiagnosticsTime;
        private long _lastFrameStallGcBytes;
        private long _lastFrameStallMonoUsedBytes;
        private long _lastFrameStallTotalAllocatedBytes;
        private long _lastFrameStallTransportDroppedDataFrames;
        private int _lastFrameStallGcCount0;
        private int _lastFrameStallGcCount1;
        private int _lastFrameStallGcCount2;
        private double _frameStallStageRuntimeTickMs;
        private double _frameStallStageClientLifecycleDrainMs;
        private double _frameStallStageClientMessageDrainMs;
        private double _frameStallStagePublishCadenceDiagnosticsMs;
        private double _frameStallStageLiveOutputModeWatchersMs;
        private double _frameStallStageRemoteMcapRefreshMs;
        private double _frameStallStageReplayCursorEndpointRefreshMs;
        private double _frameStallStageManagerUpdateMs;
        private bool _publishCadenceDiagnosticsWasEnabled;
        private bool _frameStallDiagnosticsWasEnabled;

        /// <summary>Whether per-topic publish cadence diagnostics are currently enabled.</summary>
        public bool PublishCadenceDiagnosticsEnabled
        {
            get => _publishCadenceDiagnosticsEnabled;
            set
            {
                if (_publishCadenceDiagnosticsEnabled == value)
                    return;

                _publishCadenceDiagnosticsEnabled = value;
                if (!value)
                    _publishCadenceDiagnostics.Clear();
                _nextPublishCadenceDiagnosticsSummaryTime = 0d;
            }
        }

        private void RecordPublishCadence(string topic, string encoding)
        {
            if (!_publishCadenceDiagnosticsEnabled)
                return;

            _publishCadenceDiagnostics.Record(
                topic,
                encoding,
                Time.unscaledTimeAsDouble,
                Time.frameCount);
        }

        private void FlushPublishCadenceDiagnosticsIfNeeded()
        {
            if (!_publishCadenceDiagnosticsEnabled)
            {
                if (_publishCadenceDiagnosticsWasEnabled)
                {
                    _publishCadenceDiagnostics.Clear();
                    _nextPublishCadenceDiagnosticsSummaryTime = 0d;
                    _publishCadenceDiagnosticsWasEnabled = false;
                }

                return;
            }

            _publishCadenceDiagnosticsWasEnabled = true;

            var now = Time.unscaledTimeAsDouble;
            var interval = Mathf.Max(0.5f, _publishCadenceDiagnosticsSummaryIntervalSeconds);
            if (_nextPublishCadenceDiagnosticsSummaryTime <= 0d)
            {
                _nextPublishCadenceDiagnosticsSummaryTime = now + interval;
                return;
            }

            if (now + 1e-9d < _nextPublishCadenceDiagnosticsSummaryTime)
                return;

            var summary = _publishCadenceDiagnostics.BuildAndResetSummary();
            _nextPublishCadenceDiagnosticsSummaryTime = now + interval;
            if (!string.IsNullOrEmpty(summary))
                LogPublishCadenceSummary(summary);
        }

        private void RecordFrameStallDiagnosticsIfNeeded()
        {
            if (!_frameStallDiagnosticsEnabled)
            {
                if (_frameStallDiagnosticsWasEnabled)
                {
                    _lastFrameStallDiagnosticsTime = 0d;
                    _lastFrameStallGcBytes = 0L;
                    _lastFrameStallMonoUsedBytes = 0L;
                    _lastFrameStallTotalAllocatedBytes = 0L;
                    _lastFrameStallTransportDroppedDataFrames = 0L;
                    _lastFrameStallGcCount0 = 0;
                    _lastFrameStallGcCount1 = 0;
                    _lastFrameStallGcCount2 = 0;
                    ResetFrameStallStageTimingValues();
                    _frameStallDiagnosticsWasEnabled = false;
                }

                return;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            var gcBytes = GC.GetTotalMemory(forceFullCollection: false);
            var gcCount0 = GC.CollectionCount(0);
            var gcCount1 = GC.CollectionCount(1);
            var gcCount2 = GC.CollectionCount(2);
            var frameCount = Time.frameCount;
            var deltaTimeMs = Time.deltaTime * 1000f;
            var unscaledDeltaTimeMs = Time.unscaledDeltaTime * 1000f;
            var fixedDeltaTimeMs = Time.fixedDeltaTime * 1000f;
            var timeScale = Time.timeScale;
            var monoUsedBytes = Profiler.GetMonoUsedSizeLong();
            var totalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
            var transportStats = GetTransportStatsSnapshot();
            var transportSupported = transportStats.Supported;
            var transportClients = transportSupported ? transportStats.ActiveClientCount : 0;
            var transportDroppedTotal = transportSupported ? transportStats.TotalDroppedDataFrames : 0L;
            var transportQueuedFrames = transportSupported ? transportStats.TotalQueuedFrames : 0L;
            var transportQueuedBytes = transportSupported ? transportStats.TotalQueuedBytes : 0L;
            if (!_frameStallDiagnosticsWasEnabled || _lastFrameStallDiagnosticsTime <= 0d)
            {
                _lastFrameStallDiagnosticsTime = now;
                _lastFrameStallGcBytes = gcBytes;
                _lastFrameStallMonoUsedBytes = monoUsedBytes;
                _lastFrameStallTotalAllocatedBytes = totalAllocatedBytes;
                _lastFrameStallTransportDroppedDataFrames = transportDroppedTotal;
                _lastFrameStallGcCount0 = gcCount0;
                _lastFrameStallGcCount1 = gcCount1;
                _lastFrameStallGcCount2 = gcCount2;
                _frameStallDiagnosticsWasEnabled = true;
                return;
            }

            var deltaMs = (now - _lastFrameStallDiagnosticsTime) * 1000d;
            var gcBytesDelta = gcBytes - _lastFrameStallGcBytes;
            var monoUsedBytesDelta = monoUsedBytes - _lastFrameStallMonoUsedBytes;
            var totalAllocatedBytesDelta = totalAllocatedBytes - _lastFrameStallTotalAllocatedBytes;
            var transportDroppedDelta = transportDroppedTotal - _lastFrameStallTransportDroppedDataFrames;
            var gcCount0Delta = gcCount0 - _lastFrameStallGcCount0;
            var gcCount1Delta = gcCount1 - _lastFrameStallGcCount1;
            var gcCount2Delta = gcCount2 - _lastFrameStallGcCount2;
            _lastFrameStallDiagnosticsTime = now;
            _lastFrameStallGcBytes = gcBytes;
            _lastFrameStallMonoUsedBytes = monoUsedBytes;
            _lastFrameStallTotalAllocatedBytes = totalAllocatedBytes;
            _lastFrameStallTransportDroppedDataFrames = transportDroppedTotal;
            _lastFrameStallGcCount0 = gcCount0;
            _lastFrameStallGcCount1 = gcCount1;
            _lastFrameStallGcCount2 = gcCount2;

            var thresholdMs = Mathf.Max(10f, _frameStallDiagnosticsThresholdMs);
            if (deltaMs + 1e-9d < thresholdMs)
                return;

            var cameraSnapshot = CameraPublishDiagnostics.LastSnapshotOrDefault;
            LogFrameStallDiagnostics(
                frameCount,
                deltaMs,
                thresholdMs,
                deltaTimeMs,
                unscaledDeltaTimeMs,
                fixedDeltaTimeMs,
                timeScale,
                gcBytesDelta,
                gcCount0Delta,
                gcCount1Delta,
                gcCount2Delta,
                monoUsedBytes,
                monoUsedBytesDelta,
                totalAllocatedBytes,
                totalAllocatedBytesDelta,
                transportSupported,
                transportClients,
                transportDroppedDelta,
                transportDroppedTotal,
                transportQueuedFrames,
                transportQueuedBytes,
                _frameStallStageTimingDiagnosticsEnabled,
                _frameStallStageRuntimeTickMs,
                _frameStallStageClientLifecycleDrainMs,
                _frameStallStageClientMessageDrainMs,
                _frameStallStagePublishCadenceDiagnosticsMs,
                _frameStallStageLiveOutputModeWatchersMs,
                _frameStallStageRemoteMcapRefreshMs,
                _frameStallStageReplayCursorEndpointRefreshMs,
                _frameStallStageManagerUpdateMs,
                cameraSnapshot,
                now);
        }

        private static void LogFrameStallDiagnostics(
            int frameCount,
            double deltaMs,
            float thresholdMs,
            float deltaTimeMs,
            float unscaledDeltaTimeMs,
            float fixedDeltaTimeMs,
            float timeScale,
            long gcBytesDelta,
            int gcCount0Delta,
            int gcCount1Delta,
            int gcCount2Delta,
            long monoUsedBytes,
            long monoUsedBytesDelta,
            long totalAllocatedBytes,
            long totalAllocatedBytesDelta,
            bool transportSupported,
            int transportClients,
            long transportDroppedDelta,
            long transportDroppedTotal,
            long transportQueuedFrames,
            long transportQueuedBytes,
            bool stageTimingEnabled,
            double stageRuntimeTickMs,
            double stageClientLifecycleDrainMs,
            double stageClientMessageDrainMs,
            double stagePublishCadenceDiagnosticsMs,
            double stageLiveOutputModeWatchersMs,
            double stageRemoteMcapRefreshMs,
            double stageReplayCursorEndpointRefreshMs,
            double stageManagerUpdateMs,
            CameraTimingSnapshot cameraSnapshot,
            double nowRealtimeSeconds)
        {
#if UNITY_EDITOR
            var editorNow = UnityEditor.EditorApplication.timeSinceStartup;
            var editorCompiling = UnityEditor.EditorApplication.isCompiling;
            var editorUpdating = UnityEditor.EditorApplication.isUpdating;
            if (editorCompiling || editorUpdating)
                NoteFrameStallEditorAssetRefreshForDiagnostics(editorNow);
            var editorAssetRefreshAgeMs = GetFrameStallEditorAssetRefreshAgeMs(editorNow);
            var editorAssetRefreshRecent = editorAssetRefreshAgeMs >= 0d
                                           && editorAssetRefreshAgeMs <= FrameStallEditorAssetRefreshCooldownSeconds * 1000d;
            var editorState = string.Format(
                CultureInfo.InvariantCulture,
                "compiling={0} updating={1} editorAssetRefreshRecent={2} editorAssetRefreshAgeMs={3:F2}",
                editorCompiling,
                editorUpdating,
                editorAssetRefreshRecent,
                editorAssetRefreshAgeMs);
#else
            const string editorState = "compiling=n/a updating=n/a editorAssetRefreshRecent=n/a editorAssetRefreshAgeMs=n/a";
#endif
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "[Foxglove] Frame stall diagnostics: frame={0} realDeltaMs={1:F2} thresholdMs={2:F2} deltaTimeMs={3:F2} unscaledDeltaTimeMs={4:F2} fixedDeltaMs={5:F2} timeScale={6:F2} focused={7} playing={8} {9} gcBytesDelta={10} gcCountDelta={11}/{12}/{13} monoUsedBytes={14} monoUsedBytesDelta={15} totalAllocatedBytes={16} totalAllocatedBytesDelta={17} transportSupported={18} transportClients={19} transportDroppedDelta={20} transportDroppedTotal={21} transportQueuedFrames={22} transportQueuedBytes={23} stageTiming={24} stageRuntimeTickMs={25:F2} stageClientLifecycleDrainMs={26:F2} stageClientMessageDrainMs={27:F2} stagePublishCadenceMs={28:F2} stageLiveOutputWatchersMs={29:F2} stageRemoteMcapMs={30:F2} stageReplayCursorMs={31:F2} stageManagerUpdateMs={32:F2} cameraSnapshotAgeMs={33:F2} cameraRenderMs={34:F2} cameraReadbackLatencyMs={35:F2} cameraReadbackCopyMs={36:F2} cameraCompletedJpegDrainMs={37:F2} cameraJpegMs={38:F2} cameraSerializeMs={39:F2} cameraJpegBytes={40} cameraPendingReadbacksBefore={41} cameraPendingReadbacksAfter={42} cameraEncodeQueue={43} cameraCompletedQueue={44}",
                frameCount,
                deltaMs,
                thresholdMs,
                deltaTimeMs,
                unscaledDeltaTimeMs,
                fixedDeltaTimeMs,
                timeScale,
                Application.isFocused,
                Application.isPlaying,
                editorState,
                gcBytesDelta,
                gcCount0Delta,
                gcCount1Delta,
                gcCount2Delta,
                monoUsedBytes,
                monoUsedBytesDelta,
                totalAllocatedBytes,
                totalAllocatedBytesDelta,
                transportSupported,
                transportClients,
                transportDroppedDelta,
                transportDroppedTotal,
                transportQueuedFrames,
                transportQueuedBytes,
                stageTimingEnabled,
                stageRuntimeTickMs,
                stageClientLifecycleDrainMs,
                stageClientMessageDrainMs,
                stagePublishCadenceDiagnosticsMs,
                stageLiveOutputModeWatchersMs,
                stageRemoteMcapRefreshMs,
                stageReplayCursorEndpointRefreshMs,
                stageManagerUpdateMs,
                cameraSnapshot.AgeMs(nowRealtimeSeconds),
                cameraSnapshot.RenderMs,
                cameraSnapshot.ReadbackLatencyMs,
                cameraSnapshot.ReadbackCopyMs,
                cameraSnapshot.CompletedJpegDrainMs,
                cameraSnapshot.JpegEncodeMs,
                cameraSnapshot.SerializeMs,
                cameraSnapshot.JpegBytes,
                cameraSnapshot.PendingReadbacksBefore,
                cameraSnapshot.PendingReadbacksAfter,
                cameraSnapshot.EncodeQueueDepth,
                cameraSnapshot.CompletedQueueDepth);
            LogDiagnosticsWithoutStackTrace(message);
        }

        private enum FrameStallStage
        {
            RuntimeTick,
            ClientLifecycleDrain,
            ClientMessageDrain,
            PublishCadenceDiagnostics,
            LiveOutputModeWatchers,
            RemoteMcapRefresh,
            ReplayCursorEndpointRefresh
        }

        private double BeginFrameStallStageTiming()
        {
            if (!_frameStallDiagnosticsEnabled || !_frameStallStageTimingDiagnosticsEnabled)
            {
                ResetFrameStallStageTimingValues();
                return 0d;
            }

            ResetFrameStallStageTimingValues();
            return Time.realtimeSinceStartupAsDouble;
        }

        private void RecordFrameStallStageTiming(ref double frameStallStageStart, FrameStallStage stage)
        {
            if (!_frameStallDiagnosticsEnabled || !_frameStallStageTimingDiagnosticsEnabled || frameStallStageStart <= 0d)
                return;

            var now = Time.realtimeSinceStartupAsDouble;
            var elapsedMs = (now - frameStallStageStart) * 1000d;
            frameStallStageStart = now;
            _frameStallStageManagerUpdateMs += elapsedMs;

            switch (stage)
            {
                case FrameStallStage.RuntimeTick:
                    _frameStallStageRuntimeTickMs = elapsedMs;
                    break;
                case FrameStallStage.ClientLifecycleDrain:
                    _frameStallStageClientLifecycleDrainMs = elapsedMs;
                    break;
                case FrameStallStage.ClientMessageDrain:
                    _frameStallStageClientMessageDrainMs = elapsedMs;
                    break;
                case FrameStallStage.PublishCadenceDiagnostics:
                    _frameStallStagePublishCadenceDiagnosticsMs = elapsedMs;
                    break;
                case FrameStallStage.LiveOutputModeWatchers:
                    _frameStallStageLiveOutputModeWatchersMs = elapsedMs;
                    break;
                case FrameStallStage.RemoteMcapRefresh:
                    _frameStallStageRemoteMcapRefreshMs = elapsedMs;
                    break;
                case FrameStallStage.ReplayCursorEndpointRefresh:
                    _frameStallStageReplayCursorEndpointRefreshMs = elapsedMs;
                    break;
            }
        }

        private void ResetFrameStallStageTimingValues()
        {
            _frameStallStageRuntimeTickMs = 0d;
            _frameStallStageClientLifecycleDrainMs = 0d;
            _frameStallStageClientMessageDrainMs = 0d;
            _frameStallStagePublishCadenceDiagnosticsMs = 0d;
            _frameStallStageLiveOutputModeWatchersMs = 0d;
            _frameStallStageRemoteMcapRefreshMs = 0d;
            _frameStallStageReplayCursorEndpointRefreshMs = 0d;
            _frameStallStageManagerUpdateMs = 0d;
        }

#if UNITY_EDITOR
        internal static void NoteFrameStallEditorAssetRefreshForDiagnostics()
        {
            NoteFrameStallEditorAssetRefreshForDiagnostics(UnityEditor.EditorApplication.timeSinceStartup);
        }

        private static void NoteFrameStallEditorAssetRefreshForDiagnostics(double editorTime)
        {
            s_lastEditorAssetRefreshTime = editorTime;
        }

        private static double GetFrameStallEditorAssetRefreshAgeMs(double editorNow)
        {
            if (double.IsNegativeInfinity(s_lastEditorAssetRefreshTime))
                return -1d;

            return Math.Max(0d, editorNow - s_lastEditorAssetRefreshTime) * 1000d;
        }
#endif

        private static void LogPublishCadenceSummary(string summary)
        {
            LogDiagnosticsWithoutStackTrace(summary);
        }

        /// <summary>
        /// Emits high-volume diagnostics without stack traces by temporarily
        /// changing Unity's global Log stack-trace mode on the main thread.
        /// Keep the mutation window limited to the single Debug.Log call.
        /// </summary>
        private static void LogDiagnosticsWithoutStackTrace(string message)
        {
            var previousStackTraceMode = Application.GetStackTraceLogType(LogType.Log);
            try
            {
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Debug.Log(message);
            }
            finally
            {
                Application.SetStackTraceLogType(LogType.Log, previousStackTraceMode);
            }
        }

        private sealed class PublishCadenceDiagnostics
        {
            private readonly Dictionary<string, TopicStats> _topics = new();

            public void Record(string topic, string encoding, double nowSec, int frameCount)
            {
                var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? "(empty)" : topic.Trim();
                var normalizedEncoding = string.IsNullOrWhiteSpace(encoding) ? "(empty)" : encoding.Trim();
                var key = normalizedTopic + "|" + normalizedEncoding;
                if (!_topics.TryGetValue(key, out var stats))
                {
                    stats = new TopicStats(normalizedTopic, normalizedEncoding);
                    _topics.Add(key, stats);
                }

                stats.Record(nowSec, frameCount);
            }

            public void Clear()
            {
                _topics.Clear();
            }

            public string BuildAndResetSummary()
            {
                if (_topics.Count == 0)
                    return string.Empty;

                var builder = new StringBuilder();
                builder.Append("[Foxglove] Publish cadence diagnostics:");
                foreach (var stats in _topics.Values)
                {
                    builder.AppendLine();
                    builder.Append("  ");
                    builder.Append(stats.BuildSummary());
                }

                _topics.Clear();
                return builder.ToString();
            }

            private sealed class TopicStats
            {
                private readonly string _topic;
                private readonly string _encoding;
                private long _messageCount;
                private long _intervalCount;
                private double _lastPublishSec = double.NaN;
                private double _minIntervalSec = double.PositiveInfinity;
                private double _maxIntervalSec;
                private double _sumIntervalSec;
                private double _sumSquaredIntervalSec;
                private int _lastFrame = int.MinValue;
                private int _currentFrameCount;
                private int _maxPerFrame;
                private long _burstFrames;

                public TopicStats(string topic, string encoding)
                {
                    _topic = topic;
                    _encoding = encoding;
                }

                public void Record(double nowSec, int frame)
                {
                    _messageCount++;

                    if (!double.IsNaN(_lastPublishSec))
                    {
                        var interval = Math.Max(0d, nowSec - _lastPublishSec);
                        _intervalCount++;
                        _minIntervalSec = Math.Min(_minIntervalSec, interval);
                        _maxIntervalSec = Math.Max(_maxIntervalSec, interval);
                        _sumIntervalSec += interval;
                        _sumSquaredIntervalSec += interval * interval;
                    }

                    _lastPublishSec = nowSec;

                    if (frame == _lastFrame)
                    {
                        _currentFrameCount++;
                    }
                    else
                    {
                        if (_currentFrameCount > 1)
                            _burstFrames++;
                        _lastFrame = frame;
                        _currentFrameCount = 1;
                    }

                    _maxPerFrame = Math.Max(_maxPerFrame, _currentFrameCount);
                }

                public string BuildSummary()
                {
                    if (_currentFrameCount > 1)
                        _burstFrames++;

                    var minMs = _intervalCount > 0 ? _minIntervalSec * 1000d : 0d;
                    var maxMs = _intervalCount > 0 ? _maxIntervalSec * 1000d : 0d;
                    var meanMs = _intervalCount > 0 ? (_sumIntervalSec / _intervalCount) * 1000d : 0d;
                    var variance = 0d;
                    if (_intervalCount > 0)
                    {
                        var mean = _sumIntervalSec / _intervalCount;
                        variance = Math.Max(0d, (_sumSquaredIntervalSec / _intervalCount) - mean * mean);
                    }

                    var stdMs = Math.Sqrt(variance) * 1000d;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "topic={0} encoding={1} messages={2} intervalMs[min={3:F2}, mean={4:F2}, max={5:F2}, std={6:F2}] maxPerFrame={7} burstFrames={8}",
                        _topic,
                        _encoding,
                        _messageCount,
                        minMs,
                        meanMs,
                        maxMs,
                        stdMs,
                        _maxPerFrame,
                        _burstFrames);
                }
            }
        }
    }

#if UNITY_EDITOR
    internal sealed class FrameStallEditorAssetRefreshProbe : UnityEditor.AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            FoxgloveManager.NoteFrameStallEditorAssetRefreshForDiagnostics();
        }
    }
#endif
}
