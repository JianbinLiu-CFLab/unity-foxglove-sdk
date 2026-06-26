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

        private readonly PublishCadenceDiagnostics _publishCadenceDiagnostics = new();
        private double _nextPublishCadenceDiagnosticsSummaryTime;
        private double _lastFrameStallDiagnosticsTime;
        private long _lastFrameStallGcBytes;
        private int _lastFrameStallGcCount0;
        private int _lastFrameStallGcCount1;
        private int _lastFrameStallGcCount2;
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
                _lastFrameStallDiagnosticsTime = 0d;
                _lastFrameStallGcBytes = 0L;
                _lastFrameStallGcCount0 = 0;
                _lastFrameStallGcCount1 = 0;
                _lastFrameStallGcCount2 = 0;
                _frameStallDiagnosticsWasEnabled = false;
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
            if (!_frameStallDiagnosticsWasEnabled || _lastFrameStallDiagnosticsTime <= 0d)
            {
                _lastFrameStallDiagnosticsTime = now;
                _lastFrameStallGcBytes = gcBytes;
                _lastFrameStallGcCount0 = gcCount0;
                _lastFrameStallGcCount1 = gcCount1;
                _lastFrameStallGcCount2 = gcCount2;
                _frameStallDiagnosticsWasEnabled = true;
                return;
            }

            var deltaMs = (now - _lastFrameStallDiagnosticsTime) * 1000d;
            var gcBytesDelta = gcBytes - _lastFrameStallGcBytes;
            var gcCount0Delta = gcCount0 - _lastFrameStallGcCount0;
            var gcCount1Delta = gcCount1 - _lastFrameStallGcCount1;
            var gcCount2Delta = gcCount2 - _lastFrameStallGcCount2;
            _lastFrameStallDiagnosticsTime = now;
            _lastFrameStallGcBytes = gcBytes;
            _lastFrameStallGcCount0 = gcCount0;
            _lastFrameStallGcCount1 = gcCount1;
            _lastFrameStallGcCount2 = gcCount2;

            var thresholdMs = Mathf.Max(10f, _frameStallDiagnosticsThresholdMs);
            if (deltaMs + 1e-9d < thresholdMs)
                return;

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
                totalAllocatedBytes);
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
            long totalAllocatedBytes)
        {
#if UNITY_EDITOR
            var editorState = string.Format(
                CultureInfo.InvariantCulture,
                "compiling={0} updating={1}",
                UnityEditor.EditorApplication.isCompiling,
                UnityEditor.EditorApplication.isUpdating);
#else
            const string editorState = "compiling=n/a updating=n/a";
#endif
            var message = string.Format(
                CultureInfo.InvariantCulture,
                "[Foxglove] Frame stall diagnostics: frame={0} realDeltaMs={1:F2} thresholdMs={2:F2} deltaTimeMs={3:F2} unscaledDeltaTimeMs={4:F2} fixedDeltaMs={5:F2} timeScale={6:F2} focused={7} playing={8} {9} gcBytesDelta={10} gcCountDelta={11}/{12}/{13} monoUsedBytes={14} totalAllocatedBytes={15}",
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
                totalAllocatedBytes);
            LogDiagnosticsWithoutStackTrace(message);
        }

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
}
