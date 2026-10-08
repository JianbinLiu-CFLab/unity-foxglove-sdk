// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 92 validation for productized ROS2 publisher delivery.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase92Validation.
    /// </summary>
    public static partial class Phase92Validation
    {
        private static void VerifyDocsAndBoundaries()
        {
            var bridgeManifest = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/package.json");
            var sensorDocs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/14_Typed_Sensor_Publishers.md");
            var readme = ReadRepoText("README.md");
            Check(bridgeManifest.Contains("CDR codecs")
                  && bridgeManifest.Contains("ROS schema adapters")
                  && sensorDocs.Contains("companion Providers")
                  && sensorDocs.Contains("CompressedPointCloud"),
                "92G-1: Bridge manifest and sensor docs describe Provider-owned ROS2 coverage");
            Check(readme.Contains("ROS2") || readme.Contains("ROS 2"),
                "92G-2: README mentions user-facing ROS2 output");

            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            Check(manager.Contains("_defaultPublisherEncoding = GlobalEncoding.Protobuf"),
                "92G-3: existing scenes still default to Protobuf");
        }

        private static void CheckPublisher(string fileName, string builderToken)
        {
            var source = ReadPublisher(fileName);
            var builderSource = fileName == "FoxgloveCameraPublisher.cs"
                ? source + ReadPublisher("CameraSensorProfileResolver.cs")
                : source;
            Check(source.Contains("publishProvider")
                  && builderSource.Contains(builderToken)
                  && source.Contains("PublishOrdinaryTransport")
                  && !source.Contains("PublishRos2")
                  && !source.Contains("Ros2Cdr"),
                "92D-source: " + fileName + " exposes a neutral Provider value");
        }

        private static List<Phase92Sample> BuildProductSamples()
        {
            var pointFrame = BuildPointCloudFrame();
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

            return new List<Phase92Sample>
            {
                new Phase92Sample("/tf", Ros2PublisherSchemaNames.FrameTransform,
                    Ros2CdrFrameTransformBuilder.Serialize(new FrameTransformMessage
                    {
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(SampleTimeNs),
                        ParentFrameId = "unity_world",
                        ChildFrameId = "cube",
                        Translation = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                        Rotation = new FoxgloveQuaternion { W = 1 }
                    })),
                new Phase92Sample("/scene", Ros2PublisherSchemaNames.SceneUpdate,
                    Ros2CdrSceneUpdateBuilder.Serialize(scene)),
                new Phase92Sample("/unity/camera", Ros2PublisherSchemaNames.CompressedImage,
                    Ros2CdrCompressedImageBuilder.Serialize(SampleTimeNs, "camera", new byte[] { 0xff, 0xd8, 0xff }, "jpeg")),
                new Phase92Sample("/unity/camera/calibration", Ros2PublisherSchemaNames.CameraCalibration,
                    Ros2CdrCameraCalibrationBuilder.Serialize(SampleTimeNs, "camera", 640, 480, "plumb_bob", Array.Empty<double>(), k, r, p)),
                new Phase92Sample("/unity/laser_scan", Ros2PublisherSchemaNames.LaserScan,
                    Ros2CdrLaserScanBuilder.Serialize(SampleTimeNs, "laser", -1, 1, new[] { 1.0, 2.0 }, Array.Empty<double>())),
                new Phase92Sample("/unity/point_cloud", Ros2PublisherSchemaNames.PointCloud,
                    Ros2CdrPointCloudBuilder.Serialize(pointFrame)),
                new Phase92Sample("/unity/point_cloud_draco", Ros2PublisherSchemaNames.CompressedPointCloud,
                    Ros2CdrCompressedPointCloudBuilder.Serialize(pointFrame, new byte[] { 0x44, 0x52, 0x41, 0x43, 0x4f })),
            };
        }

        private static void WriteProductMcap(Stream stream, IReadOnlyList<Phase92Sample> samples, DefaultSchemaRegistry registry)
        {
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession("phase92-mcap", new Phase92FakeTransport(), schemaRegistry: registry);
            session.SetRecorder(recorder);
            for (var i = 0; i < samples.Count; i++)
            {
                var channelId = (uint)(i + 1);
                session.RegisterRos2MsgSchemaChannel(channelId, samples[i].Topic, samples[i].SchemaName);
                session.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
            }

            session.SetRecorder(null);
            recorder.Close();
        }

        private static PointCloudFrame BuildPointCloudFrame()
        {
            var frame = new PointCloudFrame { UnixNs = SampleTimeNs, FrameId = "unity_world" };
            frame.Points.Add(new PointCloudPoint(1, 2, 3) { Intensity = 4 });
            frame.Points.Add(new PointCloudPoint(5, 6, 7) { Intensity = 8 });
            return frame;
        }

        private static IEnumerable<(string schemaName, string sourceToken)> ProductSchemaMappings()
        {
            yield return (Ros2PublisherSchemaNames.FrameTransform,
                "FrameTransform = Ros2CdrFrameTransformBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.SceneUpdate,
                "SceneUpdate = Ros2CdrSceneUpdateBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.CompressedImage,
                "CompressedImage = Ros2CdrCompressedImageBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.CameraCalibration,
                "CameraCalibration = Ros2CdrCameraCalibrationBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.LaserScan,
                "LaserScan = Ros2CdrLaserScanBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.PointCloud,
                "PointCloud = Ros2CdrPointCloudBuilder.SchemaName");
            yield return (Ros2PublisherSchemaNames.CompressedPointCloud,
                "CompressedPointCloud = Ros2CdrCompressedPointCloudBuilder.SchemaName");
        }

        private static string ReadPublisher(string fileName)
        {
            if (fileName == "FoxgloveCameraPublisher.cs")
                return ReadCameraPublisherSources();

            return ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/" + fileName);
        }

        private static string ReadCameraPublisherSources()
            => PhaseValidationSourceHelpers.ReadCameraPublisherSources();

        private static JToken FirstAdvertisedChannel(string json)
        {
            var adv = JObject.Parse(json);
            return (adv["channels"] as JArray)?[0];
        }

        private static List<JObject> FindAdvertisedChannels(IEnumerable<string> texts)
        {
            var result = new List<JObject>();
            foreach (var text in texts)
            {
                var obj = JObject.Parse(text);
                if (obj["op"]?.ToString() != "advertise")
                    continue;

                if (obj["channels"] is not JArray channels)
                    continue;

                foreach (var channel in channels.OfType<JObject>())
                    result.Add(channel);
            }

            return result;
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

        private sealed class Phase92Sample
        {
            public Phase92Sample(string topic, string schemaName, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }

        private sealed class Phase92FakeTransport : IFoxgloveTransport
        {
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public bool IsRunning { get; private set; }
            public string LastBroadcastText;
            public readonly List<string> SentTexts = new List<string>();
            public readonly List<byte[]> SentBinaries = new List<byte[]>();

            /// <summary>
            /// Validation method for Start.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            public void Start(string host, int port) => IsRunning = true;
            /// <summary>
            /// Validation method for Stop.
            /// </summary>
            public void Stop() => IsRunning = false;
            /// <summary>
            /// Validation method for BroadcastText.
            /// </summary>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void BroadcastText(string json)
            {
                LastBroadcastText = json;
                SentTexts.Add(json);
            }

            /// <summary>
            /// Validation method for BroadcastBinary.
            /// </summary>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void BroadcastBinary(byte[] data) { }
            /// <summary>
            /// Validation method for SendText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SendText(uint clientId, string json) => SentTexts.Add(json);
            /// <summary>
            /// Validation method for SendBinary.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void SendBinary(uint clientId, byte[] data) => SentBinaries.Add(data);
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() { }
            /// <summary>
            /// Validation method for SimulateConnect.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            /// <summary>
            /// Validation method for SimulateText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
