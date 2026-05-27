// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-31 regression coverage for generator/build architecture scripts.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_31Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-31: Generator Build Architecture Scripts ===");
            _passed = 0;

            VerifyUnityIl2CppBuildRunner();
            VerifyConformanceWrapper();
            VerifyPerformanceBaselineRunner();
            VerifyArchitectureAnalyzer();
            VerifyFullDemoSync();
            VerifyNativeHelpers();
            VerifyRos2SchemaGenerators();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 134-31: {_passed} checks passed.");
        }

        private static void VerifyUnityIl2CppBuildRunner()
        {
            var source = ReadRepoText("Scripts/build_tools/unity_il2cpp.py");

            Check(source.Contains("or find_unity_from_project_version(project_path)", StringComparison.Ordinal)
                  && source.IndexOf("or find_unity_from_project_version(project_path)", StringComparison.Ordinal)
                  < source.IndexOf("or find_unity_from_hub()", StringComparison.Ordinal),
                "134-31A-1: Unity resolver prefers ProjectVersion.txt before generic Hub newest-editor discovery");
            Check(source.Contains("DEFAULT_BUILD_TIMEOUT_MINUTES", StringComparison.Ordinal)
                  && source.Contains("\"--timeout-minutes\"", StringComparison.Ordinal)
                  && source.Contains("EXIT_TIMEOUT = 124", StringComparison.Ordinal),
                "134-31A-2: Unity IL2CPP runner exposes a bounded process timeout");
            Check(source.Contains("terminate_process(process)", StringComparison.Ordinal)
                  && source.Contains("Unity timed out after", StringComparison.Ordinal),
                "134-31A-3: Unity IL2CPP timeout path terminates the hung process with a clear diagnostic");
        }

        private static void VerifyConformanceWrapper()
        {
            var source = ReadRepoText("Scripts/mcap/conformance/run_phase121_conformance.py");

            Check(source.Contains("RUNNER_ARRAY_DECLARATION", StringComparison.Ordinal)
                  && source.Contains("if RUNNER_ARRAY_DECLARATION not in index", StringComparison.Ordinal)
                  && source.Contains("overlay output is missing C# runners", StringComparison.Ordinal),
                "134-31B-1: MCAP conformance overlay asserts runner injection success");
            Check(source.Contains("def read_target_framework()", StringComparison.Ordinal)
                  && source.Contains("def resolve_conformance_dll_path()", StringComparison.Ordinal)
                  && !source.Contains("build/McapConformance/Release/net9.0", StringComparison.Ordinal),
                "134-31B-2: MCAP conformance DLL path is resolved from the C# project target framework");
            Check(source.Contains("r\"(?m)^(Error:|FAIL\\b|\\w+Error:)\"", StringComparison.Ordinal)
                  && !source.Contains("fail\\s+", StringComparison.Ordinal),
                "134-31B-3: MCAP conformance error parsing no longer treats diagnostic 'fail count' lines as failures");
        }

        private static void VerifyPerformanceBaselineRunner()
        {
            var source = ReadRepoText("Scripts/performance/run_baseline.py");

            Check(source.Contains("DEFAULT_QUICK_TIMEOUT_MINUTES", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_FULL_TIMEOUT_MINUTES", StringComparison.Ordinal)
                  && source.Contains("\"--timeout-minutes\"", StringComparison.Ordinal),
                "134-31C-1: performance baseline runner exposes mode-specific timeouts");
            Check(source.Contains("except subprocess.TimeoutExpired", StringComparison.Ordinal)
                  && source.Contains("dotnet process timed out", StringComparison.Ordinal)
                  && source.Contains("EXIT_TIMEOUT = 124", StringComparison.Ordinal),
                "134-31C-2: performance baseline timeout reports a deterministic failure");
        }

        private static void VerifyArchitectureAnalyzer()
        {
            var source = ReadRepoText("Scripts/architecture/analyze_coupling.py");

            Check(source.Contains("def canonical_cycle", StringComparison.Ordinal)
                  && source.Contains("seen: set[tuple[str, ...]]", StringComparison.Ordinal),
                "134-31D-1: architecture analyzer de-duplicates rotated asmdef cycles");
            Check(source.Contains("Unity2Foxglove Architecture Coupling Report", StringComparison.Ordinal)
                  && !source.Contains("Unity2Foxglove Phase126 Architecture Coupling Report", StringComparison.Ordinal),
                "134-31D-2: architecture report title is phase-neutral");
        }

        private static void VerifyFullDemoSync()
        {
            var source = ReadRepoText("Scripts/samples/sync_full_demo.py");

            Check(source.Contains("body = line.rstrip(\"\\r\\n\")", StringComparison.Ordinal)
                  && !source.Contains("body = line.rstrip(\"\\r\\n\").rstrip()", StringComparison.Ordinal),
                "134-31E-1: full-demo scene portability preserves trailing spaces on untouched lines");
            Check(source.Contains("missing destination:", StringComparison.Ordinal)
                  && source.Contains("stale destination:", StringComparison.Ordinal)
                  && source.Contains("expected = portable_full_demo_scene_payload(src)", StringComparison.Ordinal),
                "134-31E-2: sync_full_demo validate mode checks destination presence and content parity");
        }

        private static void VerifyNativeHelpers()
        {
            var dracoNative = ReadRepoText("Scripts/native/draco_native/Unity2FoxgloveDracoNative.cpp");
            var dracoProbe = ReadRepoText("Scripts/native/draco_probe/draco_probe_encoder.cpp");
            var openH264 = ReadRepoText("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");

            Check(dracoNative.Contains("#ifndef _WIN32", StringComparison.Ordinal)
                  && dracoNative.Contains("Windows-only Unity native plugin", StringComparison.Ordinal),
                "134-31F-1: Draco native plugin declares its Windows-only build contract");
            Check(dracoProbe.Contains("Draco POINT_CLOUD probe encoder helper", StringComparison.Ordinal)
                  && !dracoProbe.Contains("Spike-only", StringComparison.Ordinal)
                  && dracoProbe.Contains("if (point_count == 0)", StringComparison.Ordinal)
                  && dracoProbe.Contains("WriteUint32(0)", StringComparison.Ordinal),
                "134-31F-2: Draco probe comments are productized and zero-point frames produce an empty payload");
            Check(openH264.Contains("enum class FrameReadStatus", StringComparison.Ordinal)
                  && openH264.Contains("PartialFrame", StringComparison.Ordinal)
                  && !openH264.Contains("Partial I420 frame received before EOF. bytes=\" << read << std::endl;\n            std::exit(3);", StringComparison.Ordinal),
                "134-31F-3: OpenH264 probe partial stdin returns through normal cleanup instead of std::exit");
            Check(openH264.Contains("--openh264-dll is ignored on non-Windows builds", StringComparison.Ordinal),
                "134-31F-4: OpenH264 probe warns when non-Windows builds ignore --openh264-dll");
        }

        private static void VerifyRos2SchemaGenerators()
        {
            var serializerGenerator = ReadRepoText("Scripts/schema/generate_ros2_cdr_serializers.py");
            var serializerRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Msg/Generated/Ros2CdrSerializerRegistry.g.cs");
            var catalogGenerator = ReadRepoText("Scripts/schema/generate_ros2_msg_schema_catalog.py");
            var catalog = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs");

            Check(serializerGenerator.Contains("_sampleFactory = sampleFactory;", StringComparison.Ordinal)
                  && serializerGenerator.Contains("does not have a deterministic sample factory", StringComparison.Ordinal)
                  && serializerRegistry.Contains("does not have a deterministic sample factory", StringComparison.Ordinal),
                "134-31G-1: generated serializer sample factory availability is meaningful rather than constructor-guaranteed");
            Check(catalogGenerator.Contains("BuildSchemaNameMap", StringComparison.Ordinal)
                  && catalog.Contains("BySchemaName.TryGetValue(schemaName, out entry)", StringComparison.Ordinal),
                "134-31G-2: generated ROS2 msg schema catalog uses dictionary-backed lookup");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("<Compile Include=\"Phase134_31Validation.cs\" />", StringComparison.Ordinal),
                "134-31H-1: Phase134_31Validation is compiled by the runtime test project");
            Check(registry.Contains("Ci(\"--phase134-31\", \"Phase 134-31\", Phase134_31Validation.Validate)", StringComparison.Ordinal),
                "134-31H-2: Phase134_31Validation is wired into the validation registry");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }

                throw new InvalidOperationException("Could not locate repository root.");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[OK] " + message);
        }
    }
}
