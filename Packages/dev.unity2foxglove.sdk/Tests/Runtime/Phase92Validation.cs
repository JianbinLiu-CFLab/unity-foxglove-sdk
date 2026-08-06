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
    public static class Phase92Validation
    {
        private const ulong SampleTimeNs = 1_700_092_000_000_000_000UL;
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
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

        /// <summary>
        /// Validation method for GenerateRos2ProductMcap.
        /// </summary>
        /// <param name="outputPath">Path where the validation output is written.</param>
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
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs",
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
                  && (int)GlobalEncoding.MsgPack == 3,
                "92B-1: core encoding policy contains only Foxglove wire encodings");
            Check((int)PublisherEncodingOverride.UseManager == 0
                  && (int)PublisherEncodingOverride.Json == 1
                  && (int)PublisherEncodingOverride.Protobuf == 2
                  && (int)PublisherEncodingOverride.MsgPack == 4,
                "92B-2: per-publisher overrides contain only Foxglove wire encodings");
            Check((int)PublisherEffectiveEncoding.Json == 0
                  && (int)PublisherEffectiveEncoding.Protobuf == 1
                  && (int)PublisherEffectiveEncoding.Unsupported == 2
                  && (int)PublisherEffectiveEncoding.MsgPack == 4,
                "92B-3: effective encoding preserves Unsupported and MsgPack values");

            Check(Ros2BridgeMcapCodecs.MessageEncoding == "cdr"
                  && Ros2BridgeMcapCodecs.SchemaEncoding == "ros2msg",
                "92B-4: Bridge owns its CDR and schema labels");

            var msgPackSupported = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.MsgPack,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: true,
                supportsMsgPack: true);
            Check(msgPackSupported.Effective == PublisherEffectiveEncoding.MsgPack && !msgPackSupported.FellBack,
                "92B-5: global MessagePack resolves when supported");

            var protobufFallback = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.MsgPack,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: false,
                supportsProtobuf: true,
                supportsMsgPack: false);
            Check(protobufFallback.Requested == PublisherEffectiveEncoding.MsgPack
                  && protobufFallback.Effective == PublisherEffectiveEncoding.Protobuf
                  && protobufFallback.FellBack,
                "92B-6: unsupported MessagePack falls back to Protobuf");

            var jsonFallback = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: false,
                supportsMsgPack: false);
            Check(jsonFallback.Effective == PublisherEffectiveEncoding.Json && jsonFallback.EffectiveLabel == "JSON",
                "92B-7: unsupported Protobuf falls back to JSON");

            var overrideMsgPack = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: true,
                PublisherEncodingOverride.MsgPack,
                supportsJson: true,
                supportsProtobuf: true,
                supportsMsgPack: true);
            Check(overrideMsgPack.Requested == PublisherEffectiveEncoding.MsgPack
                  && overrideMsgPack.Effective == PublisherEffectiveEncoding.MsgPack,
                "92B-8: per-publisher MessagePack override resolves");
        }

        private static void VerifyManagerProductPath()
        {
            var managerProviders = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");
            var managerServer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var bridgeProvider = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var factorySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/SessionFactory.cs");
            Check(publisherBase.Contains("ShouldPrepareOrdinaryTransportPayload")
                  && managerProviders.Contains("HasOrdinaryTransportDemand"),
                "92C-1: core publisher preflight uses frozen ordinary Provider demand");
            Check(publisherBase.Contains("PublishOrdinaryTransport")
                  && managerProviders.Contains("PublishOrdinaryTransports"),
                "92C-2: core publisher exposes neutral ordinary Provider fanout");
            Check(managerProviders.Contains("new FoxRunTransportPublishRoute")
                  && managerProviders.Contains("contribution.MessageEncoding")
                  && managerProviders.Contains("contribution.SchemaEncoding")
                  && managerProviders.Contains("request.DeliveryPolicy"),
                "92C-3: Provider route carries topic, schema encodings, and delivery policy");
            Check(bridgeProvider.Contains("TryMapOrdinary")
                  && bridgeProvider.Contains("MatchesLogicalType")
                  && bridgeProvider.Contains("FoxgloveRos2MsgSchemaCatalog.TryGet")
                  && bridgeProvider.Contains("IFoxRunOrdinaryPayloadMapper"),
                "92C-4: Bridge Provider validates and maps supported logical schemas");
            Check(bridgeProvider.Contains("Ros2CdrPayloadValidator.Validate(payload);")
                  && !bridgeProvider.Contains("payload ??="),
                "92C-5: Bridge Provider preserves strict CDR payload validation");
            Check(runtimeSource.Contains("_additionalMessageEncodings")
                  && runtimeSource.Contains("EnableMessageEncoding")
                  && factorySource.Contains("additionalMessageEncodings")
                  && factorySource.Contains("session.EnableMessageEncoding(encoding)"),
                "92C-6: optional packages explicitly add non-core message encodings");
            Check(!managerServer.Contains("\"cdr\"")
                  && !managerServer.Contains("Ros2Bridge")
                  && !managerProviders.Contains("Ros2Bridge"),
                "92C-7: core Manager keeps Bridge CDR out of its default session");
            Check(!managerServer.Contains("RegisterRos2InteractivePublishTargetChannels")
                  && !managerProviders.Contains("Ros2InteractivePublishSchemas"),
                "92C-8: manager does not add fake ROS navigation publish topics");
        }

        private static void VerifyPublisherIntegration()
        {
            var schemaNames = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs");
            var bridgeProvider = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");
            foreach (var mapping in ProductSchemaMappings())
                Check(schemaNames.Contains(mapping.sourceToken),
                    "92D-1: schema mapping contains " + mapping.schemaName);

            Check(bridgeProvider.Contains("Ros2CdrSerializerRegistry.TryGetByClrType")
                  && bridgeProvider.Contains("Ros2BridgeOrdinaryLogicalSchema.Matches")
                  && bridgeProvider.Contains("serializer.Serialize(message)"),
                "92D-1b: Bridge Provider maps registered Foxglove protobuf publisher schemas");

            CheckPublisher(
                "FoxgloveTransformPublisher.cs",
                "CreateProtobufTransform");
            CheckPublisher(
                "FoxgloveSceneCubePublisher.cs",
                "CreateProtobufSceneUpdate");
            CheckPublisher(
                "FoxgloveCameraPublisher.cs",
                "SensorCompressedImageFrame");
            CheckPublisher(
                "FoxgloveCameraCalibrationPublisher.cs",
                "CameraCalibrationMessageBuilder.CreateProtobuf");
            CheckPublisher(
                "FoxgloveLaserScanPublisher.cs",
                "LaserScanMessageBuilder.CreateProtobuf");

            var camera = ReadPublisher("FoxgloveCameraPublisher.cs");
            Check(camera.Contains("CameraOutputMode.Jpeg")
                  && camera.Contains("PublishOrdinaryTransport")
                  && camera.Contains("SensorCompressedImageFrame")
                  && !camera.Contains("PublishRos2"),
                "92D-2: camera exposes JPEG values through the neutral Provider boundary");

            var pointCloud = ReadPublisher("FoxglovePointCloudPublisher.cs")
                + "\n" + ReadPublisher("FoxglovePointCloudPublisher.Raw.cs")
                + "\n" + ReadPublisher("FoxglovePointCloudPublisher.Draco.cs")
                + "\n" + ReadPublisher("FoxglovePointCloudPublisher.PackedPointCloud.cs")
                + "\n" + ReadPublisher("PointCloudOutputMode.cs");
            var pointCloudWorkers = ReadPublisher("PointCloudWorkerEncoders.cs");
            Check(pointCloud.Contains("PublishOrdinaryTransport")
                  && pointCloud.Contains("PackedPointCloudFrame")
                  && pointCloud.Contains("PointCloudWorkerEncoders.EncodeDracoRequest")
                  && pointCloud.Contains("PointCloudWorkerEncoders.EncodePackedPointCloudRequest")
                  && pointCloudWorkers.Contains("CompressedPointCloudMessageBuilder.CreateProtobuf")
                  && pointCloudWorkers.Contains("DracoPointCloudNativeEncoder.TryEncode")
                  && bridgeProvider.Contains("request.Value is PackedPointCloudFrame")
                  && bridgeProvider.Contains("Ros2CdrSensorPointCloud2Builder.Serialize")
                  && !pointCloud.Contains("Ros2Cdr")
                  && !pointCloudWorkers.Contains("Ros2Cdr")
                  && !pointCloud.Contains("new byte[] { 1, 2, 3, 4 }")
                  && !pointCloudWorkers.Contains("new byte[] { 1, 2, 3, 4 }"),
                "92D-3: point-cloud modes hand neutral values to Bridge-owned CDR mappings");

            var spike = ReadPublisher("FoxgloveCompressedPointCloudPublisher.cs");
            Check(!spike.Contains("PublishOrdinaryTransport"),
                "92D-4: legacy Draco spike publisher stays outside Provider fanout");
        }

        private static void VerifyInspectorUx()
        {
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/PublisherEncodingEditorLabels.cs");
            Check(labels.Contains("\"JSON\"")
                  && labels.Contains("\"Protobuf\"")
                  && labels.Contains("\"MsgPack\"")
                  && labels.Contains("DrawGlobalEncoding")
                  && labels.Contains("DrawPublisherOverride")
                  && !labels.Contains("\"ROS2\"")
                  && !labels.Contains("\"CDR\""),
                "92E-1: core encoding labels expose only Foxglove wire encodings");

            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var publishDataEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var publisherBaseEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs");
            var cameraEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var pointCloudEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            Check(publishDataEditor.Contains("DrawGlobalEncodingProperty(")
                  && publishDataEditor.Contains("\"_defaultPublisherEncoding\"")
                  && publisherBaseEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && publisherBaseEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding")
                  && publishDataEditor.Contains("FoxRunTransportProviderDrawerRegistry.Capture"),
                "92E-2: Manager and publisher inspectors combine encoding labels with Provider extensions");
            Check(cameraEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && cameraEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding")
                  && cameraEditor.Contains("Provider Payload")
                  && !cameraEditor.Contains("ROS2")
                  && !cameraEditor.Contains("cdr"),
                "92E-3: camera inspector exposes Provider-neutral payload controls");
            Check(pointCloudEditor.Contains("Packed Provider Frame")
                  && pointCloudEditor.Contains("PublisherEncodingEditorLabels.DrawPublisherOverride")
                  && pointCloudEditor.Contains("PublisherEncodingEditorLabels.DrawEffectiveEncoding")
                  && !pointCloudEditor.Contains("ROS2"),
                "92E-4: point-cloud inspector advertises Provider-neutral handoff");
        }

        private static void VerifyWebSocketMcapAndReplay()
        {
            var samples = BuildProductSamples();
            Check(samples.Count == 7, "92F-1: product smoke contains seven ROS2 publisher samples");

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase92FakeTransport();
            using var session = new FoxgloveSession("phase92-session", transport, schemaRegistry: registry);
            session.EnableRos2BridgeSchemas();
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
