// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138L validation for SLAM PointCloud2 native pipeline boundaries.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Phase 138L checks for standard sensor_msgs/msg/PointCloud2 SLAM output.
    /// </summary>
    public static class Phase138LValidation
    {
        private const int ExpectedFoxgloveRos2SchemaSnapshotCount = 41;
        private static int _passed;

        /// <summary>Runs all Phase 138L validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138L: SLAM PointCloud2 Native Pipeline ===");
            _passed = 0;

            ExistingRawRos2SchemaRemainsFoxglovePointCloud();
            SensorPointCloud2BuilderWritesStandardPointCloud2();
            NativeVirtualLidarPackedDataSkipsInvalidRaysWithoutPointCloudFrame();
            NativeFrameHandoffValidatesLayout();
            PointCloud2NativeModeUsesStandardSchemaAndNativeQueue();
            SensorPointCloud2SchemaIsRegisteredWithoutChangingFoxgloveSnapshot();
            R2fuProductBridgeConsumesPreparedNativeFrames();
            ValidationRegistryWiresPhase138L();
            VirtualLidarKeepsStaticBudgetInvariant();

            Console.WriteLine($"Phase 138L: {_passed} checks passed.");
        }

        private static void ExistingRawRos2SchemaRemainsFoxglovePointCloud()
        {
            Check(Ros2CdrPointCloudBuilder.SchemaName == "foxglove_msgs/msg/PointCloud",
                "138L-1A: existing Raw ROS2 CDR builder remains foxglove_msgs/msg/PointCloud");
            Check(Ros2PublisherSchemaNames.PointCloud == Ros2CdrPointCloudBuilder.SchemaName,
                "138L-1B: existing PointCloud publisher schema still maps to the Foxglove message");
        }

        private static void SensorPointCloud2BuilderWritesStandardPointCloud2()
        {
            var frame = BuildFullStrideFrame();
            var packed = PointCloudPackedDataBuilder.Build(frame);
            var payload = Ros2CdrSensorPointCloud2Builder.Serialize(frame);
            var reader = new Ros2CdrTestReader(payload);

            Check(Ros2CdrSensorPointCloud2Builder.SchemaName == "sensor_msgs/msg/PointCloud2",
                "138L-2A: new builder declares standard sensor_msgs/msg/PointCloud2");

            Check(reader.ReadInt32() == 1700000123, "138L-2B: PointCloud2 header stamp sec is written");
            Check(reader.ReadUInt32() == 456789012U, "138L-2C: PointCloud2 header stamp nanosec is written");
            Check(reader.ReadString() == "os_lidar", "138L-2D: PointCloud2 header frame_id is written");
            Check(reader.ReadUInt32() == 1U, "138L-2E: PointCloud2 is unorganized by default");
            Check(reader.ReadUInt32() == 2U, "138L-2F: PointCloud2 width equals point count");

            var fields = Enumerable.Range(0, checked((int)reader.ReadUInt32()))
                .Select(_ => ReadPointField(reader))
                .ToArray();
            Check(fields.Length == 8, "138L-2G: PointCloud2 field sequence preserves full SLAM stride");
            Check(HasField(fields, "x", 0, 7)
                  && HasField(fields, "y", 4, 7)
                  && HasField(fields, "z", 8, 7)
                  && HasField(fields, "intensity", 12, 7)
                  && HasField(fields, "reflectivity", 16, 7)
                  && HasField(fields, "ring", 20, 4)
                  && HasField(fields, "time_offset", 22, 7)
                  && HasField(fields, "t", 26, 6),
                "138L-2H: PointCloud2 field offsets and datatypes match packed SLAM layout");

            Check(!reader.ReadBool(), "138L-2I: PointCloud2 is little-endian");
            Check(reader.ReadUInt32() == packed.PointStride, "138L-2J: point_step matches shared packed stride");
            Check(reader.ReadUInt32() == packed.PointStride * 2U, "138L-2K: row_step matches point_step * width");
            Check(reader.ReadByteArray().SequenceEqual(packed.Data), "138L-2L: PointCloud2 data bytes match shared packed data");
            Check(reader.ReadBool(), "138L-2M: compacted PointCloud2 output is dense");
        }

        private static void NativeVirtualLidarPackedDataSkipsInvalidRaysWithoutPointCloudFrame()
        {
            var nativePoints = new[]
            {
                new VirtualLidarPointData
                {
                    X = 1f,
                    Y = 2f,
                    Z = 3f,
                    Intensity = 0.5f,
                    Reflectivity = 0.25f,
                    Ring = 7,
                    TimeOffsetSeconds = 0.001f,
                    IsValid = 1
                },
                new VirtualLidarPointData
                {
                    X = 100f,
                    Y = 200f,
                    Z = 300f,
                    Intensity = 9f,
                    Reflectivity = 9f,
                    Ring = 99,
                    TimeOffsetSeconds = 9f,
                    IsValid = 0
                },
                new VirtualLidarPointData
                {
                    X = 4f,
                    Y = 5f,
                    Z = 6f,
                    Intensity = 0.75f,
                    Reflectivity = 0.5f,
                    Ring = 8,
                    TimeOffsetSeconds = 0.002f,
                    IsValid = 1
                }
            };
            var packed = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(nativePoints, emitAbsoluteTimeNs: true);
            var expected = PointCloudPackedDataBuilder.Build(BuildFullStrideFrame());

            Check(packed.PointStride == 30U, "138L-2N: native PointCloud2 packed stride is full SLAM stride");
            Check(packed.Data.Length == 60, "138L-2O: native PointCloud2 packed data compacts valid rays only");
            Check(packed.Data.SequenceEqual(expected.Data), "138L-2P: native PointCloud2 packed bytes match managed full-stride reference");
            Check(packed.Fields.Count == expected.Fields.Count
                  && packed.Fields.Select(field => field.Name).SequenceEqual(expected.Fields.Select(field => field.Name)),
                "138L-2Q: native PointCloud2 packed fields match managed full-stride reference");
        }

        private static void NativeFrameHandoffValidatesLayout()
        {
            var packed = PointCloudPackedDataBuilder.Build(BuildFullStrideFrame());
            var handoff = new PointCloud2NativeFrame(
                1_700_000_123_456_789_012UL,
                "os_lidar",
                height: 1U,
                width: 2U,
                fields: packed.Fields,
                pointStep: packed.PointStride,
                data: packed.Data,
                isDense: true);

            Check(handoff.FrameId == "os_lidar"
                  && handoff.Width == 2U
                  && handoff.RowStep == packed.PointStride * 2U
                  && handoff.Data.SequenceEqual(packed.Data)
                  && handoff.ValidCount == 2,
                "138L-2R: schema-neutral PointCloud2NativeFrame carries layout and data for DDS handoff");

            var rejected = false;
            try
            {
                _ = new PointCloud2NativeFrame(
                    1UL,
                    "bad",
                    height: 1U,
                    width: 2U,
                    fields: packed.Fields,
                    pointStep: packed.PointStride,
                    data: new byte[1],
                    isDense: true);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Check(rejected, "138L-2S: PointCloud2NativeFrame rejects mismatched data length");
        }

        private static void PointCloud2NativeModeUsesStandardSchemaAndNativeQueue()
        {
            var mode = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudOutputMode.cs");
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var nativePublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PointCloud2Native.cs");
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var lidarFramePublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanFramePublisher.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");

            Check(mode.Contains("PointCloud2Native", StringComparison.Ordinal)
                  && mode.Contains("PointCloud2NativeTopic", StringComparison.Ordinal)
                  && mode.Contains("PointCloud2NativeSchema", StringComparison.Ordinal),
                "138L-2T: PointCloud2Native is an explicit output profile, not an overload of Raw");
            var workerEncoders = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs");
            Check(mode.Contains("Ros2PublisherSchemaNames.SensorPointCloud2", StringComparison.Ordinal)
                  && publisher.Contains("PointCloudWorkerEncoders.EncodePointCloud2NativeRequest", StringComparison.Ordinal)
                  && publisher.Contains("PublishCompletedPointCloud2NativePayload", StringComparison.Ordinal)
                  && workerEncoders.Contains("BuildPointCloud2NativePayload", StringComparison.Ordinal)
                  && workerEncoders.Contains("Ros2CdrSensorPointCloud2Builder.Serialize", StringComparison.Ordinal),
                "138L-2U: PointCloud2Native publishes standard sensor_msgs/msg/PointCloud2 CDR through the worker encoder");
            Check(publisher.Contains("CanQueueVirtualLidarPointCloud2NativeFrame", StringComparison.Ordinal)
                  && nativePublisher.Contains("TryQueueVirtualLidarPointCloud2NativeFrame", StringComparison.Ordinal),
                "138L-2V: PointCloud2Native exposes a native VirtualLidar queue entry point");
            Check(lidar.Contains("UseNativePointCloudSnapshotPath", StringComparison.Ordinal)
                  && lidarFramePublisher.Contains("TryPublishNativePointCloud2Scan", StringComparison.Ordinal),
                "138L-2W: VirtualLidar can bypass managed Points.Add for PointCloud2Native");
            Check(editor.Contains("PointCloud2 Native", StringComparison.Ordinal),
                "138L-2X: Inspector labels the SLAM PointCloud2 mode explicitly");
            Check(publisher.Contains("event Action<PointCloud2NativeFrame> PointCloud2NativeFrameReady", StringComparison.Ordinal)
                  && publisher.Contains("PointCloud2NativeFrameReady != null", StringComparison.Ordinal),
                "138L-2Y: PointCloud2Native can prepare frames for optional DDS subscribers without websocket demand");
            var dracoQueueTakesNativeFrameDemand = Regex.IsMatch(
                publisher,
                @"QueueVirtualLidarDracoEncode\s*\([^)]*bool\s+publishNativeFrame",
                RegexOptions.Singleline);
            var pointCloud2QueueTakesNativeFrameDemand = Regex.IsMatch(
                nativePublisher,
                @"QueueVirtualLidarPointCloud2Native\s*\([^)]*bool\s+publishNativeFrame",
                RegexOptions.Singleline);
            Check(!dracoQueueTakesNativeFrameDemand && pointCloud2QueueTakesNativeFrameDemand,
                "138L-2Yb: DDS native-frame demand stays on the PointCloud2 native queue, not the Draco queue");
        }

        private static void SensorPointCloud2SchemaIsRegisteredWithoutChangingFoxgloveSnapshot()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.SourceFileCount == ExpectedFoxgloveRos2SchemaSnapshotCount
                  && FoxgloveRos2MsgSchemaCatalog.Entries.Count == ExpectedFoxgloveRos2SchemaSnapshotCount,
                "138L-2Z: Foxglove ROS2 schema snapshot count stays at "
                + ExpectedFoxgloveRos2SchemaSnapshotCount
                + "; update the expected count intentionally when adding/removing generated schemas");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet(Ros2PublisherSchemaNames.SensorPointCloud2, out var entry)
                  && entry.Content.Contains("sensor_msgs/PointField", StringComparison.Ordinal)
                  && entry.Content.Contains("MSG: sensor_msgs/PointField", StringComparison.Ordinal),
                "138L-2AA: standard sensor_msgs/msg/PointCloud2 schema resolves for ROS2 publish");

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            Check(registry.TryGetSchema(Ros2PublisherSchemaNames.SensorPointCloud2, "ros2msg", out var registered)
                  && registered.Content.Contains("std_msgs/Header", StringComparison.Ordinal),
                "138L-2AB: standard PointCloud2 schema is registered for CDR advertisement");
        }

        private static void R2fuProductBridgeConsumesPreparedNativeFrames()
        {
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            var builder = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2MessageBuilder.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            var asmdef = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            var readme = Read("Packages/dev.unity2foxglove.ros2forunity/README.md");
            var sampleReadme = Read("Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/README.md");
            var rvizLauncher = Read("Scripts/smoke/ros2/launch_phase138l_rviz2.py");

            Check(publisher.Contains("public bool IsPointCloud2NativeOutput", StringComparison.Ordinal)
                  && publisher.Contains("public string PointCloud2NativeTopic", StringComparison.Ordinal)
                  && publisher.Contains("public string PointCloudFrameId", StringComparison.Ordinal)
                  && publisher.Contains("public bool PublishPointCloud2NativeTfAnchor", StringComparison.Ordinal)
                  && publisher.Contains("public string PointCloud2NativeTfParentFrame", StringComparison.Ordinal)
                  && publisher.Contains("public string PointCloud2NativeTfChildFrame", StringComparison.Ordinal),
                "138L-5A: core publisher exposes read-only product state for optional R2FU DDS adapters");
            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY\"", StringComparison.Ordinal),
                "138L-5B: native R2FU bridge compiles only when the ROS2 runtime symbol is active");
            Check(bridge.Contains("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal)
                  && bridge.Contains("FindObjectsByType<FoxglovePointCloudPublisher>", StringComparison.Ordinal)
                  && bridge.Contains("Ros2NativeOutputPolicy.Enabled", StringComparison.Ordinal),
                "138L-5C: R2FU PointCloud2 bridge is an automatic product path gated by the Manager toggle");
            Check(bridge.Contains("_source.PointCloud2NativeFrameReady += OnPointCloud2NativeFrameReady", StringComparison.Ordinal)
                  && bridge.Contains("CreateSensorPublisher<sensor_msgs.msg.PointCloud2>(topic)", StringComparison.Ordinal)
                  && !bridge.Contains("Phase138VirtualLidarPointCloud2Smoke", StringComparison.Ordinal),
                "138L-5D: R2FU bridge consumes prepared native frames with sensor-data QoS and without requiring the Phase138 smoke component");
            Check(bridge.Contains("TfAnchorTopic = \"/tf\"", StringComparison.Ordinal)
                  && bridge.Contains("CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic)", StringComparison.Ordinal)
                  && bridge.Contains("geometry_msgs.msg.TransformStamped", StringComparison.Ordinal)
                  && bridge.Contains("PublishTfAnchor", StringComparison.Ordinal)
                  && bridge.Contains("ResolveDynamicTfAnchor", StringComparison.Ordinal)
                  && bridge.Contains("CoordinateConverter.UnityToFoxglovePosition(_source.transform.position)", StringComparison.Ordinal)
                  && bridge.Contains("CoordinateConverter.UnityToFoxgloveRotation(_source.transform.rotation)", StringComparison.Ordinal)
                  && bridge.Contains("PointCloud2 Native DDS ready", StringComparison.Ordinal)
                  && !publisher.Contains("tf2_msgs", StringComparison.Ordinal),
                "138L-5Da: R2FU bridge publishes a dynamic product TF anchor while the core SDK stays ROS-free");
            Check(builder.Contains("Build(PointCloud2NativeFrame frame", StringComparison.Ordinal)
                  && builder.Contains("Data = frame.Data", StringComparison.Ordinal)
                  && !builder.Contains("PointCloudFrame", StringComparison.Ordinal),
                "138L-5E: product message builder maps PointCloud2NativeFrame data without per-point packing");
            Check(publisher.Contains("private bool _publishPointCloud2NativeTfAnchor;", StringComparison.Ordinal)
                  && !publisher.Contains("EnsurePointCloud2NativeTfAnchorInitialized", StringComparison.Ordinal)
                  && editor.Contains("Optional TF Anchor", StringComparison.Ordinal)
                  && editor.Contains("Publish PointCloud2 TF Anchor", StringComparison.Ordinal)
                  && editor.Contains("TF Parent Frame", StringComparison.Ordinal)
                  && editor.Contains("TF Child Frame", StringComparison.Ordinal),
                "138L-5Ea: PointCloud2 Native Inspector exposes an opt-in TF anchor without stealing existing TF trees by default");
            Check(readme.Contains("No extra smoke component is required", StringComparison.Ordinal)
                  && readme.Contains("Publish PointCloud2 TF Anchor", StringComparison.Ordinal)
                  && readme.Contains("Enable it only as an RViz fallback", StringComparison.Ordinal)
                  && readme.Contains("/tf", StringComparison.Ordinal)
                  && readme.Contains("ros2 topic info /points", StringComparison.Ordinal)
                  && readme.Contains("ros2 topic hz /points", StringComparison.Ordinal)
                  && readme.Contains("ros2 topic bw /points", StringComparison.Ordinal)
                  && readme.Contains("ros2 topic echo /points --once", StringComparison.Ordinal),
                "138L-5F: optional package README documents the product acceptance flow");
            Check(sampleReadme.Contains("not required for the product path", StringComparison.Ordinal)
                  && sampleReadme.Contains("FoxgloveManager", StringComparison.Ordinal)
                  && sampleReadme.Contains("PointCloud2 Native", StringComparison.Ordinal)
                  && sampleReadme.Contains("Publish PointCloud2 TF Anchor", StringComparison.Ordinal)
                  && sampleReadme.Contains("Enable it only as an RViz fallback", StringComparison.Ordinal)
                  && sampleReadme.Contains("/points", StringComparison.Ordinal),
                "138L-5G: Virtual LiDAR sample README no longer teaches manual smoke mounting as the default path");
            Check(bridge.Contains("OnApplicationQuit", StringComparison.Ordinal)
                  && bridge.Contains("Application.quitting", StringComparison.Ordinal)
                  && bridge.Contains("IsShuttingDown", StringComparison.Ordinal)
                  && bridge.Contains("_ros2RuntimeWasReady", StringComparison.Ordinal)
                  && bridge.Contains("BeginShutdown();", StringComparison.Ordinal)
                  && bridge.Contains("return false;", StringComparison.Ordinal),
                "138L-5H: R2FU bridge suppresses expected runtime-not-ready noise during Play Mode shutdown");
            Check(rvizLauncher.Contains("subprocess.TimeoutExpired", StringComparison.Ordinal)
                  && rvizLauncher.Contains("--strict-topic-probe", StringComparison.Ordinal)
                  && rvizLauncher.Contains("RViz2 can still launch", StringComparison.Ordinal)
                  && rvizLauncher.Contains("static_transform_publisher", StringComparison.Ordinal)
                  && rvizLauncher.Contains("--static-tf", StringComparison.Ordinal)
                  && rvizLauncher.Contains("Static TF fallback", StringComparison.Ordinal)
                  && rvizLauncher.Contains("--no-static-tf", StringComparison.Ordinal),
                "138L-5I: RViz2 launcher keeps graph probe non-blocking and makes static TF fallback opt-in");
        }

        private static void ValidationRegistryWiresPhase138L()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("--phase138l", StringComparison.Ordinal)
                  && registry.Contains("Phase138LValidation.Validate", StringComparison.Ordinal),
                "138L-3A: validation registry exposes --phase138l");
            Check(project.Contains("Phase138LValidation.cs", StringComparison.Ordinal),
                "138L-3B: test project compiles Phase138L validation");
        }

        private static void VirtualLidarKeepsStaticBudgetInvariant()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var buffers = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanBuffers.cs");

            Check(lidar.Contains("_maxRaycastCommandsPerFixedUpdate = 6144", StringComparison.Ordinal),
                "138L-4A: VirtualLidar keeps the 138I static raycast budget cap");
            Check(lidar.Contains("_scanBuffers.BudgetColumnsPerTick(_maxRaycastCommandsPerFixedUpdate)", StringComparison.Ordinal)
                  && buffers.Contains("return Math.Max(1, maxRaycastCommandsPerFixedUpdate / perColumn)", StringComparison.Ordinal),
                "138L-4B: BudgetColumnsPerTick remains cap-based");
            Check(lidar.Contains("StartNewScan(Time.fixedTimeAsDouble)", StringComparison.Ordinal),
                "138L-4C: scan timestamps remain physics-time anchored");
        }

        private static PointCloudFrame BuildFullStrideFrame()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 1_700_000_123_456_789_012UL,
                FrameId = "os_lidar",
                EmitAbsoluteTimeNs = true
            };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f)
            {
                Intensity = 0.5f,
                Reflectivity = 0.25f,
                Ring = 7,
                TimeOffsetSeconds = 0.001f
            });
            frame.Points.Add(new PointCloudPoint(4f, 5f, 6f)
            {
                Intensity = 0.75f,
                Reflectivity = 0.5f,
                Ring = 8,
                TimeOffsetSeconds = 0.002f
            });
            return frame;
        }

        private static PointFieldRecord ReadPointField(Ros2CdrTestReader reader)
        {
            return new PointFieldRecord(
                reader.ReadString(),
                reader.ReadUInt32(),
                reader.ReadUInt8(),
                reader.ReadUInt32());
        }

        private static bool HasField(PointFieldRecord[] fields, string name, uint offset, byte datatype)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.Name == name
                    && field.Offset == offset
                    && field.Datatype == datatype
                    && field.Count == 1U)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Read(string path) => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private sealed class PointFieldRecord
        {
            public PointFieldRecord(string name, uint offset, byte datatype, uint count)
            {
                Name = name;
                Offset = offset;
                Datatype = datatype;
                Count = count;
            }

            public string Name { get; }
            public uint Offset { get; }
            public byte Datatype { get; }
            public uint Count { get; }
        }
    }
}
