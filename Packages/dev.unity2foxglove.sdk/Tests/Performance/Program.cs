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
        private static string RepoRoot
        {
            get
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // Walk up from build/performance/dotnet/<framework>/ to repo root
                return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
            }
        }

        static int Main(string[] args)
        {
            var mode = "quick";
            string outputDir = null;
            string thresholdPath = null;
            var thresholdsEnabled = true;
            var thresholdSelfTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--quick": mode = "quick"; break;
                    case "--full": mode = "full"; break;
                    case "--no-thresholds": thresholdsEnabled = false; break;
                    case "--threshold-self-test": thresholdSelfTest = true; break;
                    case "--output":
                        if (i + 1 < args.Length) outputDir = args[++i];
                        break;
                    case "--thresholds":
                        if (i + 1 < args.Length) thresholdPath = args[++i];
                        break;
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
                var proc = System.Diagnostics.Process.Start(psi);
                commit = proc?.StandardOutput.ReadToEnd()?.Trim() ?? "";
                proc?.WaitForExit();
            }
            catch { }

            var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string resolvedThresholdPath;
            PerformanceThresholdConfig thresholds;
            if (thresholdsEnabled)
            {
                thresholds = LoadThresholds(mode, thresholdPath, out resolvedThresholdPath);
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

            var results = PerformanceRunner.RunAll(mode, thresholds);

            var output = new
            {
                runId,
                mode,
                startedAtUtc = DateTime.UtcNow.ToString("o"),
                machine = Environment.MachineName,
                dotnetVersion = Environment.Version.ToString(),
                commit,
                thresholdsEnabled = thresholds.enabled,
                thresholdPath = resolvedThresholdPath,
                scenarios = results
            };

            var json = JsonConvert.SerializeObject(output, Formatting.Indented, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });

            var outputPath = Path.Combine(outputDir, $"phase35_performance_{mode}_{runId}.json");
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
            var config = JsonConvert.DeserializeObject<PerformanceThresholdConfig>(File.ReadAllText(fullPath))
                         ?? PerformanceRunner.CreateDefaultThresholds(mode);
            resolvedThresholdPath = fullPath;
            return config;
        }
    }
}
