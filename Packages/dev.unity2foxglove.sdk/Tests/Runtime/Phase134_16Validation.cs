// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-16 regression coverage for ROS2 bridge frame payload ownership.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_16Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-16: SDK ROS2 Bridge Mirror ===");
            _passed = 0;

            PublicPayloadViewCannotMutateSerializedFrame();
            PublicPayloadViewReturnsFreshDefensiveCopies();
            WriterAndRuntimeUseOwnedPayloadSnapshot();
            PublicPayloadGetterDocumentsCopyCost();
            CommandRunnerDrainsTimedOutProcessOutput();
            HealthRunnerAvoidsHardcodedCatalogCountAndSupportsCancellation();
            RuntimeConnectContractAndWorkerGenerationAreExplicit();
            SidecarResetsPerClientStateAndRejectsMalformedFrames();
            PowerShellPreflightChecksNativeExitCodes();
            HealthProbeValidatesFixedHeaderBeforeAllocation();
            TopicAndFrameWritersRejectNewlinesAndReportSizes();

            Console.WriteLine($"Phase 134-16: {_passed} checks passed.");
        }

        private static void PublicPayloadViewCannotMutateSerializedFrame()
        {
            var originalPayload = new byte[] { 0, 1, 0, 0, 10, 20, 30, 40 };
            var frame = CreateFrame(originalPayload);

            var publicView = frame.Payload;
            publicView[4] = 99;
            publicView[5] = 98;

            var wire = Ros2BridgeFrameWriter.Write(frame);
            var serializedPayload = ExtractPayload(wire);

            Check(serializedPayload.SequenceEqual(originalPayload),
                "134-16A-1: mutating the public payload view cannot affect serialized bridge bytes");
            Check(frame.Payload.SequenceEqual(originalPayload),
                "134-16A-2: frame payload remains the constructor snapshot after external mutation");
        }

        private static void PublicPayloadViewReturnsFreshDefensiveCopies()
        {
            var frame = CreateFrame(new byte[] { 0, 1, 0, 0, 1, 2 });
            var first = frame.Payload;
            var second = frame.Payload;
            first[4] = 200;

            Check(!ReferenceEquals(first, second),
                "134-16B-1: public payload getter returns a fresh copy");
            Check(second[4] == 1 && frame.Payload[4] == 1,
                "134-16B-2: mutating one public payload copy does not affect later reads");
        }

        private static void WriterAndRuntimeUseOwnedPayloadSnapshot()
        {
            var frameSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            var writerSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            var runtimeSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");

            Check(frameSource.Contains("private readonly byte[] _payload", StringComparison.Ordinal)
                  && frameSource.Contains("_payload = (byte[])payload.Clone()", StringComparison.Ordinal)
                  && frameSource.Contains("public byte[] Payload => (byte[])_payload.Clone()", StringComparison.Ordinal),
                "134-16C-1: bridge frame owns a private cloned payload and exposes defensive copies");
            Check(writerSource.Contains("frame.PayloadLength", StringComparison.Ordinal)
                  && writerSource.Contains("frame.WritePayloadTo(stream)", StringComparison.Ordinal)
                  && !writerSource.Contains("stream.Write(frame.Payload", StringComparison.Ordinal),
                "134-16C-2: bridge writer consumes the owned snapshot instead of the public copy");
            Check(runtimeSource.Contains("frame.PayloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes", StringComparison.Ordinal)
                  && !runtimeSource.Contains("frame.Payload.Length > Ros2BridgeFrameWriter.MaxPayloadBytes", StringComparison.Ordinal),
                "134-16C-3: runtime queue size checks use the owned snapshot length");
        }

        private static void PublicPayloadGetterDocumentsCopyCost()
        {
            var frameSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");

            Check(frameSource.Contains("[Obsolete(", StringComparison.Ordinal)
                  && frameSource.Contains("PayloadLength", StringComparison.Ordinal)
                  && frameSource.Contains("WritePayloadTo", StringComparison.Ordinal),
                "134-16D-1: public Payload getter warns callers about per-call clone cost");
        }

        private static void CommandRunnerDrainsTimedOutProcessOutput()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeCommandRunner.cs");

            Check(source.Contains("process.Kill()", StringComparison.Ordinal)
                  && source.Contains("process.WaitForExit(Math.Max(1, timeoutMs))", StringComparison.Ordinal)
                  && source.Contains("process.WaitForExit();", StringComparison.Ordinal),
                "134-16E-1: timed-out ROS2 CLI commands wait for process exit after kill before reading buffered output");
        }

        private static void HealthRunnerAvoidsHardcodedCatalogCountAndSupportsCancellation()
        {
            var optionsSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthOptions.cs");
            var runnerSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthRunner.cs");

            Check(optionsSource.Contains("CancellationToken cancellationToken = default", StringComparison.Ordinal)
                  && optionsSource.Contains("public CancellationToken CancellationToken", StringComparison.Ordinal)
                  && runnerSource.Contains("ThrowIfCancellationRequested(options)", StringComparison.Ordinal),
                "134-16F-1: health diagnostics expose and observe cancellation between bounded checks");
            Check(!runnerSource.Contains("!= 41", StringComparison.Ordinal)
                  && runnerSource.Contains("SourceFileCount != FoxgloveRos2MsgSchemaCatalog.Entries.Count", StringComparison.Ordinal),
                "134-16F-2: health diagnostics compare schema catalog source and entry counts without a hardcoded 41");
        }

        private static void RuntimeConnectContractAndWorkerGenerationAreExplicit()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");

            Check(source.Contains("_workerGeneration", StringComparison.Ordinal)
                  && source.Contains("WorkerLoop(generation)", StringComparison.Ordinal)
                  && source.Contains("generation != _workerGeneration", StringComparison.Ordinal),
                "134-16G-1: bridge runtime invalidates stale workers with generation checks");
            Check(source.Contains("Connect must use the configured host and port", StringComparison.Ordinal)
                  && source.Contains("NormalizeLoopbackHost(host)", StringComparison.Ordinal)
                  && source.Contains("timeoutMs <= 0", StringComparison.Ordinal),
                "134-16G-2: Connect validates host, port, and timeout instead of silently ignoring arguments");
        }

        private static void SidecarResetsPerClientStateAndRejectsMalformedFrames()
        {
            var source = File.ReadAllText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");

            Check(source.Contains("class ScopedFd", StringComparison.Ordinal)
                  && source.Contains("ScopedFd listen_fd", StringComparison.Ordinal)
                  && source.Contains("ScopedFd client_fd", StringComparison.Ordinal),
                "134-16H-1: ROS2 sidecar wraps listen/client sockets in RAII handles");
            Check(source.Contains("BridgeNode bridge(node, options.payload_format);", StringComparison.Ordinal)
                  && source.IndexOf("BridgeNode bridge(node, options.payload_format);", StringComparison.Ordinal)
                  > source.IndexOf("ScopedFd client_fd", StringComparison.Ordinal),
                "134-16H-2: sidecar publisher maps are scoped per client connection");
            Check(!source.Contains("value(\"op\", \"publish\")", StringComparison.Ordinal)
                  && source.Contains("reject frame: missing or invalid op", StringComparison.Ordinal)
                  && source.Contains("topic must not contain newline", StringComparison.Ordinal),
                "134-16H-3: sidecar rejects missing op fields and newline-bearing topics");
            Check(!source.Contains("Phase 94 accepts only IPv4 loopback hosts", StringComparison.Ordinal)
                  && source.Contains("--port must be an integer in 1..65535", StringComparison.Ordinal),
                "134-16H-4: sidecar argument errors are explicit and avoid stale phase wording");
        }

        private static void PowerShellPreflightChecksNativeExitCodes()
        {
            var source = File.ReadAllText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1");

            Check(source.Contains("function Invoke-Ros2Checked", StringComparison.Ordinal)
                  && source.Contains("$LASTEXITCODE", StringComparison.Ordinal)
                  && source.Contains("ROS2 Bridge launch failed with exit code", StringComparison.Ordinal),
                "134-16I-1: PowerShell sample preflight fails on nonzero ros2 command exit codes");
        }

        private static void HealthProbeValidatesFixedHeaderBeforeAllocation()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs");

            Check(source.Contains("U2R2 response magic is invalid", StringComparison.Ordinal)
                  && source.Contains("U2R2 response version is unsupported", StringComparison.Ordinal)
                  && source.IndexOf("var headerLength = ReadUInt32LE", StringComparison.Ordinal)
                  > source.IndexOf("U2R2 response flags must be zero", StringComparison.Ordinal),
                "134-16J-1: health probe validates U2R2 fixed header before allocating variable sections");
        }

        private static void TopicAndFrameWritersRejectNewlinesAndReportSizes()
        {
            var frameSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            var writerSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            var topicProfileSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeTopicProfile.cs");

            Check(frameSource.Contains("topic.IndexOf('\\r')", StringComparison.Ordinal)
                  && topicProfileSource.Contains("ContainsNewline", StringComparison.Ordinal)
                  && topicProfileSource.Contains("new char[value.Length]", StringComparison.Ordinal)
                  && !topicProfileSource.Contains("while (value.Contains(\"//\"))", StringComparison.Ordinal),
                "134-16K-1: C# bridge topic normalization rejects newlines and collapses slashes in one pass");
            Check(writerSource.Contains("frame.PayloadLength} bytes", StringComparison.Ordinal)
                  && writerSource.Contains("headerBytes.Length} bytes", StringComparison.Ordinal)
                  && !writerSource.Contains("Phase 94 maximum", StringComparison.Ordinal),
                "134-16K-2: frame writer oversize errors include actual sizes and avoid stale phase wording");
        }

        private static Ros2BridgeFrame CreateFrame(byte[] payload)
        {
            return new Ros2BridgeFrame(
                "/unity2foxglove/test",
                "foxglove_msgs/msg/Log",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 123,
                sequence: 7,
                payload);
        }

        private static byte[] ExtractPayload(byte[] wire)
        {
            var headerLength = ReadUInt32LE(wire, 8);
            var payloadLength = ReadUInt32LE(wire, 12);
            var payloadStart = 16 + checked((int)headerLength);
            var payload = new byte[checked((int)payloadLength)];
            Buffer.BlockCopy(wire, payloadStart, payload, 0, payload.Length);
            return payload;
        }

        private static uint ReadUInt32LE(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                          | (bytes[offset + 1] << 8)
                          | (bytes[offset + 2] << 16)
                          | (bytes[offset + 3] << 24));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
