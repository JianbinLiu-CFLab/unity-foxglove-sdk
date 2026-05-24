// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 130 MarkerArray RViz2 acceptance kit validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase130Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string SampleName = "RViz2 MarkerArray Acceptance";
        private const string SamplePath = OptionalPackage + "/Samples~/RViz2 MarkerArray Acceptance";
        private const string SampleReadmePath = SamplePath + "/README.md";
        private const string SmokeScriptPath = SamplePath + "/Phase130Rviz2MarkerArraySmoke.cs";
        private const string BuilderScriptPath = SamplePath + "/Phase130MarkerArrayMessageBuilder.cs";
        private const string RvizConfigPath = SamplePath + "/rviz2_phase130_markerarray.rviz";
        private const string EvidenceTemplatePath = SamplePath + "/phase130_markerarray_evidence_template.md";
        private const string AcceptanceScriptPath = "Scripts/smoke/phase130_markerarray_acceptance.py";
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 130: MarkerArray Scene Mapping v1 ===");
            _passed = 0;

            VerifyPackageSampleMetadata();
            VerifySampleFiles();
            VerifySmokeScript();
            VerifyMarkerArrayBuilder();
            VerifyRvizConfig();
            VerifyAcceptanceHelper();
            VerifyDocsAndEvidenceTemplate();
            VerifyReleaseValidatorAcceptsFourSamples();
            VerifyCoreAndOptionalRuntimeBoundaries();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 130: {_passed} checks passed.");
        }

        private static void VerifyPackageSampleMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "130A-1: optional package declares package samples");

            JsonElement? sample = null;
            foreach (var entry in samples.EnumerateArray())
            {
                if (entry.TryGetProperty("displayName", out var displayName)
                    && displayName.GetString() == SampleName)
                {
                    sample = entry;
                    break;
                }
            }

            Check(sample.HasValue, "130A-2: optional package sample entry exists for MarkerArray acceptance");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/RViz2 MarkerArray Acceptance",
                "130A-3: optional package sample entry points at the MarkerArray acceptance sample");
            Check(value.TryGetProperty("description", out var description)
                  && (description.GetString() ?? string.Empty).Contains("visualization_msgs/msg/MarkerArray", StringComparison.Ordinal)
                  && (description.GetString() ?? string.Empty).Contains("/markers", StringComparison.Ordinal),
                "130A-4: optional package sample description names MarkerArray and /markers");
        }

        private static void VerifySampleFiles()
        {
            foreach (var path in new[]
                     {
                         SampleReadmePath,
                         SmokeScriptPath,
                         BuilderScriptPath,
                         RvizConfigPath,
                         EvidenceTemplatePath,
                         AcceptanceScriptPath
                     })
            {
                Check(RepoFileExists(path), "130B-1: Phase130 file exists: " + path);
            }
        }

        private static void VerifySmokeScript()
        {
            var script = ReadRepoText(SmokeScriptPath);

            Check(script.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(script),
                "130C-1: smoke script guards ROS2 For Unity and generated message references");
            Check(script.Contains("NodeName = \"unity2foxglove_phase130_markerarray\"", StringComparison.Ordinal)
                  && script.Contains("MarkersTopic = \"/markers\"", StringComparison.Ordinal)
                  && !script.Contains("\"/clock\"", StringComparison.Ordinal),
                "130C-2: smoke script uses the required node and marker topic without /clock");
            Check(script.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains(".Ok()", StringComparison.Ordinal),
                "130C-3: smoke script finds or adds ROS2UnityComponent and waits for Ok()");
            Check(script.Contains("CreatePublisher<visualization_msgs.msg.MarkerArray>", StringComparison.Ordinal)
                  && !script.Contains("CreatePublisher<tf2_msgs.msg.TFMessage>", StringComparison.Ordinal)
                  && !script.Contains("CreatePublisher<sensor_msgs.msg", StringComparison.Ordinal)
                  && !script.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "130C-4: smoke script publishes only MarkerArray with default R2FU QoS");
            Check(script.Contains("CreateStamp", StringComparison.Ordinal)
                  && script.Contains("ROS2 Time.sec is int32", StringComparison.Ordinal)
                  && script.Contains("Y2038", StringComparison.Ordinal)
                  && !script.Contains("(uint)sec", StringComparison.Ordinal),
                "130C-5: smoke script writes monotonic ROS-compatible timestamps without unsigned sec casts");
            Check(script.Contains("_warnedMissingStartExecutor", StringComparison.Ordinal)
                  && script.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal)
                  && script.Contains("Import ROS2 For Unity", StringComparison.Ordinal),
                "130C-6: smoke script reports missing define and optional StartExecutor diagnostics clearly");
            Check(script.Contains("_publishedMarkerArrayCount", StringComparison.Ordinal)
                  && script.Contains("_activeMarkerCount", StringComparison.Ordinal)
                  && script.Contains("_lastMarkerId", StringComparison.Ordinal)
                  && script.Contains("_lastAction", StringComparison.Ordinal)
                  && script.Contains("_runtimeRoot", StringComparison.Ordinal)
                  && script.Contains("_runtimeRootIsPackage", StringComparison.Ordinal)
                  && script.Contains("_assetRuntimePresent", StringComparison.Ordinal)
                  && script.Contains("_lastError", StringComparison.Ordinal),
                "130C-7: smoke script exposes required Inspector evidence fields");
            Check(script.Contains("BuildAddOrModify", StringComparison.Ordinal)
                  && script.Contains("BuildDelete(", StringComparison.Ordinal)
                  && script.Contains("BuildDeleteAll", StringComparison.Ordinal)
                  && script.Contains("DeleteCycleLength", StringComparison.Ordinal),
                "130C-8: smoke script exercises ADD, DELETE, and DELETEALL marker actions");
        }

        private static void VerifyMarkerArrayBuilder()
        {
            var builder = ReadRepoText(BuilderScriptPath);

            Check(builder.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(builder),
                "130D-1: MarkerArray builder guards generated ROS2 message references");
            Check(builder.Contains("DefaultFrameId = \"map\"", StringComparison.Ordinal)
                  && builder.Contains("DefaultNamespace = \"unity2foxglove\"", StringComparison.Ordinal)
                  && builder.Contains("CreateDeterministicId", StringComparison.Ordinal)
                  && builder.Contains("FnvOffsetBasis", StringComparison.Ordinal)
                  && builder.Contains("FnvPrime", StringComparison.Ordinal)
                  && builder.Contains("0x7fffffff", StringComparison.Ordinal),
                "130D-2: builder uses explicit map frame, namespace, and positive FNV-1a IDs");
            Check(builder.Contains("Marker.ADD", StringComparison.Ordinal)
                  && builder.Contains("Marker.DELETE", StringComparison.Ordinal)
                  && builder.Contains("Marker.DELETEALL", StringComparison.Ordinal)
                  && builder.Contains("Marker.CUBE", StringComparison.Ordinal)
                  && builder.Contains("Type = type", StringComparison.Ordinal)
                  && builder.Contains("Action = action", StringComparison.Ordinal),
                "130D-3: builder writes cube markers and cleanup actions");
            Check(builder.Contains("Lifetime = new builtin_interfaces.msg.Duration", StringComparison.Ordinal)
                  && builder.Contains("Sec = 0", StringComparison.Ordinal)
                  && builder.Contains("Nanosec = 0u", StringComparison.Ordinal)
                  && builder.Contains("Frame_locked = false", StringComparison.Ordinal),
                "130D-4: builder sets zero lifetime and does not frame-lock markers");
            Check(builder.Contains("BuildDelete(string stableName", StringComparison.Ordinal)
                  && builder.Contains("CreateBaseMarker(stableName", StringComparison.Ordinal)
                  && builder.Contains("Ns = DefaultNamespace", StringComparison.Ordinal)
                  && builder.Contains("Id = CreateDeterministicId(stableName)", StringComparison.Ordinal),
                "130D-5: DELETE reuses the same namespace and deterministic ID as ADD");
        }

        private static void VerifyRvizConfig()
        {
            var rviz = ReadRepoText(RvizConfigPath);
            Check(rviz.Contains("Fixed Frame: map", StringComparison.Ordinal)
                  && rviz.Contains("/markers", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/MarkerArray", StringComparison.Ordinal),
                "130E-1: RViz2 config targets map and /markers MarkerArray");
            Check(!ContainsAny(rviz, new[]
                  {
                      "rviz_default_plugins/TF",
                      "LaserScan",
                      "PointCloud2",
                      "CameraInfo",
                      "rviz_default_plugins/Image",
                      "MCAP",
                      "rosbag2"
                  }),
                "130E-2: RViz2 config avoids unrelated displays and workflows");
        }

        private static void VerifyAcceptanceHelper()
        {
            var script = ReadRepoText(AcceptanceScriptPath);

            Check(script.Contains("# Purpose:", StringComparison.Ordinal)
                  && script.Contains("argparse", StringComparison.Ordinal)
                  && script.Contains("phase130", StringComparison.Ordinal),
                "130F-1: Python acceptance helper has repository header and CLI entry point");
            Check(script.Contains("ros2-script.py", StringComparison.Ordinal)
                  && script.Contains(".pixi", StringComparison.Ordinal)
                  && script.Contains(@"C:\ros2_jazzy\ros2-windows", StringComparison.Ordinal),
                "130F-2: helper uses pinned Windows Jazzy pixi Python and ros2-script.py");
            Check(script.Contains("--no-daemon", StringComparison.Ordinal)
                  && script.Contains("topic\", \"info\"", StringComparison.Ordinal)
                  && script.Contains("\"-v\"", StringComparison.Ordinal)
                  && script.Contains("node\", \"list\"", StringComparison.Ordinal),
                "130F-3: helper uses no-daemon graph checks, node list, and topic info -v");
            Check(script.Contains("unity2foxglove_phase130_markerarray", StringComparison.Ordinal)
                  && script.Contains("/markers", StringComparison.Ordinal)
                  && script.Contains("visualization_msgs/msg/MarkerArray", StringComparison.Ordinal)
                  && script.Contains("Publisher count:", StringComparison.Ordinal)
                  && script.Contains("Node name:", StringComparison.Ordinal),
                "130F-4: helper proves required publisher endpoint belongs to the Phase130 node");
            Check(script.Contains("--once", StringComparison.Ordinal)
                  && script.Contains("--spin-time", StringComparison.Ordinal)
                  && script.Contains("frame_id: map", StringComparison.Ordinal)
                  && script.Contains("ns: unity2foxglove", StringComparison.Ordinal)
                  && script.Contains("type: 1", StringComparison.Ordinal)
                  && script.Contains("action: 0", StringComparison.Ordinal)
                  && script.Contains("action: 2", StringComparison.Ordinal)
                  && script.Contains("action: 3", StringComparison.Ordinal)
                  && script.Contains("sec: 0", StringComparison.Ordinal)
                  && script.Contains("nanosec: 0", StringComparison.Ordinal),
                "130F-5: helper echoes MarkerArray once with bounded spin time and content checks");
            Check(script.Contains("--launch-rviz", StringComparison.Ordinal)
                  && script.Contains("--rviz-config", StringComparison.Ordinal)
                  && script.Contains("--rmw", StringComparison.Ordinal)
                  && script.Contains("--discovery-range", StringComparison.Ordinal)
                  && script.Contains("rviz2.exe", StringComparison.Ordinal)
                  && script.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && script.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && !script.Contains("\"run\", \"rviz2\"", StringComparison.Ordinal)
                  && !script.Contains("env[\"ROS_AUTOMATIC_DISCOVERY_RANGE\"] = \"SUBNET\"", StringComparison.Ordinal),
                "130F-6: helper supports RMW/discovery selection and launches RViz2 through direct rviz2.exe");
        }

        private static void VerifyDocsAndEvidenceTemplate()
        {
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var sampleReadme = ReadRepoText(SampleReadmePath);
            var evidence = ReadRepoText(EvidenceTemplatePath);
            var combined = optionalReadme + "\n" + sampleReadme + "\n" + evidence;

            Check(optionalReadme.Contains("RViz2 MarkerArray Acceptance", StringComparison.Ordinal)
                  && optionalReadme.Contains("/markers", StringComparison.Ordinal)
                  && optionalReadme.Contains("visualization_msgs/msg/MarkerArray", StringComparison.Ordinal),
                "130G-1: optional package README mentions the MarkerArray acceptance kit");
            Check(sampleReadme.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sampleReadme.Contains("external ROS2 For Unity", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("python Scripts\\smoke\\phase130_markerarray_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("--ros2-root C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && sampleReadme.Contains("--rviz-config", StringComparison.Ordinal)
                  && sampleReadme.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-script.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("/markers", StringComparison.Ordinal)
                  && sampleReadme.Contains("frame_id = map", StringComparison.Ordinal)
                  && !sampleReadme.Contains("\nros2 topic", StringComparison.Ordinal),
                "130G-2: sample README documents fixed-frame markers and the Windows helper path");
            Check(sampleReadme.Contains("FNV-1a", StringComparison.Ordinal)
                  && sampleReadme.Contains("DELETE", StringComparison.Ordinal)
                  && sampleReadme.Contains("DELETEALL", StringComparison.Ordinal)
                  && sampleReadme.Contains("lifetime is zero", StringComparison.OrdinalIgnoreCase),
                "130G-3: sample README documents marker ID, lifetime, and cleanup behavior");
            Check(evidence.Contains("OS", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("Unity version", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("ROS2 distro", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RMW implementation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("runtime root", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("topic info -v /markers", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("/markers echo", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RViz2 MarkerArray observation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("verdict", StringComparison.OrdinalIgnoreCase),
                "130G-4: evidence template captures CLI, runtime, RViz2, screenshot, and verdict evidence");
            Check(!ContainsAny(combined, new[]
                  {
                      "supports arbitrary marker types",
                      "supports mesh resources",
                      "supports text markers",
                      "supports interactive markers",
                      "supports PointCloud2 subscription",
                      "supports CameraInfo",
                      "supports Image",
                      "supports MCAP replay fanout",
                      "supports rosbag2"
                  }),
                "130G-5: docs do not over-claim deferred MarkerArray or ROS2 workflows");
        }

        private static void VerifyReleaseValidatorAcceptsFourSamples()
        {
            var script = ReadRepoText("Scripts/release/validate_ros2forunity_package.py");
            Check(script.Contains("RVIZ_MARKERARRAY_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RViz2 MarkerArray Acceptance", StringComparison.Ordinal)
                  && script.Contains("len(samples) == 4", StringComparison.Ordinal)
                  && script.Contains("External Adapter", StringComparison.Ordinal)
                  && script.Contains("Phase 128", StringComparison.Ordinal)
                  && script.Contains("Phase 129", StringComparison.Ordinal)
                  && script.Contains("Phase 130", StringComparison.Ordinal),
                "130H-1: release validator accepts External Adapter, Phase 128, Phase 129, and Phase 130 samples");
        }

        private static void VerifyCoreAndOptionalRuntimeBoundaries()
        {
            var coreProductionRoots = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime",
                "Packages/dev.unity2foxglove.sdk/Editor",
                "Packages/dev.unity2foxglove.sdk/Samples~"
            };

            var coreHits = coreProductionRoots
                .SelectMany(ExistingTextFilesOrSingleFile)
                .SelectMany(path => CoreProductionForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(coreHits.Count == 0,
                "130I-1: core SDK production surface has no hard R2FU or standard ROS2 message references"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));

            var optionalRuntimeHits = ExistingTextFilesOrSingleFile(OptionalPackage + "/Runtime")
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(optionalRuntimeHits.Count == 0,
                "130I-2: optional package Runtime remains facade-only with no R2FU/message references"
                + (optionalRuntimeHits.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeHits) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase130\"", StringComparison.Ordinal)
                  && registry.Contains("Phase130Validation.Validate", StringComparison.Ordinal),
                "130J-1: validation registry wires --phase130");
            Check(project.Contains("Phase130Validation.cs", StringComparison.Ordinal),
                "130J-2: test project compiles Phase130Validation");
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "tf2_msgs.msg",
                "sensor_msgs.msg",
                "visualization_msgs.msg"
            };
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "ISubscription<",
                "tf2_msgs",
                "sensor_msgs",
                "visualization_msgs"
            };
        }

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            var tokens = new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "tf2_msgs",
                "sensor_msgs",
                "visualization_msgs",
                "std_msgs",
                "geometry_msgs",
                "builtin_interfaces"
            };

            var stack = new Stack<bool>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                {
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(false);
                    continue;
                }

                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (tokens.Any(token => line.Contains(token, StringComparison.Ordinal))
                    && !stack.Any(guarded => guarded))
                {
                    throw new InvalidOperationException("Unguarded Phase130 R2FU reference on line " + (i + 1) + ": " + trimmed);
                }
            }

            return true;
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (File.Exists(path))
                return new[] { path };
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(IsTextFile)
                .ToArray();
        }

        private static bool IsTextFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase130 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static string Rel(string path)
        {
            return path.Replace(RepoRoot() + Path.DirectorySeparatorChar, string.Empty)
                .Replace('\\', '/');
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException(description);

            _passed++;
            Console.WriteLine("[PASS] " + description);
        }
    }
}
