// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 94 validation for the localhost Unity-to-ROS2 bridge spike.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Foxglove;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase94Validation
    {
        private const ulong SampleTimeNs = 1_700_094_000_000_000_000UL;
        private const int LoopbackTimeoutMs = 10_000;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 94: Unity To ROS2 Bridge Spike ===");
            _passed = 0;

            VerifyFrameWriter();
            VerifyFrameValidation();
            VerifyTcpClient();
            VerifyPublisherWrapper();
            VerifyBridgeSendSamples();
            VerifySourceBoundaries();

            Console.WriteLine($"Phase 94: {_passed} checks passed.");
        }

        public static void RunBridgeSendSmoke(string host, int port)
        {
            using var sink = new Ros2BridgeTcpClient();
            sink.Connect(host, port, timeoutMs: 3000);
            Console.WriteLine($"[phase94] connected {host}:{port}");

            var publisher = new Ros2BridgePublisher(sink);
            foreach (var sample in BuildGateBSamples())
            {
                for (var i = 0; i < 20; i++)
                    publisher.Publish(sample.Topic, sample.SchemaName, sample.Message, SampleTimeNs + (ulong)i);
                Console.WriteLine($"[phase94] sent {sample.Topic} {sample.SchemaName} count=20");
            }

            Console.WriteLine("[phase94] PASS bridge send smoke");
        }

        private static void VerifyFrameWriter()
        {
            var frame = new Ros2BridgeFrame(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                "cdr",
                SampleTimeNs,
                sequence: 7,
                new byte[] { 0, 1, 0, 0, 9, 8, 7 });

            var bytes = Ros2BridgeFrameWriter.Write(frame);
            Check(bytes.Length > 16, "94A-1: frame writer emits header and body");
            Check(bytes[0] == (byte)'U' && bytes[1] == (byte)'2' && bytes[2] == (byte)'R' && bytes[3] == (byte)'2',
                "94A-2: frame magic is U2R2");
            Check(ReadUInt16(bytes, 4) == 1, "94A-3: frame version is little-endian 1");
            Check(ReadUInt16(bytes, 6) == 0, "94A-4: frame flags are little-endian 0");

            var headerLength = ReadUInt32(bytes, 8);
            var payloadLength = ReadUInt32(bytes, 12);
            Check(headerLength > 0 && headerLength <= Ros2BridgeFrameWriter.MaxHeaderBytes,
                "94A-5: frame header length is bounded");
            Check(payloadLength == frame.Payload.Length, "94A-6: frame payload length matches payload");

            var headerJson = Encoding.UTF8.GetString(bytes, 16, checked((int)headerLength));
            var header = JObject.Parse(headerJson);
            Check(header["topic"]?.ToString() == "/unity/tf", "94A-7: JSON header contains topic");
            Check(header["schemaName"]?.ToString() == "foxglove_msgs/msg/FrameTransform",
                "94A-8: JSON header contains schemaName");
            Check(header["encoding"]?.ToString() == "cdr", "94A-9: JSON header contains cdr encoding");
            Check(header["logTimeNs"]?.Value<ulong>() == SampleTimeNs, "94A-10: JSON header contains logTimeNs");
            Check(header["sequence"]?.Value<ulong>() == 7UL, "94A-11: JSON header contains sequence");
            Check(frame.Payload.SequenceEqual(bytes.Skip(16 + checked((int)headerLength))),
                "94A-12: payload bytes are appended unchanged");
        }

        private static void VerifyFrameValidation()
        {
            Check(Throws<ArgumentException>(() => new Ros2BridgeFrame("unity/tf", "foxglove_msgs/msg/FrameTransform", "cdr", 1, 1, new byte[] { 1 })),
                "94B-1: frame rejects topics without leading slash");
            Check(Throws<ArgumentException>(() => new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/Missing", "cdr", 1, 1, new byte[] { 1 })),
                "94B-2: frame rejects unknown ROS2 schema names");
            Check(Throws<ArgumentException>(() => new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", "json", 1, 1, new byte[] { 1 })),
                "94B-3: frame rejects non-cdr encoding");
            Check(Throws<ArgumentException>(() => new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", "cdr", 1, 1, Array.Empty<byte>())),
                "94B-4: frame rejects empty payload");
            Check(Throws<ArgumentException>(() => Ros2BridgeFrameWriter.Write(new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", "cdr", 1, 1, new byte[Ros2BridgeFrameWriter.MaxPayloadBytes + 1]))),
                "94B-5: frame writer rejects oversized payloads");
            Check(Throws<ArgumentException>(() => Ros2BridgeTcpClient.ValidateLoopbackHost("0.0.0.0")),
                "94B-6: bridge rejects wildcard host");
            Check(Throws<ArgumentException>(() => Ros2BridgeTcpClient.ValidateLoopbackHost("192.168.1.10")),
                "94B-7: bridge rejects LAN host");
        }

        private static void VerifyTcpClient()
        {
            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var port = ((IPEndPoint)server.LocalEndpoint).Port;

            var received = new List<Ros2BridgeFrameCapture>();
            Exception serverError = null;
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try
                {
                    using var client = server.AcceptTcpClient();
                    using var stream = client.GetStream();
                    received.Add(ReadFrameCapture(stream));
                }
                catch (Exception ex)
                {
                    serverError = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            thread.IsBackground = true;
            thread.Start();

            using (var sink = new Ros2BridgeTcpClient())
            {
                sink.Connect("127.0.0.1", port, timeoutMs: LoopbackTimeoutMs);
                Check(sink.IsConnected, "94C-1: TCP client connects to loopback server");
                var frame = new Ros2BridgeFrame(
                    "/unity/tf",
                    "foxglove_msgs/msg/FrameTransform",
                    "cdr",
                    SampleTimeNs,
                    1,
                    new byte[] { 0, 1, 0, 0, 1, 2, 3 });
                sink.Send(frame, timeoutMs: LoopbackTimeoutMs);
                sink.Disconnect();
                Check(!sink.IsConnected, "94C-2: TCP client disconnect closes socket state");
            }

            Check(done.Wait(LoopbackTimeoutMs), "94C-3: loopback server receives one frame");
            if (serverError != null)
                throw new Exception("94C server failed: " + serverError.Message, serverError);
            Check(received.Count == 1 && received[0].Topic == "/unity/tf",
                "94C-4: loopback server decodes sent frame topic");
            Check(received[0].SchemaName == "foxglove_msgs/msg/FrameTransform" && received[0].Payload.SequenceEqual(new byte[] { 0, 1, 0, 0, 1, 2, 3 }),
                "94C-5: loopback server decodes schema and payload");

            using var disconnected = new Ros2BridgeTcpClient();
            Check(Throws<InvalidOperationException>(() => disconnected.Send(
                new Ros2BridgeFrame("/unity/tf", "foxglove_msgs/msg/FrameTransform", "cdr", 1, 1, new byte[] { 1 }),
                timeoutMs: 1)),
                "94C-6: send while disconnected throws");
        }

        private static void VerifyPublisherWrapper()
        {
            var sink = new FakeBridgeSink();
            var publisher = new Ros2BridgePublisher(sink);
            var sample = CreateFrameTransformSample();
            publisher.Publish("/unity/tf", "foxglove_msgs/msg/FrameTransform", sample, SampleTimeNs);

            Check(sink.SentFrames.Count == 1, "94D-1: publisher sends one bridge frame");
            var frame = sink.SentFrames[0];
            Check(frame.Topic == "/unity/tf" && frame.SchemaName == "foxglove_msgs/msg/FrameTransform",
                "94D-2: publisher preserves topic and schema");
            Check(frame.Payload.Length > 4 && frame.Payload[0] == 0 && frame.Payload[1] == 1,
                "94D-3: publisher payload starts with CDR encapsulation header");
            Check(Throws<InvalidOperationException>(() => publisher.Publish("/unity/tf", "foxglove_msgs/msg/Missing", sample, SampleTimeNs)),
                "94D-4: publisher rejects unknown schema");
            Check(Throws<InvalidOperationException>(() => publisher.Publish("/unity/tf", "foxglove_msgs/msg/LaserScan", sample, SampleTimeNs)),
                "94D-5: publisher rejects CLR type mismatch");
            Check(Throws<ArgumentNullException>(() => publisher.Publish("/unity/tf", "foxglove_msgs/msg/FrameTransform", null, SampleTimeNs)),
                "94D-6: publisher rejects null messages");
        }

        private static void VerifyBridgeSendSamples()
        {
            var samples = BuildGateBSamples();
            Check(samples.Count == 3, "94E-1: Gate B sender has three representative samples");
            Check(samples.Any(s => s.Topic == "/unity/tf" && s.SchemaName == "foxglove_msgs/msg/FrameTransform"),
                "94E-2: Gate B includes FrameTransform");
            Check(samples.Any(s => s.Topic == "/unity/laser_scan" && s.SchemaName == "foxglove_msgs/msg/LaserScan"),
                "94E-3: Gate B includes LaserScan");
            Check(samples.Any(s => s.Topic == "/unity/point_cloud" && s.SchemaName == "foxglove_msgs/msg/PointCloud"),
                "94E-4: Gate B includes PointCloud");
            foreach (var sample in samples)
            {
                var payload = Ros2CdrSerializerRegistry.Serialize(sample.SchemaName, sample.Message);
                Check(payload.Length > 4 && payload[0] == 0 && payload[1] == 1,
                    "94E-5: Gate B sample serializes to CDR " + sample.SchemaName);
            }
        }

        private static void VerifySourceBoundaries()
        {
            var managerSource = ReadRepoDirectoryText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager", "FoxgloveManager*.cs");
            var docs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md");
            var readme = ReadRepoText("README.md");
            var sidecarSource = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            var sidecarCmake = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/CMakeLists.txt");
            var sidecarReadme = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");
            Check(managerSource.Contains("TryPrepareRos2BridgePublish") && managerSource.Contains("PublishRos2BridgeCdr"),
                "94F-1: Manager exposes ROS2 Bridge through explicit opt-in APIs");
            Check(docs.Contains("Phase 94") && docs.Contains("three representative"),
                "94F-2: schema coverage docs describe Phase94 bridge boundary");
            Check(readme.Contains("Unity2Foxglove does not require ROS") || readme.Contains("does not require ROS"),
                "94F-3: README keeps no-ROS default positioning");
            Check(sidecarSource.Contains("read_exact") && sidecarSource.Contains("nlohmann") && sidecarSource.Contains("create_generic_publisher"),
                "94F-4: sidecar source uses read_exact, nlohmann_json, and GenericPublisher");
            Check(sidecarSource.Contains("0.0.0.0") && sidecarSource.Contains("reject") && sidecarSource.Contains("cdr-body-only"),
                "94F-5: sidecar source documents loopback rejection and payload-format switch");
            Check(sidecarCmake.Contains("nlohmann_json") && sidecarCmake.Contains("rclcpp"),
                "94F-6: sidecar CMake declares rclcpp and nlohmann_json");
            Check(sidecarReadme.Contains("ros2 interface show") && sidecarReadme.Contains("nlohmann-json3-dev"),
                "94F-7: sidecar README documents ROS2 preflight and JSON dependency");
        }

        private static IReadOnlyList<BridgeSample> BuildGateBSamples()
        {
            return new[]
            {
                new BridgeSample("/unity/tf", "foxglove_msgs/msg/FrameTransform", CreateFrameTransformSample()),
                new BridgeSample("/unity/laser_scan", "foxglove_msgs/msg/LaserScan", CreateLaserScanSample()),
                new BridgeSample("/unity/point_cloud", "foxglove_msgs/msg/PointCloud", CreatePointCloudSample())
            };
        }

        private static FrameTransform CreateFrameTransformSample()
        {
            return new FrameTransform
            {
                Timestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = 1_700_094_000L, Nanos = 123_000_000 },
                ParentFrameId = "world",
                ChildFrameId = "base_link",
                Translation = new Vector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new Quaternion { W = 1 }
            };
        }

        private static LaserScan CreateLaserScanSample()
        {
            var scan = new LaserScan
            {
                Timestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = 1_700_094_000L, Nanos = 456_000_000 },
                FrameId = "laser",
                Pose = new Pose
                {
                    Position = new Vector3(),
                    Orientation = new Quaternion { W = 1 }
                },
                StartAngle = -1.57,
                EndAngle = 1.57
            };
            scan.Ranges.Add(new[] { 1.0, 1.5, 2.0 });
            scan.Intensities.Add(new[] { 10.0, 20.0, 30.0 });
            return scan;
        }

        private static PointCloud CreatePointCloudSample()
        {
            var cloud = new PointCloud
            {
                Timestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = 1_700_094_000L, Nanos = 789_000_000 },
                FrameId = "lidar",
                Pose = new Pose
                {
                    Position = new Vector3(),
                    Orientation = new Quaternion { W = 1 }
                },
                PointStride = 12,
                Data = Google.Protobuf.ByteString.CopyFrom(new byte[]
                {
                    0, 0, 128, 63, 0, 0, 0, 64, 0, 0, 64, 64,
                    0, 0, 128, 64, 0, 0, 160, 64, 0, 0, 192, 64
                })
            };
            cloud.Fields.Add(new PackedElementField { Name = "x", Offset = 0, Type = PackedElementField.Types.NumericType.Float32 });
            cloud.Fields.Add(new PackedElementField { Name = "y", Offset = 4, Type = PackedElementField.Types.NumericType.Float32 });
            cloud.Fields.Add(new PackedElementField { Name = "z", Offset = 8, Type = PackedElementField.Types.NumericType.Float32 });
            return cloud;
        }

        private static Ros2BridgeFrameCapture ReadFrameCapture(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            if (fixedHeader[0] != (byte)'U' || fixedHeader[1] != (byte)'2' || fixedHeader[2] != (byte)'R' || fixedHeader[3] != (byte)'2')
                throw new InvalidDataException("Bad magic.");
            var headerLength = checked((int)ReadUInt32(fixedHeader, 8));
            var payloadLength = checked((int)ReadUInt32(fixedHeader, 12));
            var headerJson = Encoding.UTF8.GetString(ReadExact(stream, headerLength));
            var payload = ReadExact(stream, payloadLength);
            var header = JObject.Parse(headerJson);
            return new Ros2BridgeFrameCapture(
                header["topic"]?.ToString(),
                header["schemaName"]?.ToString(),
                header["encoding"]?.ToString(),
                header["logTimeNs"]?.Value<ulong>() ?? 0,
                header["sequence"]?.Value<ulong>() ?? 0,
                payload);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            return buffer;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private static string ReadRepoDirectoryText(string relativePath, string searchPattern)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);

            var files = Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray();
            if (files.Length == 0)
                throw new FileNotFoundException("Required validation source files were not found.", Path.Combine(path, searchPattern));

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        private sealed class FakeBridgeSink : IRos2BridgeSink
        {
            public readonly List<Ros2BridgeFrame> SentFrames = new List<Ros2BridgeFrame>();
            public bool IsConnected => true;
            public void Connect(string host, int port, int timeoutMs) { }
            public void Send(Ros2BridgeFrame frame, int timeoutMs) => SentFrames.Add(frame);
            public void Disconnect() { }
            public void Dispose() { }
        }

        private sealed class BridgeSample
        {
            public BridgeSample(string topic, string schemaName, IMessage message)
            {
                Topic = topic;
                SchemaName = schemaName;
                Message = message;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public IMessage Message { get; }
        }

        private sealed class Ros2BridgeFrameCapture
        {
            public Ros2BridgeFrameCapture(string topic, string schemaName, string encoding, ulong logTimeNs, ulong sequence, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Encoding = encoding;
                LogTimeNs = logTimeNs;
                Sequence = sequence;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public string Encoding { get; }
            public ulong LogTimeNs { get; }
            public ulong Sequence { get; }
            public byte[] Payload { get; }
        }
    }
}
