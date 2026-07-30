// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 168 validation for MessagePack raw channel encoding support.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Tests
{
    public static class MessagePackRawChannelEncodingValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 168 Tests ---");
            _passCount = 0;

            VerifyEncodingPolicy();
            VerifyMsgPackWriter();
            VerifyMcapSchemalessMsgPackChannel();
            VerifyAdvertiseShape();
            VerifyManagerAndPublisherSourceShape();
            VerifyCompileSurfaces();
            VerifyUnitySmokeLiveCompatibilityGuard();
            VerifyLiveProbeScript();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 168: " + _passCount + " checks passed.\n");
        }

        private static void VerifyEncodingPolicy()
        {
            Check((int)GlobalEncoding.Json == 0
                  && (int)GlobalEncoding.Protobuf == 1
                  && (int)GlobalEncoding.MsgPack == 3,
                "168-1: GlobalEncoding preserves the shipped JSON, Protobuf, and MsgPack values");

            Check((int)PublisherEncodingOverride.UseManager == 0
                  && (int)PublisherEncodingOverride.Json == 1
                  && (int)PublisherEncodingOverride.Protobuf == 2
                  && (int)PublisherEncodingOverride.MsgPack == 4,
                "168-2: PublisherEncodingOverride preserves the shipped neutral values");

            Check((int)PublisherEffectiveEncoding.Json == 0
                  && (int)PublisherEffectiveEncoding.Protobuf == 1
                  && (int)PublisherEffectiveEncoding.Unsupported == 2
                  && (int)PublisherEffectiveEncoding.MsgPack == 4,
                "168-3: PublisherEffectiveEncoding keeps Unsupported and MsgPack stable");

            Check(PublisherEncodingPolicy.ToDisplayEncoding(PublisherEffectiveEncoding.MsgPack) == "MsgPack"
                  && PublisherEncodingPolicy.ToProtocolEncoding(PublisherEffectiveEncoding.MsgPack) == "msgpack"
                  && PublisherEncodingPolicy.ToSchemaEncoding(PublisherEffectiveEncoding.MsgPack) == "",
                "168-4: MsgPack labels use schemaless message encoding");

            var managerDefault = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.MsgPack,
                allowPublisherOverride: false,
                PublisherEncodingOverride.Json,
                supportsJson: true,
                supportsProtobuf: true,
                supportsMsgPack: true);
            Check(managerDefault.Requested == PublisherEffectiveEncoding.MsgPack
                  && managerDefault.Effective == PublisherEffectiveEncoding.MsgPack
                  && !managerDefault.FellBack,
                "168-5: manager default MsgPack resolves when supported");

            var fallback = PublisherEncodingPolicy.Resolve(
                GlobalEncoding.Protobuf,
                allowPublisherOverride: false,
                PublisherEncodingOverride.UseManager,
                supportsJson: true,
                supportsProtobuf: false,
                supportsMsgPack: true);
            Check(fallback.Requested == PublisherEffectiveEncoding.Protobuf
                  && fallback.Effective == PublisherEffectiveEncoding.MsgPack
                  && fallback.FellBack,
                "168-6: fallback preference chooses MsgPack before JSON when protobuf is unavailable");
        }

        private static void VerifyMsgPackWriter()
        {
            var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(2);
            writer.WriteString("ok");
            writer.WriteBool(true);
            writer.WriteString("value");
            writer.WriteInt32(42);

            var expected = new byte[]
            {
                0x82,
                0xa2, 0x6f, 0x6b,
                0xc3,
                0xa5, 0x76, 0x61, 0x6c, 0x75, 0x65,
                0x2a
            };

            Check(expected.SequenceEqual(writer.ToArray()),
                "168-7: MsgPack writer emits canonical small map payload bytes");
            var buffer = writer.GetBuffer(out var length);
            Check(length == expected.Length && expected.SequenceEqual(buffer.Take(length)),
                "168-7b: MsgPack writer exposes a valid owned buffer segment for zero-copy callers");
        }

        private static void VerifyMcapSchemalessMsgPackChannel()
        {
            var payload = new byte[] { 0x81, 0xa1, 0x78, 0x2a };

            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/custom/msgpack", "msgpack", "", "", "");
                recorder.WriteMessage(1, 1000, payload);
                recorder.Close();
            }

            ms.Position = 0;
            // McapReader does not own or dispose the caller-provided stream.
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Check(summary.Schemas.Count == 0
                  && summary.Channels.Count == 1
                  && summary.Channels[0].SchemaId == 0
                  && summary.Channels[0].MessageEncoding == "msgpack",
                "168-8: MCAP records MsgPack as a schemaless message channel");

            var chunk = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunk.ChunkStartOffset, chunk.ChunkLength, out var crcValid);
            var messages = reader.ReadChunkMessages(records);
            Check(crcValid
                  && messages.Count == 1
                  && messages[0].Data.SequenceEqual(payload),
                "168-9: MCAP MsgPack payload bytes roundtrip unchanged");
        }

        private static void VerifyAdvertiseShape()
        {
            var advertise = new Advertise();
            advertise.Channels.Add(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/custom/msgpack",
                Encoding = "msgpack",
                SchemaName = "",
                Schema = ""
            });

            var json = JsonConvert.SerializeObject(advertise);
            Check(json.Contains("\"encoding\":\"msgpack\"", StringComparison.Ordinal)
                  && json.Contains("\"schemaName\":\"\"", StringComparison.Ordinal)
                  && json.Contains("\"schema\":\"\"", StringComparison.Ordinal)
                  && !json.Contains("schemaEncoding", StringComparison.Ordinal),
                "168-10: custom-client MsgPack advertise shape omits schemaEncoding");
        }

        private static void VerifyManagerAndPublisherSourceShape()
        {
            var managerPublishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var managerChannels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var publisherGeneric = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisher.cs");
            var publisherEncoding = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherEncoding.cs");
            var msgPackChannel = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MsgPack/FoxgloveMsgPackChannel.cs");
            var editorLabels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/PublisherEncodingEditorLabels.cs");

            var publishMsgPack = PhaseValidationSourceHelpers.SourceMethod(managerPublishing, "public void PublishMsgPack");
            Check(managerPublishing.Contains("private const string MsgPackEncoding = \"msgpack\"", StringComparison.Ordinal)
                  && managerPublishing.Contains("TryPrepareMsgPackPublish", StringComparison.Ordinal)
                  && publishMsgPack.Contains("GetOrRegisterChannel(topic, MsgPackEncoding)", StringComparison.Ordinal)
                  && publishMsgPack.Contains("_runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs)", StringComparison.Ordinal)
                  && publishMsgPack.Contains("RecordPublishCadence(topic, MsgPackEncoding)", StringComparison.Ordinal)
                  && !publishMsgPack.Contains("GetOrRegisterSchemaChannel", StringComparison.Ordinal),
                "168-11: manager publishes MsgPack through a schemaless raw channel");

            Check(managerChannels.Contains("public FoxgloveMsgPackChannel CreateMsgPackChannel(string topic)", StringComparison.Ordinal)
                  && managerChannels.Contains("PublishMsgPackChannel", StringComparison.Ordinal)
                  && managerChannels.Contains("RecordPublishCadence(topic, MsgPackEncoding)", StringComparison.Ordinal)
                  && msgPackChannel.Contains("public sealed class FoxgloveMsgPackChannel", StringComparison.Ordinal)
                  && msgPackChannel.Contains("public void Log(FoxgloveMsgPackWriter writer", StringComparison.Ordinal),
                "168-12: SDK-style MsgPack channel facade wraps byte and writer payloads");

            Check(publisherBase.Contains("public virtual bool SupportsMsgPackEncoding => false", StringComparison.Ordinal)
                  && publisherBase.Contains("protected void PublishMsgPack(byte[] payload", StringComparison.Ordinal)
                  && publisherBase.Contains("_manager.TryPrepareMsgPackPublish(_topic", StringComparison.Ordinal)
                  && publisherBase.Contains("SupportsMsgPackEncoding", StringComparison.Ordinal)
                  && publisherBase.Contains("MsgPack", StringComparison.Ordinal),
                "168-13: publisher base exposes opt-in MsgPack support without enabling it by default");

            Check(publisherGeneric.Contains("protected virtual byte[] CreateMsgPackPayload(TMessage message) => null", StringComparison.Ordinal)
                  && publisherGeneric.Contains("resolution.Effective == PublisherEffectiveEncoding.MsgPack", StringComparison.Ordinal)
                  && publisherGeneric.Contains("PublishMsgPack(payload, unixNs, resolution)", StringComparison.Ordinal),
                "168-14: generic publishers branch for MsgPack and do not silently publish JSON");

            Check(editorLabels.Contains("\"MsgPack\"", StringComparison.Ordinal)
                  && editorLabels.Contains("schemaless raw channel for custom clients", StringComparison.Ordinal)
                  && editorLabels.Contains("Foxglove Desktop does not currently parse or render live MsgPack panels", StringComparison.Ordinal),
                "168-15: Inspector encoding labels expose MsgPack with custom-client expectations");
            Check(editorLabels.Contains("AssertLabelCount<GlobalEncoding>", StringComparison.Ordinal)
                  && editorLabels.Contains("AssertLabelCount<PublisherEncodingOverride>", StringComparison.Ordinal)
                  && editorLabels.Contains("AssertLabelCount<Ros2BridgeOutputOverride>", StringComparison.Ordinal),
                "168-15b: Inspector encoding label arrays assert enum cardinality");

            Check(publisherEncoding.Contains("Fallback order intentionally keeps MsgPack before JSON", StringComparison.Ordinal),
                "168-15B: encoding fallback order documents the MsgPack before JSON choice");

            var manifestModel = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestModel.cs");
            var manifestWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestJsonWriter.cs");
            var publisherCatalog = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/FoxgloveSdkPublisherCatalog.cs");
            Check(manifestModel.Contains("public bool SupportsMsgPack", StringComparison.Ordinal)
                  && manifestWriter.Contains("\"supportsMsgPack\"", StringComparison.Ordinal)
                  && publisherCatalog.Contains("supportsMsgPack: false", StringComparison.Ordinal),
                "168-16: schema manifest can represent MsgPack support without enabling built-in publishers");
        }

        private static void VerifyCompileSurfaces()
        {
            var runtimeProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var testSurface = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");

            Check(runtimeProject.Contains("../../Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs", StringComparison.Ordinal)
                  && testSurface.Contains("Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs", StringComparison.Ordinal),
                "168-17: .NET validation surfaces compile the pure MsgPack writer");
        }

        private static void VerifyUnitySmokeLiveCompatibilityGuard()
        {
            var smoke = ReadRepoText("Unity2Foxglove/Assets/Scripts/Smoke/Phase168MsgPackSmoke.cs");

            Check(smoke.Contains("private bool _publishContinuously = false", StringComparison.Ordinal)
                  && smoke.Contains("_allowUnsupportedLiveWebSocketPublish", StringComparison.Ordinal)
                  && smoke.Contains("Foxglove Desktop does not currently parse MsgPack live WebSocket channels", StringComparison.Ordinal)
                  && smoke.Contains("Enable unsupported live WebSocket publish", StringComparison.Ordinal),
                "168-18: Unity smoke keeps unsupported live MsgPack publish opt-in");
        }

        private static void VerifyLiveProbeScript()
        {
            var probe = ReadRepoText("Scripts/smoke/websocket/phase168_msgpack_live_probe.py");

            Check(probe.Contains("DEFAULT_TOPIC = \"/phase168/msgpack_smoke\"", StringComparison.Ordinal)
                  && probe.Contains("EXPECTED_ENCODING = \"msgpack\"", StringComparison.Ordinal)
                  && probe.Contains("decode_msgpack_value", StringComparison.Ordinal)
                  && probe.Contains("phase == 168", StringComparison.Ordinal)
                  && probe.Contains("--self-test", StringComparison.Ordinal),
                "168-19: live WebSocket probe validates advertised MsgPack payload bytes");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase168"),
                "168-20: validation registry exposes the MsgPack encoding support flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
