// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138Q architecture decomposition regression coverage.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Source-boundary checks for the Phase 138Q god-object decomposition pass.
    /// </summary>
    public static partial class Phase138QValidation
    {
        private static readonly Dictionary<string, string> SourceCache = new Dictionary<string, string>();

        private static int _passed;

        /// <summary>Runs all Phase 138Q validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138Q: Architecture Decomposition ===");
            _passed = 0;

            ManagerDelegatesSharedSensorClockState();
            VirtualLidarDelegatesScanDiagnostics();
            CameraPublisherDelegatesVideoSidecarOptions();
            CameraPublisherDelegatesVideoSidecarConfigFactory();
            CameraPublisherDelegatesVideoFrameValidation();
            CameraPublisherDelegatesVideoSidecarLifecycle();
            CameraPublisherDelegatesJpegWorkerPayloads();
            CameraPublisherDelegatesJpegPipelineLifecycle();
            CameraPublisherDelegatesPublishDiagnostics();
            CameraPublisherDelegatesBackpressureGate();
            CameraPublisherDelegatesReadbackTiming();
            CameraPublisherDelegatesCaptureResources();
            CameraPublisherDelegatesOutputModeRuntimeLock();
            CameraPublisherDelegatesSensorProfileResolver();
            PointCloudPublisherDelegatesWorkerPayloadTypes();
            PointCloudPublisherDelegatesWorkerEncoders();
            PointCloudPublisherDelegatesBackgroundEncodePipelines();
            PointCloudPublisherDelegatesPublishDiagnostics();
            PointCloudPublisherDelegatesTransformFallbackBuilder();
            PointCloudPublisherDelegatesPublishState();
            PointCloudPublisherDelegatesPendingFrameSlot();
            PointCloudPublisherDelegatesRosTfMath();
            VirtualLidarDelegatesScanLayout();
            VirtualLidarDelegatesScanBuffers();
            VirtualLidarDelegatesScanClock();
            VirtualLidarDelegatesUnityNumericsConversions();

            Console.WriteLine($"Phase 138Q: {_passed} checks passed.");
        }

        private static void ManagerDelegatesSharedSensorClockState()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveSharedSensorClock.cs");

            Check(helper.Contains("internal sealed class FoxgloveSharedSensorClock", StringComparison.Ordinal)
                  && helper.Contains("GetUnixTime(double physicsTimeSeconds", StringComparison.Ordinal)
                  && helper.Contains("Reset()", StringComparison.Ordinal),
                "architecture guard: 138Q-1A: manager shared sensor clock state lives in a focused helper");
            Check(manager.Contains("FoxgloveSharedSensorClock _sharedSensorClock", StringComparison.Ordinal)
                  && manager.Contains("_sharedSensorClock.GetUnixTime(physicsTimeSeconds, NowNs)", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockInitialized", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockEpochUnixNs", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockEpochPhysSeconds", StringComparison.Ordinal),
                "architecture guard: 138Q-1B: FoxgloveManager delegates shared sensor clock state");
        }

        private static void VirtualLidarDelegatesScanDiagnostics()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarScanDiagnostics.cs");

            Check(helper.Contains("internal sealed class LidarScanDiagnostics", StringComparison.Ordinal)
                  && helper.Contains("Record(", StringComparison.Ordinal)
                  && helper.Contains("Reset()", StringComparison.Ordinal),
                "architecture guard: 138Q-2A: LiDAR scan diagnostics live in a focused helper");
            Check(scheduler.Contains("LidarScanDiagnostics _scanDiagnostics", StringComparison.Ordinal)
                  && scheduler.Contains("_scanDiagnostics.Record(", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticScans", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticCompleteMsTotal", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticBuildMsTotal", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticAppendMsTotal", StringComparison.Ordinal),
                "architecture guard: 138Q-2B: VirtualLidar delegates scan diagnostic counters");
        }

        private static void CameraPublisherDelegatesVideoSidecarOptions()
        {
            var camera = ReadCameraPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarOptionsFactory.cs");
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");

            Check(helper.Contains("internal static class CameraVideoSidecarOptionsFactory", StringComparison.Ordinal)
                  && helper.Contains("CreateH264Options", StringComparison.Ordinal)
                  && helper.Contains("CreateH265Options", StringComparison.Ordinal)
                  && helper.Contains("CreateOpenH264Options", StringComparison.Ordinal)
                  && helper.Contains("CreateMediaFoundationH264Options", StringComparison.Ordinal),
                "architecture guard: 138Q-3A: camera video sidecar options live in a focused factory");
            Check(session.Contains("CameraVideoSidecarOptionsFactory.CreateH264Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateH265Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateOpenH264Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateMediaFoundationH264Options", StringComparison.Ordinal)
                  && !camera.Contains("CameraVideoSidecarOptionsFactory.Create", StringComparison.Ordinal)
                  && !camera.Contains("private FfmpegH264EncoderOptions CreateH264Options", StringComparison.Ordinal)
                  && !camera.Contains("private FfmpegH265EncoderOptions CreateH265Options", StringComparison.Ordinal)
                  && !camera.Contains("private OpenH264EncoderOptions CreateOpenH264Options", StringComparison.Ordinal)
                  && !camera.Contains("private MediaFoundationH264EncoderOptions CreateMediaFoundationH264Options", StringComparison.Ordinal),
                "architecture guard: 138Q-3B: camera video sidecar session delegates option construction");
        }

        private static void CameraPublisherDelegatesVideoSidecarConfigFactory()
        {
            var camera = ReadCameraPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarConfigFactory.cs");

            Check(helper.Contains("internal static class CameraVideoSidecarConfigFactory", StringComparison.Ordinal)
                  && helper.Contains("Create(", StringComparison.Ordinal)
                  && helper.Contains("ResolveDimension(", StringComparison.Ordinal)
                  && helper.Contains("ResolveFrameRate(", StringComparison.Ordinal)
                  && helper.Contains("CameraVideoSidecarConfig", StringComparison.Ordinal),
                "architecture guard: 138Q-3E: camera video sidecar config and frame-rate resolution live in a focused factory");
            Check(camera.Contains("CameraVideoSidecarConfigFactory.Create(", StringComparison.Ordinal)
                  && camera.Contains("CameraVideoSidecarConfigFactory.ResolveDimension(", StringComparison.Ordinal)
                  && !camera.Contains("private CameraVideoSidecarConfig CreateVideoSidecarConfig", StringComparison.Ordinal)
                  && !camera.Contains("private int ResolveEncoderFrameRate", StringComparison.Ordinal),
                "architecture guard: 138Q-3F: FoxgloveCameraPublisher delegates sidecar config creation");
        }

        private static void CameraPublisherDelegatesVideoFrameValidation()
        {
            var camera = ReadCameraPublisherSources();
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoFrameValidator.cs");

            Check(helper.Contains("internal static class CameraVideoFrameValidator", StringComparison.Ordinal)
                  && helper.Contains("TryValidateCapturedFrame(", StringComparison.Ordinal)
                  && helper.Contains("CameraVideoFrameGeometry.TryGetRgb24FrameByteCount", StringComparison.Ordinal)
                  && helper.Contains("dimensionMismatch=sidecar", StringComparison.Ordinal),
                "architecture guard: 138Q-3G: camera video readback/frame geometry validation lives in a focused helper");
            Check(pipeline.Contains("CameraVideoFrameValidator.TryValidateCapturedFrame(", StringComparison.Ordinal)
                  && camera.Contains("CameraVideoPublishPipeline _videoPublishPipeline", StringComparison.Ordinal)
                  && !camera.Contains("private bool ValidateCapturedVideoFrame", StringComparison.Ordinal)
                  && !camera.Contains("CameraVideoFrameGeometry.TryGetRgb24FrameByteCount", StringComparison.Ordinal)
                  && !camera.Contains("dimensionMismatch=byteCount", StringComparison.Ordinal),
                "architecture guard: 138Q-3H: FoxgloveCameraPublisher delegates video frame validation");
        }

        private static void CameraPublisherDelegatesVideoSidecarLifecycle()
        {
            var camera = ReadCameraPublisherSources();
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");

            Check(session.Contains("internal sealed class CameraVideoSidecarSession", StringComparison.Ordinal)
                  && session.Contains("EnsureStarted(", StringComparison.Ordinal)
                  && session.Contains("EnsureMatchesMode(", StringComparison.Ordinal)
                  && session.Contains("Stop(", StringComparison.Ordinal)
                  && session.Contains("TryDrain(", StringComparison.Ordinal),
                "architecture guard: 138Q-3C: camera video sidecar lifecycle lives in a focused session helper");
            Check(pipeline.Contains("CameraVideoSidecarSession _videoSidecarSession", StringComparison.Ordinal)
                  && pipeline.Contains("_videoSidecarSession.EnsureStarted(", StringComparison.Ordinal)
                  && pipeline.Contains("_videoSidecarSession.EnsureMatchesMode(", StringComparison.Ordinal)
                  && pipeline.Contains("_videoSidecarSession.Stop(", StringComparison.Ordinal)
                  && pipeline.Contains("_videoSidecarSession.TryDrain(", StringComparison.Ordinal)
                  && camera.Contains("CameraVideoPublishPipeline _videoPublishPipeline", StringComparison.Ordinal)
                  && !camera.Contains("ICameraVideoEncoderSidecar _videoSidecar", StringComparison.Ordinal)
                  && !camera.Contains("CameraOutputMode _videoSidecarMode", StringComparison.Ordinal)
                  && !camera.Contains("_videoSidecarWidth", StringComparison.Ordinal)
                  && !camera.Contains("_videoSidecarHeight", StringComparison.Ordinal),
                "architecture guard: 138Q-3D: FoxgloveCameraPublisher delegates video sidecar lifecycle state");
            var drain = SliceMethod(camera, "private void DrainEncodedAccessUnits()");
            var submit = SliceMethod(
                camera.Replace("\r\n", "\n", StringComparison.Ordinal),
                "private void SubmitVideoFrame(\n            AsyncGPUReadbackRequest req,\n            CameraVideoOutputProfile profile,\n            CameraCaptureIdentity identity)");
            Check(camera.Contains("private CameraVideoPublishPipeline EnsureVideoPublishPipeline()", StringComparison.Ordinal)
                  && IndexOf(drain, "var pipeline = EnsureVideoPublishPipeline();") >= 0
                  && IndexOf(drain, "var pipeline = EnsureVideoPublishPipeline();") < IndexOf(drain, "pipeline.TryDrainEncodedAccessUnits(")
                  && IndexOf(submit, "EnsureVideoPublishPipeline();") >= 0
                  && IndexOf(submit, "EnsureVideoPublishPipeline();") < IndexOf(submit, "_videoPublishPipeline.SubmitVideoFrame("),
                "architecture guard: 138Q-3D2: camera video pipeline is lazily restored before runtime drain/submit use");
        }

        private static void CameraPublisherDelegatesOutputModeRuntimeLock()
        {
            var camera = ReadCameraPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputModeRuntimeLock.cs");

            Check(helper.Contains("internal sealed class CameraOutputModeRuntimeLock", StringComparison.Ordinal)
                  && helper.Contains("Resolve(", StringComparison.Ordinal)
                  && helper.Contains("Lock(", StringComparison.Ordinal)
                  && helper.Contains("Unlock(", StringComparison.Ordinal)
                  && helper.Contains("Camera output mode changes during Play Mode are ignored", StringComparison.Ordinal),
                "architecture guard: 138Q-20A: camera runtime output-mode lock state lives in a focused helper");
            Check(camera.Contains("CameraOutputModeRuntimeLock _outputModeRuntimeLock", StringComparison.Ordinal)
                  && camera.Contains("_outputModeRuntimeLock.Resolve(", StringComparison.Ordinal)
                  && camera.Contains("_outputModeRuntimeLock.Lock(", StringComparison.Ordinal)
                  && camera.Contains("_outputModeRuntimeLock.Unlock(", StringComparison.Ordinal)
                  && !camera.Contains("private CameraOutputMode _runtimeOutputMode", StringComparison.Ordinal)
                  && !camera.Contains("_warnedRuntimeOutputModeSwitch", StringComparison.Ordinal),
                "architecture guard: 138Q-20B: FoxgloveCameraPublisher delegates Play Mode output-mode locking");
        }

        private static void PointCloudPublisherDelegatesWorkerPayloadTypes()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");

            Check(helper.Contains("internal sealed class DracoEncodeRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class DracoEncodeResult", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class PackedPointCloudRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class PackedPointCloudResult", StringComparison.Ordinal),
                "architecture guard: 138Q-4A: point-cloud worker payload records live outside the publisher");
            Check(pointcloud.Contains("DracoEncodeRequest(", StringComparison.Ordinal)
                  && pointcloud.Contains("PackedPointCloudRequest(", StringComparison.Ordinal)
                  && pointcloud.Contains("DracoEncodeResult", StringComparison.Ordinal)
                  && pointcloud.Contains("PackedPointCloudResult", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class DracoEncodeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class DracoEncodeResult", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class PackedPointCloudRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class PackedPointCloudResult", StringComparison.Ordinal),
                "architecture guard: 138Q-4B: FoxglovePointCloudPublisher uses external worker payload records");
        }

        private static void CameraPublisherDelegatesJpegWorkerPayloads()
        {
            var camera = ReadCameraPublisherSources();
            var publishPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegWorkerPayloads.cs");

            Check(helper.Contains("internal sealed class JpegEncodeRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class JpegEncodeResult", StringComparison.Ordinal)
                  && helper.Contains("internal static class CameraJpegWorkerEncoder", StringComparison.Ordinal)
                  && helper.Contains("EncodeJpegRequest(", StringComparison.Ordinal),
                "architecture guard: 138Q-5A: camera JPEG worker payload and encoder logic live outside the publisher");
            Check(camera.Contains("CameraJpegPublishPipeline _jpegPublishPipeline", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.TryQueueFrame(", StringComparison.Ordinal)
                  && publishPipeline.Contains("JpegEncodeRequest(", StringComparison.Ordinal)
                  && publishPipeline.Contains("JpegEncodeResult", StringComparison.Ordinal)
                  && publishPipeline.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeRequest", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeResult", StringComparison.Ordinal)
                  && !camera.Contains("private static JpegEncodeResult EncodeJpegRequest", StringComparison.Ordinal),
                "architecture guard: 138Q-5B: FoxgloveCameraPublisher delegates JPEG worker payload and encode details");
        }

        private static void CameraPublisherDelegatesJpegPipelineLifecycle()
        {
            var camera = ReadCameraPublisherSources();
            var publishPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");

            Check(pipeline.Contains("internal sealed class CameraJpegPipeline", StringComparison.Ordinal)
                  && pipeline.Contains("DropOldestBoundedQueue<JpegEncodeRequest> _encodeQueue", StringComparison.Ordinal)
                  && pipeline.Contains("DropOldestBoundedQueue<JpegEncodeResult> _completedQueue", StringComparison.Ordinal)
                  && pipeline.Contains("AutoResetEvent _workerSignal", StringComparison.Ordinal)
                  && pipeline.Contains("Thread _worker", StringComparison.Ordinal)
                  && pipeline.Contains("Start()", StringComparison.Ordinal)
                  && pipeline.Contains("Queue(", StringComparison.Ordinal)
                  && pipeline.Contains("Drain(", StringComparison.Ordinal)
                  && pipeline.Contains("Stop(", StringComparison.Ordinal),
                "architecture guard: 138Q-5C: camera JPEG queue/thread lifecycle lives in a focused pipeline");
            Check(camera.Contains("CameraJpegPublishPipeline _jpegPublishPipeline", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.EnsureWorkerStarted(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.TryQueueFrame(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.DrainCompleted(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline?.StopWorker(", StringComparison.Ordinal)
                  && publishPipeline.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && publishPipeline.Contains("_jpegPipeline.Start()", StringComparison.Ordinal)
                  && publishPipeline.Contains("_jpegPipeline.Queue(", StringComparison.Ordinal)
                  && publishPipeline.Contains("_jpegPipeline.Drain(", StringComparison.Ordinal)
                  && publishPipeline.Contains("_jpegPipeline.Stop(", StringComparison.Ordinal)
                  && !camera.Contains("DropOldestBoundedQueue<JpegEncodeRequest> _jpegEncodeQueue", StringComparison.Ordinal)
                  && !camera.Contains("DropOldestBoundedQueue<JpegEncodeResult> _completedJpegQueue", StringComparison.Ordinal)
                  && !camera.Contains("private Thread _jpegWorker", StringComparison.Ordinal)
                  && !camera.Contains("EncodeJpegWorkerLoop", StringComparison.Ordinal),
                "architecture guard: 138Q-5D: FoxgloveCameraPublisher delegates repeated JPEG worker lifecycle code");
            var queue = SliceMethod(
                camera.Replace("\r\n", "\n", StringComparison.Ordinal),
                "private void QueueJpegFrame(\n            AsyncGPUReadbackRequest req,\n            CameraCaptureIdentity identity,");
            var drain = SliceMethod(camera, "private void DrainCompletedJpegFrames()");
            Check(camera.Contains("private CameraJpegPublishPipeline EnsureJpegPublishPipeline()", StringComparison.Ordinal)
                  && IndexOf(queue, "EnsureJpegPublishPipeline();") >= 0
                  && IndexOf(queue, "EnsureJpegPublishPipeline();") < IndexOf(queue, "_jpegPublishPipeline.TryQueueFrame(")
                  && IndexOf(drain, "EnsureJpegPublishPipeline();") >= 0
                  && IndexOf(drain, "EnsureJpegPublishPipeline();") < IndexOf(drain, "_jpegPublishPipeline.DrainCompleted("),
                "architecture guard: 138Q-5D2: camera JPEG pipeline is lazily restored before runtime queue/drain use");
            Check(pipeline.Contains("private readonly object _queueGate", StringComparison.Ordinal)
                  && CountOccurrences(pipeline, "lock (_queueGate)") >= 4,
                "architecture guard: 138Q-5D4: camera JPEG queue migration and worker admission share one synchronization gate");
        }

        private static void CameraPublisherDelegatesPublishDiagnostics()
        {
            var camera = ReadCameraPublisherSources();
            var jpegPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var videoPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs");

            Check(diagnostics.Contains("internal sealed class CameraPublishDiagnostics", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordCameraBudgetSkip(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordJpegEncodeResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogCameraIfNeeded(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordVideoDimensionMismatchDrop(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogVideoIfNeeded(", StringComparison.Ordinal)
                  && diagnostics.Contains("ResetVideoState(", StringComparison.Ordinal),
                "architecture guard: 138Q-11A: camera publish diagnostics live in a focused helper");
            Check(camera.Contains("CameraPublishDiagnostics _diagnostics", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.RecordJpegEncodeResult(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.LogCameraIfNeeded(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.RecordVideoDimensionMismatchDrop(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.LogVideoIfNeeded(", StringComparison.Ordinal)
                  && jpegPipeline.Contains("_diagnostics.RecordCameraBudgetSkip(", StringComparison.Ordinal)
                  && videoPipeline.Contains("_diagnostics.ResetVideoState()", StringComparison.Ordinal)
                  && videoPipeline.Contains("_diagnostics.RecordVideoSubmitFailure()", StringComparison.Ordinal)
                  && videoPipeline.Contains("_diagnostics.RecordVideoFrameSubmitted()", StringComparison.Ordinal)
                  && !camera.Contains("_lastRenderMs", StringComparison.Ordinal)
                  && !camera.Contains("_videoSubmitFailureCount", StringComparison.Ordinal)
                  && !camera.Contains("private void LogCameraDiagnosticsIfNeeded", StringComparison.Ordinal)
                  && !camera.Contains("private void LogVideoDiagnosticsIfNeeded", StringComparison.Ordinal),
                "architecture guard: 138Q-11B: FoxgloveCameraPublisher delegates camera/video diagnostic counters");
        }

        private static void CameraPublisherDelegatesBackpressureGate()
        {
            var camera = ReadCameraPublisherSources();
            var gate = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraBackpressureGate.cs");

            Check(gate.Contains("internal sealed class CameraBackpressureGate", StringComparison.Ordinal)
                  && gate.Contains("CameraBackpressurePolicy.Evaluate", StringComparison.Ordinal)
                  && gate.Contains("AllowCapture(", StringComparison.Ordinal)
                  && gate.Contains("ResetSkipLogCount(", StringComparison.Ordinal)
                  && gate.Contains("Reset()", StringComparison.Ordinal),
                "architecture guard: 138Q-13A: camera backpressure runtime state lives in a focused helper");
            Check(camera.Contains("CameraBackpressureGate _backpressureGate", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.AllowCapture(", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.ResetSkipLogCount()", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.Reset()", StringComparison.Ordinal)
                  && !camera.Contains("_lastDropCount", StringComparison.Ordinal)
                  && !camera.Contains("_cooldownUntilSec", StringComparison.Ordinal)
                  && !camera.Contains("_backpressureSkipLogCount", StringComparison.Ordinal)
                  && !camera.Contains("_backpressureBaselineInitialized", StringComparison.Ordinal)
                  && !camera.Contains("private void LogBackpressureSkip", StringComparison.Ordinal),
                "architecture guard: 138Q-13B: FoxgloveCameraPublisher delegates backpressure baseline, cooldown, and skip logging state");
        }

        private static string ReadCameraPublisherSources()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers");
            const string cacheKey = "FoxgloveCameraPublisher*.cs";
            if (SourceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs"))
                output.AppendLine(File.ReadAllText(file));
            var text = output.ToString();
            SourceCache[cacheKey] = text;
            return text;
        }
}
}
