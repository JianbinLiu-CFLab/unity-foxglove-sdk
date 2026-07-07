// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Optional publish-cadence diagnostics for FoxgloveManager.

using System;
using System.Globalization;
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

        private const double FrameStallEditorAssetRefreshCooldownSeconds = 1.0d;
        private static double s_lastEditorAssetRefreshTime = double.NegativeInfinity;

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
                    _statisticsState.PublishCadenceDiagnostics.Clear();
                _statisticsState.NextPublishCadenceDiagnosticsSummaryTime = 0d;
            }
        }

        private void RecordPublishCadence(string topic, string encoding)
        {
            if (!_publishCadenceDiagnosticsEnabled)
                return;

            _statisticsState.PublishCadenceDiagnostics.Record(
                topic,
                encoding,
                Time.unscaledTimeAsDouble,
                Time.frameCount);
        }

        private void FlushPublishCadenceDiagnosticsIfNeeded()
        {
            if (!_publishCadenceDiagnosticsEnabled)
            {
                if (_statisticsState.PublishCadenceDiagnosticsWasEnabled)
                {
                    _statisticsState.PublishCadenceDiagnostics.Clear();
                    _statisticsState.NextPublishCadenceDiagnosticsSummaryTime = 0d;
                    _statisticsState.PublishCadenceDiagnosticsWasEnabled = false;
                }

                return;
            }

            _statisticsState.PublishCadenceDiagnosticsWasEnabled = true;

            var now = Time.unscaledTimeAsDouble;
            var interval = Mathf.Max(0.5f, _publishCadenceDiagnosticsSummaryIntervalSeconds);
            if (_statisticsState.NextPublishCadenceDiagnosticsSummaryTime <= 0d)
            {
                _statisticsState.NextPublishCadenceDiagnosticsSummaryTime = now + interval;
                return;
            }

            if (now + 1e-9d < _statisticsState.NextPublishCadenceDiagnosticsSummaryTime)
                return;

            var summary = _statisticsState.PublishCadenceDiagnostics.BuildAndResetSummary();
            _statisticsState.NextPublishCadenceDiagnosticsSummaryTime = now + interval;
            if (!string.IsNullOrEmpty(summary))
                LogPublishCadenceSummary(summary);
        }

        private void RecordFrameStallDiagnosticsIfNeeded()
        {
            if (!_frameStallDiagnosticsEnabled)
            {
                if (_statisticsState.FrameStallDiagnosticsWasEnabled)
                {
                    _statisticsState.ResetFrameStallDiagnostics();
                    _statisticsState.FrameStallDiagnosticsWasEnabled = false;
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
            if (!_statisticsState.FrameStallDiagnosticsWasEnabled || _statisticsState.LastFrameStallDiagnosticsTime <= 0d)
            {
                _statisticsState.LastFrameStallDiagnosticsTime = now;
                _statisticsState.LastFrameStallGcBytes = gcBytes;
                _statisticsState.LastFrameStallMonoUsedBytes = monoUsedBytes;
                _statisticsState.LastFrameStallTotalAllocatedBytes = totalAllocatedBytes;
                _statisticsState.LastFrameStallTransportDroppedDataFrames = transportDroppedTotal;
                _statisticsState.LastFrameStallGcCount0 = gcCount0;
                _statisticsState.LastFrameStallGcCount1 = gcCount1;
                _statisticsState.LastFrameStallGcCount2 = gcCount2;
                _statisticsState.FrameStallDiagnosticsWasEnabled = true;
                return;
            }

            var deltaMs = (now - _statisticsState.LastFrameStallDiagnosticsTime) * 1000d;
            var gcBytesDelta = gcBytes - _statisticsState.LastFrameStallGcBytes;
            var monoUsedBytesDelta = monoUsedBytes - _statisticsState.LastFrameStallMonoUsedBytes;
            var totalAllocatedBytesDelta = totalAllocatedBytes - _statisticsState.LastFrameStallTotalAllocatedBytes;
            var transportDroppedDelta = transportDroppedTotal - _statisticsState.LastFrameStallTransportDroppedDataFrames;
            var gcCount0Delta = gcCount0 - _statisticsState.LastFrameStallGcCount0;
            var gcCount1Delta = gcCount1 - _statisticsState.LastFrameStallGcCount1;
            var gcCount2Delta = gcCount2 - _statisticsState.LastFrameStallGcCount2;
            _statisticsState.LastFrameStallDiagnosticsTime = now;
            _statisticsState.LastFrameStallGcBytes = gcBytes;
            _statisticsState.LastFrameStallMonoUsedBytes = monoUsedBytes;
            _statisticsState.LastFrameStallTotalAllocatedBytes = totalAllocatedBytes;
            _statisticsState.LastFrameStallTransportDroppedDataFrames = transportDroppedTotal;
            _statisticsState.LastFrameStallGcCount0 = gcCount0;
            _statisticsState.LastFrameStallGcCount1 = gcCount1;
            _statisticsState.LastFrameStallGcCount2 = gcCount2;

            var thresholdMs = Mathf.Max(10f, _frameStallDiagnosticsThresholdMs);
            if (deltaMs + 1e-9d < thresholdMs)
                return;

            var cameraSnapshot = CameraTimingDiagnostics.LastSnapshotOrDefault;
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
                _statisticsState.FrameStallStageRuntimeTickMs,
                _statisticsState.FrameStallStageClientLifecycleDrainMs,
                _statisticsState.FrameStallStageClientMessageDrainMs,
                _statisticsState.FrameStallStagePublishCadenceDiagnosticsMs,
                _statisticsState.FrameStallStageLiveOutputModeWatchersMs,
                _statisticsState.FrameStallStageRemoteMcapRefreshMs,
                _statisticsState.FrameStallStageReplayCursorEndpointRefreshMs,
                _statisticsState.FrameStallStageTotalMs,
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
            double stageTotalMs,
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
                "[Foxglove] Frame stall diagnostics: frame={0} realDeltaMs={1:F2} thresholdMs={2:F2} deltaTimeMs={3:F2} unscaledDeltaTimeMs={4:F2} fixedDeltaMs={5:F2} timeScale={6:F2} focused={7} playing={8} {9} gcBytesDelta={10} gcCountDelta={11}/{12}/{13} monoUsedBytes={14} monoUsedBytesDelta={15} totalAllocatedBytes={16} totalAllocatedBytesDelta={17} transportSupported={18} transportClients={19} transportDroppedDelta={20} transportDroppedTotal={21} transportQueuedFrames={22} transportQueuedBytes={23} stageTiming={24} stageRuntimeTickMs={25:F2} stageClientLifecycleDrainMs={26:F2} stageClientMessageDrainMs={27:F2} stagePublishCadenceMs={28:F2} stageLiveOutputWatchersMs={29:F2} stageRemoteMcapMs={30:F2} stageReplayCursorMs={31:F2} stageTotalMs={32:F2} cameraSnapshotAgeMs={33:F2} cameraRenderMs={34:F2} cameraReadbackLatencyMs={35:F2} cameraReadbackCopyMs={36:F2} cameraCompletedJpegDrainMs={37:F2} cameraJpegMs={38:F2} cameraSerializeMs={39:F2} cameraJpegBytes={40} cameraPendingReadbacksBefore={41} cameraPendingReadbacksAfter={42} cameraEncodeQueue={43} cameraCompletedQueue={44}",
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
                stageTotalMs,
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
            _statisticsState.FrameStallStageTotalMs += elapsedMs;

            switch (stage)
            {
                case FrameStallStage.RuntimeTick:
                    _statisticsState.FrameStallStageRuntimeTickMs = elapsedMs;
                    break;
                case FrameStallStage.ClientLifecycleDrain:
                    _statisticsState.FrameStallStageClientLifecycleDrainMs = elapsedMs;
                    break;
                case FrameStallStage.ClientMessageDrain:
                    _statisticsState.FrameStallStageClientMessageDrainMs = elapsedMs;
                    break;
                case FrameStallStage.PublishCadenceDiagnostics:
                    _statisticsState.FrameStallStagePublishCadenceDiagnosticsMs = elapsedMs;
                    break;
                case FrameStallStage.LiveOutputModeWatchers:
                    _statisticsState.FrameStallStageLiveOutputModeWatchersMs = elapsedMs;
                    break;
                case FrameStallStage.RemoteMcapRefresh:
                    _statisticsState.FrameStallStageRemoteMcapRefreshMs = elapsedMs;
                    break;
                case FrameStallStage.ReplayCursorEndpointRefresh:
                    _statisticsState.FrameStallStageReplayCursorEndpointRefreshMs = elapsedMs;
                    break;
            }
        }

        private void ResetFrameStallStageTimingValues()
        {
            _statisticsState.ResetFrameStallStageTimingValues();
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

        /// <summary>Emits high-volume diagnostics without stack traces for this log call only.</summary>
        private static void LogDiagnosticsWithoutStackTrace(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
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
