// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-68/69/70/71/72/73 generation and editor optimization checks.

using System;
using System.IO;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-73")]
    [Trait("Domain", "Harness")]
    public sealed class JazzyRuntimeWrapperOptimizationTests
    {
        [Fact]
        public void SensorAndTransformCachesPreserveMutableState()
        {
            var sensor = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs");
            var frameName = TestSources.Slice(sensor, "public override string frameName()", "    /// <summary>\r\n    /// Visualises");
            var transformations = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Transformations.cs");
            var method = TestSources.Slice(transformations, "public static Matrix4x4 Unity2RosMatrix4x4()", "}");

            Assert.Contains("private string cachedFrameName;", sensor, StringComparison.Ordinal);
            Assert.DoesNotContain("private string cachedFrameNameOwner;", sensor, StringComparison.Ordinal);
            Assert.DoesNotContain("private string cachedFrameNameFrameId;", sensor, StringComparison.Ordinal);
            Assert.Contains("cachedFrameName = String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + \"/\" + frameID;", sensor, StringComparison.Ordinal);
            Assert.Contains("if (cachedFrameName != null)", frameName, StringComparison.Ordinal);
            Assert.Contains("return cachedFrameName;", frameName, StringComparison.Ordinal);
            Assert.Contains("static readonly Matrix4x4 Unity2RosMatrix", transformations, StringComparison.Ordinal);
            Assert.Contains("static readonly Matrix4x4 Ros2UnityMatrix", transformations, StringComparison.Ordinal);
            Assert.Contains("Unity2RosMatrix.inverse", transformations, StringComparison.Ordinal);
            Assert.Contains("return Unity2RosMatrix;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("new Matrix4x4", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".transpose", method, StringComparison.Ordinal);
        }

        [Fact]
        public void RosSupportAndExecutorSpinUsesReusableSnapshots()
        {
            var ros2 = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var checkSupport = TestSources.Slice(ros2, "private void CheckROSSupport(string ros2Codename)", "    private void RegisterCtrlCHandler()");
            var component = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var core = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs");

            Assert.Contains("private static readonly string[] SupportedRosVersions", ros2, StringComparison.Ordinal);
            Assert.Contains("private static readonly string SupportedRosVersionsString", ros2, StringComparison.Ordinal);
            Assert.DoesNotContain("new List<string>()", checkSupport, StringComparison.Ordinal);
            Assert.Contains("SupportedRosVersionsString", checkSupport, StringComparison.Ordinal);
            Assert.Contains("Array.IndexOf(SupportedRosVersions, ros2Codename)", checkSupport, StringComparison.Ordinal);
            Assert.Contains("nodesSnapshot.AddRange(ros2csNodes)", component, StringComparison.Ordinal);
            Assert.Contains("Ros2cs.SpinOnce(nodesSnapshot", component, StringComparison.Ordinal);
            Assert.Contains("nodesSnapshot.AddRange(ros2csNodes)", core, StringComparison.Ordinal);
            Assert.Contains("Ros2cs.SpinOnce(nodesSnapshot", core, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14073MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_73Validation.cs", "--phase140-73", "Phase140_73Validation.Validate");
    }
}
