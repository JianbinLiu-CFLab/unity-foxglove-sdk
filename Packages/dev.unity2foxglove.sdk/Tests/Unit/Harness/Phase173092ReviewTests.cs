// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-092 review regression guards.

using System;
using Xunit;
using Unity.FoxgloveSDK.UnitTests.Harness;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173")]
    public sealed class Phase173092ReviewTests
    {
        [Fact]
        public void JazzyScalableTimeSourceUsesLockedSnapshotAndClockState()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");

            Assert.Contains("private readonly object mutex = new object();", source, StringComparison.Ordinal);
            Assert.Contains("private readonly object clockMutex = new object();", source, StringComparison.Ordinal);
            Assert.Contains("Thread.CurrentThread.ManagedThreadId", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Thread mainThread", source, StringComparison.Ordinal);
            Assert.Contains("lock (mutex)", source, StringComparison.Ordinal);
            Assert.Contains("lock (clockMutex)", source, StringComparison.Ordinal);
            Assert.Contains("private bool timeScaleChangeLogged", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeScalableTimeSourcesKeepSharedLockingContract()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs"
            })
            {
                var source = TestSources.Text(path);

                Assert.Contains("private readonly object mutex = new object();", source, StringComparison.Ordinal);
                Assert.Contains("private readonly object clockMutex = new object();", source, StringComparison.Ordinal);
                Assert.Contains("Thread.CurrentThread.ManagedThreadId", source, StringComparison.Ordinal);
                Assert.DoesNotContain("Thread mainThread", source, StringComparison.Ordinal);
                Assert.Contains("lock (mutex)", source, StringComparison.Ordinal);
                Assert.Contains("lock (clockMutex)", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void LyricalRos2csMetadataDescriptionsStayOnLyricalDistro()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/metadata_ros2cs.xml",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Plugins/metadata_ros2cs.xml",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/metadata_ros2cs.xml"
            })
            {
                var metadata = TestSources.Text(path);
                Assert.Contains("<ros2>lyrical</ros2>", metadata, StringComparison.Ordinal);
                Assert.DoesNotContain("jazzy", metadata, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("humble", metadata, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void PointCloudNativeWorkerDiagnosticsReuseArgumentScratchArray()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Diagnostics.cs");
            var method = TestSources.Slice(source, "private void LogPointCloud2NativeWorkerTiming", "private static double ElapsedPointCloud2NativeMilliseconds");

            Assert.Contains("private readonly object[] _pointCloud2NativeWorkerTimingArgs = new object[24];", source, StringComparison.Ordinal);
            Assert.Contains("var args = _pointCloud2NativeWorkerTimingArgs;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("new object[]", method, StringComparison.Ordinal);
        }

        [Fact]
        public void RemoteGatewayPackageShapeTestsUseStrongRootAndHexHashValidation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/RemoteGatewayPackageShapeTests.cs");

            Assert.Contains("Assert.Matches(\"^[0-9a-f]{64}$\"", source, StringComparison.Ordinal);
            Assert.Contains("\"Packages\", \"dev.unity2foxglove.sdk\", \"package.json\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void TriggerEmitterAcceptsReadOnlyTopicModeMap()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TriggerEmitter.cs");
            var signature = TestSources.Slice(source, "internal static void EmitTriggers", "if (triggerMembers.Count == 0)");

            Assert.Contains("IReadOnlyDictionary<string, int> topicModes", signature, StringComparison.Ordinal);
            Assert.DoesNotContain("IReadOnlyList<string> topics, Dictionary<string, int> topicModes", signature, StringComparison.Ordinal);
        }

        [Fact]
        public void ManualStatusSmokeClearsAutoCoroutineOnDisable()
        {
            var source = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/FoxgloveStatusSmoke.cs");

            Assert.Contains("private void OnDisable()", source, StringComparison.Ordinal);
            Assert.Contains("StopAutoClearRoutine();", source, StringComparison.Ordinal);
            Assert.Contains("autoClearRoutine = null;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimePackageValidatorsGuardMetadataAndJazzyTimeSourceContracts()
        {
            var lyricalValidator = TestSources.Text("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py");
            var jazzyValidator = TestSources.Text("Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py");
            var lyricalBuilder = TestSources.Text("Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py");

            Assert.Contains("check_ros2cs_metadata_descriptions(results)", lyricalValidator, StringComparison.Ordinal);
            Assert.Contains("validate_ros2cs_metadata_descriptions(paths.package)", lyricalBuilder, StringComparison.Ordinal);
            Assert.Contains("ROS2ScalableTimeSource locks Unity time snapshot state", jazzyValidator, StringComparison.Ordinal);
            Assert.Contains("ROS2ScalableTimeSource locks lazy ROS clock creation", jazzyValidator, StringComparison.Ordinal);
        }
    }
}
