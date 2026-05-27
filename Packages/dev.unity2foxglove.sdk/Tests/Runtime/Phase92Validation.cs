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
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase92Validation
    {
        private const ulong SampleTimeNs = 1_700_092_000_000_000_000UL;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 92: ROS2 Productization And Delivery ===");
            _passed = 0;

            VerifyPlannedSourceFilesExist();
            VerifyEncodingPolicy();
            VerifyManagerProductPath();
            VerifyPublisherIntegration();
            VerifyInspectorUx();
            VerifyWebSocketMcapAndReplay();
            VerifyDocsAndBoundaries();

            Console.WriteLine($"Phase 92: {_passed} checks passed.");
        }

        public static void GenerateRos2ProductMcap(string outputPath)
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
            using var session = new FoxgloveSession("phase92-mcap", new Phase92FakeTransport(), schemaRegistry: registry);
            session.SetRecorder(recorder);

            var samples = BuildProductSamples();
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
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase92Validation.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/PublisherEncodingEditorLabels.cs",
            };

            foreach (var file in files)
                Check(!string.IsNullOrEmpty(ReadRepoText(file)), "92A-1: source exists " + Path.GetFileName(file));
        }

        private static void VerifyEncodingPolicy()
        {
            Check((int)GlobalEncoding.Json == 0
                  && (int)GlobalEncoding.Protobuf == 1
                  && (int)GlobalEncoding.Ros2 == 2,
                "92B-1: GlobalEncoding preserves serialized values and adds ROS2");
            Check((int)PublisherEncodingOverride.UseManager == 0
                  && (int)PublisherEncodingOverride.Json == 1
                  && (int)PublisherEncodingOverride.Protobuf == 2
                  && (int)PublisherEncodingOverride.Ros2 == 3,
                "92B-2: PublisherEncodingOverride preserves serialized values and adds ROS2");
            Check((int)PublisherEffectiveEncoding.Json == 0
                  && (int)PublisherEffectiveEncoding.Protobuf == 1
                  && (int)PublisherEffectiveEncoding.Unsupported == 2
                  && (int)PublisherEffectiveEncoding.Ros2 == 3,
                "92B-3: PublisherEffectiveEncoding preserves old Unsupported value");

            Check(PublisherEncodingPolicy.ToDisplayEncoding(PublisherEffectiveEncoding.Ros2) == "ROS2"
                  && PublisherEncodingPolicy.ToProtocolEncoding(PublisherEffectiveEncoding.Ros2) == "cdr"
                  && PublisherEncodingPolicy.ToSchemaEncoding(PublisherEffectiveEncoding.Ros2) == "ros2msg",
                "92B-4: ROS2 display/protocol/schema labels are split");

            var ros2Supported = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Ros2,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true);
            Check(ros2Supported.Effective == PublisherEffectiveEncoding.Ros2 && !ros2Supported.FellBack,
                "92B-5: global ROS2 resolves to ROS2 when supported");

            var videoFallback = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Ros2,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: false);
            Check(videoFallback.Requested == PublisherEffectiveEncoding.Ros2
                  && videoFallback.Effective == PublisherEffectiveEncoding.Protobuf
                  && videoFallback.FellBack
                  && videoFallback.RequestedLabel == "ROS2"
                  && videoFallback.EffectiveLabel == "Protobuf",
                "92B-6: unsupported ROS2 falls back to Protobuf with product labels");

            var jsonFallback = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Ros2,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: false,
                supportsRos2: false);
            Check(jsonFallback.Effective == PublisherEffectiveEncoding.Json && jsonFallback.EffectiveLabel == "JSON",
                "92B-7: unsupported ROS2 falls back to JSON when Protobuf is unavailable");

            var overrideRos2 = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: true,
                PublisherEncodingOverride.Ros2,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true);
            Check(overrideRos2.Requested == PublisherEffectiveEncoding.Ros2 && overrideRos2.Effective == PublisherEffectiveEncoding.Ros2,
                "92B-8: per-publisher ROS2 override resolves");
        }

        private static void VerifyManagerProductPath()
        {
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var managerServer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            Check(managerSource.Contains("TryPrepareRos2Publish"),
                "92C-1: manager exposes product ROS2 preflight");
            Check(managerSource.Contains("public void PublishRos2(string topic, string schemaName, byte[] payload, ulong logTimeNs)"),
                "92C-2: manager exposes product PublishRos2 helper");
            Check(managerSource.Contains("var key = (topic, schemaName, CdrEncoding, Ros2MsgSchemaEncoding);"),
                "92C-3: ROS2 channel cache key includes schema encoding");
            Check(managerSource.Contains("FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _)"),
                "92C-4: manager validates ROS2 schema names against catalog");
            Check(managerSource.Contains("_runtime.PublishRos2Cdr(channelId, payload, logTimeNs);")
                  && !managerSource.Contains("_runtime.PublishRos2Cdr(channelId, payload ??"),
                "92C-5: product ROS2 publish preserves payload validation");
            Check(runtimeSource.Contains("bool enableCdrClientPublish = true")
                  && runtimeSource.Contains("if (enableCdrClientPublish && _ros2MsgSchemasRegistered)"),
                "92C-6: runtime can suppress CDR advertisement for product sessions");
            Check(managerServer.Contains("_runtime.Start(_serverName, _host, _port, enableCdrClientPublish: false)"),
                "92C-7: manager keeps CDR out of client-publish supportedEncodings");
            Check(!managerServer.Contains("RegisterRos2InteractivePublishTargetChannels")
                  && !managerSource.Contains("Ros2InteractivePublishSchemas"),
                "92C-8: manager does not add fake ROS navigation publish topics");
        }

        private static void VerifyPublisherIntegration()
        {
            var schemaNames = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs");
            foreach (var mapping in ProductSchemaMappings())
                Check(schemaNames.Contains(mapping.sourceToken),
                    "92D-1: schema mapping contains " + mapping.schemaName);

            CheckPublisher(
                "FoxgloveTransformPublisher.cs",
                "Ros2PublisherSchemaNames.FrameTransform",
                "Ros2CdrFrameTransformBuilder.Serialize",
                "PublishRos2");
            CheckPublisher(
                "FoxgloveSceneCubePublisher.cs",
                "Ros2PublisherSchemaNames.SceneUpdate",
                "Ros2CdrSceneUpdateBuilder.Serialize",
                "PublishRos2");
            CheckPublisher(
                "FoxgloveCameraPublisher.cs",
                "Ros2PublisherSchemaNames.CompressedImage",
                "Ros2CdrCompressedImageBuilder.Serialize",
                "PublishRos2");
            CheckPublisher(
                "FoxgloveCameraCalibrationPublisher.cs",
                "Ros2PublisherSchemaNames.CameraCalibration",
                "Ros2CdrCameraCalibrationBuilder.Serialize",
                "PublishRos2");
            CheckPublisher(
                "FoxgloveLaserScanPublisher.cs",
                "Ros2PublisherSchemaNames.LaserScan",
                "Ros2CdrLaserScanBuilder.Serialize",
                "PublishRos2");

            var camera = ReadPublisher("FoxgloveCameraPublisher.cs");
            Check(camera.Contains("SupportsRos2Encoding => ActiveProfile.Mode == CameraOutputMode.Jpeg"),
                "92D-2: camera ROS2 support is limited to JPEG mode");

            var pointCloud = ReadPublisher("FoxglovePointCloudPublisher.cs");
            Check(pointCloud.Contains("Ros2PublisherSchemaNames.PointCloud")
                  && pointCloud.Contains("Ros2PublisherSchemaNames.CompressedPointCloud")
                  && pointCloud.Contains("Ros2CdrPointCloudBuilder.Serialize")
                  && pointCloud.Contains("Ros2CdrCompressedPointCloudBuilder.Serialize")
                  && pointCloud.Contains("DracoPointCloudNativeEncoder.TryEncode")
                  && !pointCloud.Contains("new byte[] { 1, 2, 3, 4 }"),
                "92D-3: point-cloud raw and Draco modes map to distinct real ROS2 payload paths");

            var spike = ReadPublisher("FoxgloveCompressedPointCloudPublisher.cs");
            Check(spike.Contains("SupportsRos2Encoding => false"),
                "92D-4: legacy Draco spike publisher remains protobuf-only");
        }

        private static void VerifyInspectorUx()
        {
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/PublisherEncodingEditorLabels.cs");
            Check(labels.Contains("\"ROS2\"") && labels.Contains("DrawGlobalEncoding") && labels.Contains("DrawPublisherOverride"),
                "92E-1: shared editor labels force visible ROS2 text");

            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var publisherBaseEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs");
            var cameraEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var pointCloudEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            Check(managerEditor.Contains("DrawGlobalEncodingProperty(\"_defaultPublisherEncoding\"")
                  && publisherBaseEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && publisherBaseEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding"),
                "92E-2: manager/base publisher inspector uses product encoding labels");
            Check(cameraEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && cameraEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding")
                  && !cameraEditor.Contains("cdr"),
                "92E-3: camera inspector does not expose cdr wording");
            Check(pointCloudEditor.Contains("Protobuf and ROS2")
                  && pointCloudEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && pointCloudEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding"),
                "92E-4: point-cloud inspector advertises Draco ROS2 support");
        }

        private static void VerifyWebSocketMcapAndReplay()
        {
            var samples = BuildProductSamples();
            Check(samples.Count == 7, "92F-1: product smoke contains seven ROS2 publisher samples");

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase92FakeTransport();
            using var session = new FoxgloveSession("phase92-session", transport, schemaRegistry: registry);
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
                    "92F-2: WebSocket advertises product ROS2 channel " + samples[i].SchemaName);
                transport.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":" + (200 + i) + ",\"channelId\":" + channelId + "}]}");
                session.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
            }

            Check(transport.SentBinaries.Count == samples.Count,
                "92F-3: WebSocket publishes one binary frame per product ROS2 sample");

            using var stream = new MemoryStream();
            WriteProductMcap(stream, samples, registry);
            stream.Position = 0;
            using var indexed = new McapIndexedReader(stream, leaveOpen: true);
            Check(indexed.Schemas.Count == 7
                  && indexed.Schemas.All(schema => schema.Encoding == "ros2msg")
                  && indexed.Channels.Count == 7
                  && indexed.Channels.All(channel => channel.MessageEncoding == "cdr")
                  && indexed.ReadMessages().Count == 7,
                "92F-4: MCAP preserves seven ros2msg+cdr product channels");

            var tempPath = Path.Combine(Path.GetTempPath(), "phase92_ros2_product_" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                GenerateRos2ProductMcap(tempPath);
                var replayTransport = new Phase92FakeTransport();
                using var runtime = new FoxgloveRuntime(replayTransport, new SystemClock(), new DefaultSchemaRegistry());
                runtime.EnableReplay(tempPath);
                runtime.Start("phase92-replay", "127.0.0.1", 9292);
                var replayChannels = FindAdvertisedChannels(replayTransport.SentTexts);
                Check(replayChannels.Count == 7
                      && replayChannels.All(ch => ch["encoding"]?.ToString() == "cdr")
                      && replayChannels.All(ch => ch["schemaEncoding"]?.ToString() == "ros2msg"),
                    "92F-5: replay pass-through re-advertises ros2msg+cdr channels");
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void VerifyDocsAndBoundaries()
        {
            var schemaCoverage = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md");
            var readme = ReadRepoText("README.md");
            Check(schemaCoverage.Contains("ROS2") && schemaCoverage.Contains("CompressedPointCloud"),
                "92G-1: schema coverage docs mention productized ROS2 coverage");
            Check(readme.Contains("ROS2") || readme.Contains("ROS 2"),
                "92G-2: README mentions user-facing ROS2 output");

            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");
            Check(manager.Contains("_defaultPublisherEncoding = GlobalEncoding.Protobuf"),
                "92G-3: existing scenes still default to Protobuf");
        }

        private static void CheckPublisher(string fileName, string schemaNameToken, string builderToken, string publishToken)
        {
            var source = ReadPublisher(fileName);
            Check(source.Contains("SupportsRos2Encoding")
                  && source.Contains(schemaNameToken)
                  && source.Contains(builderToken)
                  && source.Contains(publishToken),
                "92D-source: " + fileName + " productizes ROS2");
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

        private static IEnumerable<string> ProductSchemaNames()
        {
            yield return Ros2PublisherSchemaNames.FrameTransform;
            yield return Ros2PublisherSchemaNames.SceneUpdate;
            yield return Ros2PublisherSchemaNames.CompressedImage;
            yield return Ros2PublisherSchemaNames.CameraCalibration;
            yield return Ros2PublisherSchemaNames.LaserScan;
            yield return Ros2PublisherSchemaNames.PointCloud;
            yield return Ros2PublisherSchemaNames.CompressedPointCloud;
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
            => ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/" + fileName);

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

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void BroadcastText(string json)
            {
                LastBroadcastText = json;
                SentTexts.Add(json);
            }

            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => SentTexts.Add(json);
            public void SendBinary(uint clientId, byte[] data) => SentBinaries.Add(data);
            public void Dispose() { }
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
