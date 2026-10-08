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
        private static void CameraPublisherDelegatesReadbackTiming()
        {
            var camera = ReadCameraPublisherSources();
            var jpegPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var timing = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraReadbackTiming.cs");

            Check(timing.Contains("internal sealed class CameraReadbackTiming", StringComparison.Ordinal)
                  && timing.Contains("Remember(", StringComparison.Ordinal)
                  && timing.Contains("TakeLatencyMs(", StringComparison.Ordinal)
                  && timing.Contains("Clear(", StringComparison.Ordinal)
                  && timing.Contains("MaxTrackedRequests", StringComparison.Ordinal)
                  && !timing.Contains("Dictionary<ulong, long>", StringComparison.Ordinal),
                "architecture guard: 138Q-16A: camera readback timing state lives in a focused helper");
            Check(camera.Contains("CameraJpegPublishPipeline _jpegPublishPipeline", StringComparison.Ordinal)
                  && camera.Contains("EnsureJpegPublishPipeline();", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.RememberReadbackStart(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline.TakeReadbackLatencyMs(", StringComparison.Ordinal)
                  && camera.Contains("_jpegPublishPipeline?.ClearReadbackTiming()", StringComparison.Ordinal)
                  && jpegPipeline.Contains("CameraReadbackTiming _readbackTiming", StringComparison.Ordinal)
                  && jpegPipeline.Contains("_readbackTiming.Remember(", StringComparison.Ordinal)
                  && jpegPipeline.Contains("_readbackTiming.TakeLatencyMs(", StringComparison.Ordinal)
                  && jpegPipeline.Contains("_readbackTiming.Clear()", StringComparison.Ordinal)
                  && !camera.Contains("_readbackTimingGate", StringComparison.Ordinal)
                  && !camera.Contains("_readbackRequestTicks", StringComparison.Ordinal),
                "architecture guard: 138Q-16B: FoxgloveCameraPublisher delegates readback latency bookkeeping");
        }

        private static void CameraPublisherDelegatesCaptureResources()
        {
            var camera = ReadCameraPublisherSources();
            var resources = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");

            Check(resources.Contains("internal sealed class CameraCaptureResources", StringComparison.Ordinal)
                  && resources.Contains("RenderTexture _captureRenderTexture", StringComparison.Ordinal)
                  && resources.Contains("Texture2D _texture2D", StringComparison.Ordinal)
                  && resources.Contains("Ensure(", StringComparison.Ordinal)
                  && resources.Contains("Cleanup()", StringComparison.Ordinal)
                  && resources.Contains("EncodeJpeg(", StringComparison.Ordinal),
                "architecture guard: 138Q-18A: camera Unity capture resources live in a focused helper");
            Check(camera.Contains("CameraCaptureResources _captureResources", StringComparison.Ordinal)
                  && camera.Contains("_captureResources.Ensure(", StringComparison.Ordinal)
                  && camera.Contains("_captureResources.Cleanup()", StringComparison.Ordinal)
                  && camera.Contains("_captureResources.CaptureCamera.Render()", StringComparison.Ordinal)
                  && camera.Contains("_captureResources.CaptureRenderTexture", StringComparison.Ordinal)
                  && camera.Contains("_captureResources.EncodeJpeg(", StringComparison.Ordinal)
                  && !camera.Contains("private Camera _sourceCam", StringComparison.Ordinal)
                  && !camera.Contains("private Camera _captureCam", StringComparison.Ordinal)
                  && !camera.Contains("private RenderTexture _captureRT", StringComparison.Ordinal)
                  && !camera.Contains("private Texture2D _texture2D", StringComparison.Ordinal),
                "architecture guard: 138Q-18B: FoxgloveCameraPublisher delegates Unity capture object ownership");
        }

        private static void CameraPublisherDelegatesSensorProfileResolver()
        {
            var camera = ReadCameraPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");

            Check(helper.Contains("internal static class CameraSensorProfileResolver", StringComparison.Ordinal)
                  && helper.Contains("ResolveProfile(", StringComparison.Ordinal)
                  && helper.Contains("ResolveFrameId(", StringComparison.Ordinal)
                  && helper.Contains("ResolveCompressedImageTopic(", StringComparison.Ordinal)
                  && helper.Contains("ResolveRawImageTopic(", StringComparison.Ordinal)
                  && helper.Contains("ResolveImageTopic(", StringComparison.Ordinal)
                  && helper.Contains("ApplyDefaults(", StringComparison.Ordinal)
                  && helper.Contains("HasCompressedImageDemand(", StringComparison.Ordinal)
                  && helper.Contains("HasRawImageDemand(", StringComparison.Ordinal),
                "architecture guard: 138Q-22A: camera sensor profile, topic, frame, and demand helpers live outside the publisher");
            Check(camera.Contains("CameraSensorProfileResolver.ResolveProfile(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.ResolveFrameId(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.ResolveImageTopic(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.ResolveRawImageTopic(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.ApplyDefaults(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.HasCompressedImageDemand(", StringComparison.Ordinal)
                  && camera.Contains("CameraSensorProfileResolver.HasRawImageDemand(", StringComparison.Ordinal)
                  && !camera.Contains("Ros2CdrSensorCompressedImageBuilder.Serialize", StringComparison.Ordinal)
                  && !camera.Contains("Ros2CdrCompressedImageBuilder.Serialize", StringComparison.Ordinal),
                "architecture guard: 138Q-22B: FoxgloveCameraPublisher delegates sensor camera profile resolution");
        }

        private static void PointCloudPublisherDelegatesWorkerEncoders()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");

            Check(helper.Contains("internal static class PointCloudWorkerEncoders", StringComparison.Ordinal)
                  && helper.Contains("EncodeDracoRequest(", StringComparison.Ordinal)
                  && helper.Contains("EncodePackedPointCloudRequest(", StringComparison.Ordinal)
                  && helper.Contains("BuildPackedPointCloudFrame(", StringComparison.Ordinal)
                  && helper.Contains("BuildDracoPublishPayloads(", StringComparison.Ordinal),
                "architecture guard: 138Q-6A: point-cloud worker encode/build logic lives outside the publisher");
            Check(pointcloud.Contains("PointCloudWorkerEncoders.EncodeDracoRequest", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudWorkerEncoders.EncodePackedPointCloudRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static DracoEncodeResult EncodeDracoRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static PackedPointCloudResult EncodePackedPointCloudRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static byte[] BuildPackedPointCloudPayload", StringComparison.Ordinal),
                "architecture guard: 138Q-6B: FoxglovePointCloudPublisher delegates worker encode/build details");
        }

        private static void PointCloudPublisherDelegatesBackgroundEncodePipelines()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var pointcloudPipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/BackgroundEncodePipeline.cs");

            Check(pipeline.Contains("internal sealed class BackgroundEncodePipeline<TRequest, TResult>", StringComparison.Ordinal)
                  && pipeline.Contains("BackgroundWorkerLifecycle _worker", StringComparison.Ordinal)
                  && pipeline.Contains("Func<TRequest, TResult> _encode", StringComparison.Ordinal)
                  && pipeline.Contains("Enqueue(", StringComparison.Ordinal)
                  && pipeline.Contains("Drain(", StringComparison.Ordinal)
                  && pipeline.Contains("Stop(", StringComparison.Ordinal),
                "architecture guard: 138Q-7A: reusable background encode pipeline owns worker queue lifecycle");
            Check(pointcloudPipeline.Contains("internal sealed class PointCloudEncodePipeline<TRequest, TResult>", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("BackgroundEncodePipeline<TRequest, TResult> _pipeline", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("_pipeline.Enqueue(request,", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("_pipeline.Drain(", StringComparison.Ordinal)
                  && pointcloudPipeline.Contains("_pipeline.Stop(", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<DracoEncodeRequest, DracoEncodeResult> _dracoEncodePipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult> _packedPointCloudPipeline", StringComparison.Ordinal)
                  && pointcloud.Contains("_dracoEncodePipeline.Queue(", StringComparison.Ordinal)
                  && pointcloud.Contains("_packedPointCloudPipeline.Queue(", StringComparison.Ordinal)
                  && !pointcloud.Contains("RunDracoEncodeWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("RunPackedPointCloudWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("BackgroundWorkerLifecycle _dracoEncodeWorker", StringComparison.Ordinal)
                  && !pointcloud.Contains("BackgroundWorkerLifecycle _packedPointCloudWorker", StringComparison.Ordinal),
                "architecture guard: 138Q-7B: FoxglovePointCloudPublisher delegates repeated worker lifecycle code");
        }

        private static void PointCloudPublisherDelegatesPublishDiagnostics()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishDiagnostics.cs");

            Check(diagnostics.Contains("internal sealed class PointCloudPublishDiagnostics", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordPrepared(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordDrop(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordEncodeResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("RecordPackedPointCloudResult(", StringComparison.Ordinal)
                  && diagnostics.Contains("LogIfReady(", StringComparison.Ordinal),
                "architecture guard: 138Q-9A: point-cloud publish diagnostics live in a focused helper");
            Check(pointcloud.Contains("PointCloudPublishDiagnostics _diagnostics", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordPrepared(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordDrop(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordEncodeResult(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.RecordPackedPointCloudResult(", StringComparison.Ordinal)
                  && pointcloud.Contains("_diagnostics.LogIfReady(", StringComparison.Ordinal)
                  && !pointcloud.Contains("_diagnosticFrames", StringComparison.Ordinal)
                  && !pointcloud.Contains("_diagnosticDrops", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void RecordPointCloudPrepared", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void LogPointCloudDiagnosticsIfReady", StringComparison.Ordinal),
                "architecture guard: 138Q-9B: FoxglovePointCloudPublisher delegates publish diagnostic counters");
        }

        private static void PointCloudPublisherDelegatesTransformFallbackBuilder()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var builder = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudTransformFrameBuilder.cs");

            Check(builder.Contains("internal sealed class TransformPointCloudSource", StringComparison.Ordinal)
                  && builder.Contains("CreateFrameFromTransforms(", StringComparison.Ordinal)
                  && builder.Contains("AddPoint(", StringComparison.Ordinal)
                  && builder.Contains("CoordinateConverter.UnityToFoxglovePosition", StringComparison.Ordinal),
                "architecture guard: 138Q-12A: point-cloud transform fallback frame builder lives in a focused helper");
            Check(builder.IndexOf("if (child == null)", StringComparison.Ordinal)
                  < builder.IndexOf("child.gameObject.activeInHierarchy", StringComparison.Ordinal),
                "architecture guard: 138Q-12A2: transform fallback null-checks children before active-state access");
            Check(pointcloud.Contains("TransformPointCloudSource _transformPointCloudSource", StringComparison.Ordinal)
                  && pointcloud.Contains("_transformPointCloudSource.CreateFrameFromTransforms(", StringComparison.Ordinal)
                  && !pointcloud.Contains("private PointCloudFrame CreateFrameFromTransforms", StringComparison.Ordinal)
                  && !pointcloud.Contains("private void AddPoint", StringComparison.Ordinal),
                "architecture guard: 138Q-12B: FoxglovePointCloudPublisher delegates transform fallback scan and point append");
        }

        private static void PointCloudPublisherDelegatesRosTfMath()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var math = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/RosTransformMath.cs");

            Check(math.Contains("public static class RosTransformMath", StringComparison.Ordinal)
                  && math.Contains("RollPitchYawDegreesToQuaternion(", StringComparison.Ordinal)
                  && math.Contains("System.Numerics", StringComparison.Ordinal),
                "architecture guard: 138Q-14A: ROS roll/pitch/yaw quaternion math lives in a Unity-free helper");
            Check(pointcloud.Contains("RosTransformMath.RollPitchYawDegreesToQuaternion", StringComparison.Ordinal)
                  && pointcloud.Contains("new Quaternion(q.X, q.Y, q.Z, q.W)", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static Quaternion RosRollPitchYawDegreesToQuaternion", StringComparison.Ordinal),
                "architecture guard: 138Q-14B: FoxglovePointCloudPublisher delegates ROS RPY math and only adapts to Unity Quaternion");
        }

        private static void PointCloudPublisherDelegatesPublishState()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var state = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishState.cs");

            Check(state.Contains("internal sealed class PointCloudPublishState", StringComparison.Ordinal)
                  && state.Contains("MarkSourceDriven(", StringComparison.Ordinal)
                  && state.Contains("ResetSourceDriven(", StringComparison.Ordinal)
                  && state.Contains("ShouldSuppressTransformFallback(", StringComparison.Ordinal)
                  && state.Contains("SetPreparedDemand(", StringComparison.Ordinal)
                  && state.Contains("TryGetPreparedDemand(", StringComparison.Ordinal),
                "architecture guard: 138Q-15A: point-cloud source/fallback and prepared-demand state lives in a focused helper");
            Check(pointcloud.Contains("PointCloudPublishState _publishState", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.MarkSourceDriven()", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.ShouldSuppressTransformFallback(", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.SetPreparedDemand(", StringComparison.Ordinal)
                  && pointcloud.Contains("_publishState.TryGetPreparedDemand(", StringComparison.Ordinal)
                  && !pointcloud.Contains("_hasPreparedPublishDemand", StringComparison.Ordinal)
                  && !pointcloud.Contains("_hasSourceDrivenFrames", StringComparison.Ordinal),
                "architecture guard: 138Q-15B: FoxglovePointCloudPublisher delegates source/fallback and prepared-demand state");
        }

        private static void PointCloudPublisherDelegatesPendingFrameSlot()
        {
            var pointcloud = ReadPointCloudPublisherSources();
            var slot = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPendingFrameSlot.cs");

            Check(slot.Contains("internal sealed class PointCloudPendingFrameSlot", StringComparison.Ordinal)
                  && slot.Contains("private readonly object _gate = new object();", StringComparison.Ordinal)
                  && slot.Contains("SetFrame(", StringComparison.Ordinal)
                  && slot.Contains("Take()", StringComparison.Ordinal)
                  && slot.Contains("ResetReplacementWarning()", StringComparison.Ordinal)
                  && slot.Contains("PointCloud pending frame replaced", StringComparison.Ordinal),
                "architecture guard: 138Q-21A: point-cloud pending frame slot and replacement warning state live in a focused helper");
            Check(pointcloud.Contains("PointCloudPendingFrameSlot _pendingFrameSlot", StringComparison.Ordinal)
                  && pointcloud.Contains("_pendingFrameSlot.SetFrame(", StringComparison.Ordinal)
                  && pointcloud.Contains("_pendingFrameSlot.Take()", StringComparison.Ordinal)
                  && pointcloud.Contains("_pendingFrameSlot.ResetReplacementWarning()", StringComparison.Ordinal)
                  && !pointcloud.Contains("private PointCloudFrame _pendingFrame", StringComparison.Ordinal)
                  && !pointcloud.Contains("_pendingFrameGate", StringComparison.Ordinal)
                  && !pointcloud.Contains("_warnedPendingDrop", StringComparison.Ordinal),
                "architecture guard: 138Q-21B: FoxglovePointCloudPublisher delegates pending frame ownership");

            var update = SliceMethod(pointcloud, "protected virtual void Update()");
            Check(IndexOf(update, "ShouldPreparePublishPayload()") >= 0
                  && IndexOf(update, "_pendingFrameSlot.Take()") >= 0
                  && IndexOf(update, "ShouldPreparePublishPayload()") < IndexOf(update, "_pendingFrameSlot.Take()"),
                "architecture guard: 138Q-21C: point-cloud pending frame is consumed only after demand preflight");
        }

        private static void VirtualLidarDelegatesScanLayout()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanLayout.cs");
            var buffers = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanBuffers.cs");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");

            Check(helper.Contains("internal readonly struct VirtualLidarScanLayout", StringComparison.Ordinal)
                  && helper.Contains("Build(", StringComparison.Ordinal)
                  && helper.Contains("ColumnRays", StringComparison.Ordinal)
                  && helper.Contains("MaxRaysPerColumn", StringComparison.Ordinal),
                "architecture guard: 138Q-8A: virtual LiDAR scan layout calculation lives in a focused helper");
            Check(buffers.Contains("VirtualLidarScanLayout.Build(", StringComparison.Ordinal)
                  && buffers.Contains("layout.ColumnRays", StringComparison.Ordinal)
                  && scheduler.Contains("scanBuffers.ColumnRays[scanColumnCursor]", StringComparison.Ordinal)
                  && !lidar.Contains("var columnCounts = new int[_scanColumnCount]", StringComparison.Ordinal)
                  && !lidar.Contains("Bucket ray indices by column once", StringComparison.Ordinal),
                "architecture guard: 138Q-8B: VirtualLidar delegates scan column bucketing");
        }

        private static void VirtualLidarDelegatesScanBuffers()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanBuffers.cs");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");

            Check(helper.Contains("internal sealed class VirtualLidarScanBuffers", StringComparison.Ordinal)
                  && helper.Contains(": IDisposable", StringComparison.Ordinal)
                  && helper.Contains("NativeArray<RaycastCommand>", StringComparison.Ordinal)
                  && helper.Contains("NativeArray<RaycastHit>", StringComparison.Ordinal)
                  && helper.Contains("NativeArray<float>", StringComparison.Ordinal)
                  && helper.Contains("NativeArray<ushort>", StringComparison.Ordinal)
                  && helper.Contains("NativeArray<VirtualLidarPointData>", StringComparison.Ordinal)
                  && helper.Contains("VirtualLidarScanLayout.Build(", StringComparison.Ordinal),
                "architecture guard: 138Q-19A: VirtualLidar persistent scan buffers live in a focused disposable helper");
            Check(lidar.Contains("VirtualLidarScanBuffers _scanBuffers", StringComparison.Ordinal)
                  && lidar.Contains("_scanBuffers.Allocate(", StringComparison.Ordinal)
                  && lidar.Contains("_scanBuffers.Dispose()", StringComparison.Ordinal)
                  && lidar.Contains("_scanBuffers.BudgetColumnsPerTick(", StringComparison.Ordinal)
                  && scheduler.Contains("scanBuffers.ComputeProfileHash()", StringComparison.Ordinal)
                  && scheduler.Contains("RaycastCommand.ScheduleBatch(", StringComparison.Ordinal)
                  && scheduler.Contains("DrainPendingScan()", StringComparison.Ordinal)
                  && !lidar.Contains("private NativeArray<RaycastCommand> _commands", StringComparison.Ordinal)
                  && !lidar.Contains("private NativeArray<RaycastHit> _results", StringComparison.Ordinal)
                  && !lidar.Contains("private NativeArray<float> _rayTimeOffsets", StringComparison.Ordinal)
                  && !lidar.Contains("private NativeArray<ushort> _rayRings", StringComparison.Ordinal)
                  && !lidar.Contains("private NativeArray<VirtualLidarPointData> _pointData", StringComparison.Ordinal),
                "architecture guard: 138Q-19B: VirtualLidar delegates NativeArray ownership and budget/profile math");
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
                "architecture guard: 138Q-10A: virtual LiDAR scan clock epoch state lives in a focused helper");
            Check(lidar.Contains("VirtualLidarScanClock _scanClock", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.IsInitialized", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.EnsureInitialized(", StringComparison.Ordinal)
                  && lidar.Contains("_scanClock.GetScanStartUnixNs(", StringComparison.Ordinal)
                  && !lidar.Contains("_scanClockInitialized", StringComparison.Ordinal)
                  && !lidar.Contains("_scanEpochUnixNs", StringComparison.Ordinal)
                  && !lidar.Contains("_scanEpochPhysSeconds", StringComparison.Ordinal)
                  && !lidar.Contains("private ulong ComputeScanStartUnixNs", StringComparison.Ordinal),
                "architecture guard: 138Q-10B: VirtualLidar delegates scan clock epoch and timestamp math");
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
                "architecture guard: 138Q-17A: LiDAR Unity/Numerics conversion math lives in a focused helper");
            Check(lidar.Contains("LidarUnityNumericsConversions.ToUnityVector3", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToUnityQuaternion", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToNumericsVector3", StringComparison.Ordinal)
                  && lidar.Contains("LidarUnityNumericsConversions.ToNumericsQuaternion", StringComparison.Ordinal)
                  && sensorUnit.Contains("LidarUnityNumericsConversions.ToCleanUnityQuaternion", StringComparison.Ordinal)
                  && !lidar.Contains("new System.Numerics.Vector3(value.x, value.y, value.z)", StringComparison.Ordinal)
                  && !sensorUnit.Contains("private static float CleanNearZero", StringComparison.Ordinal),
                "architecture guard: 138Q-17B: VirtualLidar and SensorUnitProfile delegate duplicated Unity/Numerics conversions");
        }

        private static string Read(string relativePath)
        {
            if (SourceCache.TryGetValue(relativePath, out var cached))
                return cached;

            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new InvalidOperationException("Phase 138Q cannot find expected file: " + path);
            var text = File.ReadAllText(path);
            SourceCache[relativePath] = text;
            return text;
        }

        private static string ReadPointCloudPublisherSources()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers");
            const string cacheKey = "FoxglovePointCloudPublisher*.cs";
            if (SourceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "FoxglovePointCloudPublisher*.cs"))
                output.AppendLine(File.ReadAllText(file));
            var text = output.ToString();
            SourceCache[cacheKey] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new DirectoryNotFoundException("Could not find repository root for Phase138Q validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static int IndexOf(string text, string pattern)
            => text.IndexOf(pattern, StringComparison.Ordinal);

        private static string SliceMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return "";

            var brace = source.IndexOf('{', index);
            if (brace < 0)
                return "";

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
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
