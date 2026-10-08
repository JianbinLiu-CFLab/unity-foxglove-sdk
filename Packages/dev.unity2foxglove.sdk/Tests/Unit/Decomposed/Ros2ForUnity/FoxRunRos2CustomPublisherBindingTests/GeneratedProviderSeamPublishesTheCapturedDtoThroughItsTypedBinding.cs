// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks main-thread typed-bus custom publisher ownership and sequencing.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{


public sealed partial class FoxRunRos2CustomPublisherBindingTests
    {
        [Fact]
        public void GeneratedProviderSeamPublishesTheCapturedDtoThroughItsTypedBinding()
        {
            var backend = new FakePublisherBackend();
            var binding = CreateBinding(new FoxTopicBus(), backend, initialSequence: 3UL);
            Assert.True(binding.TryStart().Succeeded);
            var bindings = Hosted(17, binding);
            var source = new GeneratedSource(new TestDto { Value = 42 });
            var request = new FoxRunGeneratedTransportPublishRequest(
                source,
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 123UL);

            var result = FoxRunRos2CustomPublisherHub.PublishGenerated(
                bindings,
                17,
                in request);

            Assert.Equal(FoxRunTransportRouteResultState.Accepted, result.State);
            var published = Assert.Single(backend.Published);
            Assert.Equal("local-origin", published.Origin);
            Assert.Equal(3UL, published.Sequence);
            Assert.Equal(123UL, published.TimestampNs);
            Assert.Equal(42, published.Value);
            Assert.Equal(1, published.DisposeCount);
            Assert.Equal(1, binding.PublishedCount);

            source.State = new TestDto { Value = 43 };
            var next = new FoxRunGeneratedTransportPublishRequest(
                source,
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 124UL);
            Assert.Equal(
                FoxRunTransportRouteResultState.Accepted,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 17, in next).State);

            Assert.Equal(2, backend.Published.Count);
            Assert.Equal(4UL, backend.Published[1].Sequence);
            Assert.Equal(43, backend.Published[1].Value);
            Assert.Equal(2, binding.PublishedCount);
            binding.Stop();
        }

        [Fact]
        public void GeneratedProviderSeamOwnsOnlyStartedBindingsForTheSameSourceAndTopic()
        {
            var backend = new FakePublisherBackend();
            var binding = CreateBinding(new FoxTopicBus(), backend, initialSequence: 0UL);
            Assert.True(binding.TryStart().Succeeded);
            var bindings = Hosted(17, binding);
            var request = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(new TestDto { Value = 1 }),
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 1UL);

            Assert.NotNull(FoxRunRos2CustomPublisherHub.FindGeneratedBinding(bindings, 17, "/phase181/custom"));
            Assert.Null(FoxRunRos2CustomPublisherHub.FindGeneratedBinding(bindings, 18, "/phase181/custom"));
            Assert.Null(FoxRunRos2CustomPublisherHub.FindGeneratedBinding(bindings, 17, "/phase181/other"));
            Assert.Equal(
                FoxRunTransportRouteResultState.Unavailable,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 18, in request).State);

            binding.Stop();

            Assert.Null(FoxRunRos2CustomPublisherHub.FindGeneratedBinding(bindings, 17, "/phase181/custom"));
            Assert.Equal(
                FoxRunTransportRouteResultState.Unavailable,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 17, in request).State);
            Assert.Empty(backend.Published);
        }

        [Fact]
        public void GeneratedProviderSeamReportsMismatchedMembersAndFailedPublishes()
        {
            var backend = new FakePublisherBackend { PublishSucceeds = false };
            var binding = CreateBinding(new FoxTopicBus(), backend, initialSequence: 0UL);
            Assert.True(binding.TryStart().Succeeded);
            var bindings = Hosted(17, binding);
            var failed = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(new TestDto { Value = 5 }),
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 1UL);
            var mismatched = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(
                    new FoxRunGeneratedMemberAccess<string>(
                        "mismatched",
                        "/phase181/custom",
                        "Phase181.State",
                        FoxRunFlow.Publish,
                        new[] { "unity2foxglove.r2fu" },
                        null,
                        default,
                        FoxRunDeliveryPolicy.ProviderDefault,
                        () => "not a DTO")),
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 2UL);
            var unreadable = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase181/custom",
                logTimeNs: 3UL);

            Assert.Equal(
                FoxRunTransportRouteResultState.Failed,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 17, in failed).State);
            Assert.Equal(1, binding.PublishFailureCount);
            Assert.Equal(
                FoxRunTransportRouteResultState.Rejected,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 17, in mismatched).State);
            Assert.Equal(
                FoxRunTransportRouteResultState.Rejected,
                FoxRunRos2CustomPublisherHub.PublishGenerated(bindings, 17, in unreadable).State);
            Assert.Equal(0, binding.PublishedCount);
            binding.Stop();
        }

        private static List<IFoxRunRos2CustomPublisherHostedBinding> Hosted(
            int sourceInstanceId,
            FoxRunRos2CustomPublisherBinding<TestDto, TestEnvelope> binding)
            => new List<IFoxRunRos2CustomPublisherHostedBinding>
            {
                new FoxRunRos2CustomPublisherHub.HostedBinding<TestDto, TestEnvelope>(
                    sourceInstanceId + "|publisher-contract",
                    sourceInstanceId,
                    binding)
            };

        private sealed class GeneratedSource : IFoxRunGeneratedTransportSource
        {
            private readonly IFoxRunGeneratedMemberAccess[] _members;

            internal GeneratedSource()
            {
                _members = Array.Empty<IFoxRunGeneratedMemberAccess>();
            }

            internal GeneratedSource(TestDto state)
            {
                State = state;
                _members = new IFoxRunGeneratedMemberAccess[]
                {
                    new FoxRunGeneratedMemberAccess<TestDto>(
                        "Phase181.Source\nfield\nState\n/phase181/custom\n1\nstate",
                        "/phase181/custom",
                        "Phase181.State",
                        FoxRunFlow.Publish,
                        new[] { "unity2foxglove.r2fu" },
                        null,
                        default,
                        FoxRunDeliveryPolicy.ProviderDefault,
                        () => State)
                };
            }

            internal GeneratedSource(IFoxRunGeneratedMemberAccess member)
            {
                _members = new[] { member };
            }

            internal TestDto State { get; set; }

            public int FoxRunTransport_MemberCount => _members.Length;

            public IFoxRunGeneratedMemberAccess FoxRunTransport_GetMember(int index)
                => _members[index];

            public ulong FoxRunTransport_GetCaptureSequence(int topicIndex) => 0UL;
        }

        private static FoxRunRos2CustomPublisherBinding<TestDto, TestEnvelope> CreateBinding(
            FoxTopicBus bus,
            FakePublisherBackend backend,
            ulong initialSequence,
            Func<TestDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TestEnvelope> map = null,
            Func<FoxRunRos2CustomTypesupportReadiness> readiness = null,
            Action<TestEnvelope> dispose = null,
            Action onStopped = null)
        {
            return new FoxRunRos2CustomPublisherBinding<TestDto, TestEnvelope>(
                Contract(),
                bus,
                backend,
                FoxRunResolvedQos.Default,
                map ?? ((dto, origin, sequence, timestamp, budget) => new TestEnvelope
                {
                    Origin = origin,
                    Sequence = sequence,
                    TimestampNs = timestamp,
                    Value = dto.Value
                }),
                dispose ?? (value => value.Dispose()),
                "local-origin",
                new FoxRunRos2CustomSequenceSource(initialSequence),
                readiness ?? (() => FoxRunRos2CustomTypesupportReadiness.From(
                    FoxRunRos2CustomTypesupportReadinessCode.Ready)),
                onStopped);
        }

        private static FoxRunRos2CustomPublisherContract Contract()
            => new FoxRunRos2CustomPublisherContract(
                "publisher-contract",
                "/phase181/custom",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                FoxRunFlow.Publish,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false);

        private static FoxTopicContract TopicContract()
            => new FoxTopicContract(
                "/phase181/custom",
                "phase181.State",
                "json",
                "Phase181.State",
                "phase181-state",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

        private sealed class TestDto
        {
            public int Value { get; set; }
        }

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public string Origin { get; set; }
            public ulong Sequence { get; set; }
            public ulong TimestampNs { get; set; }
            public int Value { get; set; }
            public int DisposeCount { get; private set; }
            public bool IsDisposed => DisposeCount != 0;
            public void Dispose() => DisposeCount++;
        }

        private sealed class FakePublisherBackend : IFoxRunRos2NativePublisherBackend
        {
            private readonly Token _token = new Token();

            public List<TestEnvelope> Published { get; } = new List<TestEnvelope>();
            public List<string> StopOrder { get; } = new List<string>();
            public bool PublishSucceeds { get; set; } = true;
            public Exception PublishFailure { get; set; }
            public Exception RemoveFailure { get; set; }
            public Exception ReleaseFailure { get; set; }
            public Exception TokenUsabilityFailure
            {
                get => _token.UsabilityFailure;
                set => _token.UsabilityFailure = value;
            }
            public int RegisterCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public FoxRunResolvedQos RegisteredQos { get; private set; }

            public FoxRunRos2NativePublisherRegistration Register<T>(
                FoxRunRos2CustomPublisherContract contract,
                FoxRunResolvedQos qos)
                where T : ROS2.Message, new()
            {
                RegisterCount++;
                RegisteredQos = qos;
                return FoxRunRos2NativePublisherRegistration.Success(_token);
            }

            public bool TryPublish<T>(IFoxRunRos2NativePublisherToken token, T message)
                where T : ROS2.Message, new()
            {
                if (token != _token || message is not TestEnvelope envelope)
                    return false;
                if (PublishFailure != null)
                    throw PublishFailure;
                Published.Add(envelope);
                return PublishSucceeds;
            }

            public void RemovePublisher(IFoxRunRos2NativePublisherToken token)
            {
                Assert.Same(_token, token);
                StopOrder.Add("remove");
                if (RemoveFailure != null)
                    throw RemoveFailure;
            }

            public bool ReleaseNodeOwnership()
            {
                ReleaseCount++;
                StopOrder.Add("release");
                if (ReleaseFailure != null)
                    throw ReleaseFailure;
                return true;
            }

            private sealed class Token : IFoxRunRos2NativePublisherToken
            {
                public Exception UsabilityFailure { get; set; }

                public bool IsUsable
                {
                    get
                    {
                        if (UsabilityFailure != null)
                            throw UsabilityFailure;
                        return true;
                    }
                }
            }
        }
    }
}
#endif
