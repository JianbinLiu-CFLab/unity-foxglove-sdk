// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates CompressedImage schema registration, base64 roundtrip, FoxgloveTime utility, and typed channel advertising.

using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase4Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 4 tests covering CompressedImage
        /// schema registration, base64 roundtrip, FoxgloveTime utility,
        /// and typed channel advertising.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("--- Phase 4 Tests ---");
            _passCount = 0;

            TestCompressedImageSchemaRegistered();
            TestCompressedImageMessageFields();
            TestCompressedImageBase64Roundtrip();
            TestFoxgloveTimeUtil();
            TestRegisterSchemaChannelCamera();

            Console.WriteLine($"Phase 4: {_passCount} checks passed.\n");
        }

        /// <summary>
        /// Confirms <c>foxglove.CompressedImage</c> is registered with
        /// correct encoding and title after core schema initialization.
        /// </summary>
        private static void TestCompressedImageSchemaRegistered()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);

            Assert(registry.TryGetSchema("foxglove.CompressedImage", out var entry),
                "Registry has CompressedImage");
            Assert(entry.Encoding == "jsonschema", "CompressedImage encoding is jsonschema");

            var obj = JObject.Parse(entry.Content);
            Assert(obj["title"]?.ToString() == "foxglove.CompressedImage",
                "CompressedImage schema title matches");
        }

        /// <summary>
        /// Serializes a <c>CompressedImageMessage</c> and verifies all
        /// fields -- timestamp, frame_id, data, and format -- appear
        /// correctly in the JSON output.
        /// </summary>
        private static void TestCompressedImageMessageFields()
        {
            var msg = new CompressedImageMessage
            {
                Timestamp = new FoxgloveTime { Sec = 1, Nsec = 2 },
                FrameId = "camera",
                Data = "AAAA",
                Format = "jpeg"
            };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["timestamp"] != null, "has timestamp");
            Assert(obj["frame_id"]?.ToString() == "camera", "has frame_id");
            Assert(obj["data"]?.ToString() == "AAAA", "has data");
            Assert(obj["format"]?.ToString() == "jpeg", "format is jpeg");
        }

        /// <summary>
        /// Encodes binary data as base64, serializes through
        /// <c>CompressedImageMessage</c>, then decodes and verifies the
        /// data roundtrips correctly.
        /// </summary>
        private static void TestCompressedImageBase64Roundtrip()
        {
            var original = new byte[] { 0x01, 0x02, 0x03, 0xFF };
            var b64 = Convert.ToBase64String(original);
            var msg = new CompressedImageMessage { Data = b64, Format = "jpeg" };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            var roundtripped = Convert.FromBase64String(obj["data"]?.ToString());
            Assert(roundtripped.Length == 4 && roundtripped[0] == 1 && roundtripped[3] == 0xFF,
                "base64 data roundtrips");
        }

        /// <summary>
        /// Tests <c>FoxgloveTimeUtil.ToFoxgloveTime</c> with a known
        /// Unix-nanosecond value and verifies the sec/nsec split.
        /// </summary>
        private static void TestFoxgloveTimeUtil()
        {
            var unixNs = 1777645831933000000UL;
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);
            Assert(time.Sec == 1777645831, $"sec={time.Sec} (expected 1777645831)");
            Assert(time.Nsec == 933000000, $"nsec={time.Nsec} (expected 933000000)");
        }

        /// <summary>
        /// Registers a camera channel with the CompressedImage schema and
        /// verifies the broadcasted advertise carries correct schema
        /// metadata.
        /// </summary>
        private static void TestRegisterSchemaChannelCamera()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            var fake = new Phase4FakeTransport();
            var session = new FoxgloveSession("Test", fake, schemaRegistry: registry);

            session.RegisterSchemaChannel(30, "/unity/camera", "foxglove.CompressedImage");
            var advJson = fake.BroadcastTexts[0];
            var adv = JObject.Parse(advJson);
            var ch = adv["channels"]?[0] as JObject;
            Assert(ch["schemaName"]?.ToString() == "foxglove.CompressedImage",
                "advertise schemaName matches");
            Assert(ch["schemaEncoding"]?.ToString() == "jsonschema",
                "advertise schemaEncoding is jsonschema");
            Assert(ch["schema"]?.ToString().Contains("foxglove.CompressedImage") == true,
                "advertise schema contains title");
        }

        /// <summary>
        /// Minimal fake transport for Phase 4; only records
        /// BroadcastText calls.
        /// </summary>
        private sealed class Phase4FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public readonly System.Collections.Generic.List<string> BroadcastTexts = new();
            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
        }
    }
}
