// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 99 validation for ROS2 release evidence gate reporting.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{



    /// <summary>
    /// Validation type for Phase99Validation.
    /// </summary>
    public static partial class Phase99Validation
    {
        private static void VerifyPhase98BoundaryAlignment()
        {
            var phase98 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase98Validation.cs");
            Check(phase98.Contains("RequiredProductTopics") && phase98.Contains("OptionalDracoTopic"),
                "99E-1: Phase98 separates required topics from optional Draco");
            Check(phase98.Contains("ProductTopics")
                  && phase98.Contains("AllSchema")
                  && phase98.Contains("SentFrames")
                  && phase98.Contains("frames.Count == 41"),
                "99E-2: Phase98 live evidence exposes product topics and all-schema count");

            var sampleReadme = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/README.md");
            var requiredTopics = new[]
            {
                "/tf",
                "/scene",
                "/camera",
                "/camera_calibration",
                "/laser_scan",
                "/point_cloud"
            };
            Check(sampleReadme.Contains("## Ordinary Publisher Topics", StringComparison.Ordinal)
                  && requiredTopics.All(topic => sampleReadme.Contains(topic, StringComparison.Ordinal))
                  && sampleReadme.Contains("optional `/point_cloud_draco`", StringComparison.Ordinal),
                "99E-3: sample README documents six required topics plus optional Draco");
        }

        private static void VerifyCliWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("\"--phase99\"", StringComparison.Ordinal)
                  && registry.Contains("Phase99Validation.Validate", StringComparison.Ordinal),
                "99F-1: Program dispatches --phase99");
            Check(program.Contains("--phase99-live")
                  && program.Contains("--evidence-dir")
                  && program.Contains("RunPhase99Live"),
                "99F-2: Program dispatches --phase99-live evidence report");
            Check(registry.Contains("Phase99Validation.Validate", StringComparison.Ordinal),
                "99F-3: full validation includes Phase99");
            Check(csproj.Contains("Phase99Validation.cs"),
                "99F-4: Phase99 validation is included in test project");
        }

        /// <summary>
        /// Validation method for Classify.
        /// </summary>
        /// <param name="evidence">Live evidence object to serialize.</param>
        /// <returns>The value produced by the validation helper.</returns>
        public static Phase99Verdict Classify(IEnumerable<Phase99EvidenceItem> evidence)
        {
            var items = evidence?.ToList() ?? new List<Phase99EvidenceItem>();
            if (items.Any(item => item.IsCore && item.Status != Phase99EvidenceStatus.Pass))
                return Phase99Verdict.Blocked;
            if (items.Any(item => !item.IsCore && item.Status != Phase99EvidenceStatus.Pass))
                return Phase99Verdict.PassWithNotedLimitations;
            return Phase99Verdict.Pass;
        }

        private static Phase99ReleaseGateReport CreateReport(
            IEnumerable<Phase99EvidenceItem> evidence,
            string host,
            int port,
            bool liveMode)
        {
            var evidenceList = evidence?.ToList() ?? new List<Phase99EvidenceItem>();
            return new Phase99ReleaseGateReport
            {
                SchemaVersion = ReportSchemaVersion,
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                Commit = TryRunProcess("git", "rev-parse --short HEAD"),
                PackageVersion = ReadPackageVersion(),
                UnityVersion = "",
                FoxgloveVersion = "",
                RosDistro = Environment.GetEnvironmentVariable("ROS_DISTRO") ?? "",
                RmwImplementation = Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION") ?? "",
                Os = Environment.OSVersion.ToString(),
                Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host,
                Port = port <= 0 ? 8767 : port,
                LiveMode = liveMode,
                Evidence = evidenceList,
                Verdict = Classify(evidenceList),
                FinalNotes = liveMode
                    ? "Live machine evidence was collected where possible; the verdict remains Blocked until all core manual Unity/Foxglove observations are recorded as Pass."
                    : "Offline model validation only; live ROS2, Unity, and Foxglove observations are not collected by --phase99, so manual gate evidence remains explicit."
            };
        }

        private static Phase99EvidenceItem Item(string id, Phase99EvidenceStatus status, bool isCore)
            => new Phase99EvidenceItem(id, "test", id, status, isCore, "", "", "");

        private static void WriteReport(string fullPath, Phase99ReleaseGateReport report)
            => File.WriteAllText(fullPath, JsonConvert.SerializeObject(report, Formatting.Indented), Encoding.UTF8);

        private static string RequiredTopicSummary(Phase98LiveEvidence evidence)
        {
            if (evidence.ProductTopics == null)
                return "productTopics missing";
            return string.Join(", ", evidence.ProductTopics.Select(topic => topic.Topic + "=" + topic.Status));
        }

        private static IEnumerable<string> PublicDocSources()
        {
            yield return ReadRepoText("README.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/README.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/README.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/16_ROS2_Bridge_Sample.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/README.md");
            yield return ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");
        }

        private static string ReadPackageVersion()
            => PackageVersion.Value;

        private static string ReadBridgePackageVersion()
        {
            var packageJson = JObject.Parse(ReadRepoText(
                "Packages/dev.unity2foxglove.ros2bridge/package.json"));
            var version = packageJson["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException(
                    "Bridge package version is missing from package.json.");

            return version;
        }

        private static string TryRunProcess(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo(fileName, arguments)
                {
                    WorkingDirectory = Phase16Validation.FindRepoRoot() ?? Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                    return "unknown";

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch
                    {
                    }

                    return "unknown";
                }

                _ = stderrTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    return "unknown";
                return stdoutTask.GetAwaiter().GetResult().Trim();
            }
            catch
            {
                return "unknown";
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
    }


}
