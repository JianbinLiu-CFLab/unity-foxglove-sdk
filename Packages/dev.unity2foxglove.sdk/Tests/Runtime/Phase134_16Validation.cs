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
