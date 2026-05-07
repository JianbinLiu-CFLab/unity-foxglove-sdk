// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates architecture compiles, namespaces are consistent, message serialization matches Foxglove wire format.

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class SkeletonValidation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
            }
            else
            {
                throw new Exception($"[FAIL] {label}");
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string label) where T : IEquatable<T>
        {
            Assert(expected.Equals(actual), $"{label} (expected={expected}, actual={actual})");
        }

        /// <summary>
        /// Entry point: runs all skeleton tests covering runtime
        /// creation, schema registry, channel registry, subscription
        /// registry, binary codec roundtrips, and ServerInfo wire format.
        /// </summary>
        public static void Validate()
        {
            // 1. Can create a runtime with defaults
            using var runtime = new FoxgloveRuntime();
            Assert(runtime != null, "FoxgloveRuntime created");

            // 2. Can create with custom transport / clock / schemas
            var transport = new ManagedWsBackend();
            var clock = new SystemClock();
            var schemas = new DefaultSchemaRegistry();
            using var customRuntime = new FoxgloveRuntime(transport, clock, schemas);
            Assert(customRuntime != null, "FoxgloveRuntime created with custom deps");

            // 3. Schema registry
            schemas.Register(new SchemaEntry
            {
                Name = "foxglove.FrameTransform",
                Encoding = "jsonschema",
                Content = "{}"
            });
            Assert(schemas.TryGetSchema("foxglove.FrameTransform", out _), "Schema registered and found");

            // 4. ChannelRegistry
            var channels = new ChannelRegistry();
            channels.Register(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/test/pose",
                Encoding = "json",
                SchemaName = "foxglove.PoseInFrame"
            });
            AssertEqual(1, channels.Count, "Channel registered");

            // 5. SubscriptionRegistry — two clients, same channel
            var subs = new SubscriptionRegistry();
            subs.AddSubscription(clientId: 1, subscriptionId: 100, channelId: 1);
            subs.AddSubscription(clientId: 2, subscriptionId: 200, channelId: 1);
            int count = 0;
            foreach (var _ in subs.GetSubscribersForChannel(1)) count++;
            AssertEqual(2, count, "Two subscribers for channel 1");

            // 6a. Server→client MessageData roundtrip
            var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
            var frame = BinaryEncoding.EncodeServerMessageData(
                subscriptionId: 100, logTimeNs: 0x1122334455667788UL, payload: payload);
            Assert(frame.Length == 1 + 4 + 8 + payload.Length, "Server MessageData frame size");
            Assert(frame[0] == ServerOpcode.MessageData, "Server MessageData opcode");
            bool decoded = BinaryEncoding.TryDecodeServerMessageData(frame,
                out uint subId, out ulong time, out byte[] decodedPayload);
            Assert(decoded, "Server MessageData decode returned true");
            AssertEqual(100u, subId, "Server MessageData subscriptionId");
            AssertEqual(0x1122334455667788UL, time, "Server MessageData logTime");
            Assert((int)payload.Length == decodedPayload.Length, "Server MessageData payload length");

            // 6b. Client→server MessageData roundtrip (no logTime)
            var clientPayload = System.Text.Encoding.UTF8.GetBytes("client-data");
            var clientFrame = new byte[1 + 4 + clientPayload.Length];
            clientFrame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(clientFrame, 1, 42); // channelId=42
            Buffer.BlockCopy(clientPayload, 0, clientFrame, 5, clientPayload.Length);
            bool clientDecoded = BinaryEncoding.TryDecodeClientMessageData(
                clientFrame, out uint channelId, out byte[] cPayload);
            Assert(clientDecoded, "Client MessageData decode returned true");
            AssertEqual(42u, channelId, "Client MessageData channelId");
            Assert(clientPayload.Length == cPayload.Length, "Client MessageData payload length");

            // 7. ServerInfo JSON wire format (snapshot test for capability casing)
            var info = new ServerInfo
            {
                Name = "Test",
                Capabilities = new System.Collections.Generic.List<Capability> { Capability.Time, Capability.ClientPublish },
                SupportedEncodings = new System.Collections.Generic.List<string> { "json" }
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(info);
            // Verify wire-format: "op" is "serverInfo", capabilities are lowerCamelCase
            Assert(json.Contains("\"op\":\"serverInfo\""), "ServerInfo JSON contains op=serverInfo");
            Assert(json.Contains("\"time\"") && json.Contains("\"clientPublish\""),
                "Capabilities serialized as lowerCamelCase");
            // Full roundtrip
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerInfo>(json);
            Assert(parsed != null && parsed.Name == "Test", "ServerInfo JSON deserialize roundtrip");

            // 8. IFoxgloveTransport has SendText (added per Phase 0 review)
            var hasSendText = typeof(IFoxgloveTransport).GetMethod("SendText") != null;
            Assert(hasSendText, "IFoxgloveTransport has SendText(uint, string)");

            Console.WriteLine($"\nAll {_passCount} checks passed.");
        }
    }
}
