// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-24 validation for Unity demo runtime script hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_24Validation
    {
        private const string ManualContextPath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs";
        private const string Phase109SmokePath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityStringSmoke.cs";
        private const string Phase110ImportedSmokePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs";
        private const string Phase110BatchPath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs";
        private const string Phase125ManualPath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase125Ros2CdrTypedDecodeAcceptance.cs";
        private const string Phase127SmokePath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase127R2FURealProjectSmoke.cs";
        private const string FullDemoSetupPath =
            "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs";

        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyPhase109ContextUsesRequestedNodeName();
            VerifyPhase109EndpointCreationIsIdempotent();
            VerifyPhase110BatchAvoidsSmokePrivateFields();
            VerifyPhase125FixtureNamesAreUtcUnique();
            VerifyPhase127ReceiveCountersAreSynchronized();
            VerifyFullDemoUsesManagerFacadeAndBoundedLogging();

            Console.WriteLine($"Phase134_24Validation: PASS ({_passed} checks)");
        }

        private static void VerifyPhase109ContextUsesRequestedNodeName()
        {
            var source = ReadRepoFile(ManualContextPath);

            Check(source.Contains("var normalizedName = NormalizeName(nodeName);", StringComparison.Ordinal),
                "134-24-A1: Phase109 manual facade normalizes requested node name once");
            Check(source.Contains("_ros2Unity.CreateNode(normalizedName)", StringComparison.Ordinal),
                "134-24-A2: Phase109 manual facade passes normalized name to native ROS2 node creation");
            Check(source.Contains("new Phase109Ros2ForUnityNode(_ros2Unity, ros2Node, normalizedName)", StringComparison.Ordinal),
                "134-24-A3: Phase109 wrapper and native node share the same normalized name");
            Check(!source.Contains("_ros2Unity.CreateNode(\"unity2foxglove_phase109\")", StringComparison.Ordinal),
                "134-24-A4: Phase109 manual facade no longer hard-codes the native node name");
        }

        private static void VerifyPhase109EndpointCreationIsIdempotent()
        {
            var source = ReadRepoFile(Phase109SmokePath);

            Check(source.Contains("if (_subscription == null)", StringComparison.Ordinal),
                "134-24-B1: Phase109 smoke only creates subscription when missing");
            Check(source.Contains("if (_publisher == null)", StringComparison.Ordinal),
                "134-24-B2: Phase109 smoke only creates publisher when missing");
            Check(source.Contains("Phase109 endpoint creation failed:", StringComparison.Ordinal),
                "134-24-B3: Phase109 smoke surfaces partial endpoint creation failures");
            Check(source.Contains("Application.runInBackground = _previousRunInBackground;", StringComparison.Ordinal),
                "134-24-B4: Phase109 smoke restores runInBackground after manual acceptance");
        }

        private static void VerifyPhase110BatchAvoidsSmokePrivateFields()
        {
            var smoke = ReadRepoFile(Phase110ImportedSmokePath);
            var batch = ReadRepoFile(Phase110BatchPath);

            Check(smoke.Contains("public void ConfigureForBatch(", StringComparison.Ordinal),
                "134-24-C1: Phase110 smoke exposes an explicit batch configuration API");
            Check(smoke.Contains("public int PublishedCount => _publishedCount;", StringComparison.Ordinal),
                "134-24-C2: Phase110 smoke exposes published count without private reflection");
            Check(smoke.Contains("public string LastError => _lastError;", StringComparison.Ordinal),
                "134-24-C3: Phase110 smoke exposes last error without private reflection");
            Check(batch.Contains("_smoke.ConfigureForBatch(", StringComparison.Ordinal),
                "134-24-C4: Phase110 batch configures smoke through public API");
            Check(!batch.Contains("GetPrivateField", StringComparison.Ordinal)
                  && !batch.Contains("SetPrivateField", StringComparison.Ordinal),
                "134-24-C5: Phase110 batch no longer reflects smoke private fields");
            Check(batch.Contains("private bool _executorsStarted;", StringComparison.Ordinal)
                  && batch.Contains("if (_executorsStarted)", StringComparison.Ordinal),
                "134-24-C6: Phase110 batch starts reflected executors only once");
            Check(batch.Contains("Application.runInBackground = _previousRunInBackground;", StringComparison.Ordinal),
                "134-24-C7: Phase110 batch restores runInBackground");
        }

        private static void VerifyPhase125FixtureNamesAreUtcUnique()
        {
            var source = ReadRepoFile(Phase125ManualPath);

            Check(source.Contains("DateTime.UtcNow.ToString(\"yyyyMMdd_HHmmss_fffffff'Z'\")", StringComparison.Ordinal),
                "134-24-D1: Phase125 manual fixtures use high-resolution UTC filenames");
            Check(!source.Contains("DateTime.Now.ToString(\"yyyyMMdd_HHmmss\")", StringComparison.Ordinal),
                "134-24-D2: Phase125 manual fixtures avoid local-time second-resolution filenames");
        }

        private static void VerifyPhase127ReceiveCountersAreSynchronized()
        {
            var source = ReadRepoFile(Phase127SmokePath);

            Check(source.Contains("private int SnapshotReceivedCount()", StringComparison.Ordinal),
                "134-24-E1: Phase127 manual smoke centralizes synchronized received-count reads");
            Check(source.Contains("lock (_receiveGate)") && source.Contains("return _receivedCount;", StringComparison.Ordinal),
                "134-24-E2: Phase127 MonoBehaviour received-count snapshot uses receive lock");
            Check(source.Contains("return _received;", StringComparison.Ordinal),
                "134-24-E3: Phase127 batch received-count snapshot uses receive lock");
            Check(source.Contains("nativeNodeDiagnostic=<unavailable:ROS2Node.node field missing>", StringComparison.Ordinal),
                "134-24-E4: Phase127 diagnostic reflection reports unavailable private fields explicitly");
            Check(source.Contains("Application.runInBackground = _previousRunInBackground;", StringComparison.Ordinal),
                "134-24-E5: Phase127 manual acceptance restores runInBackground");
        }

        private static void VerifyFullDemoUsesManagerFacadeAndBoundedLogging()
        {
            var source = ReadRepoFile(FullDemoSetupPath);

            Check(source.Contains("private const int ClientPayloadPreviewBytes = 160;", StringComparison.Ordinal),
                "134-24-F1: Full Demo bounds client payload previews");
            Check(source.Contains("[SerializeField] private GameObject _cube;", StringComparison.Ordinal),
                "134-24-F2: Full Demo exposes an explicit cube reference");
            Check(source.Contains("private bool TryInitializeDemo()", StringComparison.Ordinal),
                "134-24-F3: Full Demo retries registration until manager runtime is ready");
            Check(source.Contains("_resetSvcId = _manager.RegisterService", StringComparison.Ordinal),
                "134-24-F4: Full Demo registers services through the manager facade");
            Check(source.Contains("_manager.OnClientMessage += OnClientMessageReceived;", StringComparison.Ordinal)
                  && source.Contains("_manager.OnClientMessage -= OnClientMessageReceived;", StringComparison.Ordinal),
                "134-24-F5: Full Demo uses manager-level client-message events");
            Check(source.Contains("FormatPayloadPreview(payload)", StringComparison.Ordinal)
                  && !source.Contains("Encoding.UTF8.GetString(payload)", StringComparison.Ordinal),
                "134-24-F6: Full Demo avoids unbounded raw UTF-8 payload logging");
            Check(source.Contains("WarnInvalidScaleOnce", StringComparison.Ordinal)
                  && !source.Contains("catch { }", StringComparison.Ordinal),
                "134-24-F7: Full Demo reports invalid scale values instead of swallowing them");
            Check(source.Contains("Player-tagged fallback object", StringComparison.Ordinal),
                "134-24-F8: Full Demo warns when falling back from Cube name lookup to Player tag");
        }

        private static string ReadRepoFile(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return File.ReadAllText(path);
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
