// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138U validation for LiDAR PointCloud2 visualization deskew contracts.

using System;
using System.IO;
using System.Numerics;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Regression checks for optional PointCloud2 motion-compensated visualization output.</summary>
    public static class Phase138UValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 138U checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138U: LiDAR Motion-Compensated PointCloud2 Visualization ===");
            _passed = 0;

            OptionsAndPublisherSurface();
            PoseHistoryInterpolation();
            MotionCompensatorMath();
            NativeFrameAndBridgeTopicRouting();
            CoreContainsNoRos2References();
            RegistryIncludesPhase138u();

            Console.WriteLine($"Phase 138U: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void OptionsAndPublisherSurface()
        {
            var options = PointCloudMotionCompensationOptions.CreateDefault();
            Check(!options.Enabled, "138U-1A: deskew defaults off");
            Check(options.OutputPolicy == PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic,
                "138U-1B: default policy preserves raw and emits separate visualization topic");
            Check(options.PreserveRawOutput, "138U-1C: raw output is preserved by default");
            Check(options.DeskewedTopic == PointCloudMotionCompensationOptions.DefaultDeskewedTopic,
                "138U-1D: default deskewed topic is stable");
            Check(PointCloudMotionCompensationOptions.IsLikelySlamInputTopic("/unity/point_cloud2"),
                "138U-1E: normal product topic is flagged as risky for ReplaceOutput");
            Check(options.ReferenceTime == PointCloudMotionCompensationReferenceTime.ScanStart,
                "138U-1Ea: default deskewed visualization uses scan-start reference time");

            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Check(publisher.Contains("_enableMotionCompensation", StringComparison.Ordinal),
                "138U-1F: point cloud publisher stores default-off deskew flag");
            Check(publisher.Contains("_deskewedPointCloud2NativeTopic", StringComparison.Ordinal),
                "138U-1G: point cloud publisher stores deskewed topic");
            Check(publisher.Contains("PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame", StringComparison.Ordinal),
                "138U-1H: publisher routes deskew through scan-reference coordinates");
            Check(publisher.Contains("FixedUpdate()", StringComparison.Ordinal)
                  && publisher.Contains("_motionPoseHistory.Add", StringComparison.Ordinal),
                "138U-1I: pose history is sampled on FixedUpdate/main-thread cadence");
            Check(publisher.Contains("GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)", StringComparison.Ordinal),
                "138U-1J: pose history uses the same shared physics clock as VirtualLidar scans");
            Check(publisher.Contains("CoordinateConverter.UnityToFoxglovePosition", StringComparison.Ordinal)
                  && publisher.Contains("CoordinateConverter.UnityToFoxgloveRotation", StringComparison.Ordinal),
                "138U-1K: pose history uses the same Foxglove coordinate system as native points");

            var pointData = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/VirtualLidarPointData.cs");
            Check(pointData.Contains("AcquisitionX", StringComparison.Ordinal)
                  && pointData.Contains("HasAcquisitionFrame", StringComparison.Ordinal),
                "138U-1L: native LiDAR point data keeps acquisition-time coordinates");

            var buildJob = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarBuildPointsJob.cs");
            Check(buildJob.Contains("AcquisitionWorldToLocal", StringComparison.Ordinal),
                "138U-1M: build job records per-batch acquisition-frame coordinates");

            var workerEncoders = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            Check(workerEncoders.Contains("useAcquisitionFrameCoordinates: true", StringComparison.Ordinal),
                "138U-1N: raw PointCloud2 Native packing uses rolling acquisition coordinates");
        }

        private static void PoseHistoryInterpolation()
        {
            var history = new SensorMotionPoseHistory(capacity: 4, maxAgeNs: 10_000_000_000UL);
            history.Add(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(10f, 0f, 0f), Quaternion.Identity);

            var snapshot = history.Snapshot();
            Check(SensorMotionPoseHistoryMath.TryInterpolate(snapshot, 150UL, out var pose),
                "138U-2A: pose history interpolates covered timestamps");
            Check(Math.Abs(pose.Translation.X - 5f) < 0.0001f,
                "138U-2B: pose history translation interpolation is deterministic");
            Check(history.Covers(100UL, 200UL), "138U-2C: pose history reports covered scan intervals");
            Check(!history.Covers(50UL, 200UL), "138U-2D: pose history rejects uncovered scan starts");
        }

        private static void MotionCompensatorMath()
        {
            var points = new[]
            {
                new VirtualLidarPointData
                {
                    X = 0f,
                    Y = 0f,
                    Z = 0f,
                    AcquisitionX = 0f,
                    AcquisitionY = 0f,
                    AcquisitionZ = 0f,
                    TimeOffsetSeconds = 0f,
                    IsValid = 1,
                    HasAcquisitionFrame = 1
                },
                new VirtualLidarPointData
                {
                    X = 99f,
                    Y = 0f,
                    Z = 0f,
                    AcquisitionX = 1f,
                    AcquisitionY = 0f,
                    AcquisitionZ = 0f,
                    Intensity = 0.5f,
                    Reflectivity = 0.25f,
                    TimeOffsetSeconds = 0.1f,
                    Ring = 7,
                    IsValid = 1,
                    HasAcquisitionFrame = 1
                }
            };
            var poses = new[]
            {
                new SensorMotionPoseSample(1_000_000_000UL, new Vector3(0f, 0f, 0f), Quaternion.Identity),
                new SensorMotionPoseSample(1_200_000_000UL, new Vector3(2f, 0f, 0f), Quaternion.Identity)
            };
            var request = new PointCloudMotionCompensationRequest(
                "/deskewed",
                PointCloudMotionCompensationReferenceTime.ScanStart,
                PointCloudMotionCompensationInputConvention.AcquisitionTimeSensorFrame,
                poses);

            Check(PointCloudMotionCompensator.TryCompensateVirtualLidar(
                    points,
                    points.Length,
                    1_000_000_000UL,
                    request,
                    out var result,
                    out var error),
                "138U-3A: motion compensator succeeds for covered translation case" + error);
            Check(Math.Abs(result.Points[1].X - 2f) < 0.0001f,
                "138U-3B: translation-only deskew uses acquisition-frame XYZ, not scan-reference XYZ");
            Check(result.Points[1].TimeOffsetSeconds == 0f,
                "138U-3C: deskewed rolling time offset is reset");
            Check(result.Points[1].Intensity == 0.5f && result.Points[1].Reflectivity == 0.25f && result.Points[1].Ring == 7,
                "138U-3D: deskew preserves intensity, reflectivity, and ring");
            Check(result.Points[1].HasAcquisitionFrame == 0,
                "138U-3E: deskewed output is marked as one reference-frame cloud");

            var rawPacked = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(
                points,
                points.Length,
                emitAbsoluteTimeNs: false,
                useAcquisitionFrameCoordinates: true);
            Check(Math.Abs(BitConverter.ToSingle(rawPacked.Data, 26) - 1f) < 0.0001f,
                "138U-3F: raw PointCloud2 packing selects acquisition-frame XYZ");

            var referencePacked = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(
                points,
                points.Length,
                emitAbsoluteTimeNs: false);
            Check(Math.Abs(BitConverter.ToSingle(referencePacked.Data, 26) - 99f) < 0.0001f,
                "138U-3G: scan-reference packing keeps closed visualization XYZ");

            var referenceRequest = new PointCloudMotionCompensationRequest(
                "/deskewed",
                PointCloudMotionCompensationReferenceTime.ScanMidpoint,
                PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame,
                Array.Empty<SensorMotionPoseSample>());
            Check(PointCloudMotionCompensator.TryCompensateVirtualLidar(
                    points,
                    points.Length,
                    1_000_000_000UL,
                    referenceRequest,
                    out var referenceResult,
                  out _)
                  && Math.Abs(referenceResult.Points[1].X - 99f) < 0.0001f
                  && referenceResult.ReferenceUnixNs == 1_000_000_000UL
                  && referenceResult.Points[1].TimeOffsetSeconds == 0f,
                "138U-3H: scan-reference deskew branch publishes closed scan-start visualization XYZ without pose history");
        }

        private static void NativeFrameAndBridgeTopicRouting()
        {
            var frame = new PointCloud2NativeFrame(
                1UL,
                "os_lidar",
                1U,
                1U,
                new[] { new PointCloudPackedField("x", 0U, PointCloudPackedNumericType.Float32) },
                4U,
                new byte[4],
                true,
                "/unity/point_cloud2_deskewed",
                true);
            Check(frame.Topic == "/unity/point_cloud2_deskewed",
                "138U-4A: native PointCloud2 frame can carry per-frame topic override");
            Check(frame.IsMotionCompensatedVisualization,
                "138U-4B: native PointCloud2 frame marks visualization deskew output");

            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            Check(bridge.Contains("ResolveFrameTopic", StringComparison.Ordinal)
                  && bridge.Contains("frame.Topic", StringComparison.Ordinal),
                "138U-4C: R2FU PointCloud2 bridge resolves per-frame topics");
            Check(bridge.Contains("Dictionary<string, IPublisher<sensor_msgs.msg.PointCloud2>>", StringComparison.Ordinal),
                "138U-4D: R2FU PointCloud2 bridge reuses one node with per-topic publishers");
        }

        private static void CoreContainsNoRos2References()
        {
            var runtimeSource = ReadDirectory("Packages/dev.unity2foxglove.sdk/Runtime");
            Check(!runtimeSource.Contains("using ROS2;", StringComparison.Ordinal),
                "138U-5A: runtime source contains no direct ROS2 using directives");
            Check(!runtimeSource.Contains("namespace ROS2", StringComparison.Ordinal),
                "138U-5B: runtime source contains no ROS2 namespace declaration");
            Check(!runtimeSource.Contains("sensor_msgs.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("std_msgs.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("builtin_interfaces.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("tf2_msgs.msg.", StringComparison.Ordinal),
                "138U-5C: runtime source contains no generated ROS2 namespaces");
        }

        private static void RegistryIncludesPhase138u()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase138u\"", StringComparison.Ordinal),
                "138U-6A: phase 138u is registered");
            Check(registry.Contains("Phase138UValidation.Validate", StringComparison.Ordinal),
                "138U-6B: phase 138u points at the right validation entrypoint");
        }

        private static string ReadDirectory(string path)
        {
            var output = "";
            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                output += File.ReadAllText(file) + "\n";
            return output;
        }

        private static string Read(string relativePath) => File.ReadAllText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
