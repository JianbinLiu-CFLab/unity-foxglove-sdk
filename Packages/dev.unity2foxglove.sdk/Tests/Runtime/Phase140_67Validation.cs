// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-67 regression coverage for ROS2 bridge mirror and sidecar optimizations.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Foxglove;
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_67Validation.
    /// </summary>
    public static class Phase140_67Validation
    {
        private const ulong SampleTimeNs = 1_700_140_067_000_000_000UL;
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-67: SDK ROS2 Bridge Mirror and Sidecar Optimization ===");
            _passed = 0;

            VerifyFrameOwnedPayloadPath();
            VerifyFrameWriterStreamOverload();
            VerifyTcpClientTimeoutCachingAndStreaming();
            VerifySidecarPublishPayloadViewAndMaps();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-67: {_passed} checks passed.");
        }

        private static void VerifyFrameOwnedPayloadPath()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            Check(source.Contains("internal static Ros2BridgeFrame CreateOwned", StringComparison.Ordinal)
                  && source.Contains("clonePayload: false", StringComparison.Ordinal)
                  && source.Contains("clonePayload ? (byte[])payload.Clone() : payload", StringComparison.Ordinal),
                "140-67A-1: Ros2BridgeFrame exposes an internal owned-payload construction path");

            var payload = new byte[] { 0, 1, 0, 0, 9, 8, 7 };
            var publicFrame = new Ros2BridgeFrame(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                1,
                payload);
            payload[4] = 0xff;
            using var publicPayload = new MemoryStream();
            publicFrame.WritePayloadTo(publicPayload);
            Check(publicPayload.ToArray()[4] == 9,
                "140-67A-2: public Ros2BridgeFrame constructor keeps defensive payload copy semantics");

            var ownedPayload = new byte[] { 0, 1, 0, 0, 4, 5, 6 };
            var ownedFrame = Ros2BridgeFrame.CreateOwned(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                2,
                ownedPayload);
            using var ownedStream = new MemoryStream();
            ownedFrame.WritePayloadTo(ownedStream);
            Check(ownedStream.ToArray().SequenceEqual(ownedPayload),
                "140-67A-3: internal ROS2 bridge publisher can transfer serializer-owned payload bytes without cloning");

            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Bridge/Ros2BridgePublisher.cs");
            Check(publisher.Contains("Ros2BridgeFrame.CreateOwned", StringComparison.Ordinal)
                  && !publisher.Contains("new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding, logTimeNs, sequence, payload)", StringComparison.Ordinal),
                "140-67A-4: Ros2BridgePublisher uses the internal owned-payload frame path");
        }

        private static void VerifyFrameWriterStreamOverload()
        {
            var frame = SampleFrame();
            var bytes = Ros2BridgeFrameWriter.Write(frame);
            using var stream = new MemoryStream();
            Ros2BridgeFrameWriter.Write(frame, stream);
            Check(stream.ToArray().SequenceEqual(bytes),
                "140-67B-1: stream overload emits byte-identical U2R2 frame output");

            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            Check(source.Contains("internal static void Write(Ros2BridgeFrame frame, Stream destination)", StringComparison.Ordinal)
                  && source.Contains("destination.Write(headerBytes, 0, headerBytes.Length)", StringComparison.Ordinal)
                  && source.Contains("frame.WritePayloadTo(destination)", StringComparison.Ordinal),
                "140-67B-2: frame writer can write directly into a caller-supplied stream");
        }

        private static void VerifyTcpClientTimeoutCachingAndStreaming()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeTcpClient.cs");
            Check(source.Contains("private int _sendTimeoutMs", StringComparison.Ordinal)
                  && source.Contains("socket.SendTimeout = timeoutMs", StringComparison.Ordinal)
                  && source.Contains("_sendTimeoutMs != timeoutMs", StringComparison.Ordinal),
                "140-67C-1: TCP client caches SendTimeout but still honors per-send timeout changes");
            Check(source.Contains("Ros2BridgeFrameWriter.Write(frame, stream)", StringComparison.Ordinal)
                  && !source.Contains("var bytes = Ros2BridgeFrameWriter.Write(frame)", StringComparison.Ordinal)
                  && !source.Contains("socket.Send(bytes", StringComparison.Ordinal),
                "140-67C-2: TCP client streams U2R2 frames without allocating a full wire byte array");
        }

        private static void VerifySidecarPublishPayloadViewAndMaps()
        {
            var source = Read("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            Check(source.Contains("struct PayloadView", StringComparison.Ordinal)
                  && source.Contains("PayloadView payload_for_publish", StringComparison.Ordinal)
                  && source.Contains("frame.payload.data() + 4", StringComparison.Ordinal)
                  && !source.Contains("std::vector<uint8_t> payload_for_publish", StringComparison.Ordinal),
                "140-67D-1: sidecar publishes from a payload view instead of copying payload vectors");
            Check(source.Contains("topic_signature_.emplace(frame.topic, signature)", StringComparison.Ordinal)
                  && !source.Contains("topic_signature_[frame.topic] = signature", StringComparison.Ordinal),
                "140-67D-2: sidecar validates topic signatures with one map insertion path");
            Check(source.Contains("publishers_.find(key)", StringComparison.Ordinal)
                  && !source.Contains("auto publisher = publishers_[key]", StringComparison.Ordinal),
                "140-67D-3: sidecar publisher lookup avoids default-inserting on every message");
            Check(!SourceMethodContains(source, "BridgeFrame parse_publish_frame", "const auto op = raw.header.at(\"op\").get<std::string>()")
                  && SourceMethodContains(source, "void process_client", "const auto op = raw.header.at(\"op\").get<std::string>()"),
                "140-67D-4: publish frame op is extracted only at the process-client dispatch boundary");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_67Validation.cs", StringComparison.Ordinal),
                "140-67E-1: test project compiles Phase140_67Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-67\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_67Validation.Validate", StringComparison.Ordinal),
                "140-67E-2: validation registry exposes --phase140-67");
        }

        private static Ros2BridgeFrame SampleFrame()
            => new Ros2BridgeFrame(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                7,
                new byte[] { 0, 1, 0, 0, 9, 8, 7 });

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static bool SourceMethodContains(string source, string signature, string text)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return false;
            var next = source.IndexOf("\n}", start + signature.Length, StringComparison.Ordinal);
            if (next < 0)
                next = source.Length;
            return source.Substring(start, next - start).Contains(text, StringComparison.Ordinal);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
