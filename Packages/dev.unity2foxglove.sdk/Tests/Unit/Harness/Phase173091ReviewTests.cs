// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-091 review regressions for DTO, profiler, R2FU, and Phase172 validation findings.

using System;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-091")]
    [Trait("Domain", "Review")]
    public sealed class Phase173091ReviewTests
    {
        [Fact]
        public void ReadOnlyListIsResponseOnlyNotMutableRequestCollection()
        {
            Assert.False(FoxServiceDtoTypeNames.IsListContract("System.Collections.Generic.IReadOnlyList<T>"));
            Assert.False(FoxServiceDtoTypeNames.IsMutableCollectionContract("System.Collections.Generic.IReadOnlyList<T>"));
            Assert.True(FoxServiceDtoTypeNames.IsListContract(
                "System.Collections.Generic.IReadOnlyList<T>",
                FoxServiceDtoRules.ResponseSide));
        }

        [Fact]
        public void ProfilerSampleScopesRecordThreadAffinity()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Profiling/UnityProfilerAdapter.cs");

            Assert.Contains("must be disposed on the same thread", source, StringComparison.Ordinal);
            Assert.Contains("private int _threadId;", source, StringComparison.Ordinal);
            Assert.Contains("_threadId = Environment.CurrentManagedThreadId;", source, StringComparison.Ordinal);
            Assert.Contains("Debug.Assert(", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraHealthValidationUsesLocalRepoRootLookup()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/CameraHealthCaptureAdmissionValidation.cs");

            Assert.Contains("private static string FindRepoRoot()", source, StringComparison.Ordinal);
            Assert.Contains("AppContext.BaseDirectory", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Phase16Validation.FindRepoRoot()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuCameraBridgeDocumentsLowFrequencyFindObjectsAllocation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var refresh = TestSources.Slice(source, "private void RefreshBindings()", "        private void RefreshRawImageBindings");

            Assert.Contains("Intentional low-frequency scan", refresh, StringComparison.Ordinal);
            Assert.Contains("0.5s throttle", refresh, StringComparison.Ordinal);
            Assert.Contains("FindObjectsByType<FoxgloveCameraPublisher>", refresh, StringComparison.Ordinal);
        }

        [Fact]
        public void R2fuBuilderTestsValidateRepoRootAndDepsHashPatch()
        {
            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var test = TestSources.Text("Scripts/ros2forunity/windows/" + distro + "/regression_checks/test_build_r2fu_runtime_package.py");
                var builder = TestSources.Text("Scripts/ros2forunity/windows/" + distro + "/build_r2fu_runtime_package.py");

                Assert.Contains("Repo root resolution failed", test, StringComparison.Ordinal);
                Assert.Contains("test_patch_deps_json_sha512_updates_inventory_hash", test, StringComparison.Ordinal);
                Assert.Contains("def sha512_file(path: Path) -> str:", builder, StringComparison.Ordinal);
                Assert.Contains("def patch_deps_json_sha512(package: Path) -> None:", builder, StringComparison.Ordinal);
                Assert.Contains("patch_deps_json_sha512(paths.package)", builder, StringComparison.Ordinal);
            }
        }
    }
}
