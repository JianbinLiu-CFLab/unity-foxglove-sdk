// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 93 validation for full ROS 2 .msg CDR serializer parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase93Validation
    {
        private const ulong SampleTimeNs = 1_700_093_000_000_000_000UL;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 93: ROS2 Full Schema Payload Parity ===");
            _passed = 0;

            VerifySchemaSnapshot();
            VerifyGeneratedRegistrySurface();
            VerifyAllSamplesSerialize();
            VerifyFixedArrayValidation();
            VerifyPhase91Compatibility();
            VerifyWebSocketAllSchemaSmoke();
            VerifyMcapAllSchemaSmoke();
            VerifyReplayPassThroughAllSchemaSmoke();
            VerifyBoundary();

            Console.WriteLine($"Phase 93: {_passed} checks passed.");
        }

        public static void GenerateRos2FullSchemaMcap(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            using var stream = File.Create(outputPath);
            WriteAllSchemaMcap(stream, registry);
        }

        public static void InspectRos2FullSchemaMcap(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("Input path is required.", nameof(inputPath));

            var fullPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Phase 93 ROS2 full-schema MCAP was not found.", fullPath);

            using var indexed = McapIndexedReader.OpenRead(fullPath);
            var expectedSchemaNames = new HashSet<string>(FoxgloveRos2MsgSchemaCatalog.Entries.Select(e => e.SchemaName));
            var actualSchemaNames = new HashSet<string>(indexed.Schemas.Select(schema => schema.Name));
            var messages = indexed.ReadMessages();

            if (indexed.Schemas.Count != expectedSchemaNames.Count)
                throw new InvalidOperationException($"Expected {expectedSchemaNames.Count} schemas, found {indexed.Schemas.Count}.");
            if (indexed.Channels.Count != expectedSchemaNames.Count)
                throw new InvalidOperationException($"Expected {expectedSchemaNames.Count} channels, found {indexed.Channels.Count}.");
            if (messages.Count != expectedSchemaNames.Count)
                throw new InvalidOperationException($"Expected {expectedSchemaNames.Count} messages, found {messages.Count}.");
            if (!indexed.Schemas.All(schema => schema.Encoding == "ros2msg"))
                throw new InvalidOperationException("Expected every schema to use schemaEncoding ros2msg.");
            if (!indexed.Channels.All(channel => channel.MessageEncoding == "cdr"))
                throw new InvalidOperationException("Expected every channel to use messageEncoding cdr.");
            if (!actualSchemaNames.SetEquals(expectedSchemaNames))
                throw new InvalidOperationException("MCAP schema names do not match the Phase 90 ROS2 schema catalog.");
            if (!messages.All(message => HasCdrHeader(message.Data)))
                throw new InvalidOperationException("Expected every message payload to include the ROS2 CDR encapsulation header.");

            Console.WriteLine($"[phase93] schemas={indexed.Schemas.Count} channels={indexed.Channels.Count} messages={messages.Count}");
            Console.WriteLine("[phase93] schemaEncoding=ros2msg");
            Console.WriteLine("[phase93] messageEncoding=cdr");
            Console.WriteLine("[phase93] PASS ros2 full schema smoke");
        }

        private static void VerifySchemaSnapshot()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.SourceFileCount == 41, "93A-1: ROS2 schema snapshot still has 41 root files");
            Check(FoxgloveRos2MsgSchemaCatalog.Entries.Count == 41, "93A-2: ROS2 schema catalog exposes 41 entries");
            Check(FoxgloveRos2MsgSchemaCatalog.Entries.All(e => e.SchemaEncoding == "ros2msg"),
                "93A-3: ROS2 schema catalog entries use ros2msg schema encoding");
        }

        private static void VerifyGeneratedRegistrySurface()
        {
            Check(Ros2CdrSerializerRegistry.SerializerCount == 41, "93B-1: generated serializer registry declares 41 serializers");
            Check(Ros2CdrSerializerRegistry.Entries.Count == 41, "93B-2: generated serializer registry exposes 41 entries");

            var catalogNames = new HashSet<string>(FoxgloveRos2MsgSchemaCatalog.Entries.Select(e => e.SchemaName));
            var registryNames = new HashSet<string>(Ros2CdrSerializerRegistry.Entries.Select(e => e.SchemaName));
            Check(catalogNames.SetEquals(registryNames), "93B-3: serializer registry covers every Phase90 ros2msg schema");

            foreach (var entry in Ros2CdrSerializerRegistry.Entries)
            {
                Check(Ros2CdrSerializerRegistry.TryGetBySchemaName(entry.SchemaName, out var byName)
                      && ReferenceEquals(entry, byName),
                    "93B-4: schema-name lookup resolves " + entry.SchemaName);
                Check(Ros2CdrSerializerRegistry.TryGetByClrType(entry.ClrType, out var byType)
                      && ReferenceEquals(entry, byType),
                    "93B-5: CLR-type lookup resolves " + entry.ClrType.Name);
                Check(entry.HasDeterministicSample && entry.CreateSample() != null,
                    "93B-6: deterministic sample exists for " + entry.SchemaName);
            }

            Check(!Ros2CdrSerializerRegistry.TryGetBySchemaName("foxglove_msgs/msg/Missing", out _),
                "93B-7: unknown schema lookup returns false");
            Check(!Ros2CdrSerializerRegistry.TrySerialize("foxglove_msgs/msg/Missing", new Foxglove.Log(), out _),
                "93B-8: TrySerialize returns false for unknown schema");
            Check(Throws<InvalidOperationException>(() => Ros2CdrSerializerRegistry.Serialize("foxglove_msgs/msg/Log", new Foxglove.Color())),
                "93B-9: Serialize throws on schema/CLR mismatch");
        }

        private static void VerifyAllSamplesSerialize()
        {
            foreach (var entry in Ros2CdrSerializerRegistry.Entries)
            {
                var sample = entry.CreateSample();
                Check(sample != null && entry.ClrType.IsInstanceOfType(sample),
                    "93C-1: sample CLR type matches " + entry.SchemaName);

                var payload = entry.Serialize(sample);
                Check(HasCdrHeader(payload) && payload.Length > 4,
                    "93C-2: sample payload has CDR header for " + entry.SchemaName);

                Check(Ros2CdrSerializerRegistry.TrySerialize(entry.SchemaName, sample, out var tryPayload)
                      && tryPayload.SequenceEqual(payload),
                    "93C-3: TrySerialize returns identical payload for " + entry.SchemaName);
            }
        }

        private static void VerifyFixedArrayValidation()
        {
            Check(FixedArrayFailure<ArgumentException>("foxglove_msgs/msg/CameraCalibration", new Foxglove.CameraCalibration()),
                "93D-1: CameraCalibration rejects missing fixed arrays");
            Check(FixedArrayFailure<ArgumentException>("foxglove_msgs/msg/Odometry", new Foxglove.Odometry()),
                "93D-2: Odometry rejects missing covariance arrays");
            Check(FixedArrayFailure<ArgumentException>("foxglove_msgs/msg/LocationFix", new Foxglove.LocationFix()),
                "93D-3: LocationFix rejects missing covariance array");
        }

        private static void VerifyPhase91Compatibility()
        {
            var frameTransform = Ros2CdrFrameTransformBuilder.Serialize(new FrameTransformMessage
            {
                Timestamp = new FoxgloveTime { Sec = 1, Nsec = 2 },
                ParentFrameId = "world",
                ChildFrameId = "child",
                Translation = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new FoxgloveQuaternion { W = 1 }
            });
            Check(HasCdrHeader(frameTransform), "93E-1: Phase91 FrameTransform builder remains source-compatible");

            var scene = new SceneUpdateMessage();
            scene.Entities.Add(new SceneEntity { FrameId = "world", Id = "cube", Cubes = new List<CubePrimitive> { new CubePrimitive { Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 } } } });
            Check(HasCdrHeader(Ros2CdrSceneUpdateBuilder.Serialize(scene)),
                "93E-2: Phase91 SceneUpdate compatibility builder remains available");
        }

        private static void VerifyWebSocketAllSchemaSmoke()
        {
            var samples = BuildSamples();
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);

            var productTransport = new Phase93FakeTransport();
            using (var runtime = new FoxgloveRuntime(productTransport, new SystemClock(), registry))
            {
                runtime.Start("phase93-product-boundary", "127.0.0.1", 9393, enableCdrClientPublish: false);
                productTransport.SimulateConnect(1);
            }

            var serverInfo = FindServerInfo(productTransport.SentTexts);
            var supported = serverInfo?["supportedEncodings"]?.Select(v => v.ToString()).ToArray() ?? Array.Empty<string>();
            Check(!supported.Contains("cdr"), "93F-1: product session keeps cdr out of supportedEncodings");

            var transport = new Phase93FakeTransport();
            var session = new FoxgloveSession("phase93-session", transport, schemaRegistry: registry);
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
                    "93F-2: WebSocket advertises ros2msg+cdr channel " + samples[i].SchemaName);

                transport.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":" + (300 + i) + ",\"channelId\":" + channelId + "}]}");
                session.PublishRos2Cdr(channelId, samples[i].Payload, SampleTimeNs + (ulong)i);
            }

            Check(transport.SentBinaries.Count == samples.Count,
                "93F-3: WebSocket publishes one binary frame per full-schema sample");
        }

        private static void VerifyMcapAllSchemaSmoke()
        {
            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            using var stream = new MemoryStream();
            WriteAllSchemaMcap(stream, registry);
            stream.Position = 0;

            using var indexed = new McapIndexedReader(stream, leaveOpen: true);
            Check(indexed.Schemas.Count == 41
                  && indexed.Schemas.All(schema => schema.Encoding == "ros2msg")
                  && indexed.Channels.Count == 41
                  && indexed.Channels.All(channel => channel.MessageEncoding == "cdr")
                  && indexed.ReadMessages().Count == 41,
                "93G-1: MCAP stores all 41 ros2msg schemas, CDR channels, and messages");
        }

        private static void VerifyReplayPassThroughAllSchemaSmoke()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "phase93_ros2_full_" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                GenerateRos2FullSchemaMcap(tempPath);
                var replayTransport = new Phase93FakeTransport();
                using var runtime = new FoxgloveRuntime(replayTransport, new SystemClock(), new DefaultSchemaRegistry());
                runtime.EnableReplay(tempPath);
                runtime.Start("phase93-replay", "127.0.0.1", 9394);
                var replayChannels = FindAdvertisedChannels(replayTransport.SentTexts);
                Check(replayChannels.Count == 41
                      && replayChannels.All(ch => ch["encoding"]?.ToString() == "cdr")
                      && replayChannels.All(ch => ch["schemaEncoding"]?.ToString() == "ros2msg"),
                    "93H-1: replay pass-through re-advertises all 41 ros2msg+cdr channels");
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void VerifyBoundary()
        {
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var managerServer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var schemaCoverage = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md");
            Check(!managerSource.Contains("PublishRos2(string topic, string schemaName, IMessage message"),
                "93I-1: Phase93 does not add IMessage Manager convenience API");
            Check(managerServer.Contains("enableCdrClientPublish: false"),
                "93I-2: Manager still suppresses CDR client-publish support");
            Check(schemaCoverage.Contains("Phase 93") && schemaCoverage.Contains("41") && schemaCoverage.Contains("low-level"),
                "93I-3: schema coverage docs describe low-level full ROS2 CDR parity");
        }

        private static List<Phase93Sample> BuildSamples()
        {
            return Ros2CdrSerializerRegistry.Entries
                .Select((entry, index) =>
                {
                    var sample = entry.CreateSample();
                    return new Phase93Sample("/phase93/" + entry.ClrType.Name, entry.SchemaName, entry.Serialize(sample));
                })
                .ToList();
        }

        private static void WriteAllSchemaMcap(Stream stream, DefaultSchemaRegistry registry)
        {
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            var session = new FoxgloveSession("phase93-mcap", new Phase93FakeTransport(), schemaRegistry: registry);
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

        private static JObject FirstAdvertisedChannel(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var obj = JObject.Parse(json);
            return obj["op"]?.ToString() == "advertise"
                ? obj["channels"]?.FirstOrDefault() as JObject
                : null;
        }

        private static JObject FindServerInfo(IEnumerable<string> texts)
        {
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var obj = JObject.Parse(text);
                if (obj["op"]?.ToString() == "serverInfo")
                    return obj;
            }

            return null;
        }

        private static List<JObject> FindAdvertisedChannels(IEnumerable<string> texts)
        {
            var result = new List<JObject>();
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var obj = JObject.Parse(text);
                if (obj["op"]?.ToString() != "advertise")
                    continue;
                foreach (var channel in obj["channels"] ?? Enumerable.Empty<JToken>())
                    if (channel is JObject channelObject)
                        result.Add(channelObject);
            }

            return result;
        }

        private static bool HasCdrHeader(byte[] payload)
        {
            return payload != null && payload.Length >= 4 && payload[0] == 0 && payload[1] == 1 && payload[2] == 0 && payload[3] == 0;
        }

        private static bool FixedArrayFailure<TException>(string schemaName, IMessage message)
            where TException : Exception
        {
            try
            {
                Ros2CdrSerializerRegistry.Serialize(schemaName, message);
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private sealed class Phase93Sample
        {
            public Phase93Sample(string topic, string schemaName, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }

        private sealed class Phase93FakeTransport : IFoxgloveTransport
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
