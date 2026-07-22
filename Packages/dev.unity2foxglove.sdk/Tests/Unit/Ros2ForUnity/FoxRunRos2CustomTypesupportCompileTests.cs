// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Labels the Phase181 fixture as compile-surface evidence only.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-C")]
    [Trait("Domain", "CompileSurface")]
    public sealed class FoxRunRos2CustomTypesupportCompileTests
    {
        [Fact]
        public void NativeCompileSurfaceContainsClosedFixtureAndCatalogSeamOnly()
        {
            Assert.True(typeof(ROS2.Message).IsAssignableFrom(
                typeof(unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope)));
            Assert.True(typeof(ROS2.Message).IsAssignableFrom(
                typeof(unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1)));
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2CustomTypesupportCatalog",
                typeof(IFoxRunRos2CustomTypesupportCatalog).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native",
                typeof(IFoxRunRos2CustomTypesupportCatalog).Assembly.GetName().Name);
        }
    }
}
#endif
