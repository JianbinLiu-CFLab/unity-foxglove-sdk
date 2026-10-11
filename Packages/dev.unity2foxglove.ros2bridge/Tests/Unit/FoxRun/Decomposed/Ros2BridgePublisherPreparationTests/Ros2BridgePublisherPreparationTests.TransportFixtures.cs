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
        private sealed class PreparationTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly string _status;
            private readonly string _errorCode;
            private readonly string _message;

            public PreparationTransport(
                string status,
                string errorCode = "",
                string message = "")
            {
                _status = status;
                _errorCode = errorCode;
                _message = message;
            }

            public List<byte[]> Requests { get; } = new List<byte[]>();
            public List<long> RequestTimes { get; } = new List<long>();
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public List<string> Events { get; } = new List<string>();
            public ManualResetEventSlim ResponseGate { get; set; }
            public ManualResetEventSlim SendGate { get; set; }
            public ManualResetEventSlim SendStarted { get; } = new ManualResetEventSlim(false);
            public bool FailNextPublish { get; set; }
            public Exception PreparationFailure { get; set; }
            public Func<byte[], byte[]> MutateResponse { get; set; }
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisposeCount { get; private set; }
            public int RequestCount
            {
                get
                {
                    lock (Requests)
                        return Requests.Count;
                }
            }
            public int SentFrameCount
            {
                get
                {
                    lock (SentFrames)
                        return SentFrames.Count;
                }
            }
            public long[] RequestTimesSnapshot
            {
                get
                {
                    lock (Requests)
                        return RequestTimes.ToArray();
                }
            }
            public string[] EventSnapshot
            {
                get
                {
                    lock (Events)
                        return Events.ToArray();
                }
            }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                    RequestTimes.Add(Environment.TickCount64);
                }
                lock (Events)
                    Events.Add("prepare");
                if (PreparationFailure != null)
                    throw PreparationFailure;
                ResponseGate?.Wait(timeoutMs);
                var parsed = Ros2BridgePublisherPreparationCodec.ParseRequest(request);
                var response = Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                    parsed.RequestId,
                    _status,
                    _errorCode,
                    _message);
                return MutateResponse == null ? response : MutateResponse(response);
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
                SendStarted.Set();
                SendGate?.Wait(timeoutMs);
                if (FailNextPublish)
                {
                    FailNextPublish = false;
                    throw new InvalidOperationException("forced reconnect");
                }
                lock (SentFrames)
                    SentFrames.Add(frame);
                lock (Events)
                    Events.Add("send");
            }

            public void Disconnect() => IsConnected = false;
            public void Dispose()
            {
                DisposeCount++;
                Disconnect();
            }
        }

        private sealed class ReconnectSequenceTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly IRos2BridgeSink[] _transports;
            private int _index;

            internal ReconnectSequenceTransport(params IRos2BridgeSink[] transports)
            {
                _transports = transports;
            }

            private IRos2BridgeSink Current => _transports[_index];

            public bool IsConnected => Current.IsConnected;

            public void Connect(string host, int port, int timeoutMs)
                => Current.Connect(host, port, timeoutMs);

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
                => Current.Send(frame, timeoutMs);

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
                => ((IRos2BridgePublisherPreparationTransport)Current)
                    .ExchangePublisherPreparation(request, timeoutMs);

            public void Disconnect()
            {
                Current.Disconnect();
                if (_index + 1 < _transports.Length)
                    _index++;
            }

            public void Dispose()
            {
                for (var i = 0; i < _transports.Length; i++)
                    _transports[i].Dispose();
            }
        }

        private sealed class FatalDisconnectSink : IRos2BridgeSink
        {
            public bool IsConnected => true;
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs) { }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
                => throw new OutOfMemoryException("stop-primary");

            public void Dispose()
                => DisposeCount++;
        }

        private sealed class TrackingLifecycleSink : IRos2BridgeSink
        {
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
            {
                DisconnectCount++;
                IsConnected = false;
            }

            public void Dispose()
            {
                DisposeCount++;
                IsConnected = false;
            }
        }

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

        private static byte[] BuildExecutableV1Wire(
            JObject fixture,
            JObject vector)
        {
            var action = vector.Value<string>("action");
            var baseFrame = ReadExecutableV1BaseFrame(
                fixture,
                vector.Value<string>("baseFrame"));
            switch (action)
            {
                case "patch_frame":
                {
                    var replacement = HexToBytes(
                        vector.Value<string>("replacementHex"));
                    var offset = vector.Value<int>("offset");
                    Assert.InRange(offset, 0, baseFrame.Length);
                    Assert.True(offset + replacement.Length <= baseFrame.Length);
                    Buffer.BlockCopy(
                        replacement,
                        0,
                        baseFrame,
                        offset,
                        replacement.Length);
                    return baseFrame;
                }
                case "truncate_frame":
                case "stream_frame":
                {
                    var length = vector["length"] != null
                        ? vector.Value<int>("length")
                        : checked(
                            baseFrame.Length
                            - vector.Value<int>("trimBytes"));
                    Assert.InRange(length, 0, baseFrame.Length - 1);
                    Array.Resize(ref baseFrame, length);
                    return baseFrame;
                }
                case "patch_json":
                {
                    ReadExecutableV1Body(
                        baseFrame,
                        out var headerBytes,
                        out var payload);
                    var header = JObject.Parse(
                        Encoding.UTF8.GetString(headerBytes));
                    var path = vector.Value<string>("path")
                        .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                    Assert.NotEmpty(path);
                    var current = header;
                    for (var index = 0; index + 1 < path.Length; index++)
                        current = Assert.IsType<JObject>(current[path[index]]);
                    current[path[path.Length - 1]] = vector["value"]?.DeepClone();
                    return RebuildExecutableV1Frame(
                        baseFrame,
                        Encoding.UTF8.GetBytes(
                            JsonConvert.SerializeObject(
                                header,
                                Formatting.None)),
                        payload);
                }
                case "duplicate_json_property":
                {
                    ReadExecutableV1Body(
                        baseFrame,
                        out var headerBytes,
                        out var payload);
                    Assert.Equal((byte)'{', headerBytes[0]);
                    var prefix = Encoding.UTF8.GetBytes(
                        JsonConvert.SerializeObject(
                            vector.Value<string>("property"))
                        + ":"
                        + JsonConvert.SerializeObject(
                            vector.Value<string>("value"))
                        + ",");
                    var duplicate = new byte[
                        checked(headerBytes.Length + prefix.Length)];
                    duplicate[0] = headerBytes[0];
                    Buffer.BlockCopy(prefix, 0, duplicate, 1, prefix.Length);
                    Buffer.BlockCopy(
                        headerBytes,
                        1,
                        duplicate,
                        1 + prefix.Length,
                        headerBytes.Length - 1);
                    return RebuildExecutableV1Frame(
                        baseFrame,
                        duplicate,
                        payload);
                }
                case "replace_header_utf8":
                {
                    ReadExecutableV1Body(
                        baseFrame,
                        out var headerBytes,
                        out var payload);
                    var needle = Encoding.UTF8.GetBytes(
                        vector.Value<string>("needle"));
                    var replacement = HexToBytes(
                        vector.Value<string>("replacementHex"));
                    var offset = FindBytes(headerBytes, needle);
                    Assert.True(offset >= 0);
                    Assert.True(replacement.Length <= needle.Length);
                    Buffer.BlockCopy(
                        replacement,
                        0,
                        headerBytes,
                        offset,
                        replacement.Length);
                    return RebuildExecutableV1Frame(
                        baseFrame,
                        headerBytes,
                        payload);
                }
                case "append_json_root":
                {
                    ReadExecutableV1Body(
                        baseFrame,
                        out var headerBytes,
                        out var payload);
                    var suffix = Encoding.UTF8.GetBytes(
                        vector.Value<string>("suffix"));
                    var appended = new byte[
                        checked(headerBytes.Length + suffix.Length)];
                    Buffer.BlockCopy(
                        headerBytes,
                        0,
                        appended,
                        0,
                        headerBytes.Length);
                    Buffer.BlockCopy(
                        suffix,
                        0,
                        appended,
                        headerBytes.Length,
                        suffix.Length);
                    return RebuildExecutableV1Frame(
                        baseFrame,
                        appended,
                        payload);
                }
                default:
                    throw new FormatException(
                        "Unknown executable v1 action: " + action);
            }
        }

        private static byte[] ReadExecutableV1BaseFrame(
            JObject fixture,
            string baseFrame)
        {
            var vector = Assert.IsType<JObject>(fixture.SelectToken(baseFrame));
            return HexToBytes(vector.Value<string>("frameHex"));
        }

        private static void ReadExecutableV1Body(
            byte[] frame,
            out byte[] header,
            out byte[] payload)
        {
            Assert.True(frame != null && frame.Length >= 16);
            var headerLength = checked((int)ReadUInt32LE(frame, 8));
            var payloadLength = checked((int)ReadUInt32LE(frame, 12));
            Assert.Equal(
                checked(16 + headerLength + payloadLength),
                frame.Length);
            header = new byte[headerLength];
            payload = new byte[payloadLength];
            Buffer.BlockCopy(frame, 16, header, 0, headerLength);
            Buffer.BlockCopy(
                frame,
                16 + headerLength,
                payload,
                0,
                payloadLength);
        }

        private static byte[] RebuildExecutableV1Frame(
            byte[] baseFrame,
            byte[] header,
            byte[] payload)
        {
            var frame = new byte[checked(16 + header.Length + payload.Length)];
            Buffer.BlockCopy(baseFrame, 0, frame, 0, 16);
            WriteUInt32LE(frame, 8, checked((uint)header.Length));
            WriteUInt32LE(frame, 12, checked((uint)payload.Length));
            Buffer.BlockCopy(header, 0, frame, 16, header.Length);
            Buffer.BlockCopy(
                payload,
                0,
                frame,
                16 + header.Length,
                payload.Length);
            return frame;
        }

        private static int FindBytes(byte[] haystack, byte[] needle)
        {
            if (needle == null || needle.Length == 0)
                return -1;
            for (var offset = 0;
                 offset + needle.Length <= haystack.Length;
                 offset++)
            {
                var matches = true;
                for (var index = 0; index < needle.Length; index++)
                {
                    if (haystack[offset + index] == needle[index])
                        continue;
                    matches = false;
                    break;
                }
                if (matches)
                    return offset;
            }
            return -1;
        }

        private static FoxRunResolvedQos ReadAuthorityQos(JObject qos)
            => new(
                ParseAuthorityProfile(qos.Value<string>("profile")),
                ParseAuthorityReliability(qos.Value<string>("reliability")),
                ParseAuthorityDurability(qos.Value<string>("durability")),
                ParseAuthorityHistory(qos.Value<string>("history")),
                qos.Value<int>("depth"));

        private static FoxRunQosProfile ParseAuthorityProfile(string value)
            => value switch
            {
                "default" => FoxRunQosProfile.Default,
                "sensor_data" => FoxRunQosProfile.SensorData,
                "system_default" => FoxRunQosProfile.SystemDefault,
                _ => throw new FormatException("Unknown fixture QoS profile: " + value)
            };

        private static FoxRunQosReliability ParseAuthorityReliability(string value)
            => value switch
            {
                "system_default" => FoxRunQosReliability.SystemDefault,
                "reliable" => FoxRunQosReliability.Reliable,
                "best_effort" => FoxRunQosReliability.BestEffort,
                _ => throw new FormatException("Unknown fixture QoS reliability: " + value)
            };

        private static FoxRunQosDurability ParseAuthorityDurability(string value)
            => value switch
            {
                "system_default" => FoxRunQosDurability.SystemDefault,
                "volatile" => FoxRunQosDurability.Volatile,
                "transient_local" => FoxRunQosDurability.TransientLocal,
                _ => throw new FormatException("Unknown fixture QoS durability: " + value)
            };

        private static FoxRunQosHistory ParseAuthorityHistory(string value)
            => value switch
            {
                "system_default" => FoxRunQosHistory.SystemDefault,
                "keep_last" => FoxRunQosHistory.KeepLast,
                "keep_all" => FoxRunQosHistory.KeepAll,
                _ => throw new FormatException("Unknown fixture QoS history: " + value)
            };

        private static byte[] HexToBytes(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0)
                throw new FormatException("Fixture hex must contain complete bytes.");
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            if (fixedHeader[0] != 'U'
                || fixedHeader[1] != '2'
                || fixedHeader[2] != 'R'
                || fixedHeader[3] != '2')
            {
                throw new FormatException("U2R2 magic is invalid.");
            }

            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (headerLength == 0
                || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes
                || payloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                throw new FormatException("U2R2 frame length is invalid.");
            }

            var bodyLength = checked((int)(headerLength + payloadLength));
            var frame = new byte[checked(16 + bodyLength)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            var body = ReadExact(stream, bodyLength);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            var data = new byte[length];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("U2R2 stream closed before the frame completed.");
                offset += read;
            }

            return data;
        }

        private static void WriteUInt32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)((value >> 8) & 0xff);
            data[offset + 2] = (byte)((value >> 16) & 0xff);
            data[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        private sealed class LegacyTransport : IRos2BridgeSink
        {
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public bool IsConnected { get; private set; }
            public void Connect(string host, int port, int timeoutMs) => IsConnected = true;
            public void Send(Ros2BridgeFrame frame, int timeoutMs) => SentFrames.Add(frame);
            public void Disconnect() => IsConnected = false;
            public void Dispose() => Disconnect();
        }

        private static Ros2BridgeFrame Frame(
            string topic,
            string schema,
            FoxRunResolvedQos qos,
            ulong sequence)
            => Ros2BridgeFrame.CreateValidated(
                topic,
                schema,
                "cdr",
                184UL + sequence,
                sequence,
                new byte[] { 0, 1, 0, 0, (byte)sequence },
                qos);
    }
}
