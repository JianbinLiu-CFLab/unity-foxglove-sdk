// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 120 MCAP official compatibility gate validation and evidence report generation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase120Validation
    {
        private static void WriteReport(bool includeOfficialPython)
        {
            var path = Path.Combine(CompatDir(), "phase120-report.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(CreateReport(includeOfficialPython), Formatting.Indented), Encoding.UTF8);
            Check(File.Exists(path), "120-H1: phase120-report.json is written");
        }

        private static Phase120Report CreateReport(bool includeOfficialPython)
        {
            var commitResult = RunProcess("git", "rev-parse --short HEAD");
            var commit = commitResult.Output.Trim();
            if (commitResult.ExitCode != 0 || string.IsNullOrWhiteSpace(commit))
                Limitations.Add("git commit provenance unavailable for Phase120 report");

            return new Phase120Report
            {
                verdict = Limitations.Count == 0 ? "PASS" : "PASS WITH NOTED LIMITATIONS",
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                commit = commit,
                coreChecks = CoreChecks,
                optionalChecks = OptionalChecks,
                officialTooling = includeOfficialPython ? "official Python mcap executed" : "official Python mcap skipped in --phase120",
                fixtures = Fixtures,
                manualChecks = "Foxglove Desktop manual open skipped/deferred",
                limitations = Limitations
            };
        }

        private static string FindPython()
        {
            foreach (var candidate in new[] { "python", "python3", "py" })
            {
                var result = RunProcess(candidate, "--version");
                if (result.ExitCode == 0)
                    return candidate;
            }

            return string.Empty;
        }

        /// <summary>Maximum time to wait for a subprocess before killing it.</summary>
        private const int SubprocessTimeoutMs = 30_000;

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
                if (process == null)
                    return new ProcessResult(-1, string.Empty, "Failed to start process.");

                // Drain stdout and stderr concurrently to prevent pipe deadlock.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(SubprocessTimeoutMs))
                {
                    try { process.Kill(); } catch { /* best effort */ }
                    return new ProcessResult(-1, stdoutTask.Result,
                        stderrTask.Result + "\nProcess timed out after " + SubprocessTimeoutMs + "ms.");
                }

                return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, string.Empty, ex.Message);
            }
        }

        private static string Quote(string value)
            => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static string CompatDir()
            => Path.Combine(RepoRoot(), "build", "mcap-compat");

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase120 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static void AddCore(string name, string status, string details)
            => CoreChecks.Add(new ReportCheck { name = name, status = status, details = details });

        private static void AddOptional(string name, string status, string details)
            => OptionalChecks.Add(new ReportCheck { name = name, status = status, details = details });

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

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

        private sealed class Phase120Report
        {
            public string verdict { get; set; }
            public string generatedAtUtc { get; set; }
            public string commit { get; set; }
            public List<ReportCheck> coreChecks { get; set; }
            public List<ReportCheck> optionalChecks { get; set; }
            public string officialTooling { get; set; }
            public List<ReportFixture> fixtures { get; set; }
            public string manualChecks { get; set; }
            public List<string> limitations { get; set; }
        }

        private sealed class ReportCheck
        {
            public string name { get; set; }
            public string status { get; set; }
            public string details { get; set; }
        }

        private sealed class ReportFixture
        {
            public string name { get; set; }
            public string path { get; set; }
            public string kind { get; set; }
            public bool generated { get; set; }
        }
    }
}
