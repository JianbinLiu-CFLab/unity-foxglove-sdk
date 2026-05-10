// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 44 validation for complete official protobuf schema coverage:
// catalog/registry parity, sample construction, protobuf publish, and MCAP roundtrip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;
using Unity.FoxgloveSDK.Transport;

public static class Phase44Validation
{
    public static void Validate()
    {
        Console.WriteLine("=== Phase 44: Official Schema Coverage ===");
        var passed = 0;
        var failed = 0;

        void Check(bool condition, string message)
        {
            if (condition) { Console.WriteLine($"[PASS] {message}"); passed++; }
            else { Console.WriteLine($"[FAIL] {message}"); failed++; }
        }

        var registryBacking = new DefaultSchemaRegistry();
        var protoRegistry = ProtobufSchemaRegistryLoader.FromDefault(registryBacking);
        var catalog = FoxgloveProtoSchemaCatalog.Entries;
        var registryNames = new HashSet<string>(protoRegistry.SchemaNames.Where(IsOfficialFoxgloveSchemaName));
        var catalogNames = new HashSet<string>(catalog.Select(e => e.SchemaName));

        Check(catalog.Count == registryNames.Count, $"44A-1: catalog count matches official foxglove registry count ({catalog.Count})");
        Check(catalogNames.SetEquals(registryNames), "44A-2: catalog names exactly match protobuf registry names");
        Check(catalog.All(e => typeof(IMessage).IsAssignableFrom(e.ClrType)), "44A-3: every catalog CLR type implements IMessage");
        Check(catalog.All(e => DescriptorFor(e).FullName == e.SchemaName), "44A-4: catalog CLR descriptors match schema names");
        Check(catalog.All(e => protoRegistry.GetFileDescriptorSet(e.SchemaName)?.Length > 0), "44A-5: every catalog schema has non-empty descriptor bytes");
        Check(catalog.All(e => DescriptorSetContains(protoRegistry.GetFileDescriptorSet(e.SchemaName), e.SchemaName)), "44A-6: descriptor subsets contain their target schema");

        ProtobufSchemasSetup.RegisterSchemas(registryBacking);
        Check(catalog.All(e => registryBacking.TryGetSchema(e.SchemaName, "protobuf", out var entry)
            && entry.RawContent != null
            && entry.RawContent.Length > 0),
            "44A-7: setup registers every catalog schema with protobuf RawContent");

        var samples = FoxgloveProtoSampleFactory.CreateAll();
        Check(samples.Count == catalog.Count, "44B-1: sample factory returns one sample per catalog schema");
        Check(samples.Select(s => s.SchemaName).ToHashSet().SetEquals(catalogNames), "44B-2: sample schemas match catalog names");
        Check(samples.All(s => s.Message != null), "44B-3: every sample is a protobuf message");
        Check(samples.All(s => s.Message.Descriptor.FullName == s.SchemaName), "44B-4: sample descriptor names match schema names");

        var roundTripOk = true;
        foreach (var sample in samples)
        {
            var bytes = sample.Message.ToByteArray();
            if (bytes.Length == 0)
            {
                roundTripOk = false;
                break;
            }
            var reparsed = FoxgloveProtoSampleFactory.Parse(sample.CatalogEntry, bytes);
            if (reparsed.Descriptor.FullName != sample.SchemaName || reparsed.ToByteArray().Length == 0)
            {
                roundTripOk = false;
                break;
            }
        }
        Check(roundTripOk, "44B-5: every sample serializes and parses through its generated parser");

        ValidatePublishCoverage(samples, registryBacking, Check);
        ValidateMcapCoverage(samples, registryBacking, Check);

        Console.WriteLine($"\nPhase 44: {passed} passed, {failed} failed.");
        if (failed > 0)
            throw new Exception($"Phase 44: {failed} test(s) failed.");
    }

    public static void GenerateAllSchemasMcap(string outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var schemaRegistry = new DefaultSchemaRegistry();
        ProtobufSchemasSetup.RegisterSchemas(schemaRegistry);
        var samples = FoxgloveProtoSampleFactory.CreateAll();

        using var fs = File.Create(outputPath);
        using var recorder = new McapRecorder(fs);
        WriteAllSamplesToRecorder(recorder, schemaRegistry, samples);
        recorder.Close();
    }

