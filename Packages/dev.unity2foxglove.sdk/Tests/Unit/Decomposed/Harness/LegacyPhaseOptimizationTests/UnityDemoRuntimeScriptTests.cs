// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-21/22/23/24/27 legacy phase optimization checks.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-27")]
    [Trait("Domain", "Harness")]
    public sealed class UnityDemoRuntimeScriptTests
    {
        [Fact]
        public void DemoRuntimeAndManualAcceptanceContractsStayExplicit()
        {
            var fullDemo = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var mouseDrag = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs");
            var status = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveStatusSmoke.cs");
            var testLog = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");
            var scene = TestSources.Text("Unity2Foxglove/Assets/Scenes/SampleScene.unity");

            Assert.Contains("var runtime = _manager?.Runtime;", fullDemo, StringComparison.Ordinal);
            Assert.Contains("if (runtime?.Session == null)", fullDemo, StringComparison.Ordinal);
            Assert.Contains("_initialized = false;", fullDemo, StringComparison.Ordinal);
            Assert.DoesNotContain("FindGameObjectWithTag(\"Player\")", fullDemo, StringComparison.Ordinal);
            Assert.DoesNotContain("using Player-tagged fallback object", fullDemo, StringComparison.Ordinal);
            Assert.Contains("[FoxService(", fullDemo, StringComparison.Ordinal);
            Assert.Contains("private ResetPoseResponse ResetPose(ResetPoseRequest request)", fullDemo, StringComparison.Ordinal);
            Assert.Contains("return new ResetPoseResponse { status = \"error\", reason = \"cube not found\" };", fullDemo, StringComparison.Ordinal);
            Assert.Contains("private static readonly UTF8Encoding StrictUtf8", fullDemo, StringComparison.Ordinal);
            Assert.Contains("new UTF8Encoding(false, true)", fullDemo, StringComparison.Ordinal);
            Assert.Contains("#if ENABLE_INPUT_SYSTEM", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("TryReadMouse", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("private Camera _camera;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("_camera = Camera.main;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("var cam = _camera;", mouseDrag, StringComparison.Ordinal);
            Assert.Contains("#if ENABLE_INPUT_SYSTEM", status, StringComparison.Ordinal);
            Assert.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", status, StringComparison.Ordinal);
            Assert.Contains("WasKeyPressed", status, StringComparison.Ordinal);
            Assert.Contains("private Vector3 _position2;", testLog, StringComparison.Ordinal);
            Assert.DoesNotContain("public Vector3 position;", testLog, StringComparison.Ordinal);
            Assert.DoesNotContain("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\r\n  _manager: {fileID: 0}\r\n  _cube: {fileID: 0}", scene, StringComparison.Ordinal);
            Assert.DoesNotContain("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\n  _manager: {fileID: 0}\n  _cube: {fileID: 0}", scene, StringComparison.Ordinal);
        }

        [Fact]
        public void FullDemoContainsOneFoxRun115FProbeAndNoFakePhase147Probe()
        {
            var liveScene = TestSources.Text("Unity2Foxglove/Assets/Scenes/SampleScene.unity");
            var packageScene = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scenes/FullDemoVisualization.unity");

            foreach (var scene in new[] { liveScene, packageScene })
            {
                Assert.DoesNotContain("m_Name: Phase147ManualProbe", scene, StringComparison.Ordinal);
                Assert.Single(
                    Regex.Matches(
                        scene,
                        "m_EditorClassIdentifier: Assembly-CSharp::FoxRun115FManualProbe",
                        RegexOptions.CultureInvariant).Cast<Match>());
            }
        }

        [Fact]
        public void FullDemoEnablesFoxRunSubscriptionsByDefault()
        {
            var liveScene = TestSources.Text("Unity2Foxglove/Assets/Scenes/SampleScene.unity");
            var packageScene = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scenes/FullDemoVisualization.unity");

            foreach (var scene in new[] { liveScene, packageScene })
                Assert.Contains("_enableFoxRunInbound: 1", scene, StringComparison.Ordinal);
        }

        [Fact]
        public void DotNetTestSourcesHaveAnExplicitDisabledUnityAssemblyBoundary()
        {
            var asmdef = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Tests/Unity.FoxgloveSDK.DotNetTests.asmdef");

            Assert.Contains(
                "\"defineConstraints\": [\n    \"UNITY2FOXGLOVE_DOTNET_TESTS\"\n  ]",
                asmdef,
                StringComparison.Ordinal);
            Assert.Contains("\"autoReferenced\": false", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"noEngineReferences\": true", asmdef, StringComparison.Ordinal);
        }

        [Fact]
        public void ManualSmokeStateAndCountersRemainLongRunningSafe()
        {
            var overlaySmoke = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveDebugOverlaySmoke.cs");
            var sampleScene = TestSources.Text("Unity2Foxglove/Assets/Scenes/SampleScene.unity");

            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveDebugOverlaySmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs");

            Assert.Contains("[SerializeField] private bool _disableNormalPublish = true;", overlaySmoke, StringComparison.Ordinal);
            Assert.Contains("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDebugOverlaySmoke", sampleScene, StringComparison.Ordinal);
            Assert.Contains("_disableNormalPublish: 1", sampleScene, StringComparison.Ordinal);

            var phase110 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");
            var phase127 = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            var probe = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxRun115FManualProbe.cs");
            var trigger = TestSources.Text("Unity2Foxglove/Assets/Scripts/FoxRun/FoxRunTriggerTelemetrySmoke.cs");
            var context = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs");

            Assert.Contains("_warnedMissingStartExecutor", phase110, StringComparison.Ordinal);
            Assert.Contains("StartExecutor reflection hook was not found", phase110, StringComparison.Ordinal);
            Assert.Contains("continuing without explicit executor start", phase110, StringComparison.Ordinal);
            Assert.Contains("_warnedMissingStartExecutor", phase127, StringComparison.Ordinal);
            Assert.Contains("StartExecutor reflection hook was not found", phase127, StringComparison.Ordinal);
            Assert.Contains("continuing without explicit executor start", phase127, StringComparison.Ordinal);
            Assert.Contains("UNITY2FOXGLOVE_R2FU_EXECUTOR_STARTED=False", phase127, StringComparison.Ordinal);
            Assert.DoesNotContain("method?.Invoke(_ros2Unity, null);", phase127, StringComparison.Ordinal);
            Assert.Contains("private long _frameCount;", probe, StringComparison.Ordinal);
            Assert.Contains("sampleList[2] = (float)(_frameCount % 16777216L);", probe, StringComparison.Ordinal);
            var updateStart = probe.IndexOf("private void Update()", StringComparison.Ordinal);
            var updateEnd = probe.IndexOf("[ContextMenu", updateStart, StringComparison.Ordinal);
            Assert.True(updateStart >= 0 && updateEnd > updateStart);
            var update = probe.Substring(updateStart, updateEnd - updateStart);
            Assert.True(
                update.IndexOf("EnsureCollections();", StringComparison.Ordinal) >= 0
                && update.IndexOf("EnsureCollections();", StringComparison.Ordinal)
                < update.IndexOf("sampleArray[0]", StringComparison.Ordinal),
                "The live manual probe must normalize mutable public containers before indexing them.");
            Assert.Contains("public long fixedCounter;", trigger, StringComparison.Ordinal);
            Assert.DoesNotContain("public int fixedCounter;", trigger, StringComparison.Ordinal);
            Assert.Contains("private static void LogTriggerResult", trigger, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.Log($\"[FoxRunTriggerSmoke]", trigger, StringComparison.Ordinal);

            var isAvailableIndex = context.IndexOf("public bool IsAvailable", StringComparison.Ordinal);
            var tryEnsureIndex = context.IndexOf("public bool TryEnsureReady()", StringComparison.Ordinal);
            Assert.True(isAvailableIndex >= 0 && tryEnsureIndex > isAvailableIndex);
            var isAvailableBlock = context.Substring(isAvailableIndex, tryEnsureIndex - isAvailableIndex);
            Assert.DoesNotContain("TryEnsureReady()", isAvailableBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("AddComponent<ROS2UnityComponent>()", isAvailableBlock, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14027MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_27Validation.cs", "--phase140-27", "Phase140_27Validation.Validate");

        [Fact]
        public void WorkstreamGHistoricalLedgerAndLifecycleGuardsRemainAuditable()
        {
            var ledger = TestSources.Text("docs/research-workstream-g-closure.md");
            Assert.Contains("reconstructed closure mapping", ledger, StringComparison.Ordinal);
            Assert.Contains("original validation checks use the `140-27A-1` through `140-27F-4` namespace", ledger, StringComparison.Ordinal);
            Assert.Contains("## Supporting historical checks", ledger, StringComparison.Ordinal);
            var registerStart = ledger.IndexOf("## Reconstructed finding register", StringComparison.Ordinal);
            var checksStart = ledger.IndexOf("## Automated closure checks", StringComparison.Ordinal);
            Assert.True(registerStart >= 0 && checksStart > registerStart);
            var provenance = ledger.Substring(registerStart, checksStart - registerStart);
            foreach (var check in new[]
                     {
                         "140-27A-1", "140-27A-2", "140-27A-3", "140-27A-4",
                         "140-27B-1", "140-27B-2", "140-27C-1", "140-27C-2",
                         "140-27C-3", "140-27D-1", "140-27D-2", "140-27E-1",
                         "140-27E-2", "140-27E-3", "140-27E-4", "140-27E-5",
                         "140-27F-1", "140-27F-2", "140-27F-3", "140-27F-4"
                     })
                Assert.Single(Regex.Matches(provenance, Regex.Escape(check)));

            foreach (var mapping in new[]
                     {
                         "| Phase140-27/P2-1 | P2 | Closed | 140-27A-1 |",
                         "| Phase140-27/P2-4 | P2 | Closed | 140-27C-1, 140-27C-2 |",
                         "| Phase140-27/P3-1 | P3 | Closed | 140-27D-2 |",
                         "| Phase140-27/P3-2 | P3 | Closed | 140-27A-2 |",
                         "| Phase140-27/P3-5 | P3 | Closed | 140-27E-2 |",
                         "| Phase140-27/P3-6 | P3 | Closed | 140-27D-1 |"
                     })
                Assert.Contains(mapping, ledger, StringComparison.Ordinal);

            foreach (var supporting in new[]
                     {
                         "- `140-27A-3`",
                         "- `140-27A-4`",
                         "- `140-27E-4`",
                         "- `140-27F-3`",
                         "- `140-27F-4`"
                     })
                Assert.Contains(supporting, ledger, StringComparison.Ordinal);

            foreach (var finding in new[]
                     {
                         "Phase140-27/P2-1",
                         "Phase140-27/P2-2",
                         "Phase140-27/P2-3",
                         "Phase140-27/P2-4",
                         "Phase140-27/P2-5",
                         "Phase140-27/P2-6",
                         "Phase140-27/P3-1",
                         "Phase140-27/P3-2",
                         "Phase140-27/P3-3",
                         "Phase140-27/P3-4",
                         "Phase140-27/P3-5",
                         "Phase140-27/P3-6"
                     })
                Assert.Contains("| " + finding + " |", ledger, StringComparison.Ordinal);

            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveDebugOverlaySmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityStringSmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs");
            VerifyRunInBackgroundRestore("Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 Standard Visualization Acceptance/Phase128Rviz2TfLaserScanSmoke.cs");
            VerifyRunInBackgroundRestore("Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 PointCloud2 Acceptance/Phase129Rviz2PointCloud2Smoke.cs");
            VerifyRunInBackgroundRestore("Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 MarkerArray Acceptance/Phase130Rviz2MarkerArraySmoke.cs");
            VerifyRunInBackgroundRestore("Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 Standard Message Expansion/Phase132StandardMessagesSmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/RViz2 Standard Visualization Acceptance/Phase128Rviz2TfLaserScanSmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/RViz2 PointCloud2 Acceptance/Phase129Rviz2PointCloud2Smoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/RViz2 MarkerArray Acceptance/Phase130Rviz2MarkerArraySmoke.cs");
            VerifyRunInBackgroundRestore("Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 Standard Message Expansion/Phase132StandardMessagesSmoke.cs");

            var demo = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            Assert.Contains("FindFirstObjectByType<FoxgloveSceneCubePublisher>()", demo, StringComparison.Ordinal);
            Assert.DoesNotContain("GameObject.Find(\"Cube\")", demo, StringComparison.Ordinal);
            Assert.Contains("scene cube publisher", demo, StringComparison.OrdinalIgnoreCase);
            var testLog = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");
            Assert.Contains("FindFirstObjectByType<FoxgloveSceneCubePublisher>()", testLog, StringComparison.Ordinal);
            Assert.DoesNotContain("GameObject.Find(\"Cube\")", testLog, StringComparison.Ordinal);
        }

        private static void VerifyRunInBackgroundRestore(string path)
        {
            var source = TestSources.Text(path);
            Assert.Contains("using Unity.FoxgloveSDK.Utilities;", source, StringComparison.Ordinal);
            Assert.Contains("private RunInBackgroundLease _runInBackgroundLease;", source, StringComparison.Ordinal);
            Assert.Contains("RunInBackgroundLease.Acquire(", source, StringComparison.Ordinal);
            Assert.Contains("_runInBackgroundLease.Release()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_previousRunInBackground", source, StringComparison.Ordinal);
        }
    }
}
