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
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Util;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase91Validation.
    /// </summary>
    public static partial class Phase91Validation
    {
        private static void VerifyWebSocketAndMcap()
        {
            var samples = BuildSamples();
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            var transport = new Phase91FakeTransport();
            using var session = new FoxgloveSession("phase91-session", transport, schemaRegistry: registry);
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

            var bridgeCodecs = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeMcapCodecs.cs");

            Check(SourceTreeAvoids(componentsRoot, "PublisherEffectiveEncoding.Cdr", "GlobalEncoding.Cdr"),
                "91G-1: Phase91 does not add publisher or global CDR output modes");
            Check(SourceTreeAvoids(publishersRoot, "PublishRos2Cdr"),
                "91G-2: product publishers do not call the low-level ROS2 CDR helper directly");
            Check(bridgeCodecs.Contains("RegisterRos2MsgSchemaChannel")
                  && bridgeCodecs.Contains("runtime.RegisterSchemaChannel")
                  && bridgeCodecs.Contains("session.RegisterSchemaChannel")
                  && bridgeCodecs.Contains("MessageEncoding")
                  && bridgeCodecs.Contains("SchemaEncoding"),
                "91G-3: Bridge package owns explicit ros2msg CDR channel helpers");
            var publishRos2CdrSignatures = new[]
            {
                "public static void PublishRos2Cdr( this FoxgloveRuntime runtime, uint channelId, byte[] payload)",
                "public static void PublishRos2Cdr( this FoxgloveRuntime runtime, uint channelId, byte[] payload, ulong logTimeNs)",
                "public static void PublishRos2Cdr( this FoxgloveSession session, uint channelId, byte[] payload)",
                "public static void PublishRos2Cdr( this FoxgloveSession session, uint channelId, byte[] payload, ulong logTimeNs)",
            };
            var publishRos2CdrMethods = publishRos2CdrSignatures
                .Select(signature => PhaseValidationSourceHelpers.RequiredSourceMethod(bridgeCodecs, signature))
                .ToArray();
            Check(publishRos2CdrMethods.Length == publishRos2CdrSignatures.Length
                  && publishRos2CdrMethods.All(method =>
                      method.Contains("Validate(payload);", StringComparison.Ordinal)
                      && !method.Contains("payload ??=", StringComparison.Ordinal)),
                "91G-4: every Bridge-owned PublishRos2Cdr overload validates payload without null-coalescing to empty");
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

        private static bool SourceTreeContains(string root, params string[] needles)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                if (needles.Any(needle => text.Contains(needle, StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }

        private static bool SourceTreeAvoids(string root, params string[] needles)
            => !SourceTreeContains(root, needles);

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
            public void BroadcastText(string json) => LastBroadcastText = json;
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
            public void SendText(uint clientId, string json) => LastSentText = json;
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
