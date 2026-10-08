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
    /// Validation type for Phase99Verdict.
    /// </summary>
    public enum Phase99Verdict
    {
        Pass = 0,
        PassWithNotedLimitations = 1,
        Blocked = 2
    }

    /// <summary>
    /// Validation type for Phase99EvidenceStatus.
    /// </summary>
    public enum Phase99EvidenceStatus
    {
        Pass = 0,
        Fail = 1,
        Skipped = 2,
        NotRun = 3
    }

    /// <summary>
    /// Validation type for Phase99Validation.
    /// </summary>
    public static partial class Phase99Validation
    {
        public const int ReportSchemaVersion = 1;
        private static readonly Lazy<string> PackageVersion =
            new Lazy<string>(ReadBridgePackageVersion);

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

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
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
            var sdkPackage =
                JObject.Parse(ReadRepoText(
                    "Packages/dev.unity2foxglove.sdk/package.json"));
            var sdkSamples =
                sdkPackage["samples"]?.Children<JObject>().ToList()
                ?? new List<JObject>();
            var bridgePackage =
                JObject.Parse(ReadRepoText(
                    "Packages/dev.unity2foxglove.ros2bridge/package.json"));
            var bridgeSamples =
                bridgePackage["samples"]?.Children<JObject>().ToList()
                ?? new List<JObject>();
            var bridgeDependencies =
                bridgePackage["dependencies"] as JObject;
            Check(sdkSamples.Count == 3
                  && sdkSamples.All(sample =>
                      sample["displayName"]?.ToString()
                      != "ROS2 Bridge Sample")
                  && bridgePackage["name"]?.ToString()
                  == "dev.unity2foxglove.ros2bridge"
                  && bridgePackage["version"]?.ToString()
                  == "0.1.0-preview.1"
                  && bridgeDependencies != null
                  && bridgeDependencies.Properties()
                      .Select(property => property.Name)
                      .SequenceEqual(new[]
                      {
                          "dev.unity2foxglove.sdk"
                      })
                  && bridgeSamples.Count == 1
                  && bridgeSamples[0]["displayName"]?.ToString()
                  == "ROS2 Bridge Sample"
                  && bridgeSamples[0]["path"]?.ToString()
                  == "Samples~/Ros2BridgeSample",
                "99D-1: the optional Bridge package, not the ROS-free SDK, owns the Bridge identity, sole SDK dependency, and sample declaration");

            var releaseValidation = ReadRepoText("Scripts/package/validate_unity_package.py");
            Check(releaseValidation.Contains("EXPECTED_SAMPLE_COUNT = len(EXPECTED_SAMPLES)")
                  && releaseValidation.Contains("ROS2_BRIDGE_PACKAGE")
                  && releaseValidation.Contains("\"ROS2 Bridge package identity\"")
                  && releaseValidation.Contains("\"ROS2 Bridge dependency boundary\"")
                  && releaseValidation.Contains("\"ROS2 Bridge sample declaration\""),
                "99D-2: release validation checks SDK samples and the optional Bridge package boundary independently");

            var docsIndex = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/README.md");
            var bridgeGuide = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/16_ROS2_Bridge_Sample.md");
            Check(docsIndex.Contains(
                      "ROS-specific setup and sample guides are shipped by the relevant companion package.",
                      StringComparison.Ordinal)
                  && docsIndex.Contains(
                      "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample",
                      StringComparison.Ordinal)
                  && bridgeGuide.Contains("# ROS2 Bridge Provider and Sample", StringComparison.Ordinal)
                  && bridgeGuide.Contains(
                      "ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py",
                      StringComparison.Ordinal),
                "99D-3: SDK docs defer ROS setup while the Bridge package owns its sample guide");

            var publicText = string.Join("\n", PublicDocSources());
            Check(!publicText.Contains("Unity auto-launches", StringComparison.OrdinalIgnoreCase)
                  && !publicText.Contains("automatically launches the sidecar", StringComparison.OrdinalIgnoreCase),
                "99D-4: public docs do not claim Unity auto-launches sidecar");
        }
    }


}
