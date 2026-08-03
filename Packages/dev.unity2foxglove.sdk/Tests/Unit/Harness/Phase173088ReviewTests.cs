// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-088 Unity review regression checks.

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-088")]
    [Trait("Domain", "UnityReview")]
    public sealed class Phase173088ReviewTests
    {
        [Fact]
        public void U2R2HealthProbeUsesBoundedWaitAndNeutralRequestId()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs");
            var wait = TestSources.ExtractMethod(source, "private static bool WaitOrCancel");

            Assert.Contains("private const string RequestIdPrefix = \"u2r2-health-\";", source, StringComparison.Ordinal);
            Assert.Contains("RequestIdPrefix + Guid.NewGuid()", source, StringComparison.Ordinal);
            Assert.Contains("task.Wait(timeoutMs, cancellationToken)", wait, StringComparison.Ordinal);
            Assert.DoesNotContain("Stopwatch.StartNew()", wait, StringComparison.Ordinal);
            Assert.DoesNotContain("Math.Min(50", wait, StringComparison.Ordinal);
            Assert.DoesNotContain("phase97-", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PackedPointCloudFrameRecycleIsInterlocked()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PackedPointCloudFrame.cs");
            var method = TestSources.ExtractMethod(source, "internal void RecycleData()");

            Assert.Contains("using System.Threading;", source, StringComparison.Ordinal);
            Assert.Contains("private int _dataRecycled;", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _dataRecycled, 1)", method, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _dataRecycled;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SchemaEvidenceProjectRootIsLazy()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");

            Assert.Contains("private static readonly Lazy<string> CachedProjectRoot", source, StringComparison.Ordinal);
            Assert.Contains("private static string ProjectRoot => CachedProjectRoot.Value;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private static readonly string CachedProjectRoot", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FullDemoMouseDragUsesRuntimeScaleBoundsAndSampleCopyStaysInSync()
        {
            var sample = TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/MouseDragCube.cs");
            var project = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs");

            Assert.Equal(sample, project);
            Assert.DoesNotContain("_minScale", sample, StringComparison.Ordinal);
            Assert.DoesNotContain("_maxScale", sample, StringComparison.Ordinal);
            Assert.Contains("FoxgloveDemoSetup.ScaleMinimum", sample, StringComparison.Ordinal);
            Assert.Contains("FoxgloveDemoSetup.ScaleMaximum", sample, StringComparison.Ordinal);
        }

        [Fact]
        public void RemoteGatewayBuildScriptKeepsPdbOptInAndHashesCopiedArtifacts()
        {
            var source = TestSources.Text("Scripts/remotegateway/build_foxglove_c_win64.py");

            Assert.Contains("APPROVED_ARTIFACTS = (\"foxglove.dll\", \"foxglove.dll.lib\")", source, StringComparison.Ordinal);
            Assert.Contains("PDB_ARTIFACT = \"foxglove.pdb\"", source, StringComparison.Ordinal);
            Assert.Contains("--include-pdb", source, StringComparison.Ordinal);
            Assert.Contains("def selected_artifacts(include_pdb: bool)", source, StringComparison.Ordinal);
            Assert.Contains("\"artifacts\": artifacts", source, StringComparison.Ordinal);
            Assert.DoesNotContain("APPROVED_ARTIFACTS = (\"foxglove.dll\", \"foxglove.dll.lib\", \"foxglove.pdb\")", source, StringComparison.Ordinal);
        }

        [Fact]
        public void DracoProbeRejectsNonFiniteXyzBeforeEncoding()
        {
            var source = TestSources.Text("Scripts/native/draco_probe/draco_probe_encoder.cpp");

            Assert.Contains("#include <cmath>", source, StringComparison.Ordinal);
            Assert.Contains("std::isfinite(xyz[i])", source, StringComparison.Ordinal);
            Assert.Contains("non-finite XYZ value", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeValidationHarnessesUseStableRepoRootAndLocalCounters()
        {
            var systemInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/SystemInfoPublisherValidation.cs");
            var registry = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationRegistryDescriptiveNamesValidation.cs");

            Assert.DoesNotContain("private static int _passed;", systemInfo, StringComparison.Ordinal);
            Assert.Contains("Phase16Validation.FindRepoRoot()", systemInfo, StringComparison.Ordinal);
            Assert.Contains("private sealed class CheckCounter", systemInfo, StringComparison.Ordinal);
            Assert.DoesNotContain("private static int _passed;", registry, StringComparison.Ordinal);
            Assert.Contains("cleanPurposeEntries * 100 >= registryEntries * 80", registry, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxTopicBusTestsDoNotDependOnObjectPayloadSourceSubstring()
        {
            var tests = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxTopicBusTests.cs");
            var runtimeValidation = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase153Validation.cs");

            Assert.DoesNotContain("object Payload", tests, StringComparison.Ordinal);
            Assert.DoesNotContain("object Payload", runtimeValidation, StringComparison.Ordinal);
        }
    }
}
