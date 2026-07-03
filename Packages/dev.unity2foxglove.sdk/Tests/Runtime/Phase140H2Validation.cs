// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140H2 IMU WebSocket visualization burst boundary validation.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates that Phase 140H2 smooths only the IMU WebSocket visualization lane.
    /// </summary>
    public static class Phase140H2Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140H2 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140H2: IMU WebSocket visualization burst boundary ===");
            _passed = 0;

            VirtualImuHasExplicitWebSocketVisualizationCap();
            VirtualImuEditorExposesWebSocketVisualizationCap();
            VirtualImuKeepsNativeHandoffOutsideWebSocketCap();
            VirtualImuCoalescesToLatestWebSocketSamples();
            VirtualImuDropDiagnosticsAreNonWarningAndThrottled();
            ManagerExposesOptInFrameStallDiagnostics();
            PointCloud2NativeDiagnosticsExposeRawDeskewAndR2fuTiming();
            LidarAndPointCloudWorkerDiagnosticsExposeSubStageTiming();
            TransportAndBaseSchedulersRemainOutOfScope();
            ValidationRegistryExposesPhase140H2();

            Console.WriteLine($"Phase 140H2: {_passed} checks passed.");
        }

        private static void VirtualImuHasExplicitWebSocketVisualizationCap()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Check(virtualImu.Contains("DefaultMaxWebSocketSamplesPerFrame = 32", StringComparison.Ordinal)
                  && virtualImu.Contains("_maxWebSocketSamplesPerFrame = DefaultMaxWebSocketSamplesPerFrame", StringComparison.Ordinal)
                  && virtualImu.Contains("Maximum IMU WebSocket visualization catch-up samples published per render frame", StringComparison.Ordinal),
                "140H2-1A: VirtualImu exposes an explicit WebSocket visualization cap");
            Check(!virtualImu.Contains("_maxWebSocketSamplesPerFrame = 1", StringComparison.Ordinal),
                "140H2-1B: default WebSocket cap does not clamp IMU to render-frame cadence");
            Check(MethodContains(virtualImu, "private void NormalizeSerializedConfiguration()", "_maxWebSocketSamplesPerFrame = 0;"),
                "140H2-1C: VirtualImu normalizes negative WebSocket cap values");
            Check(MethodContains(virtualImu, "private int ResolveWebSocketSamplesPerFrame", "_maxWebSocketSamplesPerFrame <= 0")
                  && MethodContains(virtualImu, "private int ResolveWebSocketSamplesPerFrame", "return queuedAtFrameStart;")
                  && MethodContains(virtualImu, "private int ResolveWebSocketSamplesPerFrame", "Math.Min(_maxWebSocketSamplesPerFrame, queuedAtFrameStart)"),
                "140H2-1D: zero cap preserves legacy unlimited WebSocket draining");
        }

        private static void VirtualImuEditorExposesWebSocketVisualizationCap()
        {
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualImuEditor.cs");
            Check(editor.Contains("serializedObject.FindProperty(\"_maxWebSocketSamplesPerFrame\")", StringComparison.Ordinal)
                  && editor.Contains("WebSocket Max Samples / Frame", StringComparison.Ordinal),
                "140H2-1E: VirtualImu Inspector exposes the WebSocket sample cap");
            Check(editor.Contains("640Hz needs at least 16 at 40 FPS", StringComparison.Ordinal)
                  && editor.Contains("32 at 20 FPS", StringComparison.Ordinal),
                "140H2-1F: VirtualImu Inspector documents the 640Hz cap sizing");
        }

        private static void VirtualImuKeepsNativeHandoffOutsideWebSocketCap()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = ExtractMethod(virtualImu, "private void Update()");
            var nativeInvoke = update.IndexOf("nativeFrameHandler.Invoke(nativeFrame);", StringComparison.Ordinal);
            var publish = update.IndexOf("PublishWebSocketSample(sample);", StringComparison.Ordinal);
            Check(nativeInvoke > publish && publish >= 0,
                "140H2-2A: native handoff remains in the drain loop after WebSocket selection");
            Check(update.Contains("while (_queue.Count > 0)", StringComparison.Ordinal)
                  && update.Contains("var sample = _queue.Dequeue();", StringComparison.Ordinal)
                  && update.Contains("var nativeFrameHandler = ImuNativeFrameReady;", StringComparison.Ordinal),
                "140H2-2B: VirtualImu still drains every queued sample each frame");
            Check(!IsNestedInside(update, "nativeFrameHandler.Invoke(nativeFrame);", "else if (webSocketPublished < webSocketBudget)"),
                "140H2-2C: native handoff is not gated by the WebSocket publish budget");
        }

        private static void VirtualImuCoalescesToLatestWebSocketSamples()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = ExtractMethod(virtualImu, "private void Update()");
            Check(update.Contains("var queuedAtFrameStart = _queue.Count;", StringComparison.Ordinal)
                  && update.Contains("var webSocketSkipCount = queuedAtFrameStart - webSocketBudget;", StringComparison.Ordinal),
                "140H2-3A: WebSocket cap is computed from the frame-start backlog");
            Check(update.Contains("if (webSocketSkipCount > 0)", StringComparison.Ordinal)
                  && update.Contains("webSocketSkipCount--;", StringComparison.Ordinal)
                  && update.Contains("else if (webSocketPublished < webSocketBudget)", StringComparison.Ordinal),
                "140H2-3B: older WebSocket visualization samples are skipped before latest samples publish");
            Check(MethodContains(virtualImu, "private void PublishWebSocketSample", "ImuMessageBuilder.Serialize")
                  && MethodContains(virtualImu, "private void PublishWebSocketSample", "_manager.PublishProto(_topic, ImuSchema.SchemaName, bytes, sample.TimestampNs);"),
                "140H2-3C: WebSocket serialization/publish is isolated in a helper");
        }

        private static void VirtualImuDropDiagnosticsAreNonWarningAndThrottled()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var dropLog = ExtractMethod(virtualImu, "private void LogDroppedSamplesIfNeeded()");
            Check(dropLog.Contains("Debug.LogFormat(", StringComparison.Ordinal)
                  && dropLog.Contains("LogOption.NoStacktrace", StringComparison.Ordinal)
                  && !dropLog.Contains("Debug.Log(", StringComparison.Ordinal)
                  && !dropLog.Contains("Debug.LogWarning(", StringComparison.Ordinal),
                "140H2-3D: sustained IMU queue back-pressure uses no-stacktrace non-warning diagnostics");
            Check(virtualImu.Contains("DroppedSamplesLogIntervalSeconds", StringComparison.Ordinal)
                  && virtualImu.Contains("_nextDroppedSamplesLogTime", StringComparison.Ordinal)
                  && dropLog.Contains("Time.unscaledTime", StringComparison.Ordinal),
                "140H2-3E: sustained IMU queue back-pressure diagnostics are throttled");
        }

        private static void TransportAndBaseSchedulersRemainOutOfScope()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var queue = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs");
            Check(!virtualImu.Contains("ShouldPublishNow()", StringComparison.Ordinal)
                  && !virtualImu.Contains("ShouldPublishNowFixed()", StringComparison.Ordinal),
                "140H2-4A: VirtualImu remains outside FoxglovePublisherBase scheduler helpers");
            Check(!queue.Contains("public string Topic", StringComparison.Ordinal)
                  && !queue.Contains("public uint ChannelId", StringComparison.Ordinal)
                  && !queue.Contains("PublishCadence", StringComparison.Ordinal),
                "140H2-4B: transport queue remains topic-agnostic and unpaced by this phase");
        }

        private static void ManagerExposesOptInFrameStallDiagnostics()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");
            Check(diagnostics.Contains("_frameStallDiagnosticsEnabled", StringComparison.Ordinal)
                  && diagnostics.Contains("_frameStallDiagnosticsThresholdMs = 200f", StringComparison.Ordinal)
                  && diagnostics.Contains("Frame stall diagnostics", StringComparison.Ordinal),
                "140H2-5A: manager has opt-in frame stall diagnostics");
            Check(MethodContains(manager, "private void Update()", "RecordFrameStallDiagnosticsIfNeeded();")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.realtimeSinceStartupAsDouble")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "GC.GetTotalMemory")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.frameCount")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.deltaTime")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.unscaledDeltaTime")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.fixedDeltaTime")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Time.timeScale")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "GC.CollectionCount")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Profiler.GetMonoUsedSizeLong")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "Profiler.GetTotalAllocatedMemoryLong"),
                "140H2-5B: manager samples frame stalls once per main-thread Update");
            Check(MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "UnityEditor.EditorApplication.isCompiling")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "UnityEditor.EditorApplication.isUpdating")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "Application.isFocused")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "Application.isPlaying")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "frameCount")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "deltaTimeMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "unscaledDeltaTimeMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "fixedDeltaTimeMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "timeScale")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "gcBytesDelta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "gcCount0Delta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "gcCount1Delta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "gcCount2Delta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "monoUsedBytes")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "monoUsedBytesDelta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "totalAllocatedBytes")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "totalAllocatedBytesDelta"),
                "140H2-5C: frame stall log includes frame timing, editor, focus, play, GC, and memory state");
            Check(diagnostics.Contains("FrameStallEditorAssetRefreshProbe", StringComparison.Ordinal)
                  && diagnostics.Contains("OnPostprocessAllAssets", StringComparison.Ordinal)
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "editorAssetRefreshRecent")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "editorAssetRefreshAgeMs"),
                "140H2-5C2: frame stall log identifies stalls near Editor asset refreshes");
            Check(editor.Contains("DrawFrameStallDiagnostics", StringComparison.Ordinal)
                  && editor.Contains("Frame Stall Diagnostics", StringComparison.Ordinal)
                  && editor.Contains("Stall Threshold Ms", StringComparison.Ordinal),
                "140H2-5D: manager Inspector exposes frame stall diagnostics under Diagnostics");
            Check(MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "GetTransportStatsSnapshot()")
                  && MethodContains(diagnostics, "private void RecordFrameStallDiagnosticsIfNeeded()", "transportDroppedDelta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "transportDroppedDelta")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "transportDroppedTotal")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "transportQueuedFrames")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "transportQueuedBytes"),
                "140H2-5E: frame stall log records transport queue and dropped-frame deltas");
            Check(diagnostics.Contains("_frameStallStageTimingDiagnosticsEnabled", StringComparison.Ordinal)
                  && MethodContains(manager, "private void Update()", "BeginFrameStallStageTiming()")
                  && MethodContains(manager, "private void Update()", "RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.RuntimeTick)")
                  && MethodContains(manager, "private void Update()", "RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.ClientLifecycleDrain)")
                  && MethodContains(manager, "private void Update()", "RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.ClientMessageDrain)")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "stageRuntimeTickMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "stageClientLifecycleDrainMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "stageClientMessageDrainMs")
                  && MethodContains(diagnostics, "private static void LogFrameStallDiagnostics", "stageManagerUpdateMs")
                  && editor.Contains("Stage Timing Diagnostics", StringComparison.Ordinal),
                "140H2-5F: frame stall diagnostics can include manager Update sub-stage timings");
        }

        private static void PointCloud2NativeDiagnosticsExposeRawDeskewAndR2fuTiming()
        {
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            var nativePublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PointCloud2Native.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Diagnostics.cs");
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            Check(diagnostics.Contains("BeginPointCloud2NativeTiming", StringComparison.Ordinal)
                  && diagnostics.Contains("LogPointCloud2NativeTiming", StringComparison.Ordinal)
                  && diagnostics.Contains("[Foxglove] PointCloud2 native timing:", StringComparison.Ordinal)
                  && publisher.Contains("public bool PerformanceDiagnosticsEnabled", StringComparison.Ordinal)
                  && editor.Contains("serializedObject.FindProperty(\"_logPerformanceDiagnostics\")", StringComparison.Ordinal)
                  && editor.Contains("Log Performance Diagnostics", StringComparison.Ordinal),
                "140H2-5G: point-cloud publisher exposes opt-in PointCloud2 native timing diagnostics");
            Check(MethodContains(publisher, "protected virtual void Update()", "var pointCloud2NativeDrainStart = BeginPointCloud2NativeTiming();")
                  && MethodContains(publisher, "protected virtual void Update()", "LogPointCloud2NativeTiming(pointCloud2NativeDrainStart, \"pipelineDrain\""),
                "140H2-5H: PointCloud2 native pipeline drain timing is recorded around main-thread result processing");
            Check(MethodContains(nativePublisher, "private void PublishCompletedPointCloud2NativePayload", "\"rawNativeFrameReady\"")
                  && MethodContains(nativePublisher, "private void PublishCompletedPointCloud2NativePayload", "\"deskewedNativeFrameReady\"")
                  && MethodContains(nativePublisher, "private void PublishPointCloud2NativeFrameReady", "LogPointCloud2NativeTiming"),
                "140H2-5I: raw and deskewed PointCloud2 native handoffs record separate timing stages");
            Check(bridge.Contains("PointCloud2 native publish timing", StringComparison.Ordinal)
                  && bridge.Contains("stageTryEnsurePublisherMs", StringComparison.Ordinal)
                  && bridge.Contains("stageTfAnchorMs", StringComparison.Ordinal)
                  && bridge.Contains("stageBuildMessageMs", StringComparison.Ordinal)
                  && bridge.Contains("stagePublishMs", StringComparison.Ordinal)
                  && bridge.Contains("stageTotalMs", StringComparison.Ordinal)
                  && MethodContains(bridge, "private void OnPointCloud2NativeFrameReady", "Ros2ForUnityPointCloud2MessageBuilder.Build(frame)")
                  && MethodContains(bridge, "private void OnPointCloud2NativeFrameReady", "publisher.Publish"),
                "140H2-5J: R2FU PointCloud2 native bridge publish path records sub-stage timings");
        }

        private static void LidarAndPointCloudWorkerDiagnosticsExposeSubStageTiming()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var lidarEditor = Read("Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualLidarEditor.cs");
            var payloads = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");
            var encoders = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var nativePublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PointCloud2Native.cs");
            var motionPublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.MotionCompensation.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Diagnostics.cs");
            var nativeFrame = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloud2NativeFrame.cs");
            var packedBuilder = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            Check(MethodContains(lidar, "private void FixedUpdate()", "BeginLidarFixedUpdateTiming()")
                  && MethodContains(lidar, "private void FixedUpdate()", "LogLidarFixedUpdateTiming")
                  && MethodContains(lidar, "private static void LogLidarFixedUpdateTiming", "[LidarDiag] fixed-update timing:")
                  && MethodContains(lidar, "private static void LogLidarFixedUpdateTiming", "columnsToEmit")
                  && MethodContains(lidar, "private static void LogLidarFixedUpdateTiming", "scheduleMs"),
                "140H2-5K: VirtualLidar emits immediate FixedUpdate schedule timing diagnostics");
            Check(lidarEditor.Contains("serializedObject.FindProperty(\"_logPerformanceDiagnostics\")", StringComparison.Ordinal)
                  && lidarEditor.Contains("Log Performance Diagnostics", StringComparison.Ordinal),
                "140H2-5P: VirtualLidar Inspector exposes the performance diagnostics toggle");
            Check(scheduler.Contains("[LidarDiag] batch timing:", StringComparison.Ordinal)
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "completeMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "appendMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "copyMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "boundaryPublishMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "publishActiveScanMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "motionRequestMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "enqueueMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "startNewScanMs")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "nativeSnapshot")
                  && MethodContains(scheduler, "private void LogLidarBatchTiming", "crossings"),
                "140H2-5L: VirtualLidar scan scheduler emits immediate pending-batch timing diagnostics");
            Check(payloads.Contains("RawPackMs", StringComparison.Ordinal)
                  && payloads.Contains("RawPayloadBuildMs", StringComparison.Ordinal)
                  && payloads.Contains("MotionCompensationMs", StringComparison.Ordinal)
                  && payloads.Contains("DeskewPackMs", StringComparison.Ordinal)
                  && payloads.Contains("LogPerformanceDiagnostics", StringComparison.Ordinal)
                  && nativePublisher.Contains("_logPerformanceDiagnostics", StringComparison.Ordinal),
                "140H2-5M: PointCloud2 native worker result carries sub-stage timings");
            Check(encoders.Contains("rawPackStart", StringComparison.Ordinal)
                  && encoders.Contains("rawPayloadBuildMs", StringComparison.Ordinal)
                  && encoders.Contains("motionCompensationStart", StringComparison.Ordinal)
                  && encoders.Contains("deskewPackStart", StringComparison.Ordinal)
                  && encoders.Contains("DiagnosticStart(request.LogPerformanceDiagnostics)", StringComparison.Ordinal),
                "140H2-5N: PointCloud2 native worker measures raw pack, payload, motion, and deskew stages");
            Check(nativePublisher.Contains("LogPointCloud2NativeWorkerTiming(result)", StringComparison.Ordinal)
                  && diagnostics.Contains("[Foxglove] PointCloud2 native worker timing:", StringComparison.Ordinal)
                  && diagnostics.Contains("rawPackMs", StringComparison.Ordinal)
                  && diagnostics.Contains("motionCompensationMs", StringComparison.Ordinal)
                  && diagnostics.Contains("deskewPackMs", StringComparison.Ordinal),
                "140H2-5O: point-cloud publisher logs worker sub-stage timings when diagnostics are enabled");
            Check(publisher.Contains("MaxCompletedPointCloud2NativeResults = 1", StringComparison.Ordinal)
                  && publisher.Contains("latest completed", StringComparison.Ordinal),
                "140H2-5Q: PointCloud2 native completed queue keeps latest result only to avoid main-thread drain bursts");
            Check(!MethodContains(motionPublisher, "private PointCloudMotionCompensationRequest TryCreateMotionCompensationRequest", "TryGetPointTimeRange(")
                  && encoders.Contains("TryCompensateVirtualLidarInto", StringComparison.Ordinal),
                "140H2-5R: PointCloud2 native scan-boundary queueing leaves point time-range scans to the worker");
            Check(MethodContains(encoders, "public static PointCloud2NativeResult EncodePointCloud2NativeRequest", "BuildScanReferenceDeskewedPointCloud2Frame")
                  && !MethodContains(encoders, "private static PointCloud2NativeFrame BuildScanReferenceDeskewedPointCloud2Frame", "compensatedScratch"),
                "140H2-5S: PointCloud2 native scan-reference deskew packs directly without a second point snapshot");
            Check(MethodContains(encoders, "public static PointCloud2NativeResult EncodePointCloud2NativeRequest", "preserveSourcePointCount: true")
                  && MethodContains(encoders, "private static PointCloud2NativeFrame BuildScanReferenceDeskewedPointCloud2Frame", "preserveSourcePointCount: true")
                  && encoders.Contains("validCount: packed.ValidPointCount", StringComparison.Ordinal)
                  && nativeFrame.Contains("validCount = -1", StringComparison.Ordinal),
                "140H2-5T: PointCloud2 native raw and deskew keep stable source-width buffers with separate valid-count metadata");
            Check(packedBuilder.Contains("EvictNonPreferredBuffersFor", StringComparison.Ordinal)
                  && packedBuilder.Contains("MaxPreferredSizes", StringComparison.Ordinal)
                  && encoders.Contains("preferPooledBufferRetention: true", StringComparison.Ordinal)
                  && nativeFrame.Contains("PointCloudPackedByteBufferPool.Return(Data, _preferPooledDataRetention)", StringComparison.Ordinal)
                  && payloads.IndexOf("MotionCompensatedNativeFrame?.RecycleData()", StringComparison.Ordinal)
                     < payloads.IndexOf("NativeFrame?.RecycleData()", StringComparison.Ordinal),
                "140H2-5U: PointCloud2 native raw and deskew hot buffers are preferred over noisy one-shot sizes");
            var backgroundPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");
            Check(backgroundPipeline.Contains("AutoResetEvent _workerSignal", StringComparison.Ordinal)
                  && backgroundPipeline.Contains("_workerSignal.Set();", StringComparison.Ordinal)
                  && backgroundPipeline.Contains("_workerSignal.WaitOne();", StringComparison.Ordinal)
                  && !backgroundPipeline.Contains("_worker.MarkStoppedIfCurrentLocked(workerGeneration);\r\n                            return;", StringComparison.Ordinal),
                "140H2-5V: point-cloud encode worker stays warm across idle LiDAR scan boundaries");
        }

        private static void ValidationRegistryExposesPhase140H2()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase140h2\", \"Phase 140H2: IMU WebSocket visualization burst boundary validation\", Phase140H2Validation.Validate", StringComparison.Ordinal),
                "140H2-6A: validation registry exposes --phase140h2");
        }

        private static bool IsNestedInside(string method, string expectedInner, string enclosingStart)
        {
            var inner = method.IndexOf(expectedInner, StringComparison.Ordinal);
            var start = method.IndexOf(enclosingStart, StringComparison.Ordinal);
            if (inner < 0 || start < 0 || inner < start)
                return false;

            var brace = method.IndexOf('{', start);
            if (brace < 0 || inner < brace)
                return false;

            var depth = 0;
            for (var i = brace; i < method.Length; i++)
            {
                if (method[i] == '{')
                    depth++;
                else if (method[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return inner < i;
                }
            }

            return false;
        }

        private static bool MethodContains(string source, string signature, string expected)
        {
            return ExtractMethod(source, signature).Contains(expected, StringComparison.Ordinal);
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return string.Empty;
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase140H2 validation.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
