using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_43Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-43 Tests ---");
            _passed = 0;

            VerifySchemaGeneratorsSkipIdenticalWrites();
            VerifyRegistry();

            Console.WriteLine("Phase 164-43: " + _passed + " checks passed.\n");
        }

        private static void VerifySchemaGeneratorsSkipIdenticalWrites()
        {
            var cdr = Read("Scripts/schema/generate_ros2_cdr_serializers.py");
            var catalog = Read("Scripts/schema/generate_ros2_msg_schema_catalog.py");
            var cdrWrite = PythonFunction(cdr, "def write_text(");
            var catalogWrite = PythonFunction(catalog, "def write_text_if_changed(");
            var catalogGenerate = PythonFunction(catalog, "def generate(");

            Check(cdrWrite.Contains("path.is_file() and path.read_text(encoding=\"utf-8\") == text", StringComparison.Ordinal)
                  && cdrWrite.Contains("return", StringComparison.Ordinal)
                  && cdrWrite.Contains("path.write_text(text, encoding=\"utf-8\", newline=\"\\n\")", StringComparison.Ordinal),
                "164-43A-1: CDR serializer generator skips identical generated text writes");
            Check(catalog.Contains("def write_text_if_changed(path: Path, text: str) -> None:", StringComparison.Ordinal)
                  && catalogWrite.Contains("path.is_file() and path.read_text(encoding=\"utf-8\") == text", StringComparison.Ordinal),
                "164-43A-2: ROS2 msg catalog generator has an identical-text write helper");
            Check(catalogGenerate.Contains("write_text_if_changed(output, text)", StringComparison.Ordinal)
                  && !catalogGenerate.Contains("output.write_text(text", StringComparison.Ordinal),
                "164-43A-3: ROS2 msg catalog generation routes output through the write-if-changed helper");
            Check(Read("Scripts/schema/regression_checks/test_schema_tooling.py").Contains("test_generators_skip_identical_text_writes", StringComparison.Ordinal),
                "164-43A-4: schema tooling regression covers unchanged generated-file mtimes");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-43\"", StringComparison.Ordinal), "164-43B-1: validation registry exposes Phase164-43");
            Check(project.Contains("Phase164_43Validation.cs", StringComparison.Ordinal), "164-43B-2: runtime validation project compiles Phase164-43");
        }

        private static string PythonFunction(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing Python function: " + signature);
            var next = source.IndexOf("\ndef ", start + signature.Length, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
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
