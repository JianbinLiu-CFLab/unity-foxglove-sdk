// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed partial class Ros2BridgePublisherPreparationTests
    {
        private static byte[] MutateHeader(byte[] frame, Action<JObject> mutate)
        {
            var headerLength = (int)ReadUInt32LE(frame, 8);
            var header = JObject.Parse(Encoding.UTF8.GetString(frame, 16, headerLength));
            mutate(header);
            var headerBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(header, Formatting.None));
            var result = new byte[16 + headerBytes.Length];
            Buffer.BlockCopy(frame, 0, result, 0, 16);
            WriteUInt32LE(result, 8, (uint)headerBytes.Length);
            Buffer.BlockCopy(headerBytes, 0, result, 16, headerBytes.Length);
            return result;
        }

        private static string FindV1AuthorityFixture()
        {
            const string relativePath =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json";
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Packages"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Tools")))
                {
                    return Path.Combine(
                        directory.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repository root for the shared U2R2 v1 authority fixture.");
        }

        private static void AssertAuthorityFrame(JObject vector, byte[] actual)
        {
            Assert.Equal(
                vector.Value<string>("frameHex"),
                BytesToHex(actual));
            var headerLength = checked((int)ReadUInt32LE(actual, 8));
            var headerJson = Encoding.UTF8.GetString(actual, 16, headerLength);
            Assert.Equal(vector.Value<string>("headerJson"), headerJson);
            Assert.True(
                JToken.DeepEquals(
                    Assert.IsType<JObject>(vector["header"]),
                    JObject.Parse(headerJson)));
            Assert.Equal(
                vector.Value<int>("payloadLength"),
                checked((int)ReadUInt32LE(actual, 12)));
        }

        private static void AssertExecutableV1NegativeRejected(
            JObject fixture,
            JObject vector)
        {
            var action = vector.Value<string>("action");
            var expectedFailure = vector.Value<string>("expectedFailure");
            if (string.Equals(
                    action,
                    "publish_before_publisher_ready",
                    StringComparison.Ordinal))
            {
                AssertPublishBeforeReadyRejected(fixture, expectedFailure);
                return;
            }

            var wire = BuildExecutableV1Wire(fixture, vector);
            if (string.Equals(action, "stream_frame", StringComparison.Ordinal))
            {
                Assert.Equal("eof", vector.Value<string>("termination"));
                using var stream = new MemoryStream(wire, writable: false);
                var exception = Assert.Throws<EndOfStreamException>(
                    () => Ros2BridgePublisherPreparationCodec.ReadFrame(stream));
                Assert.Contains(
                    "closed",
                    exception.Message,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal("peer_close", expectedFailure);
                return;
            }

            Action decode;
            if (string.Equals(
                    vector.Value<string>("baseFrame"),
                    "preparePublisher.response",
                    StringComparison.Ordinal))
            {
                decode = () => Ros2BridgePublisherPreparationCodec.ParseResponse(
                    wire,
                    vector.Value<string>("expectedRequestId"));
            }
            else if (string.Equals(expectedFailure, "payload_length", StringComparison.Ordinal)
                     || string.Equals(expectedFailure, "payload_completion", StringComparison.Ordinal))
            {
                decode = () => Ros2BridgeU2R2HealthCodec.ParseHealthPong(
                    wire,
                    fixture["health"]?.Value<string>("requestId"));
            }
            else
            {
                decode = () => Ros2BridgePublisherPreparationCodec.ParseRequest(wire);
            }

            var rejection = Record.Exception(decode);
            Assert.True(
                rejection != null,
                "Executable v1 negative vector was accepted: "
                + vector.Value<string>("id"));
            Assert.Contains(
                ExpectedCSharpV1FailureFragment(expectedFailure),
                rejection.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertPublishBeforeReadyRejected(
            JObject fixture,
            string expectedFailure)
        {
            Assert.Equal("publisher_readiness", expectedFailure);
            var publish = Assert.IsType<JObject>(fixture["publish"]);
            using var responseGate = new ManualResetEventSlim(false);
            var transport = new PreparationTransport("ok")
            {
                ResponseGate = responseGate
            };
            using (var runtime = Runtime(() => transport))
            {
                try
                {
                    runtime.Start(enabled: true, autoConnect: true);
                    var readiness = runtime.PreparePublisher(
                        publish.Value<string>("topic"),
                        publish.Value<string>("schemaName"),
                        ReadAuthorityQos(
                            Assert.IsType<JObject>(publish["qos"])),
                        out _);
                    Assert.Equal(
                        Ros2BridgePublisherReadiness.Pending,
                        readiness);
                    Assert.False(
                        runtime.TryEnqueuePrepared(
                            Ros2BridgeFrame.CreateValidated(
                                publish.Value<string>("topic"),
                                publish.Value<string>("schemaName"),
                                publish.Value<string>("encoding"),
                                publish.Value<ulong>("logTimeNs"),
                                publish.Value<ulong>("sequence"),
                                HexToBytes(publish.Value<string>("payloadHex")),
                                ReadAuthorityQos(
                                    Assert.IsType<JObject>(publish["qos"]))),
                            out var reason));
                    Assert.Contains(
                        "pending",
                        reason,
                        StringComparison.OrdinalIgnoreCase);
                    Assert.Empty(transport.SentFrames);
                }
                finally
                {
                    responseGate.Set();
                }
            }
        }

        private static string ExpectedCSharpV1FailureFragment(string failure)
            => failure switch
            {
                "magic" => "magic",
                "version" => "version",
                "flags" => "flags",
                "header_length" => "header length",
                "payload_length" => "payload length exceeds maximum",
                "fixed_header" => "too short",
                "header_completion" => "frame length",
                "payload_completion" => "frame length",
                "duplicate_property" => "duplicate",
                "operation" => "op must be prepare_publisher",
                "utf8" => "UTF-8",
                "json_root" => "JSON",
                "topic" => "topic",
                "schema_name" => "schemaName",
                "qos" => "QoS",
                "request_id" => "requestId does not match",
                _ => throw new FormatException(
                    "Unknown executable v1 failure classification: " + failure)
            };

    }
}
