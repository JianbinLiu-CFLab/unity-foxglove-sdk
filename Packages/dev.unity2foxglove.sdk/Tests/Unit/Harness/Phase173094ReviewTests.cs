// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-094 review regression guards.

using System;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-094")]
    [Trait("Domain", "Review")]
    public sealed class Phase173094ReviewTests
    {
        [Fact]
        public void Rgb24ToNv12MetaKeepsMonoImporterBlock()
        {
            var meta = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/Rgb24ToNv12Converter.cs.meta");

            Assert.Contains("guid: 640722833a8543f8b17e6094047f29c0", meta, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
            Assert.Contains("serializedVersion: 2", meta, StringComparison.Ordinal);
            Assert.Contains("executionOrder: 0", meta, StringComparison.Ordinal);
        }

        [Fact]
        public void PolicyEmitterDefaultsOutOfRangeTopicIndexesToNoPublish()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PolicyEmitter.cs");

            Assert.Contains("default: return false;", source, StringComparison.Ordinal);
            Assert.Contains("default: break;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("default: return true;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarScanLayoutReturnsEmptyNonNullLayoutForNullPattern()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanLayout.cs");
            var build = TestSources.Slice(source, "public static VirtualLidarScanLayout Build", "            var rawRayCount");

            Assert.Contains("Array.Empty<int>()", build, StringComparison.Ordinal);
            Assert.Contains("Array.Empty<int[]>()", build, StringComparison.Ordinal);
            Assert.Contains("non-null layout", source, StringComparison.Ordinal);
            Assert.DoesNotContain("return default;", build, StringComparison.Ordinal);
            Assert.DoesNotContain("column < 0 || column >= rawColumns", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LyricalDepsPopulateSha512AndAvoidSpuriousServiceDependency()
        {
            foreach (var name in new[] { "stereo_msgs_assembly", "visualization_msgs_assembly" })
            {
                var deps = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Plugins/" + name + ".deps.json");

                Assert.DoesNotContain("\"service_msgs_assembly\"", deps, StringComparison.Ordinal);
                Assert.DoesNotContain("\"service_msgs_assembly/0.0.0.0\"", deps, StringComparison.Ordinal);
                Assert.DoesNotContain("\"sha512\": \"\"", deps, StringComparison.Ordinal);
            }

            var validator = TestSources.Text("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py");
            Assert.Contains("check_managed_deps_consistency(results)", validator, StringComparison.Ordinal);
        }

        [Fact]
        public void LyricalValidatorRegressionIsSelfContainedAndAssertFree()
        {
            var tests = TestSources.Text("Scripts/ros2forunity/windows/lyrical/regression_checks/test_validate_r2fu_runtime_package.py");

            Assert.Contains("if spec.loader is None:", tests, StringComparison.Ordinal);
            Assert.Contains("self.validator.MANIFEST = manifest", tests, StringComparison.Ordinal);
            Assert.Contains("test_managed_deps_reject_spurious_service_msgs_for_visualization_packets", tests, StringComparison.Ordinal);
            Assert.DoesNotContain("assert spec.loader is not None", tests, StringComparison.Ordinal);
        }

        [Fact]
        public void RemoteGatewayControllerDoesNotSerializeDeviceToken()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.remotegateway.win64/Runtime/FoxgloveRemoteGatewayController.cs");

            Assert.DoesNotContain("[SerializeField] private string _deviceToken", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_deviceToken", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorProcessRunnerLogsStreamFailuresAndKillsProcessTreeWhenAvailable()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/Process/FoxgloveEditorProcessRunner.cs");

            Assert.Contains("KillProcessTreeMethod", source, StringComparison.Ordinal);
            Assert.Contains("KillProcessTreeMethod.Invoke(process, new object[] { true })", source, StringComparison.Ordinal);
            Assert.Contains("LogWarning(\"Failed to drain process output streams", source, StringComparison.Ordinal);
            Assert.Contains("LogWarning(\"Failed to kill timed-out process", source, StringComparison.Ordinal);
            Assert.DoesNotContain("catch\r\n            {\r\n            }", source, StringComparison.Ordinal);
        }

        [Fact]
        public void TopicMetadataEmitterDocumentsSharedSha256Lifetime()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");

            Assert.Contains("Process-lifetime generator helper", source, StringComparison.Ordinal);
            Assert.Contains("Unity profiles that do not expose the newer SHA256.HashData API", source, StringComparison.Ordinal);
        }
    }
}
