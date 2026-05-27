// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 49 validation for sensor typed publisher builders.

using System;
using System.Linq;
using Foxglove;
using Foxglove.Schemas;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates PointCloud, LaserScan, and CameraCalibration builders.
    /// </summary>
    public static class Phase49Validation
    {
        private static int _passed;
        private static int _failed;

        /// <summary>Run all Phase 49 checks.</summary>
        public static void Validate()
        {
            _passed = 0;
            _failed = 0;

            Console.WriteLine("=== Phase 49: Sensor Typed Publisher Parity ===");

            VerifyPointCloudXyzLayout();
            VerifyPointCloudOptionalOusterFields();
            VerifyPointCloudJsonBase64Roundtrip();
            VerifyPointCloudProtobufRoundtrip();
            VerifyLaserScanJsonAndProtobuf();
            VerifyLaserScanRejectsMismatchedIntensityCount();
            VerifyCameraCalibrationValidation();
            VerifyCameraCalibrationAutoIntrinsics();
            VerifyCameraCalibrationProtobufRoundtrip();
            VerifyJsonSchemasRegistered();
            VerifyPointCloudJsonSchemaHasPackedFieldItems();
            VerifyCatalogMarksDedicatedPublishers();

            if (_failed > 0)
                throw new Exception($"Phase 49 failed: {_failed} check(s) failed.");

            Console.WriteLine($"Phase 49: {_passed} checks passed.");
        }

        private static void VerifyPointCloudXyzLayout()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 1_234_567_890UL,
                FrameId = "os_lidar"
            };
            frame.Points.Add(new PointCloudPoint(1, 2, 3));
            frame.Points.Add(new PointCloudPoint(-1, -2, -3));

            var built = PointCloudMessageBuilder.Build(frame);

            Check(built.Json.PointStride == 12, "49A-1: XYZ-only point_stride is 12");
            Check(built.Json.Fields.Count == 3, "49A-2: XYZ-only layout has 3 fields");
            Check(built.Json.Fields[0].Name == "x" && built.Json.Fields[0].Offset == 0, "49A-3: x field offset is 0");
            Check(built.Json.Fields[1].Name == "y" && built.Json.Fields[1].Offset == 4, "49A-4: y field offset is 4");
            Check(built.Json.Fields[2].Name == "z" && built.Json.Fields[2].Offset == 8, "49A-5: z field offset is 8");
            Check(built.Data.Length == 24, "49A-6: data length equals point count * stride");
            Check(ReadSingle(built.Data, 0) == 1f && ReadSingle(built.Data, 20) == -3f, "49A-7: packed XYZ values roundtrip");
        }

        private static void VerifyPointCloudOptionalOusterFields()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 2_000_000_123UL,
                FrameId = "os_lidar"
            };
            frame.Points.Add(new PointCloudPoint(1, 2, 3)
            {
                Intensity = 4,
                Reflectivity = 5,
                Ring = 6,
                TimeOffsetSeconds = 0.007f
            });

            var built = PointCloudMessageBuilder.Build(frame);

            Check(built.Json.PointStride == 26, "49A-8: Ouster-ready stride includes intensity/reflectivity/ring/time_offset");
            Check(string.Join(",", built.Json.Fields.Select(f => f.Name)) == "x,y,z,intensity,reflectivity,ring,time_offset",
                "49A-9: optional fields use deterministic order");
            Check(built.Json.Fields.Single(f => f.Name == "ring").Offset == 20, "49A-10: ring offset is 20");
            Check(built.Json.Fields.Single(f => f.Name == "time_offset").Offset == 22, "49A-11: time_offset offset is 22");
            Check(ReadUInt16(built.Data, 20) == 6, "49A-12: ring packs as UInt16");
            Check(Math.Abs(ReadSingle(built.Data, 22) - 0.007f) < 0.000001f, "49A-13: time_offset packs as Float32 seconds");
        }

        private static void VerifyPointCloudJsonBase64Roundtrip()
        {
            var frame = new PointCloudFrame { UnixNs = 3_000_000_000UL, FrameId = "unity_world" };
            frame.Points.Add(new PointCloudPoint(0.5f, 1.5f, 2.5f));

            var json = PointCloudMessageBuilder.CreateJson(frame);
            var serialized = JsonConvert.SerializeObject(json);
            var roundtrip = JsonConvert.DeserializeObject<PointCloudMessage>(serialized);
            var bytes = Convert.FromBase64String(roundtrip.Data);

            Check(roundtrip.FrameId == "unity_world", "49A-14: PointCloud JSON frame_id roundtrips");
            Check(bytes.Length == 12
                  && ReadSingle(bytes, 0) == 0.5f
                  && ReadSingle(bytes, 4) == 1.5f
                  && ReadSingle(bytes, 8) == 2.5f,
                "49A-15: PointCloud JSON data is base64 packed XYZ bytes");
        }

        private static void VerifyPointCloudProtobufRoundtrip()
        {
            var frame = new PointCloudFrame { UnixNs = 4_000_000_456UL, FrameId = "os_lidar" };
            frame.Points.Add(new PointCloudPoint(9, 8, 7) { Ring = 31 });

            var payload = PointCloudMessageBuilder.SerializeProtobuf(frame);
            var parsed = PointCloud.Parser.ParseFrom(payload);

            Check(((IMessage)parsed).Descriptor.FullName == "foxglove.PointCloud", "49A-16: protobuf schema is foxglove.PointCloud");
            Check(parsed.FrameId == "os_lidar", "49A-17: PointCloud protobuf frame_id roundtrips");
            Check(parsed.PointStride == 14, "49A-18: PointCloud protobuf stride includes ring");
            Check(parsed.Data.Length == 14 && ReadUInt16(parsed.Data.ToByteArray(), 12) == 31, "49A-19: PointCloud protobuf data preserves ring");
        }

        private static void VerifyLaserScanJsonAndProtobuf()
        {
            var ranges = new[] { 1.0, 2.0, 3.0 };
            var intensities = new[] { 0.1, 0.2, 0.3 };

            var json = LaserScanMessageBuilder.CreateJson(5_000_000_789UL, "laser", -0.5, 0.5, ranges, intensities);
            var proto = LaserScanMessageBuilder.CreateProtobuf(5_000_000_789UL, "laser", -0.5, 0.5, ranges, intensities);

            Check(json.Ranges.Count == 3 && json.Intensities.Count == 3, "49A-20: LaserScan JSON keeps ranges and intensities");
            Check(proto.Ranges.Count == 3 && proto.Intensities.Count == 3, "49A-21: LaserScan protobuf keeps ranges and intensities");
            Check(proto.StartAngle == -0.5 && proto.EndAngle == 0.5, "49A-22: LaserScan angles are radians");
        }

        private static void VerifyLaserScanRejectsMismatchedIntensityCount()
        {
            var threw = false;
            try
            {
                LaserScanMessageBuilder.CreateJson(1, "laser", 0, 1, new[] { 1.0, 2.0 }, new[] { 1.0 });
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Check(threw, "49A-23: LaserScan rejects intensity length mismatch");
        }

        private static void VerifyCameraCalibrationValidation()
        {
            var threw = false;
            try
            {
                CameraCalibrationMessageBuilder.CreateJson(1, "camera", 640, 480, "plumb_bob",
                    Array.Empty<double>(), new double[8], new double[9], new double[12]);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Check(threw, "49A-24: CameraCalibration rejects K length other than 9");
        }

        private static void VerifyCameraCalibrationAutoIntrinsics()
        {
            var calibration = CameraCalibrationMessageBuilder.CreateAutoIntrinsics(1, "camera", 640, 480, 60);

            Check(calibration.Width == 640 && calibration.Height == 480, "49A-25: auto intrinsics preserve dimensions");
            Check(calibration.K.Count == 9 && calibration.R.Count == 9 && calibration.P.Count == 12,
                "49A-26: auto intrinsics produce K/R/P matrix sizes");
            Check(calibration.K[2] == 320 && calibration.K[5] == 240, "49A-27: auto intrinsics center principal point");
        }

        private static void VerifyCameraCalibrationProtobufRoundtrip()
        {
            var payload = CameraCalibrationMessageBuilder.SerializeProtobuf(
                6_000_000_111UL,
                "camera",
                800,
                600,
                "plumb_bob",
                Array.Empty<double>(),
                new[] { 500.0, 0, 400, 0, 500, 300, 0, 0, 1 },
                new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 },
                new[] { 500.0, 0, 400, 0, 0, 500, 300, 0, 0, 0, 1, 0 });

            var parsed = CameraCalibration.Parser.ParseFrom(payload);

            Check(((IMessage)parsed).Descriptor.FullName == "foxglove.CameraCalibration",
                "49A-28: protobuf schema is foxglove.CameraCalibration");
            Check(parsed.FrameId == "camera" && parsed.K.Count == 9 && parsed.P.Count == 12,
                "49A-29: CameraCalibration protobuf roundtrips");
        }

        private static void VerifyJsonSchemasRegistered()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);

            Check(registry.TryGetSchema(FoxgloveSchemaDefinitions.PointCloudSchemaName, out var pointCloud)
                  && pointCloud.Encoding == FoxgloveSchemaDefinitions.JsonSchemaEncoding
                  && pointCloud.Content.Contains("foxglove.PointCloud"),
                "49B-1: PointCloud JSON schema registered");
            Check(registry.TryGetSchema(FoxgloveSchemaDefinitions.LaserScanSchemaName, out var laserScan)
                  && laserScan.Encoding == FoxgloveSchemaDefinitions.JsonSchemaEncoding
                  && laserScan.Content.Contains("foxglove.LaserScan"),
                "49B-2: LaserScan JSON schema registered");
            Check(registry.TryGetSchema(FoxgloveSchemaDefinitions.CameraCalibrationSchemaName, out var calibration)
                  && calibration.Encoding == FoxgloveSchemaDefinitions.JsonSchemaEncoding
                  && calibration.Content.Contains("foxglove.CameraCalibration"),
                "49B-3: CameraCalibration JSON schema registered");
        }

        private static void VerifyPointCloudJsonSchemaHasPackedFieldItems()
        {
            var schema = JObject.Parse(FoxgloveSchemaDefinitions.PointCloudSchema);
            var fieldItems = schema["properties"]?["fields"]?["items"];
            var fieldType = fieldItems?["properties"]?["type"]?["type"]?.Value<string>();

            Check(fieldItems?["type"]?.Value<string>() == "object",
                "49B-4: PointCloud JSON schema fields has object items");
            Check(fieldItems?["properties"]?["name"]?["type"]?.Value<string>() == "string",
                "49B-5: PointCloud JSON schema field item has name");
            Check(fieldItems?["properties"]?["offset"]?["type"]?.Value<string>() == "integer",
                "49B-6: PointCloud JSON schema field item has offset");
            Check(fieldType == "integer",
                "49B-7: PointCloud JSON schema field item has type");
        }

        private static void VerifyCatalogMarksDedicatedPublishers()
        {
            Check(FoxgloveProtoSchemaCatalog.TryGet("foxglove.PointCloud", out var pointCloud)
                  && pointCloud.HasDedicatedUnityPublisher,
                "49F-1: catalog marks PointCloud as dedicated publisher");
            Check(FoxgloveProtoSchemaCatalog.TryGet("foxglove.LaserScan", out var laserScan)
                  && laserScan.HasDedicatedUnityPublisher,
                "49F-2: catalog marks LaserScan as dedicated publisher");
            Check(FoxgloveProtoSchemaCatalog.TryGet("foxglove.CameraCalibration", out var calibration)
                  && calibration.HasDedicatedUnityPublisher,
                "49F-3: catalog marks CameraCalibration as dedicated publisher");
        }

        private static float ReadSingle(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);
        private static ushort ReadUInt16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("[PASS] " + message);
            }
            else
            {
                _failed++;
                Console.WriteLine("[FAIL] " + message);
            }
        }
    }
}
