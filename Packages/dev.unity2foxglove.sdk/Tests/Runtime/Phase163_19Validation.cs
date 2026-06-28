// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-19 validation for ROS2 CDR writer and generator review fixes.

using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_19Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-19: ROS2 CDR Writers and Generator Integration ===");
            _passed = 0;

            CdrWriterReaderCoversSixteenBitPrimitives();
            CdrWriterRejectsNullRequiredSequences();
            GeneratedRegistryCountsFollowSchemaCatalog();
            GeneratorSupportsFuturePrimitivesAndGeometrySequences();
            GeneratedCdrFilesLiveOutsideProtoDirectory();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-19: {_passed} checks passed.");
        }

        private static void CdrWriterReaderCoversSixteenBitPrimitives()
        {
            var writer = new Ros2CdrWriter();
            writer.WriteUInt8(0xAB);
            writer.WriteInt16(-1234);
            writer.WriteUInt16(4567);
            var reader = new Ros2CdrReader(writer.ToArray());

            Check(reader.ReadUInt8() == 0xAB,
                "163-19A-1: CDR reader consumes the initial unaligned uint8 field");
            Check(reader.ReadInt16() == -1234,
                "163-19A-2: CDR writer/reader round-trips int16 with CDR alignment");
            Check(reader.ReadUInt16() == 4567,
                "163-19A-3: CDR writer/reader round-trips uint16 with CDR alignment");
        }

        private static void CdrWriterRejectsNullRequiredSequences()
        {
            var writer = new Ros2CdrWriter();
            Check(Throws<ArgumentNullException>(() => writer.WriteByteArray((byte[])null)),
                "163-19B-1: CDR writer rejects null required byte arrays");
            Check(Throws<ArgumentNullException>(() => writer.WriteFloat64Sequence(null)),
                "163-19B-2: CDR writer rejects null required float64 sequences");
        }

        private static void GeneratedRegistryCountsFollowSchemaCatalog()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.SourceFileCount == Ros2CdrSerializerRegistry.SerializerCount,
                "163-19C-1: serializer registry count follows the schema catalog count");
            Check(Ros2CdrSerializerRegistry.Entries.Count == Ros2CdrSerializerRegistry.SerializerCount,
                "163-19C-2: serializer registry exposes its declared count");
            Check(Ros2CdrDeserializerRegistry.DeserializerCount == FoxgloveRos2MsgSchemaCatalog.SourceFileCount,
                "163-19C-3: deserializer registry count follows the schema catalog count");
        }

        private static void GeneratorSupportsFuturePrimitivesAndGeometrySequences()
        {
            var generator = ReadRepoText("Scripts/schema/generate_ros2_cdr_serializers.py");
            var regression = ReadRepoText("Scripts/schema/regression_checks/test_schema_tooling.py");

            Check(generator.Contains("\"int16\"", StringComparison.Ordinal)
                  && generator.Contains("writer.WriteInt16((short)", StringComparison.Ordinal)
                  && generator.Contains("reader.ReadInt16()", StringComparison.Ordinal)
                  && generator.Contains("writer.WriteFloat32", StringComparison.Ordinal),
                "163-19D-1: CDR generator covers additional ROS2 primitive scalar types");
            Check(generator.Contains("WriteProtoVector3(writer, item);", StringComparison.Ordinal)
                  && generator.Contains("WriteProtoQuaternion(writer, item);", StringComparison.Ordinal)
                  && generator.Contains("ReadProtoQuaternion(reader)", StringComparison.Ordinal),
                "163-19D-2: CDR generator supports repeated Vector3 and Quaternion fields");
            Check(generator.Contains("if (value == null)", StringComparison.Ordinal)
                  && !generator.Contains("value?.W ?? 1.0", StringComparison.Ordinal),
                "163-19D-3: generated Quaternion writer rejects null instead of silently writing identity");
            Check(generator.Contains("Fixed float64 sample field must declare a positive length", StringComparison.Ordinal),
                "163-19D-4: generator rejects zero-length fixed float64 sample fields clearly");
            Check(regression.Contains("test_cdr_generator_supports_future_geometry_sequences", StringComparison.Ordinal)
                  && regression.Contains("test_cdr_generator_supports_future_scalar_primitives", StringComparison.Ordinal)
                  && regression.Contains("test_cdr_generator_rejects_zero_length_fixed_samples", StringComparison.Ordinal),
                "163-19D-5: schema tooling regression tests cover generator compatibility fixes");
        }

        private static void GeneratedCdrFilesLiveOutsideProtoDirectory()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var newPath = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Ros2Msg",
                "Generated",
                "Ros2CdrGeneratedSerializers.g.cs");
            var oldPath = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Ros2Msg",
                "Generated",
                "Ros2CdrGeneratedSerializers.g.cs");
            var generated = File.ReadAllText(newPath, Encoding.UTF8);

            Check(File.Exists(newPath),
                "163-19E-1: generated CDR serializers live under Runtime/Schemas/Ros2Msg/Generated");
            Check(!File.Exists(oldPath),
                "163-19E-2: generated CDR serializers no longer live under Runtime/Schemas/Proto/Ros2Msg/Generated");
            Check(generated.Contains("Module: Runtime/Schemas/Ros2Msg/Generated", StringComparison.Ordinal),
                "163-19E-3: generated CDR module comments match the package directory");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_19Validation.cs", StringComparison.Ordinal),
                "163-19F-1: runtime test project compiles Phase163_19Validation");
            Check(registry.Contains("--phase163-19", StringComparison.Ordinal)
                  && registry.Contains("Phase163_19Validation.Validate", StringComparison.Ordinal),
                "163-19F-2: validation registry exposes --phase163-19");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static bool Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
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
