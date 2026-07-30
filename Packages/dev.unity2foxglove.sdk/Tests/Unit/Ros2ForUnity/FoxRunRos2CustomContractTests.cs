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
                FoxRunFlow.PublishAndSubscribe,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: 0,
                hasExplicitTargets: false);

            Assert.True(contract.HasCompleteMetadata);
            Assert.True(contract.SupportsNativeOutput);
            Assert.True(contract.IsPublishAndSubscribe);
            Assert.Equal("/phase181/state", contract.Topic);
            Assert.Equal("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", contract.BaseRuntimePackageId);
            Assert.Equal((FoxRunRos2RouteEndpoint)0, contract.DeclaredSource);
            Assert.False(contract.HasExplicitSource);
            Assert.Equal((FoxRunRos2RouteEndpoint)0, contract.DeclaredTargets);
            Assert.False(contract.HasExplicitTargets);
            Assert.True(contract.HasExplicitQos);
            var qos = contract.ResolveQos(FoxRunResolvedQos.SensorData);
            Assert.True(qos.Success);
            Assert.Equal(FoxRunResolvedQos.Default, qos.Qos);
        }

        [Fact]
        public void PublisherRuntimeConstraintFailsClosedForAllFoxgloveInheritedTopology()
        {
            var contract = CreateContract(
                mode: FoxRunFlow.Publish,
                qosProfile: FoxRunQosProfile.SensorData,
                hasExplicitQosProfile: true);

            var shouldRegister = FoxRunRos2CustomPublisherHub.ShouldRegisterNativePublisher(
                contract,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.WebSocket,
                out var resolution);

            Assert.False(shouldRegister);
            Assert.False(resolution.Success);
            Assert.Equal(FoxRunRos2RouteDiagnosticCode.QosRequiresR2fu, resolution.DiagnosticCode);
            Assert.Equal(
                "R2FU QoS requires an R2FU direction.",
                resolution.DiagnosticMessage);
        }

        [Fact]
        public void PublisherRuntimeConstraintRegistersOnlyWhenResolvedPublishTargetsIncludeNative()
        {
            var inherited = CreateContract(
                mode: FoxRunFlow.Publish,
                qosProfile: FoxRunQosProfile.SensorData,
                hasExplicitQosProfile: true);
            var bridgeOnly = CreateContract(
                mode: FoxRunFlow.Publish,
                qosProfile: FoxRunQosProfile.SensorData,
                hasExplicitQosProfile: true,
                declaredTargets: FoxRunRos2RouteEndpoint.WebSocket,
                hasExplicitTargets: true);

            Assert.True(FoxRunRos2CustomPublisherHub.ShouldRegisterNativePublisher(
                inherited,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.R2fu,
                out var nativeResolution));
            Assert.True(nativeResolution.Success);
            Assert.False(FoxRunRos2CustomPublisherHub.ShouldRegisterNativePublisher(
                bridgeOnly,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.R2fu,
                out var bridgeResolution));
            Assert.False(bridgeResolution.Success);
            Assert.Equal(
                FoxRunRos2RouteDiagnosticCode.QosRequiresR2fu,
                bridgeResolution.DiagnosticCode);
        }

        [Fact]
        public void CustomPublisherAdmissionRequiresTheExactAcceptedSourceOrigin()
        {
            var bus = new FoxTopicBus();
            var contract = TopicContract(
                "/phase184/custom-owner",
                FoxTopicWriterPolicy.SingleWriter);
            var accepted = new ContractSource("source-a", contract);
            var rejected = new ContractSource("source-b", contract);
            Assert.True(bus.Register(contract, "source-a").Accepted);
            Assert.False(bus.Register(contract, "source-b").Accepted);

            Assert.True(FoxRunRos2CustomPublisherHub.TryGetAcceptedSourceOrigin(
                accepted,
                bus,
                contract.Topic,
                out var acceptedOrigin));
            Assert.Equal("source-a", acceptedOrigin);
            Assert.False(FoxRunRos2CustomPublisherHub.TryGetAcceptedSourceOrigin(
                rejected,
                bus,
                contract.Topic,
                out _));
        }

        [Fact]
        public void MatchingMultiWritersEachOwnTheirCustomPublisherOrigin()
        {
            var bus = new FoxTopicBus();
            var contract = TopicContract(
                "/phase184/custom-multi",
                FoxTopicWriterPolicy.MultiWriter);
            var first = new ContractSource("source-a", contract);
            var second = new ContractSource("source-b", contract);
            Assert.True(bus.Register(contract, "source-a").Accepted);
            Assert.True(bus.Register(contract, "source-b").Accepted);

            Assert.True(FoxRunRos2CustomPublisherHub.TryGetAcceptedSourceOrigin(
                first,
                bus,
                contract.Topic,
                out var firstOrigin));
            Assert.True(FoxRunRos2CustomPublisherHub.TryGetAcceptedSourceOrigin(
                second,
                bus,
                contract.Topic,
                out var secondOrigin));
            Assert.Equal("source-a", firstOrigin);
            Assert.Equal("source-b", secondOrigin);
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

        private static FoxRunRos2CustomPublisherContract CreateContract(
            FoxRunFlow mode,
            FoxRunQosProfile qosProfile,
            bool hasExplicitQosProfile,
            FoxRunRos2RouteEndpoint declaredTargets = 0,
            bool hasExplicitTargets = false)
            => new(
                "phase184.runtime-constraint",
                "/phase184/runtime-constraint",
                "Phase184.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase184State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase184StateEnvelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                Digest,
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                mode,
                qosProfile,
                hasExplicitQosProfile,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets,
                hasExplicitTargets);

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

        [Fact]
        public void SelfOriginDetectionUsesGeneratedSourceOriginWithoutNativePublisherRegistration()
        {
            FoxRunRos2CustomOriginRegistry.ResetForTests();
            const string endpoint = "18|bridge-only-contract";
            const string generatedSourceOrigin = "unity2foxglove-instance-18";

            Assert.True(FoxRunRos2SubscriptionHub.IsSelfOrigin(
                endpoint,
                generatedSourceOrigin,
                generatedSourceOrigin));
            Assert.False(FoxRunRos2SubscriptionHub.IsSelfOrigin(
                endpoint,
                "remote-peer",
                generatedSourceOrigin));
        }

        private static FoxTopicContract TopicContract(
            string topic,
            FoxTopicWriterPolicy writerPolicy)
            => new(
                topic,
                "phase184.CustomState",
                "json",
                "phase184.CustomState",
                "phase184-custom-state",
                FoxTopicVisibility.Exported,
                writerPolicy);

        private sealed class ContractSource :
            IFoxgloveLogSource,
            IFoxgloveTopicContractSource
        {
            private readonly FoxTopicContract _contract;

            public ContractSource(string origin, FoxTopicContract contract)
            {
                FoxgloveLog_Origin = origin;
                _contract = contract;
            }

            public int FoxgloveLog_TopicCount => 1;
            public string FoxgloveLog_Origin { get; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
                => new(
                    _contract.Topic,
                    10f,
                    FoxRunPolicy.Trigger,
                    0f,
                    FoxRunFlow.Publish,
                    new[]
                    {
                        FoxRunRos2TransportProvider.IdValue
                    },
                    subscribeTransportId: null,
                    declaredEncoding: 0,
                    hasExplicitEncoding: false,
                    deliveryPolicy:
                        FoxRunDeliveryPolicy.ProviderDefault,
                    hasExplicitDeliveryPolicy: false);

            public FoxTopicContract FoxgloveLog_GetContract(int index)
                => _contract;

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
            {
            }
        }
    }
}
#endif
