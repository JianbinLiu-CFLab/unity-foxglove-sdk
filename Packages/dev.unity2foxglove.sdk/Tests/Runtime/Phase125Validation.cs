// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 125 validation for typed ROS2 CDR MCAP decode.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase125Validation
    {
        private static readonly string[] ProductizedSchemas =
        {
            "foxglove_msgs/msg/CameraCalibration",
            "foxglove_msgs/msg/CompressedImage",
            "foxglove_msgs/msg/CompressedPointCloud",
            "foxglove_msgs/msg/FrameTransform",
            "foxglove_msgs/msg/LaserScan",
            "foxglove_msgs/msg/PointCloud",
            "foxglove_msgs/msg/SceneUpdate"
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 125: MCAP ROS2 CDR Typed Decode v1 ===");
            _passed = 0;

            VerifyApiSurface();
            VerifyRuntimeReader();
            VerifyGeneratedRegistryParity();
            VerifyAllSchemaRoundTrips();
            VerifyProductizedPayloads();
            VerifyFailureBehavior();
            VerifyDataLoaderIntegration();
            VerifySourceWiring();

            Console.WriteLine($"Phase 125: {_passed} checks passed.");
        }

        private static void VerifyApiSurface()
        {
            Check(typeof(Ros2CdrReader).GetConstructor(new[] { typeof(byte[]) }) != null,
                "125-A1: runtime Ros2CdrReader exists");
            Check(typeof(Ros2CdrReader).GetMethod("ReadString") != null
                  && typeof(Ros2CdrReader).GetMethod("ReadByteArray") != null
                  && typeof(Ros2CdrReader).GetMethod("ReadFloat64Fixed") != null,
                "125-A2: runtime reader exposes string, bytes, fixed-array reads");
            Check(Ros2CdrDeserializerRegistry.DeserializerCount == 41
                  && Ros2CdrDeserializerRegistry.Entries.Count == 41,
                "125-A3: generated deserializer registry declares 41 entries");
            Check(Enum.IsDefined(typeof(McapDecodedPayloadKind), McapDecodedPayloadKind.Ros2CdrTyped)
                  && (int)McapDecodedPayloadKind.Ros2CdrTyped == 6,
                "125-A4: decoded payload kind includes stable Ros2CdrTyped value 6");
        }

        private static void VerifyRuntimeReader()
        {
            var writer = new Ros2CdrWriter();
            writer.WriteBool(true);
            writer.WriteUInt8(7);
            writer.WriteUInt32(123U);
            writer.WriteUInt64(456UL);
            writer.WriteFloat64(1.5);
            writer.WriteString("phase125");
            writer.WriteByteArray(new byte[] { 1, 2, 3 });
            writer.WriteFloat64Sequence(new[] { 2.0, 3.0 });
            writer.WriteUInt32Sequence(new[] { 4U, 5U });
            writer.WriteFloat64Fixed(new[] { 6.0, 7.0, 8.0 }, 3, "fixed");

            var reader = new Ros2CdrReader(writer.ToArray());
            Check(reader.LittleEndian
                  && reader.ReadBool()
                  && reader.ReadUInt8() == 7
                  && reader.ReadUInt32() == 123U
                  && reader.ReadUInt64() == 456UL
                  && Math.Abs(reader.ReadFloat64() - 1.5) < 0.000001
                  && reader.ReadString() == "phase125"
                  && reader.ReadByteArray().SequenceEqual(new byte[] { 1, 2, 3 })
                  && reader.ReadFloat64Sequence().SequenceEqual(new[] { 2.0, 3.0 })
                  && reader.ReadUInt32Sequence().SequenceEqual(new[] { 4U, 5U })
                  && reader.ReadFloat64Fixed(3).SequenceEqual(new[] { 6.0, 7.0, 8.0 }),
                "125-B1: runtime reader mirrors writer primitive and sequence alignment");

            Check(Throws<InvalidDataException>(() => new Ros2CdrReader(new byte[] { 0, 1, 0 })),
                "125-B2: short CDR header fails clearly");
            Check(Throws<NotSupportedException>(() => new Ros2CdrReader(new byte[] { 0, 0, 0, 0 })),
                "125-B3: big-endian CDR header is rejected");
            Check(Throws<EndOfStreamException>(() => new Ros2CdrReader(new byte[] { 0, 1, 0, 0 }).ReadUInt32()),
                "125-B4: reads beyond payload fail clearly");

            var hugeSequence = new byte[] { 0, 1, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F };
            Check(Throws<InvalidDataException>(() => new Ros2CdrReader(hugeSequence).ReadSequenceLength()),
                "125-B5: sequence lengths are bounded by remaining CDR payload bytes before allocation");
        }

        private static void VerifyGeneratedRegistryParity()
        {
            var serializers = Ros2CdrSerializerRegistry.Entries.Select(e => e.SchemaName).OrderBy(v => v, StringComparer.Ordinal).ToList();
            var deserializers = Ros2CdrDeserializerRegistry.Entries.Select(e => e.SchemaName).OrderBy(v => v, StringComparer.Ordinal).ToList();
            Check(serializers.SequenceEqual(deserializers),
                "125-C1: serializer and deserializer schema-name sets are identical");

            foreach (var entry in Ros2CdrDeserializerRegistry.Entries)
            {
                Check(entry.SchemaName.StartsWith("foxglove_msgs/msg/", StringComparison.Ordinal)
                      && typeof(IMessage).IsAssignableFrom(entry.ClrType),
                    "125-C2: deserializer entry maps packaged schema to IMessage type: " + entry.SchemaName);
            }
        }

        private static void VerifyAllSchemaRoundTrips()
        {
            foreach (var serializer in Ros2CdrSerializerRegistry.Entries)
            {
                var sample = serializer.CreateSample();
                var payload = serializer.Serialize(sample);
                Check(Ros2CdrDeserializerRegistry.TryDeserialize(serializer.SchemaName, payload, out var decoded)
                      && decoded != null
                      && decoded.GetType() == serializer.ClrType
                      && sample.Equals(decoded),
                    "125-D1: generated ROS2 CDR round-trip succeeds for " + serializer.SchemaName);
            }
        }

        private static void VerifyProductizedPayloads()
        {
            foreach (var schemaName in ProductizedSchemas)
            {
                var hasSerializer = Ros2CdrSerializerRegistry.TryGetBySchemaName(schemaName, out var serializer);
                var hasDeserializer = Ros2CdrDeserializerRegistry.TryGetBySchemaName(schemaName, out var deserializer);
                Check(hasSerializer && hasDeserializer,
                    "125-E1: productized schema has serializer/deserializer: " + schemaName);
                var sample = serializer.CreateSample();
                var payload = serializer.Serialize(sample);
                var decoded = deserializer.Deserialize(payload);
                Check(decoded.GetType() == serializer.ClrType && sample.Equals(decoded),
                    "125-E2: productized payload typed-decodes: " + schemaName);
            }
        }

        private static void VerifyFailureBehavior()
        {
            Check(!Ros2CdrDeserializerRegistry.TryDeserialize("unknown_msgs/msg/Nope", new byte[] { 0, 1, 0, 0 }, out var unsupported)
                  && unsupported == null,
                "125-F1: unsupported schema returns false from TryDeserialize");
            Check(Throws<InvalidOperationException>(() =>
                    Ros2CdrDeserializerRegistry.Deserialize("unknown_msgs/msg/Nope", new byte[] { 0, 1, 0, 0 })),
                "125-F2: unsupported schema throws from Deserialize");
            Check(Throws<NotSupportedException>(() =>
                    Ros2CdrDeserializerRegistry.Deserialize("foxglove_msgs/msg/Log", new byte[] { 0, 0, 0, 0 })),
                "125-F3: malformed supported payload preserves CDR reader exception");
        }

        private static void VerifyDataLoaderIntegration()
        {
            var path = CreateDecodedFixture();
            using var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests);

            var typed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/typed" }
            }).Single();
            Check(typed.Payload.Kind == McapDecodedPayloadKind.Ros2CdrTyped
                  && typed.Payload.Value is Foxglove.Log typedLog
                  && typedLog.Message == "phase125 typed"
                  && typed.Problems.Count == 0
                  && typed.Payload.RawData.SequenceEqual(typed.Raw.Data),
                "125-G1: decoded DataLoader returns Ros2CdrTyped IMessage for supported ros2msg+cdr");

            var unknown = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/unknown" }
            }).Single();
            var unknownDiagnostic = unknown.Payload.Value as McapRos2CdrDiagnosticPayload;
            Check(unknown.Payload.Kind == McapDecodedPayloadKind.Ros2CdrDiagnostic
                  && unknownDiagnostic != null
                  && !unknownDiagnostic.SchemaKnown,
                "125-G2: unsupported ROS2 schema falls back to diagnostic payload");

            var malformed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/malformed" }
            }).Single();
            Check(malformed.Payload.Kind == McapDecodedPayloadKind.Ros2CdrDiagnostic
                  && malformed.Problems.Count == 1
                  && malformed.Problems[0].Code == "McapRos2CdrTypedDecodeFailed",
                "125-G3: RawWithProblem falls back to diagnostic when typed decode fails");

            Check(Throws<InvalidDataException>(() =>
                    loader.CreateDecodedIterator(
                        new McapDataLoaderQuery { Topics = new List<string> { "/phase125/malformed" } },
                        new McapDecodeOptions { FailurePolicy = McapDecodeFailurePolicy.Throw }).ToList()),
                "125-G4: Throw policy propagates typed decode failures");

            var fallbackFailed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/fallback-failed" }
            }).Single();
            Check(fallbackFailed.Payload.Kind == McapDecodedPayloadKind.Failed
                  && fallbackFailed.Problems.Any(problem => problem.Code == "McapDecodeFailed")
                  && fallbackFailed.Problems.Any(problem => problem.Code == "McapRos2CdrDiagnosticFallbackFailed"),
                "125-G5: diagnostic fallback failures are reported instead of being silently swallowed");
        }

        private static void VerifySourceWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");
            var generator = ReadRepoText("Scripts/schema/generate_ros2_cdr_serializers.py");

            Check(program.Contains("--phase125", StringComparison.Ordinal)
                  && project.Contains("Phase125Validation.cs", StringComparison.Ordinal),
                "125-H1: Program.cs and test project wire --phase125");
            Check(registry.Contains("McapRos2CdrTypedDecoderFactory", StringComparison.Ordinal)
                  && registry.IndexOf("TryCreateRos2CdrTypedFactory", StringComparison.Ordinal) <
                  registry.IndexOf("McapRos2CdrDiagnosticDecoderFactory", StringComparison.Ordinal),
                "125-H2: typed ROS2 CDR factory is installed before diagnostic fallback");
            Check(generator.Contains("generate_deserializers", StringComparison.Ordinal)
                  && generator.Contains("generate_deserializer_registry", StringComparison.Ordinal),
                "125-H3: generator owns deserializer source and registry output");

            var cdrReader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrReader.cs");
            Check(!cdrReader.Contains("CopyEndianBytes", StringComparison.Ordinal)
                  && cdrReader.Contains("BitConverter.ToDouble(_data, _offset)", StringComparison.Ordinal),
                "125-H4: CDR primitive reads avoid per-field endian byte-array allocations on little-endian platforms");
        }

        private static string CreateDecodedFixture()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase125_" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, new McapWriterOptions { UseChunking = false, EnableDataCrcs = true }, leaveOpen: true))
            {
                var log = new Foxglove.Log
                {
                    Level = Foxglove.Log.Types.Level.Info,
                    Message = "phase125 typed",
                    Name = "Phase125Validation",
                    File = "Phase125Validation.cs",
                    Line = 125
                };
                var typedPayload = Ros2CdrSerializerRegistry.Serialize("foxglove_msgs/msg/Log", log);
                recorder.AddChannel(1, "/phase125/typed", "cdr", "foxglove_msgs/msg/Log", FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, string.Empty);
                recorder.WriteMessage(1, 10, typedPayload);

                recorder.AddChannel(2, "/phase125/unknown", "cdr", "unknown_msgs/msg/Nope", FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, string.Empty);
                recorder.WriteMessage(2, 20, new byte[] { 0, 1, 0, 0 });

                recorder.AddChannel(3, "/phase125/malformed", "cdr", "foxglove_msgs/msg/Log", FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, string.Empty);
                recorder.WriteMessage(3, 30, new byte[] { 0, 1, 0, 0 });

                recorder.AddChannel(4, "/phase125/fallback-failed", "cdr", "foxglove_msgs/msg/Log", FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, string.Empty);
                recorder.WriteMessage(4, 40, new byte[] { 0 });

                recorder.Close();
            }

            return path;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !File.Exists(Path.Combine(dir, "README.md")); i++)
                dir = Directory.GetParent(dir)?.FullName ?? dir;
            return dir;
        }

        private static bool Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return true;
            }

            return false;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
