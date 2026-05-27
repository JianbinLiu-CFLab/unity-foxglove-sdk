// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-23 validation for core SDK sample package import boundaries.

using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_23Validation
    {
        private const string PackageRoot = "Packages/dev.unity2foxglove.sdk";
        private const string SamplesRoot = PackageRoot + "/Samples~";
        private const string ReleaseValidator = "Scripts/release/validate_package.py";

        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyPackageSampleDeclarations();
            VerifySampleImportAssets();
            VerifySampleMetaCoverage();
            VerifyReleaseValidatorKeepsSampleChecks();
            VerifySampleScriptBoundaries();
            VerifyDeepWikiSampleFindingsAreClosed();

            Console.WriteLine($"Phase134_23Validation: PASS ({_passed} checks)");
        }

        private static void VerifyPackageSampleDeclarations()
        {
            var packageJson = JObject.Parse(ReadRepoFile(PackageRoot + "/package.json"));
            var samples = packageJson["samples"] as JArray
                          ?? throw new InvalidOperationException("package.json samples must be an array");

            Check(samples.Count == 3, "134-23-A1: package.json declares exactly three importable core SDK samples");
            VerifySampleDeclaration(samples, "Basic Visualization", "Samples~/BasicVisualization");
            VerifySampleDeclaration(samples, "Full Demo Visualization", "Samples~/FullDemoVisualization");
            VerifySampleDeclaration(samples, "ROS2 Bridge Sample", "Samples~/Ros2BridgeSample");
        }

        private static void VerifySampleImportAssets()
        {
            RequireSampleFile("BasicVisualization/FoxgloveSimpleLayout.json", "134-23-B1: Basic sample layout asset exists");
            RequireSampleFile("BasicVisualization/Scenes/BasicVisualization.unity", "134-23-B2: Basic sample scene exists");
            RequireSampleFile("FullDemoVisualization/FoxgloveFullLayout.json", "134-23-B3: Full demo layout asset exists");
            RequireSampleFile("FullDemoVisualization/InputSystem_Actions.inputactions", "134-23-B4: Full demo input actions asset exists");
            RequireSampleFile("FullDemoVisualization/Scenes/FullDemoVisualization.unity", "134-23-B5: Full demo scene exists");
            RequireSampleFile("FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs", "134-23-B6: Full demo setup script exists");
            RequireSampleFile("Ros2BridgeSample/FoxgloveRos2BridgeLayout.json", "134-23-B7: ROS2 bridge sample layout asset exists");
            RequireSampleFile("Ros2BridgeSample/Scenes/Ros2BridgeSample.unity", "134-23-B8: ROS2 bridge sample scene exists");
            RequireSampleFile("Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs", "134-23-B9: ROS2 bridge sample controller exists");
            RequireSampleFile("Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs", "134-23-B10: ROS2 bridge point cloud helper exists");
            RequireSampleFile("Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs", "134-23-B11: ROS2 bridge laser scan helper exists");
        }

        private static void VerifySampleMetaCoverage()
        {
            VerifyMetaSidecar("BasicVisualization/FoxgloveSimpleLayout.json", "134-23-C1: Basic layout has Unity meta sidecar");
            VerifyMetaSidecar("BasicVisualization/Scenes/BasicVisualization.unity", "134-23-C2: Basic scene has Unity meta sidecar");
            VerifyMetaSidecar("FullDemoVisualization/FoxgloveFullLayout.json", "134-23-C3: Full demo layout has Unity meta sidecar");
            VerifyMetaSidecar("FullDemoVisualization/InputSystem_Actions.inputactions", "134-23-C4: Full demo input actions have Unity meta sidecar");
            VerifyMetaSidecar("FullDemoVisualization/Scenes/FullDemoVisualization.unity", "134-23-C5: Full demo scene has Unity meta sidecar");
            VerifyMetaSidecar("FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs", "134-23-C6: Full demo setup has Unity meta sidecar");
            VerifyMetaSidecar("Ros2BridgeSample/FoxgloveRos2BridgeLayout.json", "134-23-C7: ROS2 bridge layout has Unity meta sidecar");
            VerifyMetaSidecar("Ros2BridgeSample/Scenes/Ros2BridgeSample.unity", "134-23-C8: ROS2 bridge scene has Unity meta sidecar");
            VerifyMetaSidecar("Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs", "134-23-C9: ROS2 bridge controller has Unity meta sidecar");
            VerifyMetaSidecar("Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs", "134-23-C10: ROS2 bridge point cloud helper has Unity meta sidecar");
            VerifyMetaSidecar("Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs", "134-23-C11: ROS2 bridge laser scan helper has Unity meta sidecar");
        }

        private static void VerifyReleaseValidatorKeepsSampleChecks()
        {
            var validator = ReadRepoFile(ReleaseValidator);

            Check(validator.Contains("EXPECTED_SAMPLE_COUNT = 3", StringComparison.Ordinal)
                  && validator.Contains("SAMPLES = PACKAGE / \"Samples~\"", StringComparison.Ordinal),
                "134-23-D1: release validator keeps explicit Samples~ package boundary");
            Check(validator.Contains("\"Basic Visualization\": \"Samples~/BasicVisualization\"", StringComparison.Ordinal)
                  && validator.Contains("\"Full Demo Visualization\": \"Samples~/FullDemoVisualization\"", StringComparison.Ordinal)
                  && validator.Contains("\"ROS2 Bridge Sample\": \"Samples~/Ros2BridgeSample\"", StringComparison.Ordinal),
                "134-23-D2: release validator checks all core SDK sample declarations");
            Check(validator.Contains("check_sample_meta(results)", StringComparison.Ordinal)
                  && validator.Contains("check_sample_boundaries(results)", StringComparison.Ordinal)
                  && validator.Contains("check_forbidden_sample_artifacts(results)", StringComparison.Ordinal),
                "134-23-D3: release validator runs sample meta, boundary, and artifact checks");
            Check(validator.Contains("FORBIDDEN_SAMPLE_PARTS", StringComparison.Ordinal)
                  && validator.Contains("\"Generated\"", StringComparison.Ordinal)
                  && validator.Contains("__pycache__", StringComparison.Ordinal),
                "134-23-D4: release validator rejects generated and cache artifacts from package samples");
        }

        private static void VerifySampleScriptBoundaries()
        {
            var demoSetup = ReadRepoFile(SamplesRoot + "/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            Check(demoSetup.Contains("private void OnDestroy()", StringComparison.Ordinal)
                  && demoSetup.Contains("runtime.Parameters.OnParameterChanged -= OnParameterChanged", StringComparison.Ordinal)
                  && demoSetup.Contains("runtime.UnregisterService(_resetSvcId)", StringComparison.Ordinal),
                "134-23-E1: Full demo setup unregisters runtime callbacks and services on destroy");
            Check(demoSetup.Contains("SynchronizationContext.Current", StringComparison.Ordinal)
                  && demoSetup.Contains("_unityContext.Post", StringComparison.Ordinal),
                "134-23-E2: Full demo setup marshals parameter callbacks back to Unity context");
            Check(demoSetup.Contains("Mathf.Clamp(s, ScaleMinimum, ScaleMaximum)", StringComparison.Ordinal)
                  && demoSetup.Contains("float.IsNaN", StringComparison.Ordinal)
                  && demoSetup.Contains("float.IsInfinity", StringComparison.Ordinal),
                "134-23-E3: Full demo setup clamps and rejects invalid scale values");

            var pointCloud = ReadRepoFile(SamplesRoot + "/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs");
            Check(pointCloud.Contains("[SerializeField, Min(8)] private int _pointCount = 96;", StringComparison.Ordinal)
                  && pointCloud.Contains("_points = new Transform[_pointCount];", StringComparison.Ordinal)
                  && pointCloud.Contains("private void Awake()", StringComparison.Ordinal),
                "134-23-E4: ROS2 bridge point cloud helper creates a bounded sample point set during Awake");
            var updateIndex = pointCloud.IndexOf("private void Update()", StringComparison.Ordinal);
            Check(updateIndex >= 0
                  && pointCloud.IndexOf("new GameObject", updateIndex, StringComparison.Ordinal) < 0,
                "134-23-E5: ROS2 bridge point cloud helper does not allocate GameObjects in Update");
        }

        private static void VerifyDeepWikiSampleFindingsAreClosed()
        {
            var demoSetup = ReadRepoFile(SamplesRoot + "/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            Check(demoSetup.Contains("_manager.OnClientMessage += OnClientMessageReceived", StringComparison.Ordinal)
                  && demoSetup.Contains("_manager.OnClientMessage -= OnClientMessageReceived", StringComparison.Ordinal)
                  && !demoSetup.Contains("Session.OnClientMessage += OnClientMessageReceived", StringComparison.Ordinal),
                "134-23-F1: Full demo client-message logging uses FoxgloveManager main-thread event");
            Check(demoSetup.Contains("ClientPayloadPreviewBytes", StringComparison.Ordinal)
                  && demoSetup.Contains("FormatPayloadPreview", StringComparison.Ordinal)
                  && demoSetup.Contains("bytes={payload.Length}", StringComparison.Ordinal)
                  && !demoSetup.Contains("Encoding.UTF8.GetString(payload)", StringComparison.Ordinal),
                "134-23-F2: Full demo client-message payload logging is bounded and binary-aware");
            Check(demoSetup.Contains("_manager.RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor", StringComparison.Ordinal),
                "134-23-F3: Full demo service handler registration uses the FoxgloveManager facade");
            Check(demoSetup.Contains("WarnInvalidScaleOnce", StringComparison.Ordinal)
                  && demoSetup.Contains("Debug.LogWarning(\"[FoxgloveDemo] Ignoring invalid /cube/scale parameter", StringComparison.Ordinal),
                "134-23-F4: Full demo reports invalid scale parameters instead of silently swallowing them");
            Check(demoSetup.Contains("[SerializeField] private GameObject _cube;", StringComparison.Ordinal)
                  && demoSetup.Contains("using Player-tagged fallback object", StringComparison.Ordinal),
                "134-23-F5: Full demo prefers explicit cube assignment and warns on Player-tag fallback");

            var mouseDrag = ReadRepoFile(SamplesRoot + "/FullDemoVisualization/Scripts/MouseDragCube.cs");
            Check(mouseDrag.Contains("#if ENABLE_INPUT_SYSTEM", StringComparison.Ordinal)
                  && mouseDrag.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", StringComparison.Ordinal)
                  && mouseDrag.Contains("TryReadMouse", StringComparison.Ordinal),
                "134-23-F6: MouseDragCube compiles without a hard Input System package dependency");

            var manager = ReadRepoFile(PackageRoot + "/Runtime/Components/FoxgloveManager.cs");
            Check(manager.Contains("System.Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler", StringComparison.Ordinal)
                  && manager.Contains("_runtime?.RegisterService(descriptor, handler)", StringComparison.Ordinal),
                "134-23-F7: FoxgloveManager exposes handler-based service registration facade");

            var testLog = ReadRepoFile(SamplesRoot + "/FullDemoVisualization/Scripts/TestLog.cs");
            Check(testLog.Contains("_health = 95f + Mathf.Sin", StringComparison.Ordinal),
                "134-23-F8: Full demo FoxRun health telemetry changes over time");

            var triggerSmoke = ReadRepoFile(SamplesRoot + "/FullDemoVisualization/Scripts/FoxRunTriggerTelemetrySmoke.cs");
            Check(triggerSmoke.Contains("public long fixedCounter;", StringComparison.Ordinal),
                "134-23-F9: FoxRun trigger telemetry heartbeat counter is long-running safe");
        }

        private static void VerifySampleDeclaration(JArray samples, string displayName, string expectedPath)
        {
            foreach (var item in samples)
            {
                if (string.Equals((string)item["displayName"], displayName, StringComparison.Ordinal))
                {
                    Check(string.Equals((string)item["path"], expectedPath, StringComparison.Ordinal),
                        "134-23-A2: package.json sample path is stable for " + displayName);
                    Check(Directory.Exists(RepoPath(PackageRoot + "/" + expectedPath)),
                        "134-23-A3: package.json sample directory exists for " + displayName);
                    return;
                }
            }

            throw new InvalidOperationException("Missing package.json sample declaration: " + displayName);
        }

        private static void RequireSampleFile(string relativePath, string checkName)
        {
            Check(File.Exists(RepoPath(SamplesRoot + "/" + relativePath)), checkName);
        }

        private static void VerifyMetaSidecar(string relativePath, string checkName)
        {
            Check(File.Exists(RepoPath(SamplesRoot + "/" + relativePath + ".meta")), checkName);
        }

        private static string ReadRepoFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

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
