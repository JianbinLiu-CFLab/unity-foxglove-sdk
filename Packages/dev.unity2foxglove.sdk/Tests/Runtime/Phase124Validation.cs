// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 124 validation for decoded MCAP DataLoader iteration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase124Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 124: MCAP Decoded DataLoader v1 ===");
            _passed = 0;

            VerifyApiSurface();
            VerifyBuiltInDecoders();
            VerifyFailurePolicies();
            VerifyCustomFactoryCache();
            VerifyRawIteratorPreserved();

            Console.WriteLine($"Phase 124: {_passed} checks passed.");
        }

        private static void VerifyApiSurface()
        {
            var loader = typeof(McapDataLoader);
            Check(loader.GetMethod("CreateDecodedIterator") != null,
                "124-A1: McapDataLoader exposes CreateDecodedIterator");
            Check(loader.GetMethod("TryDecodeMessage") != null,
                "124-A2: McapDataLoader exposes TryDecodeMessage");
            Check(typeof(IMcapMessageDecoderFactory).GetMethod("TryCreate") != null
                  && typeof(IMcapMessageDecoder).GetMethod("Decode") != null,
                "124-A3: decoder interfaces exist");
            Check(Enum.IsDefined(typeof(McapDecodedPayloadKind), McapDecodedPayloadKind.Json)
                  && Enum.IsDefined(typeof(McapDecodedPayloadKind), McapDecodedPayloadKind.Protobuf)
                  && Enum.IsDefined(typeof(McapDecodedPayloadKind), McapDecodedPayloadKind.Ros2CdrDiagnostic),
                "124-A4: decoded payload kind enum covers JSON/protobuf/ROS2 diagnostic");
            Check(new McapDecodeOptions().FailurePolicy == McapDecodeFailurePolicy.RawWithProblem,
                "124-A5: default decode failure policy is non-breaking RawWithProblem");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Check(source.Contains("CreateDecodedIterator", StringComparison.Ordinal)
                  && source.Contains("TryDecodeMessage", StringComparison.Ordinal),
                "124-A6: DataLoader source wires decoded iterator APIs");
        }

        private static void VerifyBuiltInDecoders()
        {
            var path = CreateDecodedFixture();
            using var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests);

            var json = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase124/json" }
            }).ToList();
            var jsonToken = json[0].Payload.Value as JToken;
            Check(json.Count == 1
                  && json[0].Payload.Kind == McapDecodedPayloadKind.Json
                  && (int)jsonToken["value"] == 42,
                "124-B1: JSON decoder returns JToken payload");

            var proto = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase124/proto" }
            }).Single();
            var log = proto.Payload.Value as Foxglove.Log;
            Check(proto.Payload.Kind == McapDecodedPayloadKind.Protobuf
                  && log != null
                  && log.Message == "phase124 protobuf"
                  && proto.Payload.Text.Contains("phase124 protobuf", StringComparison.Ordinal),
                "124-B2: packaged Foxglove protobuf decoder returns IMessage payload and diagnostic JSON");

            var ros2 = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase124/ros2" }
            }).Single();
            var diagnostic = ros2.Payload.Value as McapRos2CdrDiagnosticPayload;
            Check(ros2.Payload.Kind == McapDecodedPayloadKind.Ros2CdrDiagnostic
                  && diagnostic != null
                  && diagnostic.SchemaKnown
                  && diagnostic.EncapsulationKind == 1
                  && diagnostic.IsLittleEndian
                  && diagnostic.DataByteLength == 3,
                "124-B3: ROS2 CDR decoder returns schema-aware diagnostic envelope");

            var unknown = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase124/unknown" }
            }).Single();
            Check(unknown.Payload.Kind == McapDecodedPayloadKind.Unsupported
                  && unknown.Problems.Count == 1
                  && unknown.Problems[0].Code == "McapDecodeUnsupported",
                "124-B4: unknown encoding returns unsupported raw payload with structured problem");
        }

        private static void VerifyFailurePolicies()
        {
            var path = CreateDecodedFixture();
            using var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests);

            var failed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase124/badjson" }
            }).Single();
            Check(failed.Payload.Kind == McapDecodedPayloadKind.Failed
                  && failed.Problems.Count == 1
                  && failed.Problems[0].Code == "McapDecodeFailed"
                  && Encoding.UTF8.GetString(failed.Raw.Data).Contains("not-json", StringComparison.Ordinal),
                "124-C1: RawWithProblem keeps malformed raw payload with structured failure");

            var threw = false;
            try
            {
                loader.CreateDecodedIterator(
                    new McapDataLoaderQuery { Topics = new List<string> { "/phase124/badjson" } },
                    new McapDecodeOptions { FailurePolicy = McapDecodeFailurePolicy.Throw }).ToList();
            }
            catch
            {
                threw = true;
            }

            Check(threw, "124-C2: Throw failure policy propagates malformed decode exceptions");
        }

        private static void VerifyCustomFactoryCache()
        {
            var path = CreateDecodedFixture();
            using var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests);
            var factory = new CountingFactory();
            var decoded = loader.CreateDecodedIterator(
                new McapDataLoaderQuery { Topics = new List<string> { "/phase124/custom" } },
                new McapDecodeOptions { DecoderFactories = new List<IMcapMessageDecoderFactory> { factory } }).ToList();

            Check(decoded.Count == 2
                  && decoded.All(m => m.Payload.Kind == McapDecodedPayloadKind.Raw)
                  && factory.TryCreateCount == 1
                  && factory.DecodeCount == 2,
                "124-D1: caller factories run before built-ins and cache decoder per channel");
        }

        private static void VerifyRawIteratorPreserved()
        {
            var path = CreateDecodedFixture();
            using var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests);
            var query = new McapDataLoaderQuery
            {
                StartTimeNs = 10,
                EndTimeNs = 40,
                MaxMessages = 3
            };
            var raw = loader.CreateIterator(query).ToList();
            var decoded = loader.CreateDecodedIterator(query).ToList();

            Check(raw.Count == decoded.Count
                  && raw.Zip(decoded, (left, right) =>
                      left.ChannelId == right.Raw.ChannelId &&
                      left.LogTime == right.Raw.LogTime &&
                      left.Data.SequenceEqual(right.Raw.Data)).All(v => v),
                "124-E1: decoded iterator preserves raw DataLoader iteration bytes and timing");

            Check(loader.TryDecodeMessage(raw[0], null, out var single)
                  && single.Raw.Data.SequenceEqual(raw[0].Data),
                "124-E2: TryDecodeMessage returns decoded wrapper while preserving raw message");
        }

        private static string CreateDecodedFixture()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase124_" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, new McapWriterOptions { UseChunking = false, EnableDataCrcs = true }, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase124/json", "json", "phase124.Json", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"value\":42}"));

                var log = new Foxglove.Log
                {
                    Level = Foxglove.Log.Types.Level.Info,
                    Message = "phase124 protobuf",
                    Name = "Phase124Validation"
                };
                recorder.AddChannel(2, "/phase124/proto", "protobuf", "foxglove.Log", "protobuf", string.Empty);
                recorder.WriteMessage(2, 20, log.ToByteArray());

                recorder.AddChannel(3, "/phase124/ros2", "cdr", "foxglove_msgs/msg/Log", "ros2msg", string.Empty);
                recorder.WriteMessage(3, 30, new byte[] { 0, 1, 0, 0, 1, 2, 3 });

                recorder.AddChannel(4, "/phase124/unknown", "binary", "phase124.Binary", "binaryschema", string.Empty);
                recorder.WriteMessage(4, 40, new byte[] { 1, 2, 3 });

                recorder.AddChannel(5, "/phase124/badjson", "json", "phase124.BadJson", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(5, 50, Encoding.UTF8.GetBytes("{not-json"));

                recorder.AddChannel(6, "/phase124/custom", "custom", "phase124.Custom", "customschema", string.Empty);
                recorder.WriteMessage(6, 60, Encoding.UTF8.GetBytes("one"));
                recorder.WriteMessage(6, 70, Encoding.UTF8.GetBytes("two"));
                recorder.Close();
            }

            return path;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !File.Exists(Path.Combine(dir, "README.md")); i++)
                dir = Directory.GetParent(dir)?.FullName ?? dir;
            return dir;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class CountingFactory : IMcapMessageDecoderFactory
        {
            public int TryCreateCount;
            public int DecodeCount;

            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
            {
                TryCreateCount++;
                return string.Equals(channel?.MessageEncoding, "custom", StringComparison.Ordinal)
                    ? new Decoder(this)
                    : null;
            }

            private sealed class Decoder : IMcapMessageDecoder
            {
                private readonly CountingFactory _owner;

                public Decoder(CountingFactory owner)
                {
                    _owner = owner;
                }

                public McapDecodedPayload Decode(McapDataLoaderMessage message)
                {
                    _owner.DecodeCount++;
                    return McapDecodedPayload.Raw(message.Data);
                }
            }
        }
    }
}
