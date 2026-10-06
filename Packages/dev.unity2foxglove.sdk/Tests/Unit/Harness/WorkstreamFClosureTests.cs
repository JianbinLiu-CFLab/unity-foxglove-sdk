using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Utilities;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class WorkstreamFClosureTests
    {
        [Fact]
        public void NativeCleanupRetainsOwnerAfterInjectedFailure()
        {
            var handle = new object();
            var attempts = 0;

            Assert.False(RetryableNativeCleanup.TryRemove(
                ref handle,
                _ =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("injected");
                },
                _ => { }));
            Assert.NotNull(handle);

            Assert.True(RetryableNativeCleanup.TryRemove(ref handle, _ => attempts++, _ => { }));
            Assert.Null(handle);
            Assert.Equal(2, attempts);
        }

        [Fact]
        public void RunInBackgroundLeaseDoesNotClobberAnotherOwner()
        {
            var value = false;
            var lease = RunInBackgroundLease.Acquire(() => value, next => value = next);
            Assert.True(value);

            value = false;
            Assert.False(lease.Release());
            Assert.False(value);
        }

        [Fact]
        public void RunInBackgroundLeaseKeepsGlobalSettingForOverlappingOwners()
        {
            var value = false;
            var first = RunInBackgroundLease.Acquire(() => value, next => value = next);
            var second = RunInBackgroundLease.Acquire(() => value, next => value = next);

            Assert.True(first.Release());
            Assert.True(value);
            Assert.True(second.Release());
            Assert.False(value);
        }

        [Fact]
        public void WiringGenerationRejectsQueuedCallbacksAfterReenable()
        {
            var gate = new WiringGenerationGate();
            var first = gate.Activate();
            gate.Invalidate();
            var second = gate.Activate();

            Assert.NotEqual(first, second);
            Assert.False(gate.IsCurrent(first));
            Assert.True(gate.IsCurrent(second));
        }

        [Fact]
        public void MazeCoordinateAuthorityAppliesNonDefaultModeToBothDirections()
        {
            var fields = new Dictionary<string, CoordinateMode>();
            CoordinateModeAuthority.Apply((name, value) => fields[name] = value, CoordinateMode.LeftHand);

            Assert.Equal(CoordinateMode.LeftHand, fields[CoordinateModeAuthority.OutputFieldName]);
            Assert.Equal(CoordinateMode.LeftHand, fields[CoordinateModeAuthority.InputFieldName]);
        }

        [Fact]
        public void DynamicTfAnchorPreservesNonIdentityPose()
        {
            var pose = PackedPointCloudTfAnchorResolver.Resolve(
                positionX: 1f,
                positionY: 2f,
                positionZ: 3f,
                rotationX: 0f,
                rotationY: 0f,
                rotationZ: 0.70710677f,
                rotationW: 0.70710677f,
                offsetX: 0.5f,
                offsetY: -0.25f,
                offsetZ: 1f,
                offsetRotationX: 0f,
                offsetRotationY: 0f,
                offsetRotationZ: 0f,
                offsetRotationW: 1f);

            Assert.Equal(3.5f, pose.TranslationX, 4);
            Assert.Equal(-1.25f, pose.TranslationY, 4);
            Assert.Equal(3f, pose.TranslationZ, 4);
            Assert.NotEqual(1f, pose.RotationW);
            Assert.NotEqual(0f, pose.RotationX);
        }

        [Fact]
        public void ProductionSourcesUseClosureGuards()
        {
            var fullDemo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            var bridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            var phase138 = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/Phase138VirtualLidarPointCloud2Smoke.cs");
            var batch = TestSources.Text("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase110StringSmokeBatchAcceptance.cs");

            Assert.Contains("WiringGenerationGate", fullDemo, StringComparison.Ordinal);
            Assert.Contains("var wiringGeneration = _wiringGeneration.Activate()", fullDemo, StringComparison.Ordinal);
            Assert.Contains("OnParameterChangedForRuntime(rt, wiringGeneration", fullDemo, StringComparison.Ordinal);
            Assert.Contains("_wiringGeneration.IsCurrent(wiringGeneration)", fullDemo, StringComparison.Ordinal);
            Assert.DoesNotContain("_wiringGeneration.Capture()", fullDemo, StringComparison.Ordinal);
            Assert.Contains("PackedPointCloudTfAnchorResolver.Resolve", bridge, StringComparison.Ordinal);
            Assert.DoesNotContain("TransformStamped", phase138, StringComparison.Ordinal);
            Assert.Contains("RunInBackgroundLease", batch, StringComparison.Ordinal);
        }
    }
}
