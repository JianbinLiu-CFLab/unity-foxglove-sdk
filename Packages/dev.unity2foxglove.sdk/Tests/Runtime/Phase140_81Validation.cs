// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-81 source-shape regression coverage for generator/build/performance script optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_81Validation.
    /// </summary>
    public static class Phase140_81Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-81: Generator Architecture Build and Performance Scripts Optimization ===");
            _passed = 0;

            VerifyRos2CatalogGeneratorCachesMsgBytes();
            VerifyOpenH264ProbeReusesAccessUnitBuffer();
            VerifyDracoProbeReusesXyzBuffer();
            VerifyAsmdefCycleSearchUsesMutableStack();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-81: {_passed} checks passed.");
        }

        private static void VerifyRos2CatalogGeneratorCachesMsgBytes()
        {
            var source = Read("Scripts/schema/generate_ros2_msg_schema_catalog.py");
            var generate = Slice(source, "def generate(input_dir: Path, output: Path) -> str:", "    entries = ");
            var sourceTreeSha = Slice(source, "def source_tree_sha", "def try_source_commit");
            Check(source.Contains("file_bytes = {path: path.read_bytes() for path in files}", StringComparison.Ordinal)
                  && source.Contains("def decode_schema_text(data: bytes) -> str:", StringComparison.Ordinal)
                  && source.Contains("return data.decode(\"utf-8\").replace(\"\\r\\n\", \"\\n\").replace(\"\\r\", \"\\n\")", StringComparison.Ordinal)
                  && source.Contains("local_sources = {path.stem: decode_schema_text(file_bytes[path]) for path in files}", StringComparison.Ordinal)
                  && source.Contains("tree_sha = source_tree_sha(files, file_bytes)", StringComparison.Ordinal)
                  && source.Contains("source_sha = hashlib.sha256(file_bytes[path]).hexdigest()", StringComparison.Ordinal)
                  && sourceTreeSha.Contains("sha.update(file_bytes[path])", StringComparison.Ordinal)
                  && !generate.Contains("path.read_text", StringComparison.Ordinal)
                  && Count(generate, "path.read_bytes()") == 1,
                "140-81A-1: ROS2 message catalog generator reads each .msg file as bytes once");
        }

        private static void VerifyOpenH264ProbeReusesAccessUnitBuffer()
        {
            var source = Read("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");
            var packageSource = Read("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp");
            var writeAccessUnit = Slice(source, "void WriteAccessUnit", "int main");
            var main = Slice(source, "int main(int argc, char** argv)", "    if (exitCode == 0)");
            Check(source.Contains("void WriteAccessUnit(const SFrameBSInfo& info, std::vector<uint8_t>& accessUnit)", StringComparison.Ordinal)
                  && packageSource.Contains("void WriteAccessUnit(const SFrameBSInfo& info, std::vector<uint8_t>& accessUnit)", StringComparison.Ordinal)
                  && writeAccessUnit.Contains("accessUnit.clear();", StringComparison.Ordinal)
                  && !writeAccessUnit.Contains("std::vector<uint8_t> accessUnit;", StringComparison.Ordinal)
                  && main.Contains("std::vector<uint8_t> accessUnit;", StringComparison.Ordinal)
                  && main.Contains("WriteAccessUnit(info, accessUnit);", StringComparison.Ordinal),
                "140-81B-1: OpenH264 probe reuses one access-unit vector across frames");
        }

        private static void VerifyDracoProbeReusesXyzBuffer()
        {
            var source = Read("Scripts/native/draco_probe/draco_probe_encoder.cpp");
            var processOneFrame = Slice(source, "bool ProcessOneFrame", "}  // namespace");
            var main = Slice(source, "int main()", "  return 0;");
            Check(source.Contains("bool ProcessOneFrame(std::vector<float>* xyz)", StringComparison.Ordinal)
                  && processOneFrame.Contains("xyz->resize(float_count);", StringComparison.Ordinal)
                  && processOneFrame.Contains("ReadExact(reinterpret_cast<char*>(xyz->data())", StringComparison.Ordinal)
                  && processOneFrame.Contains("EncodePointCloud(*xyz, point_count, &buffer)", StringComparison.Ordinal)
                  && !processOneFrame.Contains("std::vector<float> xyz(float_count);", StringComparison.Ordinal)
                  && main.Contains("std::vector<float> xyz;", StringComparison.Ordinal)
                  && main.Contains("ProcessOneFrame(&xyz)", StringComparison.Ordinal),
                "140-81C-1: Draco probe reuses one XYZ scratch vector across frames");
        }

        private static void VerifyAsmdefCycleSearchUsesMutableStack()
        {
            var source = Read("Scripts/architecture/analyze_coupling.py");
            var method = Slice(source, "def find_asmdef_cycles", "def find_default_test_private_references");
            Check(method.Contains("stack.append(node)", StringComparison.Ordinal)
                  && method.Contains("stack.pop()", StringComparison.Ordinal)
                  && method.Contains("visit(child, stack)", StringComparison.Ordinal)
                  && !method.Contains("path + [current]", StringComparison.Ordinal)
                  && !method.Contains("stack + [node]", StringComparison.Ordinal),
                "140-81D-1: asmdef cycle search reuses a mutable DFS stack");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_81Validation.cs", StringComparison.Ordinal),
                "140-81E-1: test project compiles Phase140_81Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-81\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_81Validation.Validate", StringComparison.Ordinal),
                "140-81E-2: validation registry exposes --phase140-81");
        }

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

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var start = 0;
            while (true)
            {
                var index = source.IndexOf(value, start, StringComparison.Ordinal);
                if (index < 0)
                    return count;
                count++;
                start = index + value.Length;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
