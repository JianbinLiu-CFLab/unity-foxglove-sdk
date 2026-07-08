// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-085 Unity review regression checks.

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-085")]
    [Trait("Domain", "UnityReview")]
    public sealed class Phase173085ReviewTests
    {
        [Fact]
        public void VirtualImuEditorCachesInspectorPropertiesPerInstance()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualImuEditor.cs");
            var onEnable = TestSources.ExtractMethod(source, "private void OnEnable()");

            Assert.Contains("private bool _showAdvancedImuModel;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("static bool _showAdvancedImuModel", source, StringComparison.Ordinal);
            Assert.Contains("CacheProperties();", onEnable, StringComparison.Ordinal);
            Assert.Contains("private readonly SerializedProperty[] _orientationCovarianceElements", source, StringComparison.Ordinal);
            Assert.Contains("CacheCovarianceElements(_orientationCovarianceProperty", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DrawProperty(string propertyName", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SystemInfoPublisherUsesBaseManagerResolutionCooldown()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxgloveSystemInfoPublisher.cs");
            var update = TestSources.ExtractMethod(source, "private void Update()");

            Assert.Contains("if (!EnsureManagerAvailable()) return;", update, StringComparison.Ordinal);
            Assert.DoesNotContain("ResolveManager();", update, StringComparison.Ordinal);
        }

        [Fact]
        public void HumbleScalableTimeSourceDeduplicatesRosUnavailableWarningAndAvoidsNestedClockLock()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            var generator = TestSources.Text("Scripts/ros2forunity/windows/humble/build_r2fu_runtime_package.py");
            var getTime = TestSources.ExtractMethod(source, "public bool GetTime");

            Assert.Contains("rosUnavailableWarningLogged", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 1)", getTime, StringComparison.Ordinal);
            Assert.DoesNotContain("rosUnityTimeOffset = GetRosNowSeconds() - readingSecs;", getTime, StringComparison.Ordinal);
            Assert.Contains("var rosNowSecs = GetRosNowSeconds();", getTime, StringComparison.Ordinal);
            Assert.Contains("rosUnityTimeOffset = rosNowSecs - readingSecs;", getTime, StringComparison.Ordinal);
            Assert.Contains("rosUnavailableWarningLogged", generator, StringComparison.Ordinal);
            Assert.Contains("rosNowSecs = GetRosNowSeconds()", generator, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraCalibrationPublisherCachesFallbackCameraAndFixedMatrices()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraCalibrationPublisher.cs");
            var build = TestSources.ExtractMethod(source, "private CameraCalibrationMessage BuildCalibration");
            var resolve = TestSources.ExtractMethod(source, "private Camera ResolveSourceCamera");

            Assert.Contains("private readonly double[] _k = new double[9];", source, StringComparison.Ordinal);
            Assert.Contains("private readonly double[] _r = new double[9];", source, StringComparison.Ordinal);
            Assert.Contains("private readonly double[] _p = new double[12];", source, StringComparison.Ordinal);
            Assert.Contains("private Camera _cachedMainCamera;", source, StringComparison.Ordinal);
            Assert.Contains("WriteMatrices(fx, fy, cx, cy);", build, StringComparison.Ordinal);
            Assert.DoesNotContain("new[] { fx", build, StringComparison.Ordinal);
            Assert.Contains("now < _nextMainCameraResolveTime", resolve, StringComparison.Ordinal);
            Assert.Contains("_cachedMainCamera = Camera.main;", resolve, StringComparison.Ordinal);
        }
    }
}
