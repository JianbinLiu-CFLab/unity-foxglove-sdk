// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-20 validation for safe native editor process execution.

using System;
using System.Diagnostics;
using System.IO;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_20Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyRunnerDrainsLargeStderr();
            VerifyRunnerTimeoutKillsProcess();
            VerifyOpenSslToolingUsesSafeRunner();

            Console.WriteLine($"Phase134_20Validation: PASS ({_passed} checks)");
        }

        private static void VerifyRunnerDrainsLargeStderr()
        {
            var result = FoxgloveEditorProcessRunner.Run(CreateStderrFloodStartInfo(), 10000);
            Check(!result.TimedOut, "134-20-A1: safe process runner does not time out on large stderr output");
            Check(result.ExitCode == 0, "134-20-A2: safe process runner preserves child exit code");
            Check(result.Stderr.Length > 100000, "134-20-A3: safe process runner drains redirected stderr concurrently");
        }

        private static void VerifyRunnerTimeoutKillsProcess()
        {
            var result = FoxgloveEditorProcessRunner.Run(CreateSlowProcessStartInfo(), 250);
            Check(result.TimedOut, "134-20-B1: safe process runner reports timeout for hung native tool");
            Check(result.ExitCode == -1, "134-20-B2: safe process runner uses sentinel exit code for killed process");
        }

        private static void VerifyOpenSslToolingUsesSafeRunner()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            Check(source.Contains("OpenSslToolTimeoutMs", StringComparison.Ordinal)
                  && source.Contains("FoxgloveEditorProcessRunner.Run", StringComparison.Ordinal),
                "134-20-C1: OpenSSL certificate backend uses bounded safe process runner");
            Check(!source.Contains("StandardOutput.ReadToEnd", StringComparison.Ordinal)
                  && !source.Contains("StandardError.ReadToEnd", StringComparison.Ordinal),
                "134-20-C2: OpenSSL certificate backend no longer reads redirected streams synchronously");
            Check(source.Contains("timed out after", StringComparison.Ordinal)
                  && source.Contains("result.Stderr", StringComparison.Ordinal)
                  && source.Contains("result.Stdout", StringComparison.Ordinal),
                "134-20-C3: OpenSSL certificate backend reports timeout diagnostics from both streams");
        }

        private static ProcessStartInfo CreateStderrFloodStartInfo()
        {
            if (IsWindows)
            {
                return new ProcessStartInfo(
                    "cmd.exe",
                    "/c for /L %i in (1,1,12000) do @echo stderr-line-abcdefghijklmnopqrstuvwxyz0123456789 1>&2");
            }

            return new ProcessStartInfo(
                "/bin/sh",
                "-c \"i=0; while [ $i -lt 12000 ]; do echo stderr-line-abcdefghijklmnopqrstuvwxyz0123456789 >&2; i=$((i+1)); done\"");
        }

        private static ProcessStartInfo CreateSlowProcessStartInfo()
        {
            if (IsWindows)
                return new ProcessStartInfo("cmd.exe", "/c ping -n 6 127.0.0.1 > nul");

            return new ProcessStartInfo("/bin/sh", "-c \"sleep 5\"");
        }

        private static bool IsWindows
            => Path.DirectorySeparatorChar == '\\';

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
