// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks custom-envelope main-thread reconstruction and origin filtering.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "CustomNativeTransport")]
    public sealed class FoxRunRos2CustomRoundTripTests
    {
        private const string Digest = "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        [Fact]
        public void SameOriginIsDeepCopiedThenDroppedBeforeDtoConstructionAndDisposedExactlyOnce()
        {
            FoxRunRos2CustomOriginRegistry.ResetForTests();
            const string identity = "17|custom-contract";
            var localOrigin = FoxRunRos2CustomOriginRegistry.BeginPublisher(identity);
            var backend = new FakeBackend();
            var dtoConstructionCount = 0;
            var owned = default(TestEnvelope);
            var binding = CreateBinding(
                backend,
                identity,
                envelope =>
                {
                    dtoConstructionCount++;
                    return new TestDto { Value = envelope.Value };
                },
                _ => { },
                value => owned = value);

            Assert.True(binding.TryRegister().Succeeded);
            var borrowed = new TestEnvelope { Origin = localOrigin, Value = 42 };
            backend.Invoke(borrowed);

            Assert.False(binding.TryApplyLatest(1));
            Assert.Equal(1, backend.CopyCount);
            Assert.Equal(0, dtoConstructionCount);
            Assert.Equal(1, binding.SameOriginDropCount);
            Assert.NotNull(owned);
            Assert.Equal(1, owned.DisposeCount);

            binding.Stop();
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            FoxRunRos2CustomOriginRegistry.EndPublisher(identity, localOrigin);
        }

        [Theory]
        [InlineData("remote-origin")]
        [InlineData("")]
        public void RemoteOrEmptyOriginConstructsDtoOnTheMainThreadAndKeepsNoRosHandle(string origin)
        {
            FoxRunRos2CustomOriginRegistry.ResetForTests();
            const string identity = "18|custom-contract";
            var localOrigin = FoxRunRos2CustomOriginRegistry.BeginPublisher(identity);
            var backend = new FakeBackend();
            TestDto applied = null;
            TestEnvelope owned = null;
            var binding = CreateBinding(
                backend,
                identity,
                envelope => new TestDto { Value = envelope.Value },
                value => applied = value,
                value => owned = value);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new TestEnvelope { Origin = origin, Value = 73 });

            Assert.True(binding.TryApplyLatest(1));
            Assert.NotNull(applied);
            Assert.Equal(73, applied.Value);
            Assert.Equal(0, binding.SameOriginDropCount);
            Assert.NotNull(owned);
            Assert.Equal(0, owned.DisposeCount);

            binding.Stop();
            Assert.Equal(1, owned.DisposeCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            FoxRunRos2CustomOriginRegistry.EndPublisher(identity, localOrigin);
        }

        private static FoxRunRos2SubscriptionBinding<TestEnvelope> CreateBinding(
            FakeBackend backend,
            string identity,
            Func<TestEnvelope, TestDto> mapToDto,
            Action<TestDto> onAppliedDto,
            Action<TestEnvelope> onOwned = null)
        {
            var contract = new FoxRunRos2GeneratedContract(
                "custom-contract",
                "/phase181/custom",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181StateEnvelope",
                FoxRunFlow.PublishAndSubscribe,
                FoxRunEndpoint.Ros2Native,
                FoxRunRos2QosPreset.Reliable,
                true,
                FoxRunEncoding.JSON,
                FoxRunRos2GeneratedContractKind.CustomInterface,
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                Digest,
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State",
                value => value is TestEnvelope envelope ? envelope.Origin : string.Empty);

            return new FoxRunRos2SubscriptionBinding<TestEnvelope>(
                contract,
                1,
                () => 1,
                4L * 1024L * 1024L,
                (borrowed, _) =>
                {
                    backend.CopyCount++;
                    var copy = new TestEnvelope { Origin = borrowed.Origin, Value = borrowed.Value };
                    onOwned?.Invoke(copy);
                    return copy;
                },
                value => value.Dispose(),
                value =>
                {
                    var dto = mapToDto(value);
                    Assert.IsType<TestDto>(dto);
                    onAppliedDto?.Invoke(dto);
                },
                _ => false,
                backend,
                FoxRunRos2QosPreset.Reliable,
                new ManagedQosFactory(),
                value => contract.TryGetCustomEnvelopeOrigin(value, out var origin)
                         && FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(identity, origin));
        }

        private sealed class TestDto
        {
            public int Value { get; set; }
        }

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public string Origin { get; set; }
            public int Value { get; set; }
            public int DisposeCount { get; private set; }
            public bool IsDisposed => DisposeCount != 0;
            public void Dispose() => DisposeCount++;
        }

        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private Action<TestEnvelope> _callback;

            public int CopyCount { get; set; }
            public int RemoveCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                _callback = value => callback((T)(ROS2.Message)value);
                return FoxRunRos2NativeBackendRegistration.Success(new Token());
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token) => RemoveCount++;
            public void ReleaseNodeOwnership() => ReleaseCount++;
            public void Invoke(TestEnvelope envelope) => _callback(envelope);

            private sealed class Token : IFoxRunRos2NativeSubscriptionToken
            {
                public bool IsUsable => true;
            }
        }

        private sealed class ManagedQosFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset) => new ManagedQosProfile();
        }

        private sealed class ManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile => null;
            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }
            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability) { }
            public void Dispose() { }
        }
    }
}
#endif
