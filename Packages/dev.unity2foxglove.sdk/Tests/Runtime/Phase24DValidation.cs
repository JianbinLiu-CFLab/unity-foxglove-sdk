// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates MCAP mixed-schema guards, client publish schema dedup, encoding normalization (empty == json), and duplicate topic rejection.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase24DValidation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 24D tests covering MCAP
        /// mixed-schema guards, client publish schema dedup,
        /// encoding normalization (empty == json), and duplicate
        /// topic rejection.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 24D Tests ---");
            _passCount = 0;
            TestServerChannelWithSchemaThenClientWithoutSchemaIsSkipped();
            TestNewSchemalessClientTopicIsRecorded();
            TestClientWithMatchingSchemaIsRecorded();
            TestClientWithDifferentSchemaIsSkipped();
            TestSameSchemaNameDifferentContentIsSkipped();
            TestDifferentEncodingIsSkipped();
            TestServerDuplicateTopicWithIncompatibleSchemaIsSkipped();
            TestClientCanReuseAdvertisedTopicSchemaWithoutContent();
            TestEmptyEncodingEquivalentToJson();
            Console.WriteLine($"Phase 24D: {_passCount} checks passed.");
        }

        /// <summary>
        /// When server registers a typed schema topic and a client publishes
        /// the same topic without a schema, the client channel should be skipped.
        /// This reproduces the /unity/camera mixed-schema bug.
        /// </summary>
        static void TestServerChannelWithSchemaThenClientWithoutSchemaIsSkipped()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);
            recorder.AddChannel(1, "/unity/camera", "json", "foxglove.CompressedImage", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1, 2, 3 });
            recorder.WriteMessage(1, 100, new byte[] { 4, 5, 6 });

            // Client publishes same topic without schema
            recorder.WriteClientMessage(99, 42, 200, new byte[] { 7, 8, 9 }, "/unity/camera",
                enc: "json", sName: "", sEnc: "", sContent: "");

            recorder.Close();

            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Mixed schema: only 1 channel recorded (second was skipped)");
            Assert(summary.Channels[0].Topic == "/unity/camera",
                "Mixed schema: recorded channel is /unity/camera");
            Assert(summary.Channels[0].SchemaId != 0,
                "Mixed schema: channel keeps non-zero schema id");
        }

        /// <summary>
        /// A new schemaless client topic with no existing server channel
        /// should still be recorded.
        /// </summary>
        static void TestNewSchemalessClientTopicIsRecorded()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            // Client publishes a completely new topic with no schema
            recorder.WriteClientMessage(1, 10, 500, new byte[] { 1 }, "/move_base_simple/goal",
                enc: "json", sName: "", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "New schemaless client: 1 channel recorded");
            Assert(summary.Channels[0].Topic == "/move_base_simple/goal",
                "New schemaless client: channel topic correct");
        }

        /// <summary>
        /// A client publishing the same topic with a matching schema signature
        /// should be recorded and should not create a duplicate channel.
        /// </summary>
        static void TestClientWithMatchingSchemaIsRecorded()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/tf", "json", "foxglove.FrameTransform", "jsonschema", "schema_v1");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Client publishes same topic with same schema
            recorder.WriteClientMessage(2, 20, 100, new byte[] { 2, 3 }, "/tf",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "schema_v1");

            recorder.WriteClientMessage(2, 20, 200, new byte[] { 4, 5 }, "/tf",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "schema_v1");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 2,
                "Matching schema: 2 channels (server + client)");
            Assert(summary.Statistics.MessageCount == 3,
                "Matching schema: exactly 3 total messages recorded");
            Assert(summary.Statistics.ChannelMessageCounts.Values.Contains(1UL)
                   && summary.Statistics.ChannelMessageCounts.Values.Contains(2UL),
                "Matching schema: server/client channel message counts are 1 and 2");
        }

        /// <summary>
        /// A client publishing the same topic with a different schema name
        /// should be skipped.
        /// </summary>
        static void TestClientWithDifferentSchemaIsSkipped()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/data", "json", "foxglove.SceneUpdate", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Client publishes same topic with DIFFERENT schema
            recorder.WriteClientMessage(2, 30, 100, new byte[] { 2 }, "/data",
                enc: "json", sName: "foxglove.FrameTransform", sEnc: "jsonschema", sContent: "{}");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Different schema: only 1 channel recorded (client incompatible was skipped)");
            Assert(summary.Channels[0].Topic == "/data",
                "Different schema: the server channel is kept");
        }

        /// <summary>
        /// Same schema name with different schema content must be rejected
        /// because the content hash differs from the first recorded channel.
        /// </summary>
        static void TestSameSchemaNameDifferentContentIsSkipped()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/metrics", "json", "custom.Metrics", "jsonschema", @"{""type"":""object""}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Same schema name, completely different content
            recorder.WriteClientMessage(2, 40, 100, new byte[] { 2 }, "/metrics",
                enc: "json", sName: "custom.Metrics", sEnc: "jsonschema", sContent: @"{""type"":""array""}");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Diff content: only server channel recorded (client incompatible skipped)");
        }

        /// <summary>
        /// Same topic with different message encoding must be rejected.
        /// </summary>
        static void TestDifferentEncodingIsSkipped()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/binary", "json", "foo.Binary", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Same topic, different encoding (protobuf instead of json)
            recorder.WriteClientMessage(2, 50, 100, new byte[] { 2 }, "/binary",
                enc: "protobuf", sName: "foo.Binary", sEnc: "jsonschema", sContent: "{}");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Diff encoding: only server channel recorded (client incompatible skipped)");
        }

        /// <summary>
        /// Server registering the same topic twice with incompatible schema
        /// must be guarded in AddChannel.
        /// </summary>
        static void TestServerDuplicateTopicWithIncompatibleSchemaIsSkipped()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/server_data", "json", "schema.A", "jsonschema", "{}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Server re-registers same topic with different schema name
            recorder.AddChannel(2, "/server_data", "json", "schema.B", "jsonschema", "{}");
            recorder.WriteMessage(2, 100, new byte[] { 2 });

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Server duplicate: only 1 channel (second was skipped)");
            Assert(summary.Channels[0].Topic == "/server_data",
                "Server duplicate: the first channel recorded is /server_data");
        }

        /// <summary>
        /// Foxglove Publish can advertise a known schema name without sending
        /// the full schema content back to the server. If Unity already
        /// advertised the same topic/schema, the recorder should reuse that
        /// channel instead of treating the missing client schema content as a
        /// mixed-schema conflict.
        /// </summary>
        static void TestClientCanReuseAdvertisedTopicSchemaWithoutContent()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/unity/client_log", "json", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");

            recorder.WriteClientMessage(2, 60, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                "/unity/client_log", enc: "json", sName: "foxglove.Log", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Client schema name only: reused existing /unity/client_log channel");
            Assert(summary.Statistics.MessageCount == 1,
                "Client schema name only: client message recorded");
        }

        /// <summary>
        /// Empty encoding and explicit "json" encoding must be treated as
        /// equivalent for topic signature purposes. A server channel registered
        /// with "json" and a client publish with empty encoding (or vice versa)
        /// should not be treated as incompatible.
        /// </summary>
        static void TestEmptyEncodingEquivalentToJson()
        {
            var ms = new MemoryStream();
            var recorder = new McapRecorder(ms);

            // Server registers with "json"
            recorder.AddChannel(1, "/enc_test", "json", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");
            recorder.WriteMessage(1, 0, new byte[] { 1 });

            // Client publishes with empty encoding — should reuse, not create conflict
            recorder.WriteClientMessage(2, 70, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                "/enc_test", enc: "", sName: "foxglove.Log", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Empty encoding: reused existing channel (empty == json)");
            Assert(summary.Statistics.MessageCount == 2,
                "Empty encoding: server and client messages both recorded");

            // Reverse: server empty, client "json"
            ms = new MemoryStream();
            recorder = new McapRecorder(ms);

            recorder.AddChannel(1, "/enc_test2", "", "foxglove.Log", "jsonschema", @"{""title"":""foxglove.Log""}");

            recorder.WriteClientMessage(2, 71, 100, Encoding.UTF8.GetBytes(@"{""message"":""hello""}"),
                "/enc_test2", enc: "json", sName: "foxglove.Log", sEnc: "", sContent: "");

            recorder.Close();
            ms.Position = 0;
            summary = new McapReader(ms).ReadSummary();
            Assert(summary.Channels.Count == 1,
                "Empty encoding reverse: reused existing channel (json == empty)");
            Assert(summary.Channels[0].MessageEncoding == "json",
                "Empty encoding reverse: stored encoding normalized to json");
        }
    }
}
