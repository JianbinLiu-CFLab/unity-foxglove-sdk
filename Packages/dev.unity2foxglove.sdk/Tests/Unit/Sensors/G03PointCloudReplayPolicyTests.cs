// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R4-G03")]
    [Trait("Domain", "Sensors")]
    public sealed class G03PointCloudReplayPolicyTests
    {
        [Fact]
        public void UpdateUsesManagerSuppressionPolicyBeforeDrainingOrPublishing()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var method = TestSources.ExtractMethod(source, "protected virtual void Update()");

            Assert.Contains("if (_manager == null) return;", method, StringComparison.Ordinal);
            Assert.Contains("if (_manager.SuppressLivePublishersForReplay) return;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("Runtime?.ReplayEnabled", method, StringComparison.Ordinal);
            Assert.True(
                method.IndexOf("SuppressLivePublishersForReplay", StringComparison.Ordinal)
                    < method.IndexOf("EnsureEncodePipelines", StringComparison.Ordinal));
        }

        [Fact]
        public void DracoVirtualLidarAdmissionUsesManagerSuppressionPolicy()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs");
            AssertSuppressionGate(
                TestSources.ExtractMethod(source, "internal bool TryQueueVirtualLidarDracoFrame("));
        }

        [Fact]
        public void PackedVirtualLidarAdmissionUsesManagerSuppressionPolicy()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PackedPointCloud.cs");
            AssertSuppressionGate(
                TestSources.ExtractMethod(source, "internal bool TryQueueVirtualLidarPackedPointCloudFrame("));
        }

        private static void AssertSuppressionGate(string method)
        {
            const string gate = "_manager == null || _manager.SuppressLivePublishersForReplay";

            Assert.Contains(gate, method, StringComparison.Ordinal);
            Assert.DoesNotContain("Runtime?.ReplayEnabled", method, StringComparison.Ordinal);
            Assert.True(
                method.IndexOf("ResolveManager()", StringComparison.Ordinal)
                    < method.IndexOf(gate, StringComparison.Ordinal));
            Assert.True(
                method.IndexOf(gate, StringComparison.Ordinal)
                    < method.IndexOf("VirtualLidarPointSnapshotPool.Return", StringComparison.Ordinal));
        }
    }
}
