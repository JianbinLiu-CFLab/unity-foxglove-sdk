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
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    public enum Phase99Verdict
    {
        Pass = 0,
        PassWithNotedLimitations = 1,
        Blocked = 2
    }

    public enum Phase99EvidenceStatus
    {
        Pass = 0,
        Fail = 1,
        Skipped = 2,
        NotRun = 3
    }

    public static class Phase99Validation
    {
        public const int ReportSchemaVersion = 1;

        private static readonly string[] PhaseCommands =
        {
            "--phase90",
            "--phase91",
            "--phase92",
            "--phase93",
            "--phase94",
            "--phase95",
            "--phase96",
            "--phase97",
            "--phase98"
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 99: ROS2 Release Evidence Gate ===");
            _passed = 0;

            VerifyReportModelSerialization();
            VerifyVerdictClassification();
            VerifyEvidenceInventory();
            VerifyPackageAndDocsHygiene();
            VerifyPhase98BoundaryAlignment();
            VerifyCliWiring();

            Console.WriteLine($"Phase 99: {_passed} checks passed.");
        }

        public static Phase99ReleaseGateReport GenerateLiveReport(
            string jsonPath,
            string evidenceDir,
            string host,
            int port,
            string ros2Path)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("Phase 99 live report requires an output JSON path.", nameof(jsonPath));

            var fullJsonPath = Path.GetFullPath(jsonPath);
            var fullEvidenceDir = string.IsNullOrWhiteSpace(evidenceDir)
                ? Path.GetDirectoryName(fullJsonPath) ?? "."
                : Path.GetFullPath(evidenceDir);
            Directory.CreateDirectory(fullEvidenceDir);
            Directory.CreateDirectory(Path.GetDirectoryName(fullJsonPath) ?? ".");

            var items = new List<Phase99EvidenceItem>();
            var healthPath = Path.Combine(fullEvidenceDir, "phase97_health.live.json");
            var samplePath = Path.Combine(fullEvidenceDir, "phase98_sample.live.json");

            var health = Phase97Validation.GenerateHealthReport(
                healthPath,
                liveMode: true,
                ros2Path: ros2Path,
                host: host,
                port: port);
            items.Add(new Phase99EvidenceItem(
                "bridge.phase97.health",
                "ROS2 Bridge",
                "Phase 97 live health is Ready",
                health.Summary == Ros2BridgeHealthSummary.Ready ? Phase99EvidenceStatus.Pass : Phase99EvidenceStatus.Fail,
                true,
                "dotnet run ... -- --phase97-health --phase97-live --json " + healthPath,
                healthPath,
                "summary=" + health.Summary));

            Phase98LiveEvidence sampleEvidence = null;
            try
            {
                sampleEvidence = Phase98Validation.GenerateLiveEvidence(samplePath, host, port, ros2Path);
            }
            catch (Exception ex)
            {
                items.Add(new Phase99EvidenceItem(
                    "bridge.phase98.live",
                    "ROS2 Bridge",
                    "Phase 98 live sample evidence",
                    Phase99EvidenceStatus.Fail,
                    true,
                    "dotnet run ... -- --phase98-live --json " + samplePath,
                    samplePath,
                    ex.Message));
            }

            if (sampleEvidence != null)
            {
                var requiredTopicsPass = sampleEvidence.ProductTopics != null
                                         && sampleEvidence.ProductTopics.Length == 6
                                         && sampleEvidence.ProductTopics.All(topic => topic.Status.StartsWith("sent", StringComparison.Ordinal));
                items.Add(new Phase99EvidenceItem(
                    "bridge.phase98.required_topics",
                    "ROS2 Bridge",
                    "Six required product topics were sent",
                    requiredTopicsPass ? Phase99EvidenceStatus.Pass : Phase99EvidenceStatus.Fail,
                    true,
                    "dotnet run ... -- --phase98-live --json " + samplePath,
                    samplePath,
                    RequiredTopicSummary(sampleEvidence)));

                var allSchemaPass = sampleEvidence.AllSchema != null && sampleEvidence.AllSchema.SentFrames == 41;
                items.Add(new Phase99EvidenceItem(
                    "bridge.phase98.all_schema",
                    "ROS2 Bridge",
                    "All 41 deterministic schema samples were sent",
                    allSchemaPass ? Phase99EvidenceStatus.Pass : Phase99EvidenceStatus.Fail,
                    true,
                    "dotnet run ... -- --phase98-live --json " + samplePath,
                    samplePath,
                    sampleEvidence.AllSchema == null
                        ? "allSchema missing"
                        : "sentFrames=" + sampleEvidence.AllSchema.SentFrames));

                var dracoStatus = sampleEvidence.OptionalDracoTopic?.Status ?? "skipped_no_status";
                items.Add(new Phase99EvidenceItem(
                    "optional.draco",
                    "Optional",
                    "Optional Draco compressed point cloud",
                    dracoStatus.StartsWith("sent", StringComparison.Ordinal)
                        ? Phase99EvidenceStatus.Pass
                        : dracoStatus.StartsWith("skipped", StringComparison.Ordinal)
                            ? Phase99EvidenceStatus.Skipped
                            : Phase99EvidenceStatus.Fail,
                    false,
                    "dotnet run ... -- --phase98-live --json " + samplePath,
                    samplePath,
                    dracoStatus));
            }

            items.Add(new Phase99EvidenceItem(
                "manual.ros2.topic_checks",
                "Manual ROS2",
                "Representative ros2 topic list/info/echo/hz checks",
                Phase99EvidenceStatus.NotRun,
                true,
                "ros2 topic list/info/echo/hz commands from Phase 99 plan",
                "",
                "Manual observation must be recorded in the release handoff or PR evidence summary."));
            items.Add(new Phase99EvidenceItem(
                "manual.unity.sample_import",
                "Manual Unity",
                "Import and run ROS2 Bridge Sample in Unity",
                Phase99EvidenceStatus.NotRun,
                true,
                "Import ROS2 Bridge Sample, open scene, press Play",
                "",
                "Manual observation must be recorded in the release handoff or PR evidence summary."));
            items.Add(new Phase99EvidenceItem(
                "manual.foxglove.visual",
                "Manual Foxglove",
                "Foxglove layout renders representative panels",
                Phase99EvidenceStatus.NotRun,
                true,
                "Open FoxgloveRos2BridgeLayout.json and inspect panels",
                "",
                "Manual observation must be recorded in the release handoff or PR evidence summary."));
            items.Add(new Phase99EvidenceItem(
                "optional.rosbag2",
                "Optional",
                "rosbag2 short recording",
                Phase99EvidenceStatus.Skipped,
                false,
                "ros2 bag record /unity2foxglove/...",
                "",
                "Optional evidence is collected manually when rosbag2 is available."));
            items.Add(new Phase99EvidenceItem(
                "optional.rviz2",
                "Optional",
                "RViz2 exploratory observation",
                Phase99EvidenceStatus.Skipped,
                false,
                "Open RViz2 if the environment has a realistic foxglove_msgs path",
                "",
                "Native RViz2 compatibility is deferred to Phase 120."));

            var report = CreateReport(items, host, port, liveMode: true);
            WriteReport(fullJsonPath, report);
            return report;
        }

        private static void VerifyReportModelSerialization()
        {
            var report = CreateReport(new[]
            {
                Item("automated.phase98", Phase99EvidenceStatus.Pass, isCore: true),
                Item("optional.draco", Phase99EvidenceStatus.Skipped, isCore: false)
            }, "127.0.0.1", 8767, liveMode: false);
            var json = JsonConvert.SerializeObject(report, Formatting.Indented);
            var parsed = JObject.Parse(json);

            Check(parsed["schemaVersion"]?.Value<int>() == ReportSchemaVersion,
                "99A-1: report schema version serializes");
            Check(parsed["verdict"]?.ToString() == "PassWithNotedLimitations",
                "99A-2: verdict serializes as stable string");
            Check(parsed["evidence"]?.Children().Count() == 2,
                "99A-3: evidence items serialize as array");
            Check(parsed["packageVersion"]?.ToString() == ReadPackageVersion(),
                "99A-4: report records package version");
            Check(!string.IsNullOrWhiteSpace(parsed["commit"]?.ToString()),
                "99A-5: report records commit field");
        }

        private static void VerifyVerdictClassification()
        {
            Check(Classify(new[]
            {
                Item("core", Phase99EvidenceStatus.Pass, true),
                Item("optional", Phase99EvidenceStatus.Pass, false)
            }) == Phase99Verdict.Pass, "99B-1: all pass maps to PASS");
            Check(Classify(new[]
            {
                Item("core", Phase99EvidenceStatus.Pass, true),
                Item("optional", Phase99EvidenceStatus.Skipped, false)
            }) == Phase99Verdict.PassWithNotedLimitations, "99B-2: optional skipped maps to PASS WITH NOTED LIMITATIONS");
            Check(Classify(new[]
            {
                Item("core", Phase99EvidenceStatus.NotRun, true),
                Item("optional", Phase99EvidenceStatus.Pass, false)
            }) == Phase99Verdict.Blocked, "99B-3: core not-run maps to BLOCKED");
            Check(Classify(new[]
            {
                Item("core", Phase99EvidenceStatus.Fail, true),
                Item("optional", Phase99EvidenceStatus.Skipped, false)
            }) == Phase99Verdict.Blocked, "99B-4: core fail maps to BLOCKED");
            Check(Classify(new[]
            {
                Item("core", Phase99EvidenceStatus.Pass, true),
                Item("optional", Phase99EvidenceStatus.Fail, false)
            }) == Phase99Verdict.PassWithNotedLimitations, "99B-5: optional fail is limitation unless promoted to core");
        }

        private static void VerifyEvidenceInventory()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            foreach (var phaseCommand in PhaseCommands)
                Check(registry.Contains("\"" + phaseCommand + "\"", StringComparison.Ordinal),
                    "99C-1: Program exposes " + phaseCommand);

            Check(program.Contains("--phase91-ros2-cdr-mcap")
                  && program.Contains("--phase92-ros2-product-mcap")
                  && program.Contains("--phase93-ros2-full-mcap"),
                "99C-2: MCAP evidence commands remain available");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase98Validation.cs")
                    .Contains("--phase98-live"),
                "99C-3: Phase98 live evidence path is documented in validation source");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase98Validation.cs")
                    .Contains("deferred_to_phase99_manual_gate"),
                "99C-4: Phase98 honestly defers ROS2 graph observation to Phase99");
        }

        private static void VerifyPackageAndDocsHygiene()
        {
            var packageJson = JObject.Parse(ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json"));
            var samples = packageJson["samples"]?.Children<JObject>().ToList() ?? new List<JObject>();
            Check(samples.Count == 3 && samples.Any(sample => sample["displayName"]?.ToString() == "ROS2 Bridge Sample"),
                "99D-1: package metadata includes ROS2 Bridge Sample as third sample");

            var releaseValidation = ReadRepoText("Scripts/release/validate_package.py");
            Check(releaseValidation.Contains("EXPECTED_SAMPLE_COUNT = 3")
                  && releaseValidation.Contains("\"ROS2 Bridge Sample\""),
                "99D-2: release validation expects the third sample");

            var docsIndex = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/README.md");
            Check(docsIndex.Contains("16_ROS2_Bridge_Sample"),
                "99D-3: package docs index links ROS2 Bridge sample guide");

            var publicText = string.Join("\n", PublicDocSources());
            Check(!publicText.Contains("Unity auto-launches", StringComparison.OrdinalIgnoreCase)
                  && !publicText.Contains("automatically launches the sidecar", StringComparison.OrdinalIgnoreCase),
                "99D-4: public docs do not claim Unity auto-launches sidecar");
        }

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

            var sampleReadme = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/README.md");
            Check(sampleReadme.Contains("Required bridge topics")
                  && sampleReadme.Contains("Optional topic")
                  && sampleReadme.Contains("skips compressed point-cloud output"),
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
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/16_ROS2_Bridge_Sample.md");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/README.md");
            yield return ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");
        }

        private static string ReadPackageVersion()
        {
            var packageJson = JObject.Parse(ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json"));
            var version = packageJson["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("Package version is missing from package.json.");

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

                if (process.ExitCode != 0)
                    return "unknown";
                return process.StandardOutput.ReadToEnd().Trim();
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
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }
    }

    public sealed class Phase99ReleaseGateReport
    {
        [JsonProperty("schemaVersion", Order = 1)]
        public int SchemaVersion { get; set; }

        [JsonProperty("generatedAtUtc", Order = 2)]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("commit", Order = 3)]
        public string Commit { get; set; }

        [JsonProperty("packageVersion", Order = 4)]
        public string PackageVersion { get; set; }

        [JsonProperty("unityVersion", Order = 5)]
        public string UnityVersion { get; set; }

        [JsonProperty("foxgloveVersion", Order = 6)]
        public string FoxgloveVersion { get; set; }

        [JsonProperty("rosDistro", Order = 7)]
        public string RosDistro { get; set; }

        [JsonProperty("rmwImplementation", Order = 8)]
        public string RmwImplementation { get; set; }

        [JsonProperty("os", Order = 9)]
        public string Os { get; set; }

        [JsonProperty("host", Order = 10)]
        public string Host { get; set; }

        [JsonProperty("port", Order = 11)]
        public int Port { get; set; }

        [JsonProperty("liveMode", Order = 12)]
        public bool LiveMode { get; set; }

        [JsonProperty("evidence", Order = 13)]
        public List<Phase99EvidenceItem> Evidence { get; set; }

        [JsonProperty("verdict", Order = 14)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Phase99Verdict Verdict { get; set; }

        [JsonProperty("finalNotes", Order = 15)]
        public string FinalNotes { get; set; }
    }

    public sealed class Phase99EvidenceItem
    {
        public Phase99EvidenceItem(
            string id,
            string area,
            string title,
            Phase99EvidenceStatus status,
            bool isCore,
            string command,
            string artifactPath,
            string notes)
        {
            Id = id ?? string.Empty;
            Area = area ?? string.Empty;
            Title = title ?? string.Empty;
            Status = status;
            IsCore = isCore;
            Command = command ?? string.Empty;
            ArtifactPath = artifactPath ?? string.Empty;
            Notes = notes ?? string.Empty;
        }

        [JsonProperty("id", Order = 1)]
        public string Id { get; }

        [JsonProperty("area", Order = 2)]
        public string Area { get; }

        [JsonProperty("title", Order = 3)]
        public string Title { get; }

        [JsonProperty("status", Order = 4)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Phase99EvidenceStatus Status { get; }

        [JsonProperty("isCore", Order = 5)]
        public bool IsCore { get; }

        [JsonProperty("command", Order = 6)]
        public string Command { get; }

        [JsonProperty("artifactPath", Order = 7)]
        public string ArtifactPath { get; }

        [JsonProperty("notes", Order = 8)]
        public string Notes { get; }
    }
}
