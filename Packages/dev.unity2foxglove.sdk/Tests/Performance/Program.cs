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
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--quick": mode = "quick"; break;
                    case "--full": mode = "full"; break;
                    case "--output":
                        if (i + 1 < args.Length) outputDir = args[++i];
                        break;
                }
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
            var results = PerformanceRunner.RunAll(mode);

            var output = new
            {
                runId,
                mode,
                startedAtUtc = DateTime.UtcNow.ToString("o"),
                machine = Environment.MachineName,
                dotnetVersion = Environment.Version.ToString(),
                commit,
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
                Console.WriteLine($"[{status}] {r.name} - {r.messageCount} msgs, {r.elapsedMs}ms, {r.messagesPerSecond:F0} msg/s, {r.allocatedBytesPerMessage:F1} B/msg");
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
    }
}
