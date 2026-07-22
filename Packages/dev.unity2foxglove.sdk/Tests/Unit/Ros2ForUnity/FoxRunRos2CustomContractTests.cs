// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks immutable Phase181 custom native transport contract semantics.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "CustomNativeTransport")]
    public sealed class FoxRunRos2CustomContractTests
    {
        private const string Digest = "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        [Fact]
        public void CustomPublisherContractCarriesTheLockedIdentityAndDirectionalMode()
        {
            var contract = new FoxRunRos2CustomPublisherContract(
                "phase181.contract",
                "/phase181/state",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                Digest,
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                FoxRunMode.PublishAndSubscribe);

            Assert.True(contract.HasCompleteMetadata);
            Assert.True(contract.SupportsNativeOutput);
            Assert.True(contract.IsPublishAndSubscribe);
            Assert.Equal("/phase181/state", contract.Topic);
            Assert.Equal("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", contract.BaseRuntimePackageId);
        }

        [Fact]
        public void FixedOutboundBudgetDoesNotExposeTheInboundCopyBudget()
        {
            var context = FoxRunRos2CustomOutboundMappingPolicy.CreateContext();

            Assert.Equal(4L * 1024L * 1024L, FoxRunRos2CustomOutboundMappingPolicy.MaximumBytes);
            context.RequireBytes(FoxRunRos2CustomOutboundMappingPolicy.MaximumBytes);
            Assert.Equal(0, context.RemainingBytes);
            Assert.Throws<FoxRunRos2CustomOutboundBudgetExceededException>(() => context.RequireBytes(1));
        }

        [Fact]
        public void UnixNanosecondTimestampRejectsOnlyTheFirstUnrepresentableSecond()
        {
            const ulong billion = 1_000_000_000UL;
            var latest = ((ulong)int.MaxValue * billion) + (billion - 1UL);

            Assert.True(FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(0, out var epoch));
            Assert.Equal(0, epoch.Seconds);
            Assert.Equal(0u, epoch.Nanoseconds);
            Assert.True(FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(1UL, out var oneNanosecond));
            Assert.Equal(0, oneNanosecond.Seconds);
            Assert.Equal(1u, oneNanosecond.Nanoseconds);
            Assert.True(FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(billion, out var exactSecond));
            Assert.Equal(1, exactSecond.Seconds);
            Assert.Equal(0u, exactSecond.Nanoseconds);
            Assert.True(FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(latest, out var last));
            Assert.Equal(int.MaxValue, last.Seconds);
            Assert.Equal(999_999_999u, last.Nanoseconds);
            Assert.False(FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(latest + 1UL, out _));
        }

        [Fact]
        public void SequenceSourceDoesNotWrapAnOriginSequencePair()
        {
            var sequence = new FoxRunRos2CustomSequenceSource(ulong.MaxValue - 1UL);

            Assert.True(sequence.TryAllocate(out var penultimate));
            Assert.True(sequence.TryAllocate(out var terminal));
            Assert.False(sequence.TryAllocate(out _));
            Assert.Equal(ulong.MaxValue - 1UL, penultimate);
            Assert.Equal(ulong.MaxValue, terminal);
        }

        [Fact]
        public void PublisherOriginRegistryDropsOnlyTheCurrentLocalOrigin()
        {
            FoxRunRos2CustomOriginRegistry.ResetForTests();
            const string endpoint = "17|custom-contract";

            var first = FoxRunRos2CustomOriginRegistry.BeginPublisher(endpoint);
            Assert.True(FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(endpoint, first));

            FoxRunRos2CustomOriginRegistry.EndPublisher(endpoint, first);
            Assert.False(FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(endpoint, first));

            var second = FoxRunRos2CustomOriginRegistry.BeginPublisher(endpoint);
            Assert.NotEqual(first, second);
            Assert.False(FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(endpoint, first));
            Assert.True(FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(endpoint, second));
        }
    }
}
#endif
