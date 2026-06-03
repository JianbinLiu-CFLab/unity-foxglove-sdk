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
            PointCloudPublisherDelegatesWorkerPayloadTypes();
            PointCloudPublisherDelegatesWorkerEncoders();
            VirtualLidarDelegatesScanLayout();

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
            Check(pointcloud.Contains("Queue<DracoEncodeResult> _completedDracoEncodes", StringComparison.Ordinal)
                  && pointcloud.Contains("Queue<PointCloud2NativeResult> _completedPointCloud2Native", StringComparison.Ordinal)
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
            Check(camera.Contains("DropOldestBoundedQueue<JpegEncodeRequest>", StringComparison.Ordinal)
                  && camera.Contains("CameraJpegWorkerEncoder.EncodeJpegRequest(request)", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeRequest", StringComparison.Ordinal)
                  && !camera.Contains("private sealed class JpegEncodeResult", StringComparison.Ordinal)
                  && !camera.Contains("private static JpegEncodeResult EncodeJpegRequest", StringComparison.Ordinal),
                "138Q-5B: FoxgloveCameraPublisher delegates JPEG worker payload and encode details");
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
            Check(pointcloud.Contains("PointCloudWorkerEncoders.EncodeDracoRequest(request)", StringComparison.Ordinal)
                  && pointcloud.Contains("PointCloudWorkerEncoders.EncodePointCloud2NativeRequest(request)", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static DracoEncodeResult EncodeDracoRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static PointCloud2NativeResult EncodePointCloud2NativeRequest", StringComparison.Ordinal)
                  && !pointcloud.Contains("private static byte[] BuildPointCloud2NativePayload", StringComparison.Ordinal),
                "138Q-6B: FoxglovePointCloudPublisher delegates worker encode/build details");
        }

        private static void VirtualLidarDelegatesScanLayout()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanLayout.cs");

            Check(helper.Contains("internal readonly struct VirtualLidarScanLayout", StringComparison.Ordinal)
                  && helper.Contains("Build(", StringComparison.Ordinal)
                  && helper.Contains("ColumnRays", StringComparison.Ordinal)
                  && helper.Contains("MaxRaysPerColumn", StringComparison.Ordinal),
                "138Q-7A: virtual LiDAR scan layout calculation lives in a focused helper");
            Check(lidar.Contains("VirtualLidarScanLayout.Build(", StringComparison.Ordinal)
                  && lidar.Contains("layout.ColumnRays", StringComparison.Ordinal)
                  && !lidar.Contains("var columnCounts = new int[_scanColumnCount]", StringComparison.Ordinal)
                  && !lidar.Contains("Bucket ray indices by column once", StringComparison.Ordinal),
                "138Q-7B: VirtualLidar delegates scan column bucketing");
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
