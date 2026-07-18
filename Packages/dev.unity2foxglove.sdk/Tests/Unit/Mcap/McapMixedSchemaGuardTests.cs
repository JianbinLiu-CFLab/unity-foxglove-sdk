// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP mixed-schema guards and encoding normalization (migrated from Phase24DValidation).

using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP recorder mixed-schema guards, client publish schema dedup,
    /// encoding normalization (empty == json), and duplicate topic rejection.
    /// Ported from Phase24DValidation.
    /// </summary>
    [Trait("Phase", "24D")]
    [Trait("Domain", "Mcap")]
    public class McapMixedSchemaGuardTests
    {
        [Fact]
        public void ServerChannelWithSchemaThenClientWithoutSchemaIsSkipped()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);
            recorder.AddChannel(1, "/unity/camera", "json", "foxglove.CompressedImage", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1, 2, 3 });
            recorder.WriteMessage(1, 100, new byte[] { 4, 5, 6 });

            recorder.WriteClientMessage(99, 42, 200, new byte[] { 7, 8, 9 }, "/unity/camera",
                enc: "json", sName: "", sEnc: "", sContent: "");

            recorder.Close();

            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "Mixed schema: only 1 channel recorded (second was skipped)");
            Assert.True(summary.Channels[0].Topic == "/unity/camera",
                "Mixed schema: recorded channel is /unity/camera");
            Assert.True(summary.Channels[0].SchemaId != 0,
                "Mixed schema: channel keeps non-zero schema id");
        }

        [Fact]
        public void NewSchemalessClientTopicIsRecorded()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.WriteClientMessage(1, 10, 500, new byte[] { 1 }, "/move_base_simple/goal",
                enc: "json", sName: "", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "New schemaless client: 1 channel recorded");
            Assert.True(summary.Channels[0].Topic == "/move_base_simple/goal",
                "New schemaless client: channel topic correct");
        }

        [Fact]
        public void ClientWithMatchingSchemaIsRecorded()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/tf", "json", "foxglove.FrameTransform", "jsonschema", "schema_v1");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            recorder.WriteClientMessage(2, 20, 100, new byte[] { 2, 3 }, "/tf",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "schema_v1");

            recorder.WriteClientMessage(2, 20, 200, new byte[] { 4, 5 }, "/tf",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "schema_v1");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 2,
                "Matching schema: 2 channels (server + client)");
            Assert.True(summary.Statistics.MessageCount == 3,
                "Matching schema: exactly 3 total messages recorded");
            Assert.True(summary.Statistics.ChannelMessageCounts.Values.Contains(1UL)
                   && summary.Statistics.ChannelMessageCounts.Values.Contains(2UL),
                "Matching schema: server/client channel message counts are 1 and 2");
        }

        [Fact]
        public void ClientWithDifferentSchemaIsSkipped()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/data", "json", "foxglove.SceneUpdate", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            recorder.WriteClientMessage(2, 30, 100, new byte[] { 2 }, "/data",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "{}");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "Different schema: only 1 channel recorded (client incompatible was skipped)");
            Assert.True(summary.Channels[0].Topic == "/data",
                "Different schema: the server channel is kept");
        }

        [Fact]
        public void SameSchemaNameDifferentContentIsSkipped()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/metrics", "json", "custom.Metrics", "jsonschema", @"{""type"":""object""}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            recorder.WriteClientMessage(2, 40, 100, new byte[] { 2 }, "/metrics",
                enc: "json", sName: "custom.Metrics", sEnc: "jsonschema", sContent: @"{""type"":""array""}");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "Diff content: only server channel recorded (client incompatible skipped)");
        }

        [Fact]
        public void DifferentEncodingIsSkipped()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/binary", "json", "foo.Binary", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            recorder.WriteClientMessage(2, 50, 100, new byte[] { 2 }, "/binary",
                enc: "protobuf", sName: "foo.Binary", sEnc: "jsonschema", sContent: "{}");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "Diff encoding: only server channel recorded (client incompatible skipped)");
        }

        [Fact]
        public void ServerDuplicateTopicWithIncompatibleSchemaIsSkipped()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/server_data", "json", "schema.A", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            recorder.AddChannel(2, "/server_data", "json", "schema.B", "jsonschema", "{}");
            recorder.WriteMessage(2, 100, new byte[] { 2 });

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 1,
                "Server duplicate: only 1 channel (second was skipped)");
            Assert.True(summary.Channels[0].Topic == "/server_data",
                "Server duplicate: the first channel recorded is /server_data");
        }

        [Fact]
        public void ClientWithAdvertisedTopicSchemaWithoutContentUsesDistinctInputChannel()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/unity/client_log", "json", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");

            recorder.WriteClientMessage(2, 60, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                "/unity/client_log", enc: "json", sName: "foxglove.Log", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Channels.Count == 2,
                "Client schema name only: output and input use distinct /unity/client_log channels");
            Assert.True(summary.Statistics.MessageCount == 1,
                "Client schema name only: client message recorded");
        }

        [Fact]
        public void EmptyEncodingEquivalentToJson()
        {
            using (var ms = new MemoryStream())
            {
                using (var recorder = new McapRecorder(ms))
                {
                    recorder.AddChannel(1, "/enc_test", "json", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");
                    recorder.WriteMessage(1, 0, new byte[] { 1 });

                    recorder.WriteClientMessage(2, 70, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                        "/enc_test", enc: "", sName: "foxglove.Log", sEnc: "", sContent: "");

                    recorder.Close();
                }

                ms.Position = 0;
                using var reader = new McapReader(ms);
                var summary = reader.ReadSummary();
                Assert.True(summary.Channels.Count == 2,
                    "Empty encoding: output and input stay distinct while empty == json");
                Assert.True(summary.Statistics.MessageCount == 2,
                    "Empty encoding: server and client messages both recorded");
            }

            using (var ms = new MemoryStream())
            {
                using (var recorder = new McapRecorder(ms))
                {
                    recorder.AddChannel(1, "/enc_test2", "", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");

                    recorder.WriteClientMessage(2, 71, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                        "/enc_test2", enc: "json", sName: "foxglove.Log", sEnc: "", sContent: "");

                    recorder.Close();
                }

                ms.Position = 0;
                using var reader = new McapReader(ms);
                var summary = reader.ReadSummary();
                Assert.True(summary.Channels.Count == 2,
                    "Empty encoding reverse: output and input stay distinct while json == empty");
                Assert.True(summary.Channels.All(channel => channel.MessageEncoding == "json"),
                    "Empty encoding reverse: stored encodings normalized to json");
            }
        }
    }
}