    private static void ValidatePublishCoverage(
        IReadOnlyList<FoxgloveProtoSample> samples,
        DefaultSchemaRegistry schemaRegistry,
        Action<bool, string> check)
    {
        var transport = new Phase44FakeTransport();
        var session = new FoxgloveSession("phase44-publish", transport, schemaRegistry: schemaRegistry);
        session.EnableProtobuf();
        transport.SimulateConnect(1);

        var subscriptions = new List<Subscription>();
        uint channelId = 1;
        uint subscriptionId = 1000;
        foreach (var sample in samples)
        {
            session.RegisterProtobufSchemaChannel(channelId, $"/phase44/{sample.SchemaName}", sample.SchemaName);
            var adv = JObject.Parse(transport.LastBroadcastText);
            var channel = (adv["channels"] as JArray)?[0];
            if (channel?["schemaName"]?.ToString() != sample.SchemaName
                || channel?["encoding"]?.ToString() != "protobuf"
                || channel?["schemaEncoding"]?.ToString() != "protobuf")
            {
                check(false, $"44C-1: advertise uses protobuf for {sample.SchemaName}");
                return;
            }
            subscriptions.Add(new Subscription { Id = subscriptionId++, ChannelId = channelId++ });
        }

        transport.SimulateText(1, JsonConvert.SerializeObject(new SubscribeMessage { Subscriptions = subscriptions }));

        channelId = 1;
        foreach (var sample in samples)
        {
            session.PublishProto(channelId++, sample.Message, 1_000_000_000UL + channelId);
        }

        check(transport.SentBinaryFrames.Count == samples.Count, "44C-1: protobuf publish emits one binary frame per schema");
        check(transport.SentBinaryFrames.All(frame => frame != null && frame.Length > 0), "44C-2: every protobuf publish frame is non-empty");
    }

    private static void ValidateMcapCoverage(
        IReadOnlyList<FoxgloveProtoSample> samples,
        DefaultSchemaRegistry schemaRegistry,
        Action<bool, string> check)
    {
        using var ms = new MemoryStream();
        using (var recorder = new McapRecorder(ms))
        {
            WriteAllSamplesToRecorder(recorder, schemaRegistry, samples);
            recorder.Close();
        }

        ms.Position = 0;
        var reader = new McapReader(ms);
        var summary = reader.ReadSummary();
        check(summary.Schemas.Count == samples.Count, "44C-3: MCAP summary contains one schema per catalog entry");
        check(summary.Channels.Count == samples.Count, "44C-4: MCAP summary contains one channel per catalog entry");
        check(summary.Statistics != null && summary.Statistics.MessageCount == (ulong)samples.Count, "44C-5: MCAP statistics message count matches catalog");
        check(summary.Schemas.All(s => s.Encoding == "protobuf" && s.Data != null && s.Data.Length > 0), "44C-6: all MCAP schemas use protobuf encoding with data");
        check(summary.Channels.All(c => c.MessageEncoding == "protobuf"), "44C-7: all MCAP channels use protobuf message encoding");
    }

    private static void WriteAllSamplesToRecorder(McapRecorder recorder, DefaultSchemaRegistry schemaRegistry, IReadOnlyList<FoxgloveProtoSample> samples)
    {
        uint channelId = 1;
        foreach (var sample in samples)
        {
            if (!schemaRegistry.TryGetSchema(sample.SchemaName, "protobuf", out var schema))
                throw new InvalidOperationException($"Schema not registered: {sample.SchemaName}");

            recorder.AddChannel(channelId, $"/phase44/{sample.SchemaName}", "protobuf", sample.SchemaName, "protobuf", schema.Content);
            recorder.WriteMessage(channelId, 1_000_000_000UL + channelId, sample.Message.ToByteArray());
            channelId++;
        }
    }

    private static MessageDescriptor DescriptorFor(FoxgloveProtoSchemaCatalogEntry entry)
    {
        return (MessageDescriptor)entry.ClrType.GetProperty("Descriptor")?.GetValue(null);
    }

    private static bool DescriptorSetContains(byte[] descriptorSetBytes, string schemaName)
    {
        if (descriptorSetBytes == null || descriptorSetBytes.Length == 0) return false;
        var fds = FileDescriptorSet.Parser.ParseFrom(descriptorSetBytes);
        foreach (var file in fds.File)
        {
            foreach (var message in file.MessageType)
            {
                if ($"{file.Package}.{message.Name}" == schemaName)
                    return true;
            }
        }
        return false;
    }

    private static bool IsOfficialFoxgloveSchemaName(string schemaName)
    {
        return schemaName != null && schemaName.StartsWith("foxglove.", StringComparison.Ordinal);
    }

    private sealed class Phase44FakeTransport : IFoxgloveTransport
    {
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public bool IsRunning => true;
        public string LastSentText;
        public string LastBroadcastText;
        public readonly List<byte[]> SentBinaryFrames = new();

        public void Start(string host, int port) { }
        public void Stop() { }
        public void SendText(uint clientId, string json) => LastSentText = json;
        public void BroadcastText(string json) => LastBroadcastText = json;
        public void SendBinary(uint clientId, byte[] data) => SentBinaryFrames.Add(data);
        public void BroadcastBinary(byte[] data) { }
        public void Disconnect(uint clientId) { }
        public void Dispose() { }
        public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
        public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
    }
}
