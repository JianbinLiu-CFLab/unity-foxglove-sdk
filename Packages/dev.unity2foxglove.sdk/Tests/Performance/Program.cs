// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Performance harness entry point. Runs scenarios and writes JSON results.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Unity.FoxgloveSDK.Performance
{
    static class Program
    {
        private const int GitCommitTimeoutMs = 3000;
        private const string DefaultResultFilePrefix = "phase35_performance";

        private static string RepoRoot
        {
            get
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // Walk up from build/performance/dotnet/<framework>/ to repo root
                var candidate = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
                var sentinel = Path.Combine(candidate, "Packages", "dev.unity2foxglove.sdk", "package.json");
                if (!File.Exists(sentinel))
                    throw new InvalidOperationException($"RepoRoot resolution failed: {candidate}");
                return candidate;
            }
        }

        static int Main(string[] args)
        {
            var mode = "quick";
            var modeWasSpecified = false;
            string outputDir = null;
            string thresholdPath = null;
            var resultPrefix = DefaultResultFilePrefix;
            var thresholdsEnabled = true;
            var thresholdSelfTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--quick":
                        if (modeWasSpecified && mode != "quick")
                            return UsageError("--quick and --full cannot be used together.");
                        mode = "quick";
                        modeWasSpecified = true;
                        break;
                    case "--full":
                        if (modeWasSpecified && mode != "full")
                            return UsageError("--quick and --full cannot be used together.");
                        mode = "full";
                        modeWasSpecified = true;
                        break;
                    case "--no-thresholds": thresholdsEnabled = false; break;
                    case "--threshold-self-test": thresholdSelfTest = true; break;
                    case "--output":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            return UsageError("--output requires a directory.");
                        outputDir = args[++i];
                        break;
                    case "--thresholds":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            return UsageError("--thresholds requires a JSON file.");
                        thresholdPath = args[++i];
                        break;
                    case "--result-prefix":
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                            return UsageError("--result-prefix requires a file-name prefix.");
                        resultPrefix = args[++i];
                        break;
                    default:
                        return UsageError("Unknown argument: " + args[i]);
                }
            }

            if (thresholdSelfTest)
            {
                var ok = PerformanceRunner.RunThresholdSelfTest();
                Console.WriteLine(ok
                    ? "Performance threshold self-test passed."
                    : "Performance threshold self-test failed.");
                return ok ? 0 : 1;
            }

            if (outputDir == null)
                outputDir = Path.Combine(RepoRoot, "build", "performance");

            Directory.CreateDirectory(outputDir);

            string commit = "";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null && proc.WaitForExit(GitCommitTimeoutMs))
                    commit = proc.StandardOutput.ReadToEnd()?.Trim() ?? "";
                else if (proc != null)
                    proc.Kill();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Performance commit lookup failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string resolvedThresholdPath;
            PerformanceThresholdConfig thresholds;
            if (thresholdsEnabled)
            {
                try
                {
                    thresholds = LoadThresholds(mode, thresholdPath, out resolvedThresholdPath);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 2;
                }
            }
            else
            {
                resolvedThresholdPath = null;
                thresholds = new PerformanceThresholdConfig { enabled = false };
            }
            if (thresholds.enabled)
                Console.WriteLine(string.IsNullOrEmpty(resolvedThresholdPath)
                    ? "Performance thresholds: built-in defaults"
                    : $"Performance thresholds: {resolvedThresholdPath}");
            else
                Console.WriteLine("Performance thresholds: disabled");

            var transportScope = string.IsNullOrWhiteSpace(thresholds.transportScope)
                ? PerformanceRunner.DefaultTransportScope
                : thresholds.transportScope;
            Console.WriteLine("Performance transport: " + transportScope);
            if (!string.IsNullOrWhiteSpace(thresholds.calibratedOn))
                Console.WriteLine("Performance thresholds calibrated on: " + thresholds.calibratedOn);

            var startedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var results = PerformanceRunner.RunAll(mode, thresholds);

            var output = new
            {
                runId,
                mode,
                startedAtUtc,
                machine = Environment.MachineName,
                dotnetVersion = Environment.Version.ToString(),
                commit,
                thresholdsEnabled = thresholds.enabled,
                thresholdPath = resolvedThresholdPath,
                transportScope,
                thresholdCalibratedOn = thresholds.calibratedOn,
                scenarios = results
            };

            var json = JsonConvert.SerializeObject(output, Formatting.Indented, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            var outputPath = Path.Combine(outputDir, $"{resultPrefix}_{mode}_{runId}.json");
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Results written to: {outputPath}");

            foreach (var r in results)
            {
                var status = r.passed ? "PASS" : "FAIL";
                var thresholdSuffix = r.thresholdsEvaluated ? $", thresholds: {r.thresholdNotes}" : "";
                Console.WriteLine($"[{status}] {r.name} - {r.messageCount} msgs, {r.elapsedMs}ms, {r.messagesPerSecond:F0} msg/s, {r.allocatedBytesPerMessage:F1} B/msg{thresholdSuffix}");
            }

            bool allPassed = true;
            foreach (var r in results)
                if (!r.passed) allPassed = false;

            if (!allPassed)
            {
                Console.Error.WriteLine("One or more performance scenarios failed.");
                return 1;
            }

            Console.WriteLine("Performance baseline complete");
            return 0;
        }

        private static int UsageError(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(
                "Usage: [--quick|--full] [--output <directory>] [--thresholds <json>] [--result-prefix <prefix>] [--no-thresholds] [--threshold-self-test]");
            return 2;
        }

        private static PerformanceThresholdConfig LoadThresholds(
            string mode,
            string thresholdPath,
            out string resolvedThresholdPath)
        {
            resolvedThresholdPath = null;
            var path = thresholdPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var defaultPath = Path.Combine(
                    RepoRoot,
                    "Packages",
                    "dev.unity2foxglove.sdk",
                    "Tests",
                    "Performance",
                    "performance-thresholds.json");
                if (File.Exists(defaultPath))
                    path = defaultPath;
            }

            if (string.IsNullOrWhiteSpace(path))
                return PerformanceRunner.CreateDefaultThresholds(mode);

            var fullPath = Path.GetFullPath(path);
            try
            {
                var config = JsonConvert.DeserializeObject<PerformanceThresholdConfig>(File.ReadAllText(fullPath))
                             ?? PerformanceRunner.CreateDefaultThresholds(mode);
                resolvedThresholdPath = fullPath;
                return PerformanceRunner.ResolveThresholdConfigForMode(config, mode);
            }
            catch (Exception ex) when (
                ex is IOException
                || ex is UnauthorizedAccessException
                || ex is JsonException
                || ex is ArgumentException
                || ex is NotSupportedException)
            {
                if (!string.IsNullOrWhiteSpace(thresholdPath))
                    throw new InvalidOperationException(
                        $"Explicit performance threshold config '{fullPath}' could not be loaded.",
                        ex);
                Console.Error.WriteLine(
                    $"Performance threshold config '{fullPath}' could not be loaded; using built-in {mode} defaults. {ex.GetType().Name}: {ex.Message}");
                return PerformanceRunner.CreateDefaultThresholds(mode);
            }
        }
    }
}
