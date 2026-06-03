// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138Q architecture decomposition regression coverage.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Source-boundary checks for the Phase 138Q god-object decomposition pass.
    /// </summary>
    public static class Phase138QValidation
    {
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
            CameraPublisherDelegatesVideoSidecarLifecycle();
            CameraPublisherDelegatesJpegWorkerPayloads();
            CameraPublisherDelegatesJpegPipelineLifecycle();
            CameraPublisherDelegatesPublishDiagnostics();
            CameraPublisherDelegatesBackpressureGate();
            CameraPublisherDelegatesReadbackTiming();
            PointCloudPublisherDelegatesWorkerPayloadTypes();
            PointCloudPublisherDelegatesWorkerEncoders();
            PointCloudPublisherDelegatesBackgroundEncodePipelines();
            PointCloudPublisherDelegatesPublishDiagnostics();
            PointCloudPublisherDelegatesTransformFallbackBuilder();
            PointCloudPublisherDelegatesPublishState();
            PointCloudPublisherDelegatesRosTfMath();
            VirtualLidarDelegatesScanLayout();
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
                "138Q-1A: manager shared sensor clock state lives in a focused helper");
            Check(manager.Contains("FoxgloveSharedSensorClock _sharedSensorClock", StringComparison.Ordinal)
                  && manager.Contains("_sharedSensorClock.GetUnixTime(physicsTimeSeconds, NowNs)", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockInitialized", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockEpochUnixNs", StringComparison.Ordinal)
                  && !manager.Contains("_sensorClockEpochPhysSeconds", StringComparison.Ordinal),
                "138Q-1B: FoxgloveManager delegates shared sensor clock state");
        }

        private static void VirtualLidarDelegatesScanDiagnostics()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarScanDiagnostics.cs");

            Check(helper.Contains("internal sealed class LidarScanDiagnostics", StringComparison.Ordinal)
                  && helper.Contains("Record(", StringComparison.Ordinal)
                  && helper.Contains("Reset()", StringComparison.Ordinal),
                "138Q-2A: LiDAR scan diagnostics live in a focused helper");
            Check(lidar.Contains("LidarScanDiagnostics _scanDiagnostics", StringComparison.Ordinal)
                  && lidar.Contains("_scanDiagnostics.Record(", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticScans", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticCompleteMsTotal", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticBuildMsTotal", StringComparison.Ordinal)
                  && !lidar.Contains("_diagnosticAppendMsTotal", StringComparison.Ordinal),
                "138Q-2B: VirtualLidar delegates scan diagnostic counters");
        }

        private static void CameraPublisherDelegatesVideoSidecarOptions()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarOptionsFactory.cs");
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");

            Check(helper.Contains("internal static class CameraVideoSidecarOptionsFactory", StringComparison.Ordinal)
                  && helper.Contains("CreateH264Options", StringComparison.Ordinal)
                  && helper.Contains("CreateH265Options", StringComparison.Ordinal)
                  && helper.Contains("CreateOpenH264Options", StringComparison.Ordinal)
                  && helper.Contains("CreateMediaFoundationH264Options", StringComparison.Ordinal),
                "138Q-3A: camera video sidecar options live in a focused factory");
            Check(session.Contains("CameraVideoSidecarOptionsFactory.CreateH264Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateH265Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateOpenH264Options", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarOptionsFactory.CreateMediaFoundationH264Options", StringComparison.Ordinal)
                  && !camera.Contains("CameraVideoSidecarOptionsFactory.Create", StringComparison.Ordinal)
                  && !camera.Contains("private FfmpegH264EncoderOptions CreateH264Options", StringComparison.Ordinal)
                  && !camera.Contains("private FfmpegH265EncoderOptions CreateH265Options", StringComparison.Ordinal)
                  && !camera.Contains("private OpenH264EncoderOptions CreateOpenH264Options", StringComparison.Ordinal)
                  && !camera.Contains("private MediaFoundationH264EncoderOptions CreateMediaFoundationH264Options", StringComparison.Ordinal),
                "138Q-3B: camera video sidecar session delegates option construction");
        }

        private static void CameraPublisherDelegatesVideoSidecarLifecycle()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");

            Check(session.Contains("internal sealed class CameraVideoSidecarSession", StringComparison.Ordinal)
                  && session.Contains("EnsureStarted(", StringComparison.Ordinal)
                  && session.Contains("EnsureMatchesMode(", StringComparison.Ordinal)
                  && session.Contains("Stop(", StringComparison.Ordinal)
                  && session.Contains("TryDrain(", StringComparison.Ordinal),
                "138Q-3C: camera video sidecar lifecycle lives in a focused session helper");
            Check(camera.Contains("CameraVideoSidecarSession _videoSidecarSession", StringComparison.Ordinal)
                  && camera.Contains("_videoSidecarSession.EnsureStarted(", StringComparison.Ordinal)
                  && camera.Contains("_videoSidecarSession.EnsureMatchesMode(", StringComparison.Ordinal)
                  && camera.Contains("_videoSidecarSession.Stop(", StringComparison.Ordinal)
                  && camera.Contains("_videoSidecarSession.TryDrain(", StringComparison.Ordinal)
                  && !camera.Contains("ICameraVideoEncoderSidecar _videoSidecar", StringComparison.Ordinal)
                  && !camera.Contains("CameraOutputMode _videoSidecarMode", StringComparison.Ordinal)
                  && !camera.Contains("_videoSidecarWidth", StringComparison.Ordinal)
                  && !camera.Contains("_videoSidecarHeight", StringComparison.Ordinal),
                "138Q-3D: FoxgloveCameraPublisher delegates video sidecar lifecycle state");
        }

        private static void PointCloudPublisherDelegatesWorkerPayloadTypes()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerPayloads.cs");

            Check(helper.Contains("internal sealed class DracoEncodeRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class DracoEncodeResult", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class PointCloud2NativeRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class PointCloud2NativeResult", StringComparison.Ordinal),
                "138Q-4A: point-cloud worker payload records live outside the publisher");
            Check(pointcloud.Contains("DracoEncodeRequest(", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloud2NativeRequest(", StringComparison.Ordinal)
                  && pointcloud.Contains("DracoEncodeResult", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloud2NativeResult", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class DracoEncodeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class DracoEncodeResult", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class PointCloud2NativeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private sealed class PointCloud2NativeResult", StringComparison.Ordinal),
                "138Q-4B: FoxglovePointCloudPublisher uses external worker payload records");
        }

        private static void CameraPublisherDelegatesJpegWorkerPayloads()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegWorkerPayloads.cs");

            Check(helper.Contains("internal sealed class JpegEncodeRequest", StringComparison.Ordinal)
                  && helper.Contains("internal sealed class JpegEncodeResult", StringComparison.Ordinal)
                  && helper.Contains("internal static class CameraJpegWorkerEncoder", StringComparison.Ordinal)
                  && helper.Contains("EncodeJpegRequest(", StringComparison.Ordinal),
                "138Q-5A: camera JPEG worker payload and encoder logic live outside the publisher");
            Check(camera.Contains("JpegEncodeRequest(", StringComparison.Ordinal)
                  && camera.Contains("JpegEncodeResult", StringComparison.Ordinal)
                  && camera.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeRequest", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeResult", StringComparison.Ordinal)
                  && !camera.Contains("private static JpegEncodeResult EncodeJpegRequest", StringComparison.Ordinal),
                "138Q-5B: FoxgloveCameraPublisher delegates JPEG worker payload and encode details");
        }

        private static void CameraPublisherDelegatesJpegPipelineLifecycle()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
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
                "138Q-5C: camera JPEG queue/thread lifecycle lives in a focused pipeline");
            Check(camera.Contains("CameraJpegPipeline _jpegPipeline", StringComparison.Ordinal)
                  && camera.Contains("_jpegPipeline.Start()", StringComparison.Ordinal)
                  && camera.Contains("_jpegPipeline.Queue(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPipeline.Drain(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPipeline.Stop(", StringComparison.Ordinal)
                  && !camera.Contains("DropOldestBoundedQueue<JpegEncodeRequest> _jpegEncodeQueue", StringComparison.Ordinal)
                  && !camera.Contains("DropOldestBoundedQueue<JpegEncodeResult> _completedJpegQueue", StringComparison.Ordinal)
                  && !camera.Contains("private Thread _jpegWorker", StringComparison.Ordinal)
                  && !camera.Contains("EncodeJpegWorkerLoop", StringComparison.Ordinal),
                "138Q-5D: FoxgloveCameraPublisher delegates repeated JPEG worker lifecycle code");
        }

        private static void CameraPublisherDelegatesPublishDiagnostics()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs");

            Check(diagnostics.Contains("internal sealed class CameraPublishDiagnostics", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordCameraBudgetSkip(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordJpegEncodeResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogCameraIfNeeded(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordVideoDimensionMismatchDrop(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogVideoIfNeeded(", StringComparison.Ordinal)
                  && diagnostics.Contains("ResetVideoState(", StringComparison.Ordinal),
                "138Q-11A: camera publish diagnostics live in a focused helper");
            Check(camera.Contains("CameraPublishDiagnostics _diagnostics", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.RecordCameraBudgetSkip(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.RecordJpegEncodeResult(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.LogCameraIfNeeded(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.RecordVideoDimensionMismatchDrop(", StringComparison.Ordinal)
                  && camera.Contains("_diagnostics.LogVideoIfNeeded(", StringComparison.Ordinal)
                  && !camera.Contains("_lastRenderMs", StringComparison.Ordinal)
                  && !camera.Contains("_videoSubmitFailureCount", StringComparison.Ordinal)
                  && !camera.Contains("private void LogCameraDiagnosticsIfNeeded", StringComparison.Ordinal)
                  && !camera.Contains("private void LogVideoDiagnosticsIfNeeded", StringComparison.Ordinal),
                "138Q-11B: FoxgloveCameraPublisher delegates camera/video diagnostic counters");
        }

        private static void CameraPublisherDelegatesBackpressureGate()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var gate = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraBackpressureGate.cs");

            Check(gate.Contains("internal sealed class CameraBackpressureGate", StringComparison.Ordinal)
                  && gate.Contains("CameraBackpressurePolicy.Evaluate", StringComparison.Ordinal)
                  && gate.Contains("AllowCapture(", StringComparison.Ordinal)
                  && gate.Contains("ResetSkipLogCount(", StringComparison.Ordinal)
                  && gate.Contains("Reset()", StringComparison.Ordinal),
                "138Q-13A: camera backpressure runtime state lives in a focused helper");
            Check(camera.Contains("CameraBackpressureGate _backpressureGate", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.AllowCapture(", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.ResetSkipLogCount()", StringComparison.Ordinal)
                  && camera.Contains("_backpressureGate.Reset()", StringComparison.Ordinal)
                  && !camera.Contains("_lastDropCount", StringComparison.Ordinal)
                  && !camera.Contains("_cooldownUntilSec", StringComparison.Ordinal)
                  && !camera.Contains("_backpressureSkipLogCount", StringComparison.Ordinal)
                  && !camera.Contains("_backpressureBaselineInitialized", StringComparison.Ordinal)
                  && !camera.Contains("private void LogBackpressureSkip", StringComparison.Ordinal),
                "138Q-13B: FoxgloveCameraPublisher delegates backpressure baseline, cooldown, and skip logging state");
        }

        private static void CameraPublisherDelegatesReadbackTiming()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var timing = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraReadbackTiming.cs");

            Check(timing.Contains("internal sealed class CameraReadbackTiming", StringComparison.Ordinal)
                  && timing.Contains("Remember(", StringComparison.Ordinal)
                  && timing.Contains("TakeLatencyMs(", StringComparison.Ordinal)
                  && timing.Contains("Clear(", StringComparison.Ordinal)
                  && timing.Contains("Dictionary<ulong, long>", StringComparison.Ordinal),
                "138Q-16A: camera readback timing state lives in a focused helper");
            Check(camera.Contains("CameraReadbackTiming _readbackTiming", StringComparison.Ordinal)
                  && camera.Contains("_readbackTiming.Remember(", StringComparison.Ordinal)
                  && camera.Contains("_readbackTiming.TakeLatencyMs(", StringComparison.Ordinal)
                  && camera.Contains("_readbackTiming.Clear()", StringComparison.Ordinal)
                  && !camera.Contains("_readbackTimingGate", StringComparison.Ordinal)
                  && !camera.Contains("_readbackRequestTicks", StringComparison.Ordinal),
                "138Q-16B: FoxgloveCameraPublisher delegates readback latency bookkeeping");
        }

        private static void PointCloudPublisherDelegatesWorkerEncoders()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");

            Check(helper.Contains("internal static class PointCloudWorkerEncoders", StringComparison.Ordinal)
                  && helper.Contains("EncodeDracoRequest(", StringComparison.Ordinal)
                  && helper.Contains("EncodePointCloud2NativeRequest(", StringComparison.Ordinal)
                  && helper.Contains("BuildPointCloud2NativePayload(", StringComparison.Ordinal),
                "138Q-6A: point-cloud worker encode/build logic lives outside the publisher");
            Check(pointcloud.Contains("PointCloudWorkerEncoders.EncodeDracoRequest", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudWorkerEncoders.EncodePointCloud2NativeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static DracoEncodeResult EncodeDracoRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static PointCloud2NativeResult EncodePointCloud2NativeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static byte[] BuildPointCloud2NativePayload", StringComparison.Ordinal),
                "138Q-6B: FoxglovePointCloudPublisher delegates worker encode/build details");
        }

        private static void PointCloudPublisherDelegatesBackgroundEncodePipelines()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");

            Check(pipeline.Contains("internal sealed class BackgroundEncodePipeline<TRequest, TResult>", StringComparison.Ordinal)
                  && pipeline.Contains("BackgroundWorkerLifecycle _worker", StringComparison.Ordinal)
                  && pipeline.Contains("Func<TRequest, TResult> _encode", StringComparison.Ordinal)
                  && pipeline.Contains("Enqueue(", StringComparison.Ordinal)
                  && pipeline.Contains("Drain(", StringComparison.Ordinal)
                  && pipeline.Contains("Stop(", StringComparison.Ordinal),
                "138Q-7A: reusable background encode pipeline owns worker queue lifecycle");
            Check(pointcloud.Contains("BackgroundEncodePipeline<DracoEncodeRequest, DracoEncodeResult> _dracoEncodePipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("BackgroundEncodePipeline<PointCloud2NativeRequest, PointCloud2NativeResult> _pointCloud2NativePipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("_dracoEncodePipeline.Enqueue(request,", StringComparison.Ordinal)
                  && pointcloud.Contains("_pointCloud2NativePipeline.Enqueue(request,", StringComparison.Ordinal)
                  && !pointcloud.Contains("RunDracoEncodeWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("RunPointCloud2NativeWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("BackgroundWorkerLifecycle _dracoEncodeWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("BackgroundWorkerLifecycle _pointCloud2NativeWorker", StringComparison.Ordinal),
                "138Q-7B: FoxglovePointCloudPublisher delegates repeated worker lifecycle code");
        }

        private static void PointCloudPublisherDelegatesPublishDiagnostics()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishDiagnostics.cs");

            Check(diagnostics.Contains("internal sealed class PointCloudPublishDiagnostics", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordPrepared(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordDrop(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordEncodeResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordPointCloud2NativeResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogIfReady(", StringComparison.Ordinal),
                "138Q-9A: point-cloud publish diagnostics live in a focused helper");
            Check(pointcloud.Contains("PointCloudPublishDiagnostics _diagnostics", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordPrepared(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordDrop(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordEncodeResult(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordPointCloud2NativeResult(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.LogIfReady(", StringComparison.Ordinal)
                  && !pointcloud.Contains("_diagnosticFrames", StringComparison.Ordinal)
                  && !pointcloud.Contains("_diagnosticDrops", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void RecordPointCloudPrepared", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void LogPointCloudDiagnosticsIfReady", StringComparison.Ordinal),
                "138Q-9B: FoxglovePointCloudPublisher delegates publish diagnostic counters");
        }

        private static void PointCloudPublisherDelegatesTransformFallbackBuilder()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var builder = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudTransformFrameBuilder.cs");

            Check(builder.Contains("internal static class PointCloudTransformFrameBuilder", StringComparison.Ordinal)
                  && builder.Contains("Build(", StringComparison.Ordinal)
                  && builder.Contains("AddPoint(", StringComparison.Ordinal)
                  && builder.Contains("CoordinateConverter.UnityToFoxglovePosition", StringComparison.Ordinal),
                "138Q-12A: point-cloud transform fallback frame builder lives in a focused helper");
            Check(pointcloud.Contains("PointCloudTransformFrameBuilder.Build(", StringComparison.Ordinal)
                  && !pointcloud.Contains("private PointCloudFrame CreateFrameFromTransforms", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void AddPoint", StringComparison.Ordinal),
                "138Q-12B: FoxglovePointCloudPublisher delegates transform fallback scan and point append");
        }

        private static void PointCloudPublisherDelegatesRosTfMath()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var math = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/RosTransformMath.cs");

            Check(math.Contains("public static class RosTransformMath", StringComparison.Ordinal)
                  && math.Contains("RollPitchYawDegreesToQuaternion(", StringComparison.Ordinal)
                  && math.Contains("System.Numerics", StringComparison.Ordinal),
                "138Q-14A: ROS roll/pitch/yaw quaternion math lives in a Unity-free helper");
            Check(pointcloud.Contains("RosTransformMath.RollPitchYawDegreesToQuaternion", StringComparison.Ordinal)
                  && pointcloud.Contains("new Quaternion(q.X, q.Y, q.Z, q.W)", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static Quaternion RosRollPitchYawDegreesToQuaternion", StringComparison.Ordinal),
                "138Q-14B: FoxglovePointCloudPublisher delegates ROS RPY math and only adapts to Unity Quaternion");
        }

        private static void PointCloudPublisherDelegatesPublishState()
        {
            var pointcloud = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var state = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishState.cs");

            Check(state.Contains("internal sealed class PointCloudPublishState", StringComparison.Ordinal)
                  && state.Contains("MarkSourceDriven(", StringComparison.Ordinal)
                  && state.Contains("ResetSourceDriven(", StringComparison.Ordinal)
                  && state.Contains("ShouldSuppressTransformFallback(", StringComparison.Ordinal)
                  && state.Contains("SetPreparedDemand(", StringComparison.Ordinal)
                  && state.Contains("TryGetPreparedDemand(", StringComparison.Ordinal),
                "138Q-15A: point-cloud source/fallback and prepared-demand state lives in a focused helper");
            Check(pointcloud.Contains("PointCloudPublishState _publishState", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.MarkSourceDriven()", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.ShouldSuppressTransformFallback(", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.SetPreparedDemand(", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.TryGetPreparedDemand(", StringComparison.Ordinal)
                  && !pointcloud.Contains("_hasPreparedPublishDemand", StringComparison.Ordinal)
                  && !pointcloud.Contains("_hasSourceDrivenFrames", StringComparison.Ordinal),
                "138Q-15B: FoxglovePointCloudPublisher delegates source/fallback and prepared-demand state");
        }

        private static void VirtualLidarDelegatesScanLayout()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanLayout.cs");

            Check(helper.Contains("internal readonly struct VirtualLidarScanLayout", StringComparison.Ordinal)
                  && helper.Contains("Build(", StringComparison.Ordinal)
                  && helper.Contains("ColumnRays", StringComparison.Ordinal)
                  && helper.Contains("MaxRaysPerColumn", StringComparison.Ordinal),
                "138Q-8A: virtual LiDAR scan layout calculation lives in a focused helper");
            Check(lidar.Contains("VirtualLidarScanLayout.Build(", StringComparison.Ordinal)
                  && lidar.Contains("layout.ColumnRays", StringComparison.Ordinal)
                  && !lidar.Contains("var columnCounts = new int[_scanColumnCount]", StringComparison.Ordinal)
                  && !lidar.Contains("Bucket ray indices by column once", StringComparison.Ordinal),
                "138Q-8B: VirtualLidar delegates scan column bucketing");
        }

        private static void VirtualLidarDelegatesScanClock()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanClock.cs");

            Check(helper.Contains("internal sealed class VirtualLidarScanClock", StringComparison.Ordinal)
                  && helper.Contains("bool IsInitialized", StringComparison.Ordinal)
                  && helper.Contains("EnsureInitialized(", StringComparison.Ordinal)
                  && helper.Contains("GetScanStartUnixNs(", StringComparison.Ordinal)
                  && helper.Contains("FoxgloveTimeUtil.NowUnixTimeNs()", StringComparison.Ordinal),
                "138Q-10A: virtual LiDAR scan clock epoch state lives in a focused helper");
            Check(lidar.Contains("VirtualLidarScanClock _scanClock", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.IsInitialized", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.EnsureInitialized(", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.GetScanStartUnixNs(", StringComparison.Ordinal)
                  && !lidar.Contains("_scanClockInitialized", StringComparison.Ordinal)
                  && !lidar.Contains("_scanEpochUnixNs", StringComparison.Ordinal)
                  && !lidar.Contains("_scanEpochPhysSeconds", StringComparison.Ordinal)
                  && !lidar.Contains("private ulong ComputeScanStartUnixNs", StringComparison.Ordinal),
                "138Q-10B: VirtualLidar delegates scan clock epoch and timestamp math");
        }

        private static void VirtualLidarDelegatesUnityNumericsConversions()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var sensorUnit = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/SensorUnitProfile.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarUnityNumericsConversions.cs");

            Check(helper.Contains("internal static class LidarUnityNumericsConversions", StringComparison.Ordinal)
                  && helper.Contains("ToUnityVector3(", StringComparison.Ordinal)
                  && helper.Contains("ToNumericsVector3(", StringComparison.Ordinal)
                  && helper.Contains("ToUnityQuaternion(", StringComparison.Ordinal)
                  && helper.Contains("ToCleanUnityQuaternion(", StringComparison.Ordinal)
                  && helper.Contains("ToNumericsQuaternion(", StringComparison.Ordinal),
                "138Q-17A: LiDAR Unity/Numerics conversion math lives in a focused helper");
            Check(lidar.Contains("LidarUnityNumericsConversions.ToUnityVector3", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToUnityQuaternion", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToNumericsVector3", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToNumericsQuaternion", StringComparison.Ordinal)
                  && sensorUnit.Contains("LidarUnityNumericsConversions.ToCleanUnityQuaternion", StringComparison.Ordinal)
                  && !lidar.Contains("new System.Numerics.Vector3(value.x, value.y, value.z)", StringComparison.Ordinal)
                  && !sensorUnit.Contains("private static float CleanNearZero", StringComparison.Ordinal),
                "138Q-17B: VirtualLidar and SensorUnitProfile delegate duplicated Unity/Numerics conversions");
        }

        private static string Read(string relativePath)
        {
            if (!File.Exists(relativePath))
                return "";
            return File.ReadAllText(relativePath);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
