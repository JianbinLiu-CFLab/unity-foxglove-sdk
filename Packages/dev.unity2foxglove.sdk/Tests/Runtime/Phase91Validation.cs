// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 91 validation for minimal ROS 2 CDR payload writing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Transport;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase91Validation
    {
        private const ulong SampleTimeNs = 1_700_000_123_456_789_012UL;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 91: Minimal ROS2 CDR Writer And Smoke Payloads ===");
            _passed = 0;

            VerifyPlannedSourceFilesExist();
            VerifyWriterPrimitives();
            VerifyPayloadValidation();
            VerifyMessageBuilders();
            VerifyPointCloudSharedPacking();
            VerifyWebSocketAndMcap();
            VerifyBoundary();

            Console.WriteLine($"Phase 91: {_passed} checks passed.");
        }

        public static void GenerateRos2CdrMcap(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            using var stream = File.Create(outputPath);
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession("phase91-mcap", new Phase91FakeTransport(), schemaRegistry: registry);
            session.SetRecorder(recorder);

            var samples = BuildSamples();
            for (var i = 0; i < samples.Count; i++)
            {
                var channelId = (uint)(i + 1);
                session.RegisterRos2MsgSchemaChannel(channelId, samples[i].Topic, samples[i].SchemaName);
                session.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
            }

            session.SetRecorder(null);
            recorder.Close();
        }

        private static void VerifyPlannedSourceFilesExist()
        {
            var files = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrGeometryWriter.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrPayloadValidator.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrFrameTransformBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrCompressedImageBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrCameraCalibrationBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrLaserScanBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrPointCloudBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrCompressedPointCloudBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs",
            };

            foreach (var file in files)
                Check(!string.IsNullOrEmpty(ReadRepoText(file)), "91A-1: source exists " + Path.GetFileName(file));
        }

        private static void VerifyWriterPrimitives()
        {
            var headerOnly = new Ros2CdrWriter().ToArray();
            Check(headerOnly.SequenceEqual(new byte[] { 0, 1, 0, 0 }),
                "91B-1: writer emits little-endian CDR encapsulation header");

            var aligned32 = new Ros2CdrWriter();
            aligned32.WriteUInt8(0x7f);
            aligned32.WriteUInt32(0x11223344);
            var p32 = aligned32.ToArray();
            Check(p32.Length == 12 && p32[4] == 0x7f && p32[5] == 0 && p32[6] == 0 && p32[7] == 0
                  && p32[8] == 0x44 && p32[9] == 0x33 && p32[10] == 0x22 && p32[11] == 0x11,
                "91B-2: uint32 aligns from offset 4 and writes little-endian bytes");

            var aligned64 = new Ros2CdrWriter();
            aligned64.WriteUInt8(0x01);
            aligned64.WriteFloat64(1.5);
            var r64 = new Ros2CdrTestReader(aligned64.ToArray());
            Check(r64.ReadUInt8() == 1 && r64.ReadFloat64() == 1.5 && r64.Offset == 20,
                "91B-3: float64 aligns to 8 bytes relative to CDR payload origin");

            var signed = new Ros2CdrWriter();
            signed.WriteUInt8(0x02);
            signed.WriteInt64(-42);
            signed.WriteUInt64(42);
            var signedReader = new Ros2CdrTestReader(signed.ToArray());
            Check(signedReader.ReadUInt8() == 2 && signedReader.ReadInt64() == -42 && signedReader.ReadUInt64() == 42,
                "91B-4: int64 and uint64 write/read with 8-byte alignment");

            var strings = new Ros2CdrWriter();
            strings.WriteString("hi");
            strings.WriteByteArray(new byte[] { 3, 4, 5 });
            strings.WriteFloat64Sequence(new[] { 0.25, 0.5 });
            var stringReader = new Ros2CdrTestReader(strings.ToArray());
            Check(stringReader.ReadString() == "hi"
                  && stringReader.ReadByteArray().SequenceEqual(new byte[] { 3, 4, 5 })
                  && stringReader.ReadFloat64Sequence().SequenceEqual(new[] { 0.25, 0.5 }),
                "91B-5: string, uint8 sequence, and float64 sequence round-trip");
        }

        private static void VerifyPayloadValidation()
        {
            Ros2CdrPayloadValidator.Validate(new Ros2CdrWriter().ToArray());
            Check(true, "91C-1: valid header payload passes validation");
            Check(Throws<ArgumentException>(() => Ros2CdrPayloadValidator.Validate(null)),
                "91C-2: null CDR payload is rejected");
            Check(Throws<ArgumentException>(() => Ros2CdrPayloadValidator.Validate(Array.Empty<byte>())),
                "91C-3: empty CDR payload is rejected");
            Check(Throws<ArgumentException>(() => Ros2CdrPayloadValidator.Validate(new byte[] { 0, 1, 0 })),
                "91C-4: short CDR payload is rejected");
            Check(Throws<ArgumentException>(() => Ros2CdrPayloadValidator.Validate(new byte[] { 0, 0, 0, 0 })),
                "91C-5: wrong CDR encapsulation header is rejected");

            using var session = new FoxgloveSession("phase91-validation", new Phase91FakeTransport(), schemaRegistry: new DefaultSchemaRegistry());
            Check(Throws<ArgumentException>(() => session.PublishRos2Cdr(99, new byte[] { 0, 0, 0, 0 }, 1)),
                "91C-6: session PublishRos2Cdr validates before publishing");

            using var runtime = new FoxgloveRuntime(new Phase91FakeTransport(), new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase91-runtime", "127.0.0.1", 9191);
            Check(Throws<ArgumentException>(() => runtime.PublishRos2Cdr(99, null, 1)),
                "91C-7: runtime PublishRos2Cdr rejects null without converting to empty payload");
        }

        private static void VerifyMessageBuilders()
        {
            VerifyFrameTransformBuilder();
            VerifyCompressedImageBuilder();
            VerifyCameraCalibrationBuilder();
            VerifyLaserScanBuilder();
            VerifyPointCloudBuilder();
            VerifyCompressedPointCloudBuilder();
            VerifySceneUpdateBuilder();
        }

        private static void VerifyFrameTransformBuilder()
        {
            var payload = Ros2CdrFrameTransformBuilder.Serialize(new FrameTransformMessage
            {
                Timestamp = new FoxgloveTime { Sec = 10, Nsec = 20 },
                ParentFrameId = "world",
                ChildFrameId = "cube",
                Translation = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
            });

            var reader = new Ros2CdrTestReader(payload);
            var time = ReadTime(reader);
            Check(time.sec == 10 && time.nsec == 20
                  && reader.ReadString() == "world"
                  && reader.ReadString() == "cube"
                  && ReadVector3(reader).SequenceEqual(new[] { 1.0, 2.0, 3.0 })
                  && ReadQuaternion(reader).SequenceEqual(new[] { 0.0, 0.0, 0.0, 1.0 }),
                "91D-1: FrameTransform CDR field order matches ROS2 msg");
        }

        private static void VerifyCompressedImageBuilder()
        {
            var payload = Ros2CdrCompressedImageBuilder.Serialize(1_500_000_002UL, "camera", new byte[] { 1, 2, 3 }, "jpeg");
            var reader = new Ros2CdrTestReader(payload);
            var time = ReadTime(reader);
            Check(time.sec == 1 && time.nsec == 500_000_002
                  && reader.ReadString() == "camera"
                  && reader.ReadByteArray().SequenceEqual(new byte[] { 1, 2, 3 })
                  && reader.ReadString() == "jpeg",
                "91D-2: CompressedImage CDR field order matches ROS2 msg");
        }

        private static void VerifyCameraCalibrationBuilder()
        {
            var k = Enumerable.Range(1, 9).Select(i => (double)i).ToArray();
            var r = Enumerable.Range(10, 9).Select(i => (double)i).ToArray();
            var p = Enumerable.Range(20, 12).Select(i => (double)i).ToArray();
            var payload = Ros2CdrCameraCalibrationBuilder.Serialize(2_000_000_003UL, "camera", 640, 480, "plumb_bob", new[] { 0.1, 0.2 }, k, r, p);
            var reader = new Ros2CdrTestReader(payload);
            var time = ReadTime(reader);
            Check(time.sec == 2 && time.nsec == 3
                  && reader.ReadString() == "camera"
                  && reader.ReadUInt32() == 640
                  && reader.ReadUInt32() == 480
                  && reader.ReadString() == "plumb_bob"
                  && reader.ReadFloat64Sequence().SequenceEqual(new[] { 0.1, 0.2 })
                  && reader.ReadFloat64Fixed(9).SequenceEqual(k)
                  && reader.ReadFloat64Fixed(9).SequenceEqual(r)
                  && reader.ReadFloat64Fixed(12).SequenceEqual(p),
                "91D-3: CameraCalibration CDR field order and fixed arrays match ROS2 msg");
            Check(Throws<ArgumentException>(() => Ros2CdrCameraCalibrationBuilder.Serialize(0, "", 0, 0, "", null, new[] { 1.0 }, r, p)),
                "91D-4: CameraCalibration rejects wrong K length");
        }

        private static void VerifyLaserScanBuilder()
        {
            var payload = Ros2CdrLaserScanBuilder.Serialize(3_000_000_004UL, "laser", -1.0, 1.0, new[] { 2.0, 3.0 }, new[] { 4.0, 5.0 });
            var reader = new Ros2CdrTestReader(payload);
            var time = ReadTime(reader);
            Check(time.sec == 3 && time.nsec == 4
                  && reader.ReadString() == "laser"
                  && ReadPose(reader)
                  && reader.ReadFloat64() == -1.0
                  && reader.ReadFloat64() == 1.0
                  && reader.ReadFloat64Sequence().SequenceEqual(new[] { 2.0, 3.0 })
                  && reader.ReadFloat64Sequence().SequenceEqual(new[] { 4.0, 5.0 }),
                "91D-5: LaserScan CDR field order matches ROS2 msg");
            Check(Throws<ArgumentException>(() => Ros2CdrLaserScanBuilder.Serialize(0, "", 0, 0, new[] { 1.0 }, new[] { 1.0, 2.0 })),
                "91D-6: LaserScan rejects mismatched intensities");
        }

        private static void VerifyPointCloudBuilder()
        {
            var frame = BuildPointCloudFrame();
            var payload = Ros2CdrPointCloudBuilder.Serialize(frame);
            var reader = new Ros2CdrTestReader(payload);
            var time = ReadTime(reader);
            Check(time.sec == (int)(frame.UnixNs / 1_000_000_000UL)
                  && time.nsec == (uint)(frame.UnixNs % 1_000_000_000UL)
                  && reader.ReadString() == "lidar"
                  && ReadPose(reader)
                  && reader.ReadUInt32() == 18,
                "91D-7: PointCloud CDR prefix matches timestamp/frame/identity pose/stride");

            Check(reader.ReadUInt32() == 5
                  && ReadPackedField(reader, "x", 0, PointCloudPackedNumericType.Float32)
                  && ReadPackedField(reader, "y", 4, PointCloudPackedNumericType.Float32)
                  && ReadPackedField(reader, "z", 8, PointCloudPackedNumericType.Float32)
                  && ReadPackedField(reader, "intensity", 12, PointCloudPackedNumericType.Float32)
                  && ReadPackedField(reader, "ring", 16, PointCloudPackedNumericType.Uint16)
                  && reader.ReadByteArray().SequenceEqual(PointCloudPackedDataBuilder.Build(frame).Data),
                "91D-8: PointCloud fields and packed bytes match shared packing");
        }

        private static void VerifyCompressedPointCloudBuilder()
        {
            var frame = BuildPointCloudFrame();
            var payload = Ros2CdrCompressedPointCloudBuilder.Serialize(frame, new byte[] { 9, 8, 7 });
            var reader = new Ros2CdrTestReader(payload);
            ReadTime(reader);
            Check(reader.ReadString() == "lidar"
                  && ReadPose(reader)
                  && reader.ReadByteArray().SequenceEqual(new byte[] { 9, 8, 7 })
                  && reader.ReadString() == "draco",
                "91D-9: CompressedPointCloud CDR field order matches ROS2 msg");
            Check(Throws<ArgumentException>(() => Ros2CdrCompressedPointCloudBuilder.Serialize(frame, Array.Empty<byte>())),
                "91D-10: CompressedPointCloud rejects empty compressed data");
        }

        private static void VerifySceneUpdateBuilder()
        {
            var update = new SceneUpdateMessage();
            update.Deletions.Add(new SceneEntityDeletion
            {
                Timestamp = new FoxgloveTime { Sec = 4, Nsec = 5 },
                Type = SceneEntityDeletionType.MatchingId,
                Id = "old"
            });
            update.Entities.Add(new SceneEntity
            {
                Timestamp = new FoxgloveTime { Sec = 6, Nsec = 7 },
                FrameId = "world",
                Id = "cube",
                Lifetime = new FoxgloveDuration { Sec = 2, Nsec = 3 },
                FrameLocked = true,
                Metadata = new List<FoxgloveKeyValuePair> { new FoxgloveKeyValuePair { Key = "kind", Value = "test" } },
                Cubes = new List<CubePrimitive>
                {
                    new CubePrimitive
                    {
                        Pose = new FoxglovePose
                        {
                            Position = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                            Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                        },
                        Size = new FoxgloveVector3 { X = 0.5, Y = 0.25, Z = 0.125 },
                        Color = new FoxgloveColor { R = 1, G = 0.5, B = 0.25, A = 1 }
                    }
                }
            });

            var reader = new Ros2CdrTestReader(Ros2CdrSceneUpdateBuilder.Serialize(update));
            Check(reader.ReadUInt32() == 1
                  && ReadTime(reader).sec == 4
                  && reader.ReadUInt8() == 0
                  && reader.ReadString() == "old"
                  && reader.ReadUInt32() == 1
                  && ReadTime(reader).sec == 6
                  && reader.ReadString() == "world"
                  && reader.ReadString() == "cube"
                  && ReadDuration(reader).sec == 2
                  && reader.ReadBool()
                  && reader.ReadUInt32() == 1
                  && reader.ReadString() == "kind"
                  && reader.ReadString() == "test"
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 1
                  && ReadPose(reader)
                  && ReadVector3(reader).SequenceEqual(new[] { 0.5, 0.25, 0.125 })
                  && ReadColor(reader).SequenceEqual(new[] { 1.0, 0.5, 0.25, 1.0 })
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 0
                  && reader.ReadUInt32() == 0,
                "91D-11: SceneUpdate deletion/entity/cube CDR order matches ROS2 msg");

            update.Entities[0].Spheres.Add(new SpherePrimitive());
            Check(Throws<NotSupportedException>(() => Ros2CdrSceneUpdateBuilder.Serialize(update)),
                "91D-12: SceneUpdate builder rejects unsupported primitive arrays when non-empty");
        }

        private static void VerifyPointCloudSharedPacking()
        {
            var frame = BuildPointCloudFrame();
            var packed = PointCloudPackedDataBuilder.Build(frame);
            var legacy = PointCloudMessageBuilder.Build(frame);
            var reader = new Ros2CdrTestReader(Ros2CdrPointCloudBuilder.Serialize(frame));
            ReadTime(reader);
            reader.ReadString();
            ReadPose(reader);
            reader.ReadUInt32();
            var fieldCount = reader.ReadUInt32();
            for (var i = 0; i < fieldCount; i++)
            {
                reader.ReadString();
                reader.ReadUInt32();
                reader.ReadUInt8();
            }

            Check(legacy.Data.SequenceEqual(packed.Data)
                  && reader.ReadByteArray().SequenceEqual(packed.Data),
                "91E-1: JSON/protobuf and ROS2 CDR PointCloud share identical packed data");
        }

        private static void VerifyWebSocketAndMcap()
        {
            var samples = BuildSamples();
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase91FakeTransport();
            using var session = new FoxgloveSession("phase91-session", transport, schemaRegistry: registry);
            session.EnableCdr();
            transport.SimulateConnect(1);

            for (var i = 0; i < samples.Count; i++)
            {
                var channelId = (uint)(i + 1);
                session.RegisterRos2MsgSchemaChannel(channelId, samples[i].Topic, samples[i].SchemaName);
                var channel = FirstAdvertisedChannel(transport.LastBroadcastText);
                Check(channel?["encoding"]?.ToString() == "cdr"
                      && channel?["schemaEncoding"]?.ToString() == "ros2msg"
                      && channel?["schemaName"]?.ToString() == samples[i].SchemaName,
                    "91F-1: WebSocket advertise uses cdr/ros2msg for " + samples[i].SchemaName);
                transport.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":" + (100 + i) + ",\"channelId\":" + channelId + "}]}");
                session.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
            }

            Check(transport.SentBinaries.Count == samples.Count,
                "91F-2: WebSocket publishes one MessageData frame per subscribed ROS2 CDR sample");
            for (var i = 0; i < samples.Count; i++)
            {
                Check(BinaryEncoding.TryDecodeServerMessageData(transport.SentBinaries[i], out var subId, out var logTime, out var payload)
                      && subId == 100 + (uint)i
                      && logTime == SampleTimeNs + (ulong)i
                      && payload.SequenceEqual(samples[i].Payload),
                    "91F-3: WebSocket MessageData payload survives unchanged for " + samples[i].SchemaName);
            }

            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(stream))
            {
                using var mcapSession = new FoxgloveSession("phase91-mcap", new Phase91FakeTransport(), schemaRegistry: registry);
                mcapSession.SetRecorder(recorder);
                for (var i = 0; i < samples.Count; i++)
                {
                    var channelId = (uint)(i + 1);
                    mcapSession.RegisterRos2MsgSchemaChannel(channelId, samples[i].Topic, samples[i].SchemaName);
                    mcapSession.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
                }
                mcapSession.SetRecorder(null);
                recorder.Close();
            }

            stream.Position = 0;
            using var indexed = new McapIndexedReader(stream, leaveOpen: true);
            Check(indexed.Schemas.Count == samples.Count
                  && indexed.Schemas.All(schema => schema.Encoding == "ros2msg")
                  && indexed.Channels.Count == samples.Count
                  && indexed.Channels.All(channel => channel.MessageEncoding == "cdr"),
                "91F-4: MCAP stores ROS2 msg schemas and CDR channels");
            var messages = indexed.ReadMessages();
            Check(messages.Count == samples.Count
                  && messages.All(message => HasCdrHeader(message.Data)),
                "91F-5: MCAP stores CDR payload bytes with encapsulation headers");
        }

        private static void VerifyBoundary()
        {
            var runtimeRoot = Path.Combine(Phase16Validation.FindRepoRoot(), "Packages", "dev.unity2foxglove.sdk", "Runtime");
            var componentsRoot = Path.Combine(runtimeRoot, "Components");
            var publishersRoot = Path.Combine(runtimeRoot, "Schemas", "Proto", "Publishers");
            Check(Directory.Exists(componentsRoot), "91G-0: Runtime Components source root exists");
            Check(Directory.Exists(publishersRoot), "91G-0b: Runtime Proto publisher source root exists");

            var componentText = string.Join("\n", Directory.EnumerateFiles(componentsRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var publisherText = string.Join("\n", Directory.EnumerateFiles(publishersRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var managerPublishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var sessionText = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");

            Check(!componentText.Contains("PublisherEffectiveEncoding.Cdr") && !componentText.Contains("GlobalEncoding.Cdr"),
                "91G-1: Phase91 does not add publisher or global CDR output modes");
            Check(!publisherText.Contains("PublishRos2Cdr"),
                "91G-2: product publishers do not call the low-level ROS2 CDR helper directly");
            Check(managerPublishing.Contains("GetOrRegisterRos2MsgSchemaChannel")
                  && !managerPublishing.Contains("GetOrRegisterSchemaChannel(topic, schemaName, CdrEncoding)"),
                "91G-3: manager uses a dedicated ros2msg CDR channel helper");
            Check(sessionText.Contains("Ros2CdrPayloadValidator.Validate(payload);")
                  && !sessionText.Contains("PublishRos2Cdr(uint channelId, byte[] payload, ulong logTimeNs)\n        {\n            payload ??="),
                "91G-4: PublishRos2Cdr validates payload without null-coalescing to empty");
        }

        private static List<Phase91Sample> BuildSamples()
        {
            var pointFrame = BuildPointCloudFrame();
            var k = new[] { 100.0, 0, 320, 0, 100, 240, 0, 0, 1 };
            var r = new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 };
            var p = new[] { 100.0, 0, 320, 0, 0, 100, 240, 0, 0, 0, 1, 0 };
            var scene = new SceneUpdateMessage();
            scene.Entities.Add(new SceneEntity
            {
                Timestamp = new FoxgloveTime { Sec = 8, Nsec = 9 },
                FrameId = "world",
                Id = "cube",
                Cubes = new List<CubePrimitive> { new CubePrimitive { Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 } } }
            });

            return new List<Phase91Sample>
            {
                new Phase91Sample("/phase91/frame_transform", Ros2CdrFrameTransformBuilder.SchemaName,
                    Ros2CdrFrameTransformBuilder.Serialize(new FrameTransformMessage
                    {
                        Timestamp = new FoxgloveTime { Sec = 1, Nsec = 2 },
                        ParentFrameId = "world",
                        ChildFrameId = "child",
                        Rotation = new FoxgloveQuaternion { W = 1 }
                    })),
                new Phase91Sample("/phase91/compressed_image", Ros2CdrCompressedImageBuilder.SchemaName,
                    Ros2CdrCompressedImageBuilder.Serialize(SampleTimeNs, "camera", new byte[] { 0xff, 0xd8, 0xff }, "jpeg")),
                new Phase91Sample("/phase91/camera_calibration", Ros2CdrCameraCalibrationBuilder.SchemaName,
                    Ros2CdrCameraCalibrationBuilder.Serialize(SampleTimeNs, "camera", 640, 480, "plumb_bob", Array.Empty<double>(), k, r, p)),
                new Phase91Sample("/phase91/laser_scan", Ros2CdrLaserScanBuilder.SchemaName,
                    Ros2CdrLaserScanBuilder.Serialize(SampleTimeNs, "laser", -1, 1, new[] { 1.0, 2.0 }, Array.Empty<double>())),
                new Phase91Sample("/phase91/point_cloud", Ros2CdrPointCloudBuilder.SchemaName,
                    Ros2CdrPointCloudBuilder.Serialize(pointFrame)),
                new Phase91Sample("/phase91/compressed_point_cloud", Ros2CdrCompressedPointCloudBuilder.SchemaName,
                    Ros2CdrCompressedPointCloudBuilder.Serialize(pointFrame, new byte[] { 1, 2, 3, 4 })),
                new Phase91Sample("/phase91/scene", Ros2CdrSceneUpdateBuilder.SchemaName,
                    Ros2CdrSceneUpdateBuilder.Serialize(scene)),
            };
        }

        private static PointCloudFrame BuildPointCloudFrame()
        {
            var frame = new PointCloudFrame { UnixNs = 1_700_000_123_456_789_012UL, FrameId = "lidar" };
            frame.Points.Add(new PointCloudPoint(1, 2, 3) { Intensity = 0.5f, Ring = 2 });
            frame.Points.Add(new PointCloudPoint(4, 5, 6) { Intensity = 0.75f, Ring = 3 });
            return frame;
        }

        private static (int sec, uint nsec) ReadTime(Ros2CdrTestReader reader)
        {
            return (reader.ReadInt32(), reader.ReadUInt32());
        }

        private static (int sec, uint nsec) ReadDuration(Ros2CdrTestReader reader)
        {
            return (reader.ReadInt32(), reader.ReadUInt32());
        }

        private static double[] ReadVector3(Ros2CdrTestReader reader)
        {
            return new[] { reader.ReadFloat64(), reader.ReadFloat64(), reader.ReadFloat64() };
        }

        private static double[] ReadQuaternion(Ros2CdrTestReader reader)
        {
            return new[] { reader.ReadFloat64(), reader.ReadFloat64(), reader.ReadFloat64(), reader.ReadFloat64() };
        }

        private static bool ReadPose(Ros2CdrTestReader reader)
        {
            ReadVector3(reader);
            ReadQuaternion(reader);
            return true;
        }

        private static double[] ReadColor(Ros2CdrTestReader reader)
        {
            return new[] { reader.ReadFloat64(), reader.ReadFloat64(), reader.ReadFloat64(), reader.ReadFloat64() };
        }

        private static bool ReadPackedField(Ros2CdrTestReader reader, string name, uint offset, PointCloudPackedNumericType type)
        {
            return reader.ReadString() == name
                   && reader.ReadUInt32() == offset
                   && reader.ReadUInt8() == (byte)type;
        }

        private static bool HasCdrHeader(byte[] payload)
        {
            return payload != null && payload.Length >= 4 && payload[0] == 0 && payload[1] == 1 && payload[2] == 0 && payload[3] == 0;
        }

        private static JToken FirstAdvertisedChannel(string json)
        {
            var adv = JObject.Parse(json);
            return (adv["channels"] as JArray)?[0];
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private sealed class Phase91Sample
        {
            public Phase91Sample(string topic, string schemaName, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }

        private sealed class Phase91FakeTransport : IFoxgloveTransport
        {
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public bool IsRunning { get; private set; }
            public string LastSentText;
            public string LastBroadcastText;
            public readonly List<byte[]> SentBinaries = new List<byte[]>();

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) => LastBroadcastText = json;
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => LastSentText = json;
            public void SendBinary(uint clientId, byte[] data) => SentBinaries.Add(data);
            public void Dispose() { }
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
