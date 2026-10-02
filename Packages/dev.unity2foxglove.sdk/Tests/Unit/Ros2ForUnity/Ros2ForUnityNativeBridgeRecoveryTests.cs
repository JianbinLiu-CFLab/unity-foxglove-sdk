// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed class Ros2ForUnityNativeBridgeRecoveryTests
    {
        [Fact]
        public void RuntimeLossClearsBindingsAndReopensAdmission()
        {
            var runtimeWasReady = true;
            var clearCount = 0;

            Ros2ForUnityNativeBridgeRecovery.ResetAfterRuntimeLoss(
                ref runtimeWasReady,
                () => clearCount++);

            Assert.False(runtimeWasReady);
            Assert.Equal(1, clearCount);

            Ros2ForUnityNativeBridgeRecovery.ResetAfterRuntimeLoss(
                ref runtimeWasReady,
                () => clearCount++);

            Assert.Equal(1, clearCount);
        }
    }
}
#endif
