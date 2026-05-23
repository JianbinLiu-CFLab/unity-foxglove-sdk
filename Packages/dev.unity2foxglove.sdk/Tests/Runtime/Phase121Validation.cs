// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 121 validation for the C# MCAP official conformance runner baseline.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase121Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 121: MCAP Official Conformance Runner For CSharp ===");
            _passed = 0;

            VerifyValidationWiring();
            VerifyConformanceConsole();
            VerifyHarnessOverlay();
            VerifyEvidenceNote();

            Console.WriteLine($"Phase 121: {_passed} checks passed.");
        }

        public static void ValidateConformance()
        {
            Validate();

            var script = RepoPath("Scripts/mcap/conformance/run_phase121_conformance.ps1");
            var result = RunProcess(
                "powershell",
                "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script));

            Check(result.ExitCode == 0,
                "121-E1: phase121 conformance wrapper exits successfully");

            var report = ReadRepoText("build/mcap-conformance/phase121-conformance-report.json");
            Check(report.Contains("\"externalToolingStatus\"", StringComparison.Ordinal)
                  && report.Contains("\"runners\"", StringComparison.Ordinal)
                  && report.Contains("\"verdict\"", StringComparison.Ordinal),
                "121-E2: phase121 conformance report is written with required schema");
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(program.Contains("--phase121", StringComparison.Ordinal)
                  && program.Contains("--phase121-conformance", StringComparison.Ordinal)
                  && program.Contains("RunPhase121Only", StringComparison.Ordinal)
                  && program.Contains("RunPhase121ConformanceOnly", StringComparison.Ordinal)
                  && program.Contains("Phase121Validation.Validate()", StringComparison.Ordinal),
                "121-A1: Program.cs wires --phase121 and --phase121-conformance");
            Check(project.Contains("Phase121Validation.cs", StringComparison.Ordinal),
                "121-A2: runtime test project compiles Phase121Validation");
        }

        private static void VerifyConformanceConsole()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Unity2Foxglove.McapConformance.csproj");
            Check(project.Contains("Microsoft.NET.Sdk", StringComparison.Ordinal)
                  && project.Contains("Newtonsoft.Json", StringComparison.Ordinal)
                  && project.Contains("ZstdSharp.Port", StringComparison.Ordinal),
                "121-B1: C# conformance console project exists with required runtime dependencies");

            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Program.cs");
            foreach (var required in new[] { "read-streamed", "read-indexed", "write" })
            {
                Check(program.Contains(required, StringComparison.Ordinal),
                    "121-B2: conformance console exposes mode " + required);
            }

            var json = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceJson.cs");
            foreach (var required in new[] { "\"records\"", "\"schemas\"", "\"channels\"", "\"messages\"", "\"statistics\"", "Field", "ByteArray" })
            {
                Check(json.Contains(required, StringComparison.Ordinal),
                    "121-B3: JSON normalization contains " + required);
            }

            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceReader.cs");
            foreach (var required in new[] { "ReadStreamed", "ReadIndexed", "MessageIndex", "SummaryOffset", "McapIndexedReader", "McapSequentialReadLimits" })
            {
                Check(reader.Contains(required, StringComparison.Ordinal),
                    "121-B4: reader normalization contains " + required);
            }

            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceWriter.cs");
            Check(writer.Contains("CreateOptionsFromFeatures", StringComparison.Ordinal)
                  && writer.Contains("UseChunking", StringComparison.Ordinal)
                  && writer.Contains("Unsupported", StringComparison.Ordinal),
                "121-B5: writer command exposes Phase 122 feature mapping with explicit unsupported exits");
        }

        private static void VerifyHarnessOverlay()
        {
            var script = ReadRepoText("Scripts/mcap/conformance/run_phase121_conformance.ps1");
            foreach (var required in new[]
            {
                "third-party/mcap",
                "build/mcap-conformance",
                "phase121-conformance-report.json",
                "externalToolingStatus",
                "c3cab6bd3ce79199e362766daec3a4689f3a0335",
                "Write-SkippedReport",
                "Scripts/mcap/conformance/csharp-runners"
            })
            {
                Check(script.Contains(required, StringComparison.Ordinal),
                    "121-C1: conformance wrapper contains " + required);
            }

            foreach (var file in new[]
            {
                "Scripts/mcap/conformance/csharp-runners/CsharpStreamedReaderTestRunner.ts",
                "Scripts/mcap/conformance/csharp-runners/CsharpIndexedReaderTestRunner.ts",
                "Scripts/mcap/conformance/csharp-runners/CsharpWriterTestRunner.ts"
            })
            {
                var source = ReadRepoText(file);
                Check(source.Contains("csharp-", StringComparison.Ordinal)
                      && source.Contains("supportsVariant", StringComparison.Ordinal)
                      && source.Contains("dotnet", StringComparison.Ordinal),
                    "121-C2: runner overlay is present for " + Path.GetFileName(file));
            }

            Check(!script.Contains("third-party/mcap/tests/conformance/scripts/run-tests/runners/index.ts", StringComparison.Ordinal),
                "121-C3: wrapper does not require editing tracked upstream runner index in place");
        }

        private static void VerifyEvidenceNote()
        {
            var note = ReadRepoText("Developer/MCAP CSharp Conformance Baseline.md");
            foreach (var required in new[]
            {
                "v1.9.1 Baseline",
                "externalToolingStatus",
                "phase121-conformance-report.json",
                "PASS WITH MEASURED BASELINE",
                "Phase 122",
                "Phase 123",
                "does not claim full official MCAP conformance",
                "c3cab6bd3ce79199e362766daec3a4689f3a0335"
            })
            {
                Check(note.Contains(required, StringComparison.Ordinal),
                    "121-D1: evidence note contains " + required);
            }
        }

        private static ProcessResult RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = RepoRoot()
                };
                using var process = Process.Start(psi);
                var stdout = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                var stderr = process?.StandardError.ReadToEnd() ?? string.Empty;
                process?.WaitForExit();
                return new ProcessResult(process?.ExitCode ?? -1, stdout, stderr);
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, string.Empty, ex.Message);
            }
        }

        private static string Quote(string value)
            => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase121 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class ProcessResult
        {
            public readonly int ExitCode;
            public readonly string Output;
            public readonly string Error;

            public ProcessResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output ?? string.Empty;
                Error = error ?? string.Empty;
            }
        }
    }
}
