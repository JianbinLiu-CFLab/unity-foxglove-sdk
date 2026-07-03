// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP MessagePack channel compatibility coverage.

using System.IO;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "168")]
    [Trait("Domain", "Mcap")]
    public class McapMsgPackChannelTests
    {
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
    }
}
