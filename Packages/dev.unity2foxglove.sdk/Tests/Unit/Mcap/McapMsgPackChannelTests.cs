// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP MessagePack channel compatibility coverage.

using System;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "168")]
    [Trait("Domain", "Mcap")]
    public class McapMsgPackChannelTests
    {
        [Fact]
        [Trait("Phase", "185-B")]
        public void RecordingOnlyMessagePackExactDescriptorReuseStaysSchemaless()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = NewSession("phase185-msgpack-exact");
            var descriptor = RawMessagePackChannel(41, "/phase185/exact");
            var payload = new byte[] { 0x81, 0xa1, 0x76, 0x01 };
            session.SetRecorder(recorder);

            session.RegisterRecordingOnlyChannel(descriptor);
            session.RegisterRecordingOnlyChannel(RawMessagePackChannel(41, "/phase185/exact"));

            Assert.True(session.HasRecordingDemand(41));
            session.Publish(41, payload, 185_041UL);
            session.SetRecorder(null);
            recorder.Close();

            var result = ReadStreaming(stream);
            Assert.Empty(result.Summary.Schemas);
            var channel = Assert.Single(result.Summary.Channels);
            Assert.Equal((ushort)0, channel.SchemaId);
            Assert.Equal("msgpack", channel.MessageEncoding);
            Assert.Equal(payload, Assert.Single(result.Messages).Data);
        }

        [Fact]
        [Trait("Phase", "185-B")]
        public void RecordingOnlySameTopicIncompatibleDescriptorCannotAliasAcceptedMessagePack()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = NewSession("phase185-msgpack-conflict");
            session.SetRecorder(recorder);

            session.RegisterRecordingOnlyChannel(RawMessagePackChannel(42, "/phase185/conflict"));
            session.RegisterRecordingOnlyChannel(new AdvertiseChannel
            {
                Id = 43,
                Topic = "/phase185/conflict",
                Encoding = "json",
                SchemaName = "",
                SchemaEncoding = "",
                Schema = ""
            });

            Assert.True(session.HasRecordingDemand(42));
            Assert.False(session.HasRecordingDemand(43));
            session.Publish(42, new byte[] { 0x01 }, 185_042UL);
            session.Publish(43, new byte[] { 0x02 }, 185_043UL);
            session.SetRecorder(null);
            recorder.Close();

            var result = ReadStreaming(stream);
            var channel = Assert.Single(result.Summary.Channels);
            Assert.Equal("msgpack", channel.MessageEncoding);
            Assert.Equal(new byte[] { 0x01 }, Assert.Single(result.Messages).Data);
        }

        [Fact]
        [Trait("Phase", "185-B")]
        public void RecordingOnlyMessagePackReassertionSeedsReplacementRecorder()
        {
            using var firstStream = new MemoryStream();
            using var secondStream = new MemoryStream();
            using var firstRecorder = new McapRecorder(firstStream);
            using var secondRecorder = new McapRecorder(secondStream);
            using var session = NewSession("phase185-msgpack-replacement");
            var descriptor = RawMessagePackChannel(44, "/phase185/replacement");
            var firstPayload = new byte[] { 0x91, 0x01 };
            var secondPayload = new byte[] { 0x91, 0x02 };

            session.SetRecorder(firstRecorder);
            session.RegisterRecordingOnlyChannel(descriptor);
            session.Publish(44, firstPayload, 185_044UL);
            session.SetRecorder(null);
            firstRecorder.Close();

            session.SetRecorder(secondRecorder);
            session.RegisterRecordingOnlyChannel(descriptor);
            Assert.True(session.HasRecordingDemand(44));
            session.Publish(44, secondPayload, 185_045UL);
            session.SetRecorder(null);
            secondRecorder.Close();

            Assert.Equal(firstPayload, Assert.Single(ReadStreaming(firstStream).Messages).Data);
            Assert.Equal(secondPayload, Assert.Single(ReadStreaming(secondStream).Messages).Data);
        }

        [Fact]
        [Trait("Phase", "185-B")]
        public void RawMessagePackRecordingCacheUsesFullWireDescriptorIdentity()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "u2f-phase185-manager-" + Guid.NewGuid().ToString("N") + ".mcap");
            var runtime = new FoxgloveRuntime(
                new NoopTransport(),
                new SystemClock(),
                new DefaultSchemaRegistry());
            var cache = new FoxRunRawRecordingChannelCache();
            uint nextChannelId = 1_000;
            var messagePackPayload = new byte[] { 0x81, 0xa1, 0x76, 0x01 };
            var schemaPayload = new byte[] { 0x7b, 0x7d };
            const string schema = "{\"type\":\"object\"}";
            var messagePackDescriptor = new FoxRunRawRecordingChannelDescriptor(
                "/phase185/manager",
                "msgpack",
                "",
                "",
                "");

            try
            {
                runtime.EnableRecording(path);
                runtime.Start("phase185-manager-recording", port: 0);

                var messagePackChannel = cache.GetOrAdd(
                    messagePackDescriptor,
                    () => nextChannelId++);
                runtime.RegisterRecordingOnlyChannel(
                    messagePackDescriptor.ToChannel(messagePackChannel));
                var reusedMessagePackChannel = cache.GetOrAdd(
                    new FoxRunRawRecordingChannelDescriptor(
                        "/phase185/manager",
                        "msgpack",
                        "",
                        "",
                        ""),
                    () => nextChannelId++);
                Assert.Equal(messagePackChannel, reusedMessagePackChannel);

                var conflictingDescriptor =
                    new FoxRunRawRecordingChannelDescriptor(
                    "/phase185/manager",
                    "json",
                    "",
                    "",
                    "");
                var conflictingChannel = cache.GetOrAdd(
                    conflictingDescriptor,
                    () => nextChannelId++);
                runtime.RegisterRecordingOnlyChannel(
                    conflictingDescriptor.ToChannel(conflictingChannel));
                Assert.NotEqual(messagePackChannel, conflictingChannel);
                Assert.False(runtime.HasRecordingDemand(conflictingChannel));

                const uint schemaChannel = 2_000;
                runtime.RegisterRecordingOnlyChannel(new AdvertiseChannel
                {
                    Id = schemaChannel,
                    Topic = "/phase185/schema-backed",
                    Encoding = "json",
                    SchemaName = "phase185.SchemaBacked",
                    SchemaEncoding = "jsonschema",
                    Schema = schema
                });
                Assert.True(runtime.HasRecordingDemand(messagePackChannel));
                Assert.True(runtime.HasRecordingDemand(schemaChannel));

                Assert.True(runtime.PublishRecordingOnly(
                    messagePackChannel,
                    messagePackPayload,
                    185_100UL));
                Assert.True(runtime.PublishRecordingOnly(
                    schemaChannel,
                    schemaPayload,
                    185_101UL));
            }
            finally
            {
                runtime.Dispose();
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new McapStreamingReader(stream);
                var result = reader.Read();
                var messagePack = Assert.Single(
                    result.Summary.Channels,
                    channel => channel.Topic == "/phase185/manager");
                var schemaBacked = Assert.Single(
                    result.Summary.Channels,
                    channel => channel.Topic == "/phase185/schema-backed");
                Assert.Equal("msgpack", messagePack.MessageEncoding);
                Assert.Equal((ushort)0, messagePack.SchemaId);
                Assert.NotEqual((ushort)0, schemaBacked.SchemaId);
                var recordedSchema = Assert.Single(result.Summary.Schemas);
                Assert.Equal("phase185.SchemaBacked", recordedSchema.Name);
                Assert.Equal("jsonschema", recordedSchema.Encoding);
                Assert.Equal(
                    schema,
                    System.Text.Encoding.UTF8.GetString(recordedSchema.Data));
                Assert.Equal(
                    messagePackPayload,
                    Assert.Single(
                        result.Messages,
                        message => message.ChannelId == messagePack.Id).Data);
                Assert.Equal(
                    schemaPayload,
                    Assert.Single(
                        result.Messages,
                        message => message.ChannelId == schemaBacked.Id).Data);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MsgPackChannelRecordsWithoutSchema()
        {
            var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("value");
            writer.WriteInt32(42);
            var payload = writer.ToArray();

            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/custom/msgpack", "msgpack", "", "", "");
                recorder.WriteMessage(1, 1000, payload);
                recorder.Close();
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.Empty(summary.Schemas);
            Assert.Single(summary.Channels);
            Assert.Equal((ushort)0, summary.Channels[0].SchemaId);
            Assert.Equal("/custom/msgpack", summary.Channels[0].Topic);
            Assert.Equal("msgpack", summary.Channels[0].MessageEncoding);

            var chunk = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunk.ChunkStartOffset, chunk.ChunkLength, out var crcValid);
            var messages = reader.ReadChunkMessages(records);

            Assert.True(crcValid);
            Assert.Single(messages);
            Assert.Equal(payload, messages[0].Data);
        }

        private static FoxgloveSession NewSession(string name)
            => new FoxgloveSession(
                name,
                new NoopTransport(),
                schemaRegistry: new DefaultSchemaRegistry());

        private static AdvertiseChannel RawMessagePackChannel(uint id, string topic)
            => new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "msgpack",
                SchemaName = "",
                SchemaEncoding = "",
                Schema = ""
            };

        private static McapStreamingReadResult ReadStreaming(MemoryStream stream)
        {
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            return reader.Read();
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
        }
    }
}
