// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-64/65/66/67 sensor and ROS2 optimization checks.

using System;
using System.IO;
using System.Linq;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-64")]
    [Trait("Domain", "Harness")]
    public sealed class PointCloudLaserScanOptimizationTests
    {
        [Fact]
        public void ReusedPointCloudLayoutPreservesPackedAndProtobufPayloads()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 123UL,
                FrameId = "lidar",
                EmitAbsoluteTimeNs = true
            };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f)
            {
                Intensity = 0.5f,
                Reflectivity = 0.25f,
                Ring = 7,
                TimeOffsetSeconds = 0.001f
            });
            frame.Points.Add(new PointCloudPoint(4f, 5f, 6f));

            var defaultPacked = PointCloudPackedDataBuilder.Build(frame);
            var layout = PointCloudPackedDataBuilder.BuildLayout(frame);
            var reusedPacked = PointCloudPackedDataBuilder.Build(frame, layout);

            Assert.Equal(defaultPacked.PointStride, reusedPacked.PointStride);
            Assert.Equal(defaultPacked.Fields.Count, reusedPacked.Fields.Count);
            Assert.True(defaultPacked.Data.SequenceEqual(reusedPacked.Data));
            Assert.True(PointCloudMessageBuilder.SerializeProtobuf(frame)
                .SequenceEqual(PointCloudMessageBuilder.SerializeProtobuf(frame, layout)));
        }

        [Fact]
        public void PointCloudAndLaserScanHotPathsReuseScannedState()
        {
            var qos = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs");
            var reducer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudQoSReducer.cs");
            var publisher = string.Concat(
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs"),
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Raw.cs"),
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PointCloud2Native.cs"),
                TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs"));
            var shouldQueue = TestSources.Slice(publisher, "private bool ShouldQueueVirtualLidarDracoFrame", "private ulong ResolveNativeDracoPublishIntervalNs");
            var laser = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var update = TestSources.Slice(laser, "private void Update()", "private void RefreshCachedAngles()");

            Assert.Contains("internal static void BuildVoxelSampleIndices(", qos, StringComparison.Ordinal);
            Assert.Contains("indices.Clear()", qos, StringComparison.Ordinal);
            Assert.Contains("seen.Clear()", qos, StringComparison.Ordinal);
            Assert.Contains("private readonly List<int> _voxelSampleIndices", reducer, StringComparison.Ordinal);
            Assert.Contains("private readonly HashSet<PointCloudQoS.VoxelKey> _voxelKeys", reducer, StringComparison.Ordinal);
            Assert.Contains("PointCloudQoS.BuildVoxelSampleIndices(frame, voxelSizeMeters, _voxelSampleIndices, _voxelKeys)", reducer, StringComparison.Ordinal);
            Assert.Contains("var sourceLayout = PointCloudPackedDataBuilder.BuildLayout(frame)", reducer, StringComparison.Ordinal);
            Assert.Contains("packedLayout = sourceLayout", reducer, StringComparison.Ordinal);
            Assert.Contains("out PointCloudPackedDataBuilder.PointCloudLayout packedLayout", reducer, StringComparison.Ordinal);
            Assert.Contains("PointCloudMessageBuilder.SerializeProtobuf(frame, packedLayout)", publisher, StringComparison.Ordinal);
            Assert.Contains("Ros2CdrPointCloudBuilder.Serialize(frame, packedLayout)", publisher, StringComparison.Ordinal);
            Assert.Contains("Ros2CdrSensorPointCloud2Builder.Serialize(frame, packedLayout)", publisher, StringComparison.Ordinal);
            Assert.Contains("PointCloudPackedDataBuilder.Build(frame, packedLayout)", publisher, StringComparison.Ordinal);
            Assert.Contains("_cachedNativeDracoMaxPublishRateHz", publisher, StringComparison.Ordinal);
            Assert.Contains("_cachedNativeDracoPublishIntervalNs", publisher, StringComparison.Ordinal);
            Assert.Contains("ResolveNativeDracoPublishIntervalNs(rateHz)", shouldQueue, StringComparison.Ordinal);
            Assert.DoesNotContain("Math.Round(1_000_000_000d / rateHz)", shouldQueue, StringComparison.Ordinal);
            Assert.Contains("_cachedStartAngleRadians", laser, StringComparison.Ordinal);
            Assert.Contains("_cachedEndAngleRadians", laser, StringComparison.Ordinal);
            Assert.Contains("RefreshCachedAngles()", update, StringComparison.Ordinal);
            Assert.DoesNotContain("_startAngleDegrees * Math.PI / 180.0", update, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14064MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_64Validation.cs", "--phase140-64", "Phase140_64Validation.Validate");
    }

    [Trait("Phase", "140-65")]
    [Trait("Domain", "Harness")]
    public sealed class VirtualLidarModelRegistryOptimizationTests
    {
        [Fact]
        public void VirtualLidarCachesScanBoundaryCallback()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var fixedUpdate = TestSources.Slice(source, "private void FixedUpdate()", "private int BudgetColumnsPerTick()");

            Assert.Contains("private Action _onScanBoundary", source, StringComparison.Ordinal);
            Assert.Contains("private Action OnScanBoundaryAction", source, StringComparison.Ordinal);
            Assert.Contains("private void OnScanBoundary()", source, StringComparison.Ordinal);
            Assert.Contains("OnScanBoundaryAction", fixedUpdate, StringComparison.Ordinal);
            Assert.DoesNotContain("() =>", fixedUpdate, StringComparison.Ordinal);
            Assert.DoesNotContain("new Action", fixedUpdate, StringComparison.Ordinal);
            Assert.Contains("StartNewScan(Time.fixedTimeAsDouble)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarModelRegistryAvoidsLinqAndPreservesLookupBehavior()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarModelRegistry.cs");

            Assert.Contains("private static readonly IReadOnlyList<LidarModelSpec> _allReadOnly = _all.AsReadOnly()", source, StringComparison.Ordinal);
            Assert.Contains("public static IReadOnlyList<LidarModelSpec> All => _allReadOnly", source, StringComparison.Ordinal);
            Assert.Contains("_byVendor.TryGetValue(v, out var specs)", source, StringComparison.Ordinal);
            Assert.Contains("_byModel.TryGetValue((v, model), out spec)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Linq", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".FirstOrDefault(", source, StringComparison.Ordinal);

            var all = LidarModelRegistry.All;
            var allAgain = LidarModelRegistry.All;
            Assert.Same(all, allAgain);
            Assert.NotEmpty(all);
            Assert.True(LidarModelRegistry.TryGet(LidarVendor.Ouster, "OS-1-32", out var os132));
            Assert.NotNull(os132);
            Assert.Equal(LidarVendor.Ouster, os132.Vendor);
            Assert.Equal("OS-1-32", os132.Model);
            Assert.False(LidarModelRegistry.TryGet(LidarVendor.Ouster, "missing-model", out var missing));
            Assert.Null(missing);

            var ouster = LidarModelRegistry.ForVendor(LidarVendor.Ouster).ToList();
            Assert.NotEmpty(ouster);
            Assert.All(ouster, spec => Assert.Equal(LidarVendor.Ouster, spec.Vendor));
            Assert.True(ouster.Select(s => s.Model).SequenceEqual(all.Where(s => s.Vendor == LidarVendor.Ouster).Select(s => s.Model)));
        }

        [Fact]
        public void Phase14065MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_65Validation.cs", "--phase140-65", "Phase140_65Validation.Validate");
    }

    [Trait("Phase", "140-66")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2CdrWriterOptimizationTests
    {
        [Fact]
        public void CdrWriterAvoidsTemporaryArraysAndPreservesPayloadBytes()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs");

            Assert.Contains("private byte[] _buffer", source, StringComparison.Ordinal);
            Assert.Contains("private int _position", source, StringComparison.Ordinal);
            Assert.Contains("EnsureCapacity", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteInt32LittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BinaryPrimitives.WriteInt64LittleEndian", source, StringComparison.Ordinal);
            Assert.Contains("BitConverter.SingleToInt32Bits", source, StringComparison.Ordinal);
            Assert.Contains("BitConverter.DoubleToInt64Bits", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BitConverter.GetBytes", source, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetMaxByteCount(value.Length)", source, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8.GetByteCount(value)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8.GetBytes(value ?? string.Empty)", source, StringComparison.Ordinal);
            Assert.Contains("public void WriteByteArray(ReadOnlySpan<byte> value)", source, StringComparison.Ordinal);

            var scalarWriter = new Ros2CdrWriter();
            scalarWriter.WriteUInt8(0x7f);
            scalarWriter.WriteUInt32(0x01020304);
            Assert.True(scalarWriter.ToArray().SequenceEqual(new byte[]
            {
                0x00, 0x01, 0x00, 0x00,
                0x7f, 0x00, 0x00, 0x00,
                0x04, 0x03, 0x02, 0x01
            }));

            var stringWriter = new Ros2CdrWriter();
            stringWriter.WriteString("A");
            Assert.True(stringWriter.ToArray().SequenceEqual(new byte[]
            {
                0x00, 0x01, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x41, 0x00
            }));

            var byteWriter = new Ros2CdrWriter();
            byteWriter.WriteByteArray(new byte[] { 0x01, 0x02, 0x03 }.AsSpan());
            Assert.True(byteWriter.ToArray().SequenceEqual(new byte[]
            {
                0x00, 0x01, 0x00, 0x00,
                0x03, 0x00, 0x00, 0x00,
                0x01, 0x02, 0x03
            }));
        }

        [Fact]
        public void GeneratedAndManualCdrBuildersUseSpanAndCapacityPatterns()
        {
            var generated = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs");
            var generator = TestSources.Text("Scripts/schema/generate_ros2_cdr_serializers.py");
            var generatorTests = TestSources.Text("Scripts/schema/regression_checks/test_schema_tooling.py");
            var frame = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrFrameTransformBuilder.cs");
            var scene = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs");
            var camera = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrCameraCalibrationBuilder.cs");

            Assert.DoesNotContain(".ToByteArray()", generated, StringComparison.Ordinal);
            Assert.Contains(".Data.Span", generated, StringComparison.Ordinal);
            Assert.Contains(".Span", generator, StringComparison.Ordinal);
            Assert.DoesNotContain("?.ToByteArray() ?? Array.Empty<byte>()", generator, StringComparison.Ordinal);
            Assert.Contains("capacity_hint_for_schema", generator, StringComparison.Ordinal);
            Assert.Contains("new Ros2CdrWriter(240)", generated, StringComparison.Ordinal);
            Assert.Contains("new Ros2CdrWriter(9488)", generated, StringComparison.Ordinal);
            Assert.Contains("writer.WriteByteArray(message.Data.Span)", generatorTests, StringComparison.Ordinal);
            Assert.Contains("new Ros2CdrWriter(128)", frame, StringComparison.Ordinal);
            Assert.Contains("new Ros2CdrWriter(EstimateCapacity(message))", scene, StringComparison.Ordinal);
            Assert.Contains("private static IReadOnlyList<double> ToListOrEmpty", camera, StringComparison.Ordinal);
            Assert.Contains("return Array.Empty<double>()", camera, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Linq", camera, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", camera, StringComparison.Ordinal);
            Assert.Contains("IReadOnlyCollection<double> k", camera, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14066MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_66Validation.cs", "--phase140-66", "Phase140_66Validation.Validate");
    }

    [Trait("Phase", "140-67")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2BridgeOptimizationTests
    {
        private const ulong SampleTimeNs = 1_700_140_067_000_000_000UL;

        [Fact]
        public void BridgeFrameOwnedPayloadPathPreservesPublicCopySemantics()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            var publisher = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Bridge/Ros2BridgePublisher.cs");

            Assert.Contains("internal static Ros2BridgeFrame CreateOwned", source, StringComparison.Ordinal);
            Assert.Contains("clonePayload: false", source, StringComparison.Ordinal);
            Assert.Contains("clonePayload ? (byte[])payload.Clone() : payload", source, StringComparison.Ordinal);

            var payload = new byte[] { 0, 1, 0, 0, 9, 8, 7 };
            var publicFrame = new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", Ros2BridgeFrame.CdrEncoding, SampleTimeNs, 1, payload);
            payload[4] = 0xff;
            using var publicPayload = new MemoryStream();
            publicFrame.WritePayloadTo(publicPayload);
            Assert.Equal(9, publicPayload.ToArray()[4]);

            var ownedPayload = new byte[] { 0, 1, 0, 0, 4, 5, 6 };
            var ownedFrame = Ros2BridgeFrame.CreateOwned("/unity/tf", "foxglove_msgs/msg/FrameTransform", Ros2BridgeFrame.CdrEncoding, SampleTimeNs, 2, ownedPayload);
            using var ownedStream = new MemoryStream();
            ownedFrame.WritePayloadTo(ownedStream);
            Assert.True(ownedStream.ToArray().SequenceEqual(ownedPayload));

            Assert.Contains("Ros2BridgeFrame.CreateOwned", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding, logTimeNs, sequence, payload)", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void FrameWriterTcpClientAndSidecarUseStreamingPayloadViews()
        {
            var frame = new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", Ros2BridgeFrame.CdrEncoding, SampleTimeNs, 7, new byte[] { 0, 1, 0, 0, 9, 8, 7 });
            var bytes = Ros2BridgeFrameWriter.Write(frame);
            using var stream = new MemoryStream();
            Ros2BridgeFrameWriter.Write(frame, stream);
            Assert.True(stream.ToArray().SequenceEqual(bytes));

            var writer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            var tcp = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeTcpClient.cs");
            var sidecar = TestSources.Text("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");

            Assert.Contains("internal static void Write(Ros2BridgeFrame frame, Stream destination)", writer, StringComparison.Ordinal);
            Assert.Contains("destination.Write(headerBytes, 0, headerBytes.Length)", writer, StringComparison.Ordinal);
            Assert.Contains("frame.WritePayloadTo(destination)", writer, StringComparison.Ordinal);
            Assert.Contains("private int _sendTimeoutMs", tcp, StringComparison.Ordinal);
            Assert.Contains("socket.SendTimeout = timeoutMs", tcp, StringComparison.Ordinal);
            Assert.Contains("_sendTimeoutMs != timeoutMs", tcp, StringComparison.Ordinal);
            Assert.Contains("Ros2BridgeFrameWriter.Write(frame, stream)", tcp, StringComparison.Ordinal);
            Assert.DoesNotContain("var bytes = Ros2BridgeFrameWriter.Write(frame)", tcp, StringComparison.Ordinal);
            Assert.DoesNotContain("socket.Send(bytes", tcp, StringComparison.Ordinal);
            Assert.Contains("struct PayloadView", sidecar, StringComparison.Ordinal);
            Assert.Contains("PayloadView payload_for_publish", sidecar, StringComparison.Ordinal);
            Assert.Contains("std::vector<uint8_t> & scratch", sidecar, StringComparison.Ordinal);
            Assert.Contains("scratch.insert(scratch.end(), frame.payload.begin(), frame.payload.end());", sidecar, StringComparison.Ordinal);
            Assert.DoesNotContain("std::vector<uint8_t> payload_for_publish", sidecar, StringComparison.Ordinal);
            Assert.Contains("topic_signature_.emplace(frame.topic, signature)", sidecar, StringComparison.Ordinal);
            Assert.DoesNotContain("topic_signature_[frame.topic] = signature", sidecar, StringComparison.Ordinal);
            Assert.Contains("publishers_.find(key)", sidecar, StringComparison.Ordinal);
            Assert.DoesNotContain("auto publisher = publishers_[key]", sidecar, StringComparison.Ordinal);
            Assert.False(SourceMethodContains(sidecar, "BridgeFrame parse_publish_frame", "const auto op = raw.header.at(\"op\").get<std::string>()"));
            Assert.True(SourceMethodContains(sidecar, "void process_client", "const auto op = raw.header.at(\"op\").get<std::string>()"));
        }

        [Fact]
        public void Phase14067MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_67Validation.cs", "--phase140-67", "Phase140_67Validation.Validate");

        private static bool SourceMethodContains(string source, string signature, string text)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return false;
            var next = source.IndexOf("\n}", start + signature.Length, StringComparison.Ordinal);
            if (next < 0)
                next = source.Length;
            return source.Substring(start, next - start).Contains(text, StringComparison.Ordinal);
        }
    }
}
