// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Phase173107ReviewTests
    {
        [Fact]
        public void FoxServiceAttributeRejectsNullName()
            => Assert.Throws<ArgumentNullException>(() => new FoxServiceAttribute(null));

        [Fact]
        public void Ros2NativePolicyClearsCachedManagerOnSubsystemRegistration()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/Ros2NativeOutputPolicy.cs");

            Assert.Contains("RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)", source);
            Assert.Contains("private static void ResetStaticState()", source);
            Assert.Contains("_manager = null;", source);
        }

        [Fact]
        public void CoordinateConverterDocumentsAngularVelocityInverse()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Utilities/CoordinateConverter.cs");

            Assert.Contains("FoxgloveToUnityAngularVelocity", source);
            Assert.Contains("new UnityEngine.Vector3(angular.y, -angular.z, -angular.x)", source);
        }

        [Fact]
        public void RemoteGatewayManifestDoesNotCommitLocalCargoTargetPath()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/Plugins/Windows/x86_64/foxglove-gateway-native-artifact.json");

            Assert.Contains("\"CARGO_TARGET_DIR\": \"<build-local>\"", source);
            Assert.DoesNotContain("C:\\\\u2fg171target", source);
        }

        [Fact]
        public void CameraImageAndSampleContractsStayAllocationHonest()
        {
            var camera = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CameraCompressedImageBuilder.cs");
            var sample = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs");
            var accessUnit = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/EncodedVideoAccessUnit.cs");

            Assert.Contains("System.Array.Empty<byte>()", camera);
            Assert.DoesNotContain("_points == null", sample);
            Assert.Contains("input ownership and encoded", accessUnit);
        }

        [Fact]
        public void ReplayTickBudgetTreatsNegativeValuesAsUnlimited()
        {
            var engine = new McapReplayEngine { MaxMessagesPerTick = -1 };

            Assert.Equal(0, engine.MaxMessagesPerTick);
        }
    }
}
