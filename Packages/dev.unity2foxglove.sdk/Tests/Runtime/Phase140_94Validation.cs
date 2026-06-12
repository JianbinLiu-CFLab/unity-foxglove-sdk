// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-94 source-shape validation for virtual sensor and throughput test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase140_94Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-94: Virtual Sensor And Throughput Tests Optimization ===");
            _passed = 0;

            ValidateCameraPublisherReaders();
            ValidateRepeatedSourceReadsUseCaches();
            ValidateBomProbeReadsOnlyHeader();
            ValidateRayUnitChecksAvoidSqrt();
            ValidatePhase138SDirectoryRead();
            ValidatePhase138LFieldScan();
            ValidatePhase138C2PayloadReuse();
            ValidateRegistration();

            Console.WriteLine($"Phase 140-94: {_passed} checks passed.");
        }

        private static void ValidateRepeatedSourceReadsUseCaches()
        {
            foreach (var file in new[]
            {
                "Phase138IValidation.cs",
                "Phase138JValidation.cs",
                "Phase138QValidation.cs"
            })
            {
                var source = ReadRuntimeTest(file);
                Check(source.Contains("private static readonly Dictionary<string, string>", StringComparison.Ordinal)
                      && source.Contains("TryGetValue", StringComparison.Ordinal)
                      && source.Contains("File.ReadAllText", StringComparison.Ordinal),
                    "140-94A-2: " + file + " caches repeated source reads during validation");
            }
        }

        private static void ValidateCameraPublisherReaders()
        {
            foreach (var file in new[]
            {
                "Phase138MValidation.cs",
                "Phase138PValidation.cs",
                "Phase138QValidation.cs",
                "Phase138TValidation.cs"
            })
            {
                var source = ReadRuntimeTest(file);
                var method = ExtractMethod(source, "private static string ReadCameraPublisherSources()");
                Check(method.Contains("StringBuilder", StringComparison.Ordinal)
                      && method.Contains("AppendLine(File.ReadAllText(file))", StringComparison.Ordinal)
                      && !method.Contains("output += File.ReadAllText(file)", StringComparison.Ordinal),
                    "140-94A-1: " + file + " builds camera publisher source text with StringBuilder");
            }
        }

        private static void ValidateBomProbeReadsOnlyHeader()
        {
            var source = ReadRuntimeTest("Phase138PValidation.cs");
            var method = ExtractMethod(source, "private static bool TrackedSourceHasUtf8Bom()");
            Check(method.Contains("FileStream", StringComparison.Ordinal)
                  && method.Contains("Span<byte>", StringComparison.Ordinal)
                  && method.Contains("stream.Read(header)", StringComparison.Ordinal)
                  && !method.Contains("File.ReadAllBytes(path)", StringComparison.Ordinal),
                "140-94B-1: Phase138P BOM probe reads only the UTF-8 BOM header bytes");
        }

        private static void ValidateRayUnitChecksAvoidSqrt()
        {
            foreach (var file in new[] { "Phase138Validation.cs", "Phase138BValidation.cs" })
            {
                var source = ReadRuntimeTest(file);
                Check(source.Contains("LengthSquared()", StringComparison.Ordinal)
                      && !source.Contains("var mag = dir.Length();", StringComparison.Ordinal),
                    "140-94C-1: " + file + " uses squared magnitude for ray unit checks");
            }
        }

        private static void ValidatePhase138SDirectoryRead()
        {
            var source = ReadRuntimeTest("Phase138SValidation.cs");
            var method = ExtractMethod(source, "private static string ReadDirectory(");
            Check(method.Contains("var content = File.ReadAllText(file);", StringComparison.Ordinal)
                  && method.Contains("sb.AppendLine(content)", StringComparison.Ordinal)
                  && method.Contains("bytes += content.Length", StringComparison.Ordinal)
                  && Count(method, "File.ReadAllText(file)") == 1,
                "140-94D-1: Phase138S ReadDirectory reads each file once");
        }

        private static void ValidatePhase138LFieldScan()
        {
            var source = ReadRuntimeTest("Phase138LValidation.cs");
            var method = ExtractMethod(source, "private static bool HasField(");
            Check(method.Contains("for (var i = 0; i < fields.Length; i++)", StringComparison.Ordinal)
                  && !method.Contains(".Any(", StringComparison.Ordinal),
                "140-94E-1: Phase138L HasField scans arrays without LINQ enumerator allocation");
        }

        private static void ValidatePhase138C2PayloadReuse()
        {
            var source = ReadRuntimeTest("Phase138C2Validation.cs");
            Check(source.Contains("private static readonly byte[] OkPayload", StringComparison.Ordinal)
                  && Count(source, "Encoding.UTF8.GetBytes(\"{\\\"ok\\\":true}\")") == 1
                  && source.Contains("session.Publish(77, OkPayload, 1)", StringComparison.Ordinal)
                  && source.Contains("session.Publish(78, OkPayload, 2)", StringComparison.Ordinal)
                  && source.Contains("session.Publish(79, OkPayload, 3)", StringComparison.Ordinal),
                "140-94F-1: Phase138C2 reuses the fixed OK payload bytes");
        }

        private static void ValidateRegistration()
        {
            var registry = ReadRuntimeTest("PhaseValidationRegistry.cs");
            var project = ReadRuntimeTest("FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase140-94\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_94Validation.Validate", StringComparison.Ordinal),
                "140-94G-1: validation registry exposes --phase140-94");
            Check(project.Contains("Phase140_94Validation.cs", StringComparison.Ordinal),
                "140-94G-2: test project compiles Phase140_94Validation");
        }

        private static string ReadRuntimeTest(string fileName)
            => File.ReadAllText(Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", fileName));

        private static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return string.Empty;

            var brace = source.IndexOf('{', index);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
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
