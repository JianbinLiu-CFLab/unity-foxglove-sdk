// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foxglove;
using Foxglove.Schemas;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Transport;

/// <summary>
/// Phase 32 validation: protobuf encoding support — dependency, FileDescriptorSet registry,
/// schema advertisement, message publish, MCAP recording.
/// </summary>
public class Phase32Validation
{
    public static void Run()
    {
        Console.WriteLine("=== Phase 32: Protobuf Encoding ===");
        var passed = 0;
        var failed = 0;

        void Check(bool cond, string msg)
        {
            if (cond) { Console.WriteLine($"[PASS] {msg}"); passed++; }
            else { Console.WriteLine($"[FAIL] {msg}"); failed++; }
        }

        // ── Batch 32A: Dependency and Codegen ──

        // 32A-1: Google.Protobuf assembly is loadable
        var gpAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Google.Protobuf");
        Check(gpAssembly != null, "32A-1: Google.Protobuf assembly is loaded");

        // 32A-2: Generated proto C# class is usable
        var ft = new FrameTransform
        {
            ParentFrameId = "map",
            ChildFrameId = "base_link",
            Translation = new Vector3 { X = 1, Y = 2, Z = 3 },
            Rotation = new Quaternion { W = 1 }
        };
        Check(ft.ParentFrameId == "map", "32A-2: FrameTransform can be constructed");
        Check(ft.Translation.X == 1, "32A-2b: nested message fields are set correctly");

        // 32A-3: FrameTransform can be serialized to protobuf bytes
        var serialized = ft.ToByteArray();
        Check(serialized != null && serialized.Length > 0, "32A-3: FrameTransform serializes to non-empty bytes");

        // 32A-4: FrameTransform can be deserialized round-trip
        var ft2 = FrameTransform.Parser.ParseFrom(serialized);
        Check(ft2.ParentFrameId == "map" && ft2.ChildFrameId == "base_link", "32A-4: FrameTransform round-trips correctly");

        // ── Batch 32B: FileDescriptorSet Registry ──

        // Load FileDescriptorSet from the pre-compiled FoxgloveSchemas constant (works in both Unity and .NET)
        byte[] fdsBytes = FoxgloveSchemas.FileDescriptorSetData;
        Check(fdsBytes != null && fdsBytes.Length > 0, "32B-1: FileDescriptorSet bytes from constant are non-empty");

        var schemaRegistry = new DefaultSchemaRegistry();
        var protoRegistry = ProtobufSchemaRegistryLoader.FromDefault(schemaRegistry);

        // 32B-3: Schema count matches expected (46 foxglove schemas)
        Check(protoRegistry.Count >= 46, $"32B-3: ProtobufSchemaRegistry has {protoRegistry.Count} schemas (expected >= 46)");

        // 32B-4: GetFileDescriptorSet returns valid bytes for FrameTransform
        var ftDescriptor = protoRegistry.GetFileDescriptorSet("foxglove.FrameTransform");
        Check(ftDescriptor != null && ftDescriptor.Length > 0, "32B-4: GetFileDescriptorSet('foxglove.FrameTransform') returns non-empty bytes");

        // 32B-5: RegisterAll populates the ISchemaRegistry
        protoRegistry.RegisterAll();
        Check(schemaRegistry.TryGetSchema("foxglove.FrameTransform", out var ftEntry), "32B-5: FrameTransform schema registered in ISchemaRegistry");
        Check(ftEntry.Encoding == "protobuf", "32B-5b: schema encoding is 'protobuf'");
        Check(ftEntry.RawContent != null && ftEntry.RawContent.Length > 0, "32B-5c: schema has binary RawContent");
        Check(!string.IsNullOrEmpty(ftEntry.Content), "32B-5d: schema Content is non-empty (base64)");

        // ── Batch 32C: WebSocket Protobuf Channels ──

        // Set up session with a fake transport
        var fakeTransport = new Phase32FakeTransport();
        var session = new FoxgloveSession("phase32-test", fakeTransport,
            schemaRegistry: schemaRegistry);
        session.EnableProtobuf();
        Check(session.IsProtobufEnabled, "32C-1: Protobuf is enabled on session");

        // 32C-2: Connect client and verify ServerInfo supportedEncodings includes protobuf
        fakeTransport.SimulateConnect(1);
        var serverInfoJson = fakeTransport.LastSentText;
        Check(serverInfoJson != null, "32C-2: ServerInfo was sent on connect");
        var serverInfo = JObject.Parse(serverInfoJson);
        var encodings = serverInfo["supportedEncodings"]?.ToObject<string[]>();
        Check(encodings != null && encodings.Contains("json") && encodings.Contains("protobuf"),
            "32C-2b: supportedEncodings includes both 'json' and 'protobuf'");

        // 32C-3: Register protobuf channel and verify advertise message
        session.RegisterProtobufSchemaChannel(2, "/proto_tf", "foxglove.FrameTransform");
        var advertiseJson = fakeTransport.LastBroadcastText;
        Check(advertiseJson != null, "32C-3: advertise message was broadcast");
        var adv = JObject.Parse(advertiseJson);
        var channels = adv["channels"] as JArray;
        var channel = channels?[0];
        Check(channels?.Count == 1, "32C-3b: advertise contains 1 channel");
        Check(channel?["encoding"]?.ToString() == "protobuf", "32C-3c: channel encoding is 'protobuf'");
        Check(channel?["schemaEncoding"]?.ToString() == "protobuf", "32C-3d: schemaEncoding is 'protobuf'");
        Check(channel?["schemaName"]?.ToString() == "foxglove.FrameTransform", "32C-3e: schemaName is correct");
        var schemaField = channel?["schema"]?.ToString();
        Check(!string.IsNullOrEmpty(schemaField), "32C-3f: schema field is non-empty (base64 FileDescriptorSet)");
        // Verify it's valid base64
        try
        {
            var decoded = Convert.FromBase64String(schemaField);
            Check(decoded.Length > 0, "32C-3g: schema field is valid base64 with non-empty content");
        }
        catch { Check(false, "32C-3g: schema field is valid base64"); }

        // Subscribe client to channel so Publish actually sends data
        // Simulate a client subscribing to channel 2
        var subMsg = new SubscribeMessage
        {
            Subscriptions = new List<Subscription>
            {
                new Subscription { Id = 100, ChannelId = 2 }
            }
        };
        fakeTransport.SimulateText(1, Newtonsoft.Json.JsonConvert.SerializeObject(subMsg));

        // 32C-4: Publish protobuf payload via extension method
        session.PublishProto(2, ft);
        Check(fakeTransport.LastSentBinary != null && fakeTransport.LastSentBinary.Length > 0,
            "32C-4: protobuf publish sent binary bytes");

        // 32C-5: Verify Publish via direct byte array
        var ftBytes = ft.ToByteArray();
        fakeTransport.LastSentBinary = null; // reset
        session.Publish(2, ftBytes);
        Check(fakeTransport.LastSentBinary != null && fakeTransport.LastSentBinary.Length > 0,
            "32C-5: Publish(byte[]) sends protobuf payload");

        // ── Batch 32D: MCAP Recording ──

        // 32D-1: Record protobuf channel to MCAP
        using var mcapStream = new MemoryStream();
        using var recorder = new McapRecorder(mcapStream);
        recorder.AddChannel(2, "/proto_tf", "protobuf", "foxglove.FrameTransform", "protobuf",
            ftEntry.Content); // base64 FileDescriptorSet
        recorder.WriteMessage(2, 1000, ftBytes);
        recorder.WriteMessage(2, 2000, ftBytes);
        recorder.Close();
        mcapStream.Position = 0;
        var mcapData = mcapStream.ToArray();
        Check(mcapData.Length > 0, "32D-1: MCAP file written with protobuf data");

        // 32D-2: Verify MCAP contains expected protobuf schema record
        // Search for the schema name in the MCAP binary
        var schemaNameBytes = Encoding.UTF8.GetBytes("foxglove.FrameTransform");
        var foundSchema = IndexOf(mcapData, schemaNameBytes) >= 0;
        Check(foundSchema, "32D-2: MCAP file contains 'foxglove.FrameTransform' schema name");

        // 32D-3: Verify MCAP contains encoding "protobuf"
        var encodingBytes = Encoding.UTF8.GetBytes("protobuf");
        var foundEncoding = IndexOf(mcapData, encodingBytes) >= 0;
        Check(foundEncoding, "32D-3: MCAP file contains 'protobuf' encoding string");

        // 32D-4: SchemaEntry backwards compatibility — JSON schema still works
        schemaRegistry.Register(new SchemaEntry
        {
            Name = "test.JsonOnly",
            Encoding = "jsonschema",
            Content = "{\"type\":\"object\"}",
            RawContent = null
        });
        Check(schemaRegistry.TryGetSchema("test.JsonOnly", out var jsonEntry), "32D-4: JSON schema stored alongside protobuf schemas");
        Check(jsonEntry.Content == "{\"type\":\"object\"}", "32D-4b: JSON schema Content preserved");
        Check(jsonEntry.RawContent == null, "32D-4c: JSON schema RawContent is null");

        // 32D-5: Session without protobuf enabled still advertises only json
        var noProtoTransport = new Phase32FakeTransport();
        var noProtoSession = new FoxgloveSession("no-proto", noProtoTransport, schemaRegistry: schemaRegistry);
        noProtoTransport.SimulateConnect(1);
        var noProtoInfo = JObject.Parse(noProtoTransport.LastSentText);
        var noProtoEncodings = noProtoInfo["supportedEncodings"]?.ToObject<string[]>();
        Check(noProtoEncodings != null && noProtoEncodings.Length == 1 && noProtoEncodings[0] == "json",
            "32D-5: Without EnableProtobuf(), only 'json' is advertised");

        // 32D-6: RegisterSchemaChannel with encoding param works for JSON (backward compat)
        schemaRegistry.Register(new SchemaEntry
        {
            Name = "test.CompatJson",
            Encoding = "jsonschema",
            Content = "{\"type\":\"object\"}"
        });
        noProtoSession.RegisterSchemaChannel(10, "/json_channel", "test.CompatJson", "json");
        var jsonAdv = JObject.Parse(noProtoTransport.LastBroadcastText);
        var jsonCh = (jsonAdv["channels"] as JArray)?[0];
        Check(jsonCh?["encoding"]?.ToString() == "json", "32D-6: RegisterSchemaChannel with encoding='json' sets correct encoding");

        // 32D-7: JSON and protobuf schemas with the same Foxglove name coexist.
        // Regression: proto registration must not overwrite the core JSON schema
        // used by existing JSON channels such as /tf, /scene, /unity/camera, and /unity/client_log.
        var mixedRegistry = new DefaultSchemaRegistry();
        FoxgloveSchemaDefinitions.RegisterCoreSchemas(mixedRegistry);
        ProtobufSchemaRegistryLoader.FromDefault(mixedRegistry).RegisterAll();
        var mixedTransport = new Phase32FakeTransport();
        var mixedSession = new FoxgloveSession("mixed-schema", mixedTransport, schemaRegistry: mixedRegistry);
        mixedSession.RegisterSchemaChannel(20, "/tf", FoxgloveSchemaDefinitions.FrameTransformSchemaName, "json");
        var mixedJsonAdv = JObject.Parse(mixedTransport.LastBroadcastText);
        var mixedJsonCh = (mixedJsonAdv["channels"] as JArray)?[0];
        Check(mixedJsonCh?["encoding"]?.ToString() == "json", "32D-7: mixed registry JSON channel keeps message encoding json");
        Check(mixedJsonCh?["schemaEncoding"]?.ToString() == "jsonschema", "32D-7b: mixed registry JSON channel keeps schemaEncoding jsonschema");

        mixedSession.RegisterSchemaChannel(21, "/proto_tf", "foxglove.FrameTransform", "protobuf");
        var mixedProtoAdv = JObject.Parse(mixedTransport.LastBroadcastText);
        var mixedProtoCh = (mixedProtoAdv["channels"] as JArray)?[0];
        Check(mixedProtoCh?["encoding"]?.ToString() == "protobuf", "32D-7c: mixed registry protobuf channel uses message encoding protobuf");
        Check(mixedProtoCh?["schemaEncoding"]?.ToString() == "protobuf", "32D-7d: mixed registry protobuf channel uses schemaEncoding protobuf");

        // 32D-8: Two separate registries both get protobuf schemas (no global-static flag leak)
        var registryA = new DefaultSchemaRegistry();
        ProtobufSchemasSetup.RegisterSchemas(registryA);
        Check(registryA.TryGetSchema("foxglove.FrameTransform", out _),
            "32D-8: registry A has foxglove.FrameTransform");
        var registryB = new DefaultSchemaRegistry();
        ProtobufSchemasSetup.RegisterSchemas(registryB);
        Check(registryB.TryGetSchema("foxglove.FrameTransform", out _),
            "32D-8b: registry B also has foxglove.FrameTransform (no global flag leak)");

        // -- Batch 32G: Inspector encoding policy --

        // 32G-1: Manager lock wins over a publisher override.
        var managerLocked = PublisherEncodingPolicy.Resolve(
            GlobalEncoding.Json,
            allowPublisherOverride: false,
            PublisherEncodingOverride.Protobuf,
            supportsJson: true,
            supportsProtobuf: true);
        Check(managerLocked.Effective == PublisherEffectiveEncoding.Json && !managerLocked.FellBack,
            "32G-1: Manager lock ignores publisher override");

        // 32G-2: JSON-only publishers fall back cleanly when the manager asks for protobuf.
        var jsonFallback = PublisherEncodingPolicy.Resolve(
            GlobalEncoding.Protobuf,
            allowPublisherOverride: true,
            PublisherEncodingOverride.UseManager,
            supportsJson: true,
            supportsProtobuf: false);
        Check(jsonFallback.Effective == PublisherEffectiveEncoding.Json && jsonFallback.FellBack,
            "32G-2: JSON-only publisher falls back from protobuf to json");

        // 32G-3: Dual-format publishers use protobuf when the manager asks for protobuf.
        var dualProtobuf = PublisherEncodingPolicy.Resolve(
            GlobalEncoding.Protobuf,
            allowPublisherOverride: true,
            PublisherEncodingOverride.UseManager,
            supportsJson: true,
            supportsProtobuf: true);
        Check(dualProtobuf.Effective == PublisherEffectiveEncoding.Protobuf && !dualProtobuf.FellBack,
            "32G-3: Dual-format publisher honors manager protobuf default");

        // 32G-4: Protobuf-only publishers still publish protobuf under the JSON default, with a fallback signal.
        var protobufFallback = PublisherEncodingPolicy.Resolve(
            GlobalEncoding.Json,
            allowPublisherOverride: true,
            PublisherEncodingOverride.UseManager,
            supportsJson: false,
            supportsProtobuf: true);
        Check(protobufFallback.Effective == PublisherEffectiveEncoding.Protobuf && protobufFallback.FellBack,
            "32G-4: Protobuf-only publisher falls back from json to protobuf");

        // 32G-5: A publisher that declares no supported encodings is explicit, not silently JSON.
        var unsupported = PublisherEncodingPolicy.Resolve(
            GlobalEncoding.Json,
            allowPublisherOverride: true,
            PublisherEncodingOverride.UseManager,
            supportsJson: false,
            supportsProtobuf: false);
        Check(unsupported.Effective == PublisherEffectiveEncoding.Unsupported && unsupported.FellBack,
            "32G-5: Publisher with no supported encodings resolves to Unsupported");

        var managerSourcePath = Path.Combine(
            Unity.FoxgloveSDK.Tests.Phase16Validation.FindRepoRoot(),
            "Packages",
            "dev.unity2foxglove.sdk",
            "Runtime",
            "Unity",
            "FoxgloveManager.cs");
        var managerSource = File.ReadAllText(managerSourcePath);
        Check(managerSource.Contains("_defaultPublisherEncoding = GlobalEncoding.Protobuf"),
            "32G-6: new FoxgloveManager defaults publisher encoding to protobuf");

        var publisherBaseSourcePath = Path.Combine(
            Unity.FoxgloveSDK.Tests.Phase16Validation.FindRepoRoot(),
            "Packages",
            "dev.unity2foxglove.sdk",
            "Runtime",
            "Unity",
            "FoxglovePublisherBase.cs");
        var publisherBaseSource = File.ReadAllText(publisherBaseSourcePath);
        Check(publisherBaseSource.Contains("_manager != null ? _manager.DefaultPublisherEncoding : GlobalEncoding.Protobuf"),
            "32G-7: publisher base unresolved-manager fallback matches protobuf default");

        Console.WriteLine($"\nPhase 32: {passed} passed, {failed} failed.");
        if (failed > 0)
            throw new Exception($"Phase 32: {failed} test(s) failed.");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { found = false; break; }
            }
            if (found) return i;
        }
        return -1;
    }

    /// <summary>
    /// Minimal fake transport that captures sent text and binary frames
    /// for assertion without starting a real server.
    /// </summary>
    private class Phase32FakeTransport : IFoxgloveTransport
    {
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public bool IsRunning => true;
        public string LastSentText;
        public string LastBroadcastText;
        public byte[] LastSentBinary;

        public void Start(string host, int port) { }
        public void Stop() { }
        public void SendText(uint clientId, string json) => LastSentText = json;
        public void BroadcastText(string json) => LastBroadcastText = json;
        public void SendBinary(uint clientId, byte[] data) => LastSentBinary = data;
        public void BroadcastBinary(byte[] data) { }
        public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
        public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        public void Disconnect(uint clientId) { }
        public void Dispose() { }
    }
}
