// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks main-thread typed-bus custom publisher ownership and sequencing.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "CustomNativePublisher")]
    public sealed class FoxRunRos2CustomPublisherBindingTests
    {
        [Fact]
        public void BindingSubscribesPublishesAndUnsubscribesBeforeReleasingItsEndpoint()
        {
            var bus = new FoxTopicBus();
            var backend = new FakePublisherBackend();
            var binding = CreateBinding(bus, backend, initialSequence: 7UL);

            Assert.True(binding.TryStart().Succeeded);
            Assert.True(bus.HasSubscribers("/phase181/custom"));

            bus.Publish(TopicContract(), 123UL, new TestDto { Value = 42 }, "generated-source");

            var published = Assert.Single(backend.Published);
            Assert.Equal("local-origin", published.Origin);
            Assert.Equal(7UL, published.Sequence);
            Assert.Equal(123UL, published.TimestampNs);
            Assert.Equal(42, published.Value);
            Assert.Equal(1, published.DisposeCount);
            Assert.Equal(1, binding.PublishedCount);

            binding.Stop();

            Assert.False(bus.HasSubscribers("/phase181/custom"));
            Assert.Equal(new[] { "remove", "release" }, backend.StopOrder);
            bus.Publish(TopicContract(), 124UL, new TestDto { Value = 99 }, "generated-source");
            Assert.Single(backend.Published);
        }

        [Fact]
        public void MapperFailureDoesNotConsumeTheNextSequence()
        {
            var bus = new FoxTopicBus();
            var backend = new FakePublisherBackend();
            var calls = 0;
            var binding = CreateBinding(
                bus,
                backend,
                initialSequence: 0UL,
                map: (dto, origin, sequence, timestamp, budget) =>
                {
                    calls++;
                    if (calls == 1)
                        throw new InvalidOperationException("mapper failure");
                    return new TestEnvelope
                    {
                        Origin = origin,
                        Sequence = sequence,
                        TimestampNs = timestamp,
                        Value = dto.Value
                    };
                });

            Assert.True(binding.TryStart().Succeeded);
            bus.Publish(TopicContract(), 100UL, new TestDto { Value = 1 }, "source");
            bus.Publish(TopicContract(), 101UL, new TestDto { Value = 2 }, "source");

            var published = Assert.Single(backend.Published);
            Assert.Equal(0UL, published.Sequence);
            Assert.Equal(1, binding.MapperFailureCount);
            binding.Stop();
        }

        [Fact]
        public void SequenceExhaustionStopsTheBindingWithoutWrappingTheOriginPair()
        {
            var bus = new FoxTopicBus();
            var backend = new FakePublisherBackend();
            var binding = CreateBinding(bus, backend, initialSequence: ulong.MaxValue);

            Assert.True(binding.TryStart().Succeeded);
            bus.Publish(TopicContract(), 1UL, new TestDto { Value = 1 }, "source");
            bus.Publish(TopicContract(), 2UL, new TestDto { Value = 2 }, "source");

            var published = Assert.Single(backend.Published);
            Assert.Equal(ulong.MaxValue, published.Sequence);
            Assert.True(binding.IsStopped);
            Assert.Equal(1, binding.SequenceExhaustedCount);
            Assert.False(bus.HasSubscribers("/phase181/custom"));
        }

        [Fact]
        public void NonReadyTypesupportPreventsEndpointCreationAndLeavesNoBusDelegate()
        {
            var bus = new FoxTopicBus();
            var backend = new FakePublisherBackend();
            var binding = CreateBinding(
                bus,
                backend,
                initialSequence: 0UL,
                readiness: () => FoxRunRos2CustomTypesupportReadiness.From(
                    FoxRunRos2CustomTypesupportReadinessCode.MissingCatalog));

            var result = binding.TryStart();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.RegistrationRejected, result.Error);
            Assert.Equal(0, backend.RegisterCount);
            Assert.False(bus.HasSubscribers("/phase181/custom"));
            binding.Stop();
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void FailedPublishDisposesOwnedEnvelopeAndStopRunsOriginCleanupOnlyOnce()
        {
            var bus = new FoxTopicBus();
            var backend = new FakePublisherBackend { PublishSucceeds = false };
            var originCleanupCount = 0;
            var binding = CreateBinding(
                bus,
                backend,
                initialSequence: 4UL,
                onStopped: () => originCleanupCount++);

            Assert.True(binding.TryStart().Succeeded);
            bus.Publish(TopicContract(), 123UL, new TestDto { Value = 9 }, "generated-source");

            var envelope = Assert.Single(backend.Published);
            Assert.Equal(1, binding.PublishFailureCount);
            Assert.Equal(1, envelope.DisposeCount);
            Assert.Equal(0, binding.PublishedCount);

            binding.Stop();
            binding.Stop();
            Assert.Equal(1, originCleanupCount);
            Assert.Equal(new[] { "remove", "release" }, backend.StopOrder);
        }

        private static FoxRunRos2CustomPublisherBinding<TestDto, TestEnvelope> CreateBinding(
            FoxTopicBus bus,
            FakePublisherBackend backend,
            ulong initialSequence,
            Func<TestDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TestEnvelope> map = null,
            Func<FoxRunRos2CustomTypesupportReadiness> readiness = null,
            Action onStopped = null)
        {
            return new FoxRunRos2CustomPublisherBinding<TestDto, TestEnvelope>(
                Contract(),
                bus,
                backend,
                map ?? ((dto, origin, sequence, timestamp, budget) => new TestEnvelope
                {
                    Origin = origin,
                    Sequence = sequence,
                    TimestampNs = timestamp,
                    Value = dto.Value
                }),
                value => value.Dispose(),
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
                FoxRunMode.PublishOnly);

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
            public int RegisterCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public FoxRunRos2NativePublisherRegistration Register<T>(FoxRunRos2CustomPublisherContract contract)
                where T : ROS2.Message, new()
            {
                RegisterCount++;
                return FoxRunRos2NativePublisherRegistration.Success(_token);
            }

            public bool TryPublish<T>(IFoxRunRos2NativePublisherToken token, T message)
                where T : ROS2.Message, new()
            {
                if (token != _token || message is not TestEnvelope envelope)
                    return false;
                Published.Add(envelope);
                return PublishSucceeds;
            }

            public void RemovePublisher(IFoxRunRos2NativePublisherToken token)
            {
                Assert.Same(_token, token);
                StopOrder.Add("remove");
            }

            public void ReleaseNodeOwnership()
            {
                ReleaseCount++;
                StopOrder.Add("release");
            }

            private sealed class Token : IFoxRunRos2NativePublisherToken
            {
                public bool IsUsable => true;
            }
        }
    }
}
#endif
