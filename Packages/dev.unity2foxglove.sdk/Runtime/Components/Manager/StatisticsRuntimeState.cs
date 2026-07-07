// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Runtime-only diagnostics state for FoxgloveManager.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class StatisticsRuntimeState
    {
        internal readonly PublishCadenceDiagnostics PublishCadenceDiagnostics = new();

        internal double NextPublishCadenceDiagnosticsSummaryTime;
        internal double LastFrameStallDiagnosticsTime;
        internal long LastFrameStallGcBytes;
        internal long LastFrameStallMonoUsedBytes;
        internal long LastFrameStallTotalAllocatedBytes;
        internal long LastFrameStallTransportDroppedDataFrames;
        internal int LastFrameStallGcCount0;
        internal int LastFrameStallGcCount1;
        internal int LastFrameStallGcCount2;
        internal double FrameStallStageRuntimeTickMs;
        internal double FrameStallStageClientLifecycleDrainMs;
        internal double FrameStallStageClientMessageDrainMs;
        internal double FrameStallStagePublishCadenceDiagnosticsMs;
        internal double FrameStallStageLiveOutputModeWatchersMs;
        internal double FrameStallStageRemoteMcapRefreshMs;
        internal double FrameStallStageReplayCursorEndpointRefreshMs;
        internal double FrameStallStageTotalMs;
        internal bool PublishCadenceDiagnosticsWasEnabled;
        internal bool FrameStallDiagnosticsWasEnabled;

        internal void ResetFrameStallDiagnostics()
        {
            LastFrameStallDiagnosticsTime = 0d;
            LastFrameStallGcBytes = 0L;
            LastFrameStallMonoUsedBytes = 0L;
            LastFrameStallTotalAllocatedBytes = 0L;
            LastFrameStallTransportDroppedDataFrames = 0L;
            LastFrameStallGcCount0 = 0;
            LastFrameStallGcCount1 = 0;
            LastFrameStallGcCount2 = 0;
            ResetFrameStallStageTimingValues();
        }

        internal void ResetFrameStallStageTimingValues()
        {
            FrameStallStageRuntimeTickMs = 0d;
            FrameStallStageClientLifecycleDrainMs = 0d;
            FrameStallStageClientMessageDrainMs = 0d;
            FrameStallStagePublishCadenceDiagnosticsMs = 0d;
            FrameStallStageLiveOutputModeWatchersMs = 0d;
            FrameStallStageRemoteMcapRefreshMs = 0d;
            FrameStallStageReplayCursorEndpointRefreshMs = 0d;
            FrameStallStageTotalMs = 0d;
        }
    }

    internal sealed class PublishCadenceDiagnostics
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
                var burstFrames = _burstFrames + (_currentFrameCount > 1 ? 1 : 0);

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
                    burstFrames);
            }
        }
    }
}
