// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Freeze ordinary ROS2 Bridge CDR and ROS MCAP behavior before package extraction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Phase186
{
    [Trait("Phase", "186-A")]
    [Trait("Domain", "PreMoveAuthority")]
    public sealed class PreMoveBridgeAndMcapAuthorityTests
    {
        private const ulong SampleTimeNs = 1_700_092_000_000_000_000UL;

        [Fact]
        public void OrdinaryBridgeCdrVectorsMatchPreMoveAuthority()
        {
            var fixture = LoadFixture();
            Assert.Equal(1, fixture.Value<int>("fixtureVersion"));
            Assert.Equal(SampleTimeNs, fixture.Value<ulong>("sampleTimeNs"));

            var expectedQos = Assert.IsType<JObject>(fixture["resolvedDeliveryPolicy"]);
            Assert.Equal(nameof(FoxRunQosProfile.Default), expectedQos.Value<string>("profile"));
            Assert.Equal(nameof(FoxRunQosReliability.Reliable), expectedQos.Value<string>("reliability"));
            Assert.Equal(nameof(FoxRunQosDurability.Volatile), expectedQos.Value<string>("durability"));
            Assert.Equal(nameof(FoxRunQosHistory.KeepLast), expectedQos.Value<string>("history"));
            Assert.Equal(10, expectedQos.Value<int>("depth"));
            Assert.Equal(FoxRunResolvedQos.Default, ReadQos(expectedQos));

            Assert.Equal(
                new[] { "demand_gate", "serialize_cdr", "validate", "create_frame", "enqueue" },
                fixture["preparationOrdering"]?.Values<string>().ToArray());

            var actual = BuildOrdinarySamples();
            var vectors = Assert.IsType<JArray>(fixture["ordinaryPublishers"])
                .Values<JObject>()
                .ToArray();
            Assert.Equal(10, vectors.Length);
            Assert.Equal(vectors.Length, actual.Count);

            var publicSchemaConstants = typeof(Ros2PublisherSchemaNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                publicSchemaConstants,
                actual.Select(sample => sample.SchemaName)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());

            for (var i = 0; i < vectors.Length; i++)
            {
                var vector = vectors[i];
                var sample = actual[i];
                Assert.Equal(vector.Value<string>("id"), sample.Id);
                Assert.Equal(vector.Value<string>("publisher"), sample.Publisher);
                Assert.Equal(vector.Value<string>("topic"), sample.Topic);
                Assert.Equal(vector.Value<string>("schemaName"), sample.SchemaName);
                Assert.Equal("ros2msg", vector.Value<string>("schemaEncoding"));
                Assert.Equal(Ros2BridgeFrame.CdrEncoding, vector.Value<string>("messageEncoding"));
                Assert.Equal(vector.Value<int>("payloadLength"), sample.Payload.Length);
                Assert.Equal(vector.Value<string>("payloadBase64"), Convert.ToBase64String(sample.Payload));
                Assert.Equal(vector.Value<string>("payloadSha256"), Sha256(sample.Payload));

                var frame = Ros2BridgeFrame.CreateValidated(
                    sample.Topic,
                    sample.SchemaName,
                    Ros2BridgeFrame.CdrEncoding,
                    SampleTimeNs,
                    (ulong)i,
                    sample.Payload,
                    FoxRunResolvedQos.Default);
                Assert.Equal(FoxRunResolvedQos.Default, frame.Qos);
                Assert.Equal(sample.Payload, frame.PayloadMemory.ToArray());

                var invalid = Assert.IsType<JObject>(vector["invalidInput"]);
                Assert.Equal("empty_payload", invalid.Value<string>("kind"));
                var exception = Assert.Throws<ArgumentException>(() =>
                    Ros2BridgeFrame.CreateValidated(
                        sample.Topic,
                        sample.SchemaName,
                        Ros2BridgeFrame.CdrEncoding,
                        SampleTimeNs,
                        (ulong)i,
                        Array.Empty<byte>(),
                        FoxRunResolvedQos.Default));
                Assert.Equal(invalid.Value<string>("exceptionType"), exception.GetType().Name);
                Assert.Equal(invalid.Value<string>("parameter"), exception.ParamName);
            }
        }

        [Fact]
        public void RosMcapRouteVectorsMatchPreMoveAuthority()
        {
            var fixture = Assert.IsType<JObject>(LoadFixture()["mcap"]);
            var transform = BuildOrdinarySamples().Single(sample => sample.Id == "transform");
            var typedVector = Assert.IsType<JObject>(fixture["typedFactory"]);
            var typedSchema = Schema(
                id: 1,
                typedVector.Value<string>("schemaName"),
                typedVector.Value<string>("schemaEncoding"));
            var typedChannel = Channel(
                id: 1,
                schemaId: 1,
                typedVector.Value<string>("topic"),
                typedVector.Value<string>("messageEncoding"));

            var typedFactory = new McapRos2CdrTypedDecoderFactory();
            Assert.NotNull(typedFactory.TryCreate(typedSchema, typedChannel));
            Assert.True(typedVector.Value<bool>("available"));

            var typedRegistry = Registry(
                BridgeDecodeOptions(),
                typedSchema,
                typedChannel);
            var typed = typedRegistry.Decode(Message(typedChannel, transform.Payload));
            Assert.Equal(
                typedVector.Value<string>("decodedKind"),
                typed.Payload.Kind.ToString());
            Assert.Equal(
                typedVector.Value<string>("decodedType"),
                typed.Payload.Value.GetType().FullName);
            Assert.Equal(
                typedVector.Value<string>("decoderId"),
                typed.Payload.DecoderId);
            Assert.Equal(
                typedVector.Value<string>("decodedTextSha256"),
                Sha256(System.Text.Encoding.UTF8.GetBytes(typed.Payload.Text)));
            Assert.Empty(typed.Problems);

            var diagnosticVector = Assert.IsType<JObject>(fixture["diagnosticFallback"]);
            var diagnosticSample = BuildOrdinarySamples()
                .Single(sample => sample.Id == "sensor_compressed_image");
            var diagnosticSchema = Schema(
                id: 2,
                diagnosticVector.Value<string>("schemaName"),
                diagnosticVector.Value<string>("schemaEncoding"));
            var diagnosticChannel = Channel(
                id: 2,
                schemaId: 2,
                diagnosticVector.Value<string>("topic"),
                diagnosticVector.Value<string>("messageEncoding"));
            Assert.Null(typedFactory.TryCreate(diagnosticSchema, diagnosticChannel));
            var diagnostic = Registry(
                    BridgeDecodeOptions(),
                    diagnosticSchema,
                    diagnosticChannel)
                .Decode(Message(diagnosticChannel, diagnosticSample.Payload));
            Assert.Equal(
                diagnosticVector.Value<string>("decodedKind"),
                diagnostic.Payload.Kind.ToString());
            var diagnosticPayload = Assert.IsType<Ros2CdrDiagnosticPayload>(
                diagnostic.Payload.Value);
            Assert.Equal(
                diagnosticVector.Value<string>("decoderId"),
                diagnostic.Payload.DecoderId);
            Assert.Equal(diagnosticVector.Value<bool>("schemaKnown"), diagnosticPayload.SchemaKnown);
            Assert.Equal(
                diagnosticVector.Value<int>("encapsulationKind"),
                diagnosticPayload.EncapsulationKind);
            Assert.Equal(
                diagnosticVector.Value<int>("payloadByteLength"),
                diagnosticPayload.PayloadByteLength);
            Assert.Empty(diagnostic.Problems);

            var selectionVector = Assert.IsType<JObject>(fixture["selectionOrder"]);
            var calls = new List<string>();
            var overrideOptions = new McapDecodeOptions
            {
                DecoderFactories = new List<IMcapMessageDecoderFactory>
                {
                    new RecordingFactory("explicit_0", calls, decoder: null),
                    new RecordingFactory(
                        "explicit_1",
                        calls,
                        new MarkerDecoder(selectionVector.Value<string>("marker"))),
                    new McapRos2CdrTypedDecoderFactory(),
                    new McapRos2CdrDiagnosticDecoderFactory()
                },
                UseBuiltInDecoders = true
            };
            var overridden = Registry(overrideOptions, typedSchema, typedChannel)
                .Decode(Message(typedChannel, transform.Payload));
            Assert.Equal(
                selectionVector["calls"]?.Values<string>().ToArray(),
                calls.ToArray());
            Assert.Equal(
                selectionVector.Value<string>("decodedKind"),
                overridden.Payload.Kind.ToString());
            Assert.Equal(selectionVector.Value<string>("marker"), overridden.Payload.Text);

            var absentVector = Assert.IsType<JObject>(fixture["packageAbsent"]);
            var absentOptions = new McapDecodeOptions
            {
                UseBuiltInDecoders = false
            };
            var absent = Registry(absentOptions, typedSchema, typedChannel)
                .Decode(Message(typedChannel, transform.Payload));
            Assert.Equal(absentVector.Value<string>("decodedKind"), absent.Payload.Kind.ToString());
            Assert.Single(absent.Problems);
            Assert.Equal(absentVector.Value<string>("problemCode"), absent.Problems[0].Code);

            var failureVector = Assert.IsType<JObject>(fixture["typedFailure"]);
            var malformed = new byte[] { 0x00, 0x01, 0x00, 0x00 };
            var failure = typedRegistry.Decode(new McapDataLoaderMessage
            {
                ChannelId = typedChannel.Id,
                SchemaId = typedSchema.Id,
                Topic = typedChannel.Topic,
                MessageEncoding = typedChannel.MessageEncoding,
                Data = malformed
            });
            Assert.Equal(failureVector.Value<string>("decodedKind"), failure.Payload.Kind.ToString());
            Assert.Equal(failureVector.Value<string>("decoderId"), failure.Payload.DecoderId);
            Assert.Single(failure.Problems);
            Assert.Equal(failureVector.Value<string>("problemCode"), failure.Problems[0].Code);
            Assert.Equal(
                failureVector.Value<string>("exceptionType"),
                failure.Problems[0].ExceptionType);
            Assert.Equal(malformed, failure.Payload.RawData);
        }

        [Fact]
        public void AuthorityWouldFailWhenBridgeAndTypedDecoderAreDeliberatelyExcluded()
        {
            var fixture = LoadFixture();
            var expectedPublisherCount = Assert.IsType<JArray>(fixture["ordinaryPublishers"]).Count;
            Assert.NotEqual(expectedPublisherCount, BuildOrdinarySamples(includeBridgeImplementation: false).Count);

            var typed = Assert.IsType<JObject>(Assert.IsType<JObject>(fixture["mcap"])["typedFactory"]);
            var schema = Schema(1, typed.Value<string>("schemaName"), typed.Value<string>("schemaEncoding"));
            var channel = Channel(1, 1, typed.Value<string>("topic"), typed.Value<string>("messageEncoding"));
            var excluded = Registry(
                    new McapDecodeOptions { UseBuiltInDecoders = false },
                    schema,
                    channel)
                .Decode(Message(channel, new byte[] { 0x00, 0x01, 0x00, 0x00 }));
            Assert.NotEqual(typed.Value<string>("decodedKind"), excluded.Payload.Kind.ToString());
            Assert.Equal(McapDecodedPayloadKind.Unsupported, excluded.Payload.Kind);
        }

        private static IReadOnlyList<OrdinarySample> BuildOrdinarySamples(
            bool includeBridgeImplementation = true)
        {
            if (!includeBridgeImplementation)
                return Array.Empty<OrdinarySample>();

            var pointFrame = new PointCloudFrame
            {
                UnixNs = SampleTimeNs,
                FrameId = "unity_world"
            };
            pointFrame.Points.Add(new PointCloudPoint(1, 2, 3) { Intensity = 4 });
            pointFrame.Points.Add(new PointCloudPoint(5, 6, 7) { Intensity = 8 });

            var k = new[] { 100.0, 0, 320, 0, 100, 240, 0, 0, 1 };
            var r = new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 };
            var p = new[] { 100.0, 0, 320, 0, 0, 100, 240, 0, 0, 0, 1, 0 };
            var scene = new SceneUpdateMessage
            {
                Entities = new List<SceneEntity>
                {
                    new SceneEntity
                    {
                        Id = "cube",
                        FrameId = "unity_world",
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(SampleTimeNs),
                        Lifetime = new FoxgloveDuration(),
                        Cubes = new List<CubePrimitive>
                        {
                            new CubePrimitive
                            {
                                Pose = new FoxglovePose
                                {
                                    Position = new FoxgloveVector3(),
                                    Orientation = new FoxgloveQuaternion { W = 1 }
                                },
                                Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                            }
                        }
                    }
                }
            };

            return new[]
            {
                new OrdinarySample(
                    "transform",
                    "FoxgloveTransformPublisher",
                    "/tf",
                    Ros2PublisherSchemaNames.FrameTransform,
                    Ros2CdrFrameTransformBuilder.Serialize(new FrameTransformMessage
                    {
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(SampleTimeNs),
                        ParentFrameId = "unity_world",
                        ChildFrameId = "cube",
                        Translation = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                        Rotation = new FoxgloveQuaternion { W = 1 }
                    })),
                new OrdinarySample(
                    "scene",
                    "FoxgloveSceneCubePublisher",
                    "/scene",
                    Ros2PublisherSchemaNames.SceneUpdate,
                    Ros2CdrSceneUpdateBuilder.Serialize(scene)),
                new OrdinarySample(
                    "compressed_image",
                    "FoxgloveCameraPublisher",
                    "/unity/camera",
                    Ros2PublisherSchemaNames.CompressedImage,
                    Ros2CdrCompressedImageBuilder.Serialize(
                        SampleTimeNs,
                        "camera",
                        new byte[] { 0xff, 0xd8, 0xff },
                        "jpeg")),
                new OrdinarySample(
                    "camera_calibration",
                    "FoxgloveCameraCalibrationPublisher",
                    "/unity/camera/calibration",
                    Ros2PublisherSchemaNames.CameraCalibration,
                    Ros2CdrCameraCalibrationBuilder.Serialize(
                        SampleTimeNs,
                        "camera",
                        640,
                        480,
                        "plumb_bob",
                        Array.Empty<double>(),
                        k,
                        r,
                        p)),
                new OrdinarySample(
                    "sensor_compressed_image",
                    "FoxgloveCameraPublisher",
                    "/unity/sensor/camera/image/compressed",
                    Ros2PublisherSchemaNames.SensorCompressedImage,
                    Ros2CdrSensorCompressedImageBuilder.Serialize(
                        SampleTimeNs,
                        "camera",
                        new byte[] { 0xff, 0xd8, 0xff },
                        "jpeg")),
                new OrdinarySample(
                    "sensor_camera_info",
                    "FoxgloveCameraInfoPublisher",
                    "/unity/sensor/camera/camera_info",
                    Ros2PublisherSchemaNames.SensorCameraInfo,
                    Ros2CdrSensorCameraInfoBuilder.Serialize(
                        SampleTimeNs,
                        "camera",
                        640,
                        480,
                        "plumb_bob",
                        Array.Empty<double>(),
                        k,
                        r,
                        p)),
                new OrdinarySample(
                    "laser_scan",
                    "FoxgloveLaserScanPublisher",
                    "/unity/laser_scan",
                    Ros2PublisherSchemaNames.LaserScan,
                    Ros2CdrLaserScanBuilder.Serialize(
                        SampleTimeNs,
                        "laser",
                        -1,
                        1,
                        new[] { 1.0, 2.0 },
                        Array.Empty<double>())),
                new OrdinarySample(
                    "point_cloud",
                    "FoxglovePointCloudPublisher",
                    "/unity/point_cloud",
                    Ros2PublisherSchemaNames.PointCloud,
                    Ros2CdrPointCloudBuilder.Serialize(pointFrame)),
                new OrdinarySample(
                    "sensor_point_cloud2",
                    "FoxglovePointCloudPublisher",
                    "/unity/sensor/point_cloud2",
                    Ros2PublisherSchemaNames.SensorPointCloud2,
                    Ros2CdrSensorPointCloud2Builder.Serialize(pointFrame)),
                new OrdinarySample(
                    "compressed_point_cloud",
                    "FoxglovePointCloudPublisher",
                    "/unity/point_cloud_draco",
                    Ros2PublisherSchemaNames.CompressedPointCloud,
                    Ros2CdrCompressedPointCloudBuilder.Serialize(
                        pointFrame,
                        new byte[] { 0x44, 0x52, 0x41, 0x43, 0x4f }))
            };
        }

        private static JObject LoadFixture()
        {
            var path = FindRepositoryFile(
                "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Fixtures/" +
                "pre_move_bridge_and_mcap_vectors.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    var candidate = Path.Combine(
                        current.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                        return candidate;
                    current = current.Parent;
                }
            }

            throw new FileNotFoundException(
                "Could not locate Phase186 pre-move authority fixture.",
                relativePath);
        }

        private static FoxRunResolvedQos ReadQos(JObject value)
            => new FoxRunResolvedQos(
                Enum.Parse<FoxRunQosProfile>(value.Value<string>("profile")),
                Enum.Parse<FoxRunQosReliability>(value.Value<string>("reliability")),
                Enum.Parse<FoxRunQosDurability>(value.Value<string>("durability")),
                Enum.Parse<FoxRunQosHistory>(value.Value<string>("history")),
                value.Value<int>("depth"));

        private static McapSchema Schema(ushort id, string name, string encoding)
            => new McapSchema
            {
                Id = id,
                Name = name,
                Encoding = encoding,
                Data = Array.Empty<byte>()
            };

        private static McapChannel Channel(
            ushort id,
            ushort schemaId,
            string topic,
            string messageEncoding)
            => new McapChannel
            {
                Id = id,
                SchemaId = schemaId,
                Topic = topic,
                MessageEncoding = messageEncoding
            };

        private static McapDataLoaderMessage Message(McapChannel channel, byte[] payload)
            => new McapDataLoaderMessage
            {
                ChannelId = channel.Id,
                SchemaId = channel.SchemaId,
                Topic = channel.Topic,
                MessageEncoding = channel.MessageEncoding,
                LogTime = SampleTimeNs,
                PublishTime = SampleTimeNs,
                Data = payload
            };

        private static McapDecodeRegistry Registry(
            McapDecodeOptions options,
            McapSchema schema,
            McapChannel channel)
            => new McapDecodeRegistry(
                options,
                new Dictionary<ushort, McapSchema> { [schema.Id] = schema },
                new Dictionary<ushort, McapChannel> { [channel.Id] = channel });

        private static McapDecodeOptions BridgeDecodeOptions()
            => new McapDecodeOptions
            {
                DecoderFactories = Ros2BridgeMcapCodecs
                    .CreateFactories()
                    .ToList()
            };

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private sealed class RecordingFactory : IMcapMessageDecoderFactory
        {
            private readonly string _id;
            private readonly List<string> _calls;
            private readonly IMcapMessageDecoder _decoder;

            public RecordingFactory(
                string id,
                List<string> calls,
                IMcapMessageDecoder decoder)
            {
                _id = id;
                _calls = calls;
                _decoder = decoder;
            }

            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
            {
                _calls.Add(_id);
                return _decoder;
            }
        }

        private sealed class MarkerDecoder : IMcapMessageDecoder
        {
            private readonly string _marker;

            public MarkerDecoder(string marker)
            {
                _marker = marker;
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
                => new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Json,
                    Text = _marker,
                    RawData = message.Data
                };
        }

        private sealed class OrdinarySample
        {
            public OrdinarySample(
                string id,
                string publisher,
                string topic,
                string schemaName,
                byte[] payload)
            {
                Id = id;
                Publisher = publisher;
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Id { get; }
            public string Publisher { get; }
            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }
    }
}
