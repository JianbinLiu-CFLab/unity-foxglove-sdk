using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_19Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-19 Tests ---");
            _passed = 0;

            VerifyCdrWriterHotPathOptimizations();
            VerifyGeneratedSerializersUseCapacityHints();
            VerifyGeometryWritersExposeUncheckedInternalPath();
            VerifyRegistry();

            Console.WriteLine("Phase 164-19: " + _passed + " checks passed.\n");
        }

        private static void VerifyCdrWriterHotPathOptimizations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrWriter.cs");
            var writeString = PhaseValidationSourceHelpers.SourceMethod(source, "public void WriteString");
            var align = PhaseValidationSourceHelpers.SourceMethod(source, "private void Align");
            var ensureCapacity = PhaseValidationSourceHelpers.SourceMethod(source, "private void EnsureCapacity");

            Check(writeString.Contains("Encoding.UTF8.GetMaxByteCount(value.Length)", StringComparison.Ordinal)
                  && writeString.Contains("var lengthPosition = _position;", StringComparison.Ordinal)
                  && writeString.Contains("BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(lengthPosition, 4)", StringComparison.Ordinal)
                  && !writeString.Contains("Encoding.UTF8.GetByteCount(value)", StringComparison.Ordinal),
                "164-19A-1: CDR string writer encodes UTF-8 once and back-patches the length");
            Check(align.Contains("_buffer.AsSpan(_position, padding).Clear();", StringComparison.Ordinal)
                  && !align.Contains("Array.Clear(_buffer, _position, padding)", StringComparison.Ordinal),
                "164-19A-2: CDR alignment padding uses span clearing");
            Check(ensureCapacity.Contains("Math.Max(checked(_buffer.Length * 2), required)", StringComparison.Ordinal)
                  && !ensureCapacity.Contains("while (newLength < required)", StringComparison.Ordinal),
                "164-19A-3: CDR buffer growth computes the next capacity without a loop");
        }

        private static void VerifyGeneratedSerializersUseCapacityHints()
        {
            var generator = Read("Scripts/schema/generate_ros2_cdr_serializers.py");
            var generated = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs");

            Check(generator.Contains("def capacity_hint_for_schema(schema: Schema) -> int:", StringComparison.Ordinal)
                  && generator.Contains("fixed_size_floor(field)", StringComparison.Ordinal)
                  && generator.Contains("new Ros2CdrWriter({capacity_hint_for_schema(schema)})", StringComparison.Ordinal),
                "164-19B-1: CDR generator computes per-schema writer capacity hints");
            Check(generated.Contains("new Ros2CdrWriter(64)", StringComparison.Ordinal)
                  && generated.Contains("new Ros2CdrWriter(9488)", StringComparison.Ordinal)
                  && generated.Contains("new Ros2CdrWriter(240)", StringComparison.Ordinal),
                "164-19B-2: generated serializers contain small, medium, and large capacity hints");

            var protoVector3 = PhaseValidationSourceHelpers.SourceMethod(generated, "private static void WriteProtoVector3");
            var protoQuaternion = PhaseValidationSourceHelpers.SourceMethod(generated, "private static void WriteProtoQuaternion");
            var protoPose = PhaseValidationSourceHelpers.SourceMethod(generated, "private static void WriteProtoPose");
            Check(!protoVector3.Contains("if (writer == null)", StringComparison.Ordinal)
                  && !protoQuaternion.Contains("if (writer == null)", StringComparison.Ordinal)
                  && !protoPose.Contains("if (writer == null)", StringComparison.Ordinal),
                "164-19B-3: generated nested geometry helpers skip redundant writer null checks");
        }

        private static void VerifyGeometryWritersExposeUncheckedInternalPath()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Cdr/Ros2CdrGeometryWriter.cs");
            var writeVector3 = PhaseValidationSourceHelpers.SourceMethod(source, "public static void WriteVector3");
            var writeQuaternion = PhaseValidationSourceHelpers.SourceMethod(source, "public static void WriteQuaternion");
            var writePose = PhaseValidationSourceHelpers.SourceMethod(source, "public static void WritePose");

            Check(source.Contains("internal static void WriteVector3Unchecked", StringComparison.Ordinal)
                  && source.Contains("internal static void WriteQuaternionUnchecked", StringComparison.Ordinal),
                "164-19C-1: shared geometry writer exposes internal no-null-check helpers");
            Check(writeVector3.Contains("WriteVector3Unchecked(writer, value);", StringComparison.Ordinal)
                  && writeQuaternion.Contains("WriteQuaternionUnchecked(writer, value);", StringComparison.Ordinal)
                  && writePose.Contains("WriteVector3Unchecked(writer, value?.Position);", StringComparison.Ordinal)
                  && writePose.Contains("WriteQuaternionUnchecked(writer, value?.Orientation);", StringComparison.Ordinal),
                "164-19C-2: public geometry writer validates once and delegates to unchecked helpers");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-19\"", StringComparison.Ordinal), "164-19D-1: validation registry exposes Phase164-19");
            Check(project.Contains("Phase164_19Validation.cs", StringComparison.Ordinal), "164-19D-2: runtime validation project compiles Phase164-19");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
