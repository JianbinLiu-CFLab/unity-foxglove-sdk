// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-27 regression coverage for Unity demo runtime scripts.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_27Validation.
    /// </summary>
    public static class Phase140_27Validation
    {
        private const string FullDemoSetup =
            "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs";
        private const string FullDemoTestLog =
            "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs";
        private const string FullDemoMouseDrag =
            "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs";
        private const string DebugOverlaySmoke =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveDebugOverlaySmoke.cs";
        private const string Phase106Acceptance =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs";
        private const string Phase110Batch =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs";
        private const string Phase127Smoke =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs";
        private const string Phase109Context =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs";
        private const string FoxRun115FProbe =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxRun115FManualProbe.cs";
        private const string FoxRunTriggerSmoke =
            "Unity2Foxglove/Assets/Scripts/FoxRun/FoxRunTriggerTelemetrySmoke.cs";
        private const string SampleScene =
            "Unity2Foxglove/Assets/Scenes/SampleScene.unity";

        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-27: Unity Demo Runtime Scripts ===");
            _passed = 0;

            FullDemoSetupKeepsRuntimeAndSceneBoundaries();
            ManualSmokeRestoresGlobalRunInBackground();
            R2fuExecutorReflectionFailuresAreVisible();
            ManualProbeCountersStayLongRunningSafe();
            InputAndSceneContractsAreExplicit();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-27: {_passed} checks passed.");
        }

        private static void FullDemoSetupKeepsRuntimeAndSceneBoundaries()
        {
            var source = Read(FullDemoSetup);

            Check(source.Contains("var runtime = _manager?.Runtime;", StringComparison.Ordinal)
                  && source.Contains("if (runtime?.Session == null)", StringComparison.Ordinal)
                  && source.Contains("_initialized = false;", StringComparison.Ordinal),
                "140-27A-1: Full demo setup revalidates runtime/session readiness after initialization");
            Check(!source.Contains("FindGameObjectWithTag(\"Player\")", StringComparison.Ordinal)
                  && !source.Contains("using Player-tagged fallback object", StringComparison.Ordinal),
                "140-27A-2: Full demo setup does not fall back to unrelated Player-tagged objects");
            Check(source.Contains("return JToken.Parse(\"{\\\"status\\\":\\\"error\\\",\\\"reason\\\":\\\"cube not found\\\"}\");", StringComparison.Ordinal),
                "140-27A-3: Full demo reset_pose reports missing cube as an error");
            Check(source.Contains("private static readonly UTF8Encoding StrictUtf8", StringComparison.Ordinal)
                  && source.Contains("new UTF8Encoding(false, true)", StringComparison.Ordinal),
                "140-27A-4: Full demo client-message preview uses strict UTF-8 before hex fallback");
        }

        private static void ManualSmokeRestoresGlobalRunInBackground()
        {
            VerifyRunInBackgroundRestore(DebugOverlaySmoke, "140-27B-1: debug overlay smoke restores runInBackground");
            VerifyRunInBackgroundRestore(Phase106Acceptance, "140-27B-2: Phase106 acceptance restores runInBackground");
        }

        private static void VerifyRunInBackgroundRestore(string path, string label)
        {
            var source = Read(path);
            Check(source.Contains("private bool _previousRunInBackground;", StringComparison.Ordinal)
                  && source.Contains("_previousRunInBackground = Application.runInBackground;", StringComparison.Ordinal)
                  && source.Contains("Application.runInBackground = true;", StringComparison.Ordinal)
                  && source.Contains("Application.runInBackground = _previousRunInBackground;", StringComparison.Ordinal),
                label);
        }

        private static void R2fuExecutorReflectionFailuresAreVisible()
        {
            var phase110 = Read(Phase110Batch);
            Check(phase110.Contains("_warnedMissingStartExecutor", StringComparison.Ordinal)
                  && phase110.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal)
                  && phase110.Contains("continuing without explicit executor start", StringComparison.Ordinal),
                "140-27C-1: Phase110 batch logs missing StartExecutor reflection hook once");

            var phase127 = Read(Phase127Smoke);
            Check(phase127.Contains("_warnedMissingStartExecutor", StringComparison.Ordinal)
                  && phase127.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal)
                  && phase127.Contains("continuing without explicit executor start", StringComparison.Ordinal),
                "140-27C-2: Phase127 smoke logs missing StartExecutor reflection hook once");
            Check(phase127.Contains("UNITY2FOXGLOVE_R2FU_EXECUTOR_STARTED=False", StringComparison.Ordinal)
                  && !phase127.Contains("method?.Invoke(_ros2Unity, null);", StringComparison.Ordinal),
                "140-27C-3: Phase127 batch reports executor start status truthfully");
        }

        private static void ManualProbeCountersStayLongRunningSafe()
        {
            var probe = Read(FoxRun115FProbe);
            Check(probe.Contains("private long _frameCount;", StringComparison.Ordinal)
                  && probe.Contains("sampleList[2] = (float)(_frameCount % 16777216L);", StringComparison.Ordinal),
                "140-27D-1: FoxRun115F manual probe avoids int overflow and float precision loss");
            var trigger = Read(FoxRunTriggerSmoke);
            Check(trigger.Contains("public long fixedCounter;", StringComparison.Ordinal)
                  && !trigger.Contains("public int fixedCounter;", StringComparison.Ordinal),
                "140-27D-2: FoxRun trigger smoke fixed counter is long-running safe");
        }

        private static void InputAndSceneContractsAreExplicit()
        {
            var mouseDrag = Read(FullDemoMouseDrag);
            Check(mouseDrag.Contains("#if ENABLE_INPUT_SYSTEM", StringComparison.Ordinal)
                  && mouseDrag.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", StringComparison.Ordinal)
                  && mouseDrag.Contains("TryReadMouse", StringComparison.Ordinal),
                "140-27E-1: demo mouse input compiles without a hard Input System source dependency");
            Check(mouseDrag.Contains("private Camera _camera;", StringComparison.Ordinal)
                  && mouseDrag.Contains("_camera = Camera.main;", StringComparison.Ordinal)
                  && mouseDrag.Contains("var cam = _camera;", StringComparison.Ordinal),
                "140-27E-2: demo mouse input caches the main camera");

            var status = Read("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveStatusSmoke.cs");
            Check(status.Contains("#if ENABLE_INPUT_SYSTEM", StringComparison.Ordinal)
                  && status.Contains("#elif ENABLE_LEGACY_INPUT_MANAGER", StringComparison.Ordinal)
                  && status.Contains("WasKeyPressed", StringComparison.Ordinal),
                "140-27E-3: status smoke input compiles without a hard Input System source dependency");

            var testLog = Read(FullDemoTestLog);
            Check(testLog.Contains("private Vector3 _position2;", StringComparison.Ordinal)
                  && !testLog.Contains("public Vector3 position;", StringComparison.Ordinal),
                "140-27E-4: demo FoxRun telemetry fields stay private");

            var scene = Read(SampleScene);
            Check(!scene.Contains("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\r\n  _manager: {fileID: 0}\r\n  _cube: {fileID: 0}", StringComparison.Ordinal)
                  && !scene.Contains("m_EditorClassIdentifier: Assembly-CSharp::FoxgloveDemoSetup\n  _manager: {fileID: 0}\n  _cube: {fileID: 0}", StringComparison.Ordinal),
                "140-27E-5: sample scene binds FoxgloveDemoSetup manager and cube explicitly");
        }

        private static void PhaseWiringIsPresent()
        {
            var context = Read(Phase109Context);
            var isAvailableIndex = context.IndexOf("public bool IsAvailable", StringComparison.Ordinal);
            var tryEnsureIndex = context.IndexOf("public bool TryEnsureReady()", StringComparison.Ordinal);
            Check(isAvailableIndex >= 0 && tryEnsureIndex > isAvailableIndex,
                "140-27F-1: Phase109 context exposes IsAvailable before TryEnsureReady");
            var isAvailableBlock = context.Substring(isAvailableIndex, tryEnsureIndex - isAvailableIndex);
            Check(!isAvailableBlock.Contains("TryEnsureReady()", StringComparison.Ordinal)
                  && !isAvailableBlock.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal),
                "140-27F-2: Phase109 IsAvailable observes cached state without initialization side effects");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_27Validation.cs", StringComparison.Ordinal),
                "140-27F-3: test project compiles Phase140_27Validation");
            Check(registry.Contains("Ci(\"--phase140-27\", \"Phase 140-27\", Phase140_27Validation.Validate", StringComparison.Ordinal),
                "140-27F-4: validation registry exposes --phase140-27");
        }

        private static string Read(string path)
        {
            var fullPath = Path.Combine(RepoRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + path, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
