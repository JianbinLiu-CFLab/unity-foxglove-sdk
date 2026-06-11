// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-66 regression coverage for ROS2 CDR writer and generated serializer optimizations.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_66Validation.
    /// </summary>
    public static class Phase140_66Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-66: ROS2 CDR Schema and Standard Message Optimization ===");
            _passed = 0;

            Ros2CdrWriterAvoidsTemporaryScalarAndStringArrays();
            Ros2CdrWriterPreservesAlignmentAndPayloadBytes();
            GeneratedSerializersUseByteStringSpans();
            ManualBuildersUseCapacityAndReadOnlyLists();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-66: {_passed} checks passed.");
        }

        private static void Ros2CdrWriterAvoidsTemporaryScalarAndStringArrays()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs");

            Check(source.Contains("private byte[] _buffer", StringComparison.Ordinal)
                  && source.Contains("private int _position", StringComparison.Ordinal)
                  && source.Contains("EnsureCapacity", StringComparison.Ordinal),
                "140-66A-1: Ros2CdrWriter uses a reusable byte buffer with an explicit position");
            Check(source.Contains("BinaryPrimitives.WriteInt32LittleEndian", StringComparison.Ordinal)
                  && source.Contains("BinaryPrimitives.WriteInt64LittleEndian", StringComparison.Ordinal)
                  && source.Contains("BitConverter.SingleToInt32Bits", StringComparison.Ordinal)
                  && source.Contains("BitConverter.DoubleToInt64Bits", StringComparison.Ordinal)
                  && !source.Contains("BitConverter.GetBytes", StringComparison.Ordinal),
                "140-66A-2: scalar writes avoid BitConverter.GetBytes temporary arrays");
            Check(source.Contains("Encoding.UTF8.GetByteCount(value)", StringComparison.Ordinal)
                  && source.Contains("Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position)", StringComparison.Ordinal)
                  && !source.Contains("Encoding.UTF8.GetBytes(value ?? string.Empty)", StringComparison.Ordinal),
                "140-66A-3: string writes encode directly into the writer buffer");
            Check(source.Contains("public void WriteByteArray(ReadOnlySpan<byte> value)", StringComparison.Ordinal),
                "140-66A-4: Ros2CdrWriter exposes a span byte-array writer overload");
        }

        private static void Ros2CdrWriterPreservesAlignmentAndPayloadBytes()
        {
            var scalarWriter = new Ros2CdrWriter();
            scalarWriter.WriteUInt8(0x7f);
            scalarWriter.WriteUInt32(0x01020304);
            Check(scalarWriter.ToArray().SequenceEqual(new byte[]
                {
                    0x00, 0x01, 0x00, 0x00,
                    0x7f, 0x00, 0x00, 0x00,
                    0x04, 0x03, 0x02, 0x01
                }),
                "140-66B-1: scalar writes preserve CDR alignment and little-endian bytes");

            var stringWriter = new Ros2CdrWriter();
            stringWriter.WriteString("A");
            Check(stringWriter.ToArray().SequenceEqual(new byte[]
                {
                    0x00, 0x01, 0x00, 0x00,
                    0x02, 0x00, 0x00, 0x00,
                    0x41, 0x00
                }),
                "140-66B-2: string writes preserve length including trailing NUL");

            var bytes = new byte[] { 0x01, 0x02, 0x03 };
            var byteWriter = new Ros2CdrWriter();
            byteWriter.WriteByteArray(bytes.AsSpan());
            Check(byteWriter.ToArray().SequenceEqual(new byte[]
                {
                    0x00, 0x01, 0x00, 0x00,
                    0x03, 0x00, 0x00, 0x00,
                    0x01, 0x02, 0x03
                }),
                "140-66B-3: span byte-array writes preserve sequence length and payload bytes");
        }

        private static void GeneratedSerializersUseByteStringSpans()
        {
            var generated = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs");
            var generator = Read("Scripts/schema/generate_ros2_cdr_serializers.py");
            var generatorTests = Read("Scripts/tests/test_generator_build_performance_scripts.py");

            Check(!generated.Contains(".ToByteArray()", StringComparison.Ordinal)
                  && generated.Contains("ReadOnlySpan<byte>.Empty", StringComparison.Ordinal)
                  && generated.Contains(".Data.Span", StringComparison.Ordinal),
                "140-66C-1: generated serializers write ByteString data through spans");
            Check(generator.Contains("ReadOnlySpan<byte>.Empty", StringComparison.Ordinal)
                  && generator.Contains(".Span", StringComparison.Ordinal)
                  && !generator.Contains("?.ToByteArray() ?? Array.Empty<byte>()", StringComparison.Ordinal),
                "140-66C-2: ROS2 CDR generator owns the ByteString span output pattern");
            Check(generated.Contains("var writer = new Ros2CdrWriter(256)", StringComparison.Ordinal)
                  && generator.Contains("var writer = new Ros2CdrWriter(256)", StringComparison.Ordinal),
                "140-66C-3: generated serializers use a baseline writer capacity hint");
            Check(generatorTests.Contains("writer.WriteByteArray(message.Data == null ? ReadOnlySpan<byte>.Empty : message.Data.Span)", StringComparison.Ordinal),
                "140-66C-4: generator regression tests expect the span output pattern");
        }

        private static void ManualBuildersUseCapacityAndReadOnlyLists()
        {
            var frame = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrFrameTransformBuilder.cs");
            var scene = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs");
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrCameraCalibrationBuilder.cs");

            Check(frame.Contains("new Ros2CdrWriter(128)", StringComparison.Ordinal)
                  && scene.Contains("new Ros2CdrWriter(EstimateCapacity(message))", StringComparison.Ordinal),
                "140-66D-1: FrameTransform and SceneUpdate builders provide writer capacity hints");
            Check(camera.Contains("private static IReadOnlyList<double> ToListOrEmpty", StringComparison.Ordinal)
                  && camera.Contains("return Array.Empty<double>()", StringComparison.Ordinal)
                  && !camera.Contains("using System.Linq", StringComparison.Ordinal)
                  && !camera.Contains(".ToList()", StringComparison.Ordinal),
                "140-66D-2: CameraCalibration avoids List wrapper allocations for read-only calibration arrays");
            Check(camera.Contains("IReadOnlyCollection<double> k", StringComparison.Ordinal),
                "140-66D-3: CameraCalibration validation signature matches the read-only list helper");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_66Validation.cs", StringComparison.Ordinal),
                "140-66E-1: test project compiles Phase140_66Validation");
            Check(registry.Contains("Ci(\"--phase140-66\", \"Phase 140-66\", Phase140_66Validation.Validate", StringComparison.Ordinal),
                "140-66E-2: validation registry exposes --phase140-66");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
