// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks custom-envelope teardown and apply-failure ownership paths.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-D")]
    [Trait("Domain", "CustomNativeTransport")]
    public sealed class FoxRunRos2CustomLifecycleTests
    {
        [Fact]
        public void ReentrantCustomApplyStopDrainsTheCopiedEnvelopeAndRejectsLateCallbacks()
        {
            var backend = new FakeBackend();
            TestEnvelope applied = null;
            FoxRunRos2SubscriptionBinding<TestEnvelope> binding = null;
            binding = CreateBinding(
                backend,
                value =>
                {
                    applied = value;
                    binding.Stop();
                },
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new TestEnvelope { Value = 41 });

            Assert.True(binding.TryApplyLatest(1));
            var copied = Assert.Single(backend.Copies);
            Assert.Null(applied);
            Assert.Equal(1, copied.DisposeCount);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);

            backend.Invoke(new TestEnvelope { Value = 42 });
            Assert.Single(backend.Copies);
            Assert.Equal(1, binding.RejectedAfterStopCount);
            binding.Stop();
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void CustomMapperFailurePreservesTerminalErrorAndDisposesTheOwnedEnvelope()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                _ => throw new InvalidOperationException("custom DTO mapper failed"),
                _ => false);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new TestEnvelope { Value = 7 });

            var failure = Assert.Throws<InvalidOperationException>(() => binding.TryApplyLatest(1));
            binding.RecordApplyFailure(failure);

            var copied = Assert.Single(backend.Copies);
            Assert.Equal(1, copied.DisposeCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(binding.TryGetSnapshot(1, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.ApplyFailure, snapshot.Error);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        private static FoxRunRos2SubscriptionBinding<TestEnvelope> CreateBinding(
            FakeBackend backend,
            Action<TestEnvelope> apply,
            Func<TestEnvelope, bool> clearIfOwned)
        {
            return new FoxRunRos2SubscriptionBinding<TestEnvelope>(
                Contract(),
                sessionGeneration: 1,
                activeGeneration: () => 1,
                maximumCopyBytes: 4L * 1024L * 1024L,
                copy: (borrowed, _) =>
                {
                    var owned = new TestEnvelope { Value = borrowed.Value };
                    backend.Copies.Add(owned);
                    return owned;
                },
                dispose: value => value.Dispose(),
                apply: apply,
                clearIfOwned: clearIfOwned,
                backend: backend,
                qos: FoxRunResolvedQos.Default,
                qosFactory: new ManagedQosFactory());
        }

        private static FoxRunRos2GeneratedContract Contract()
        {
            return new FoxRunRos2GeneratedContract(
                "custom-lifecycle-contract",
                "/phase181/custom-lifecycle",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                FoxRunFlow.PublishAndSubscribe,
                FoxRunRos2RouteEndpoint.R2fu,
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
                supportsRos2Native: true,
                declaredSubscriptionEncoding: FoxRunEncoding.JSON,
                contractKind: FoxRunRos2GeneratedContractKind.CustomInterface,
                staticInterfacePackageId: "dev.unity2foxglove.foxrun.ros2.interfaces",
                rosPackageName: "unity2foxglove_foxrun_interfaces_v1",
                interfaceRevision: 1,
                interfaceDigest: "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                baseRuntimePackageId: "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                canonicalPayloadType: "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1");
        }

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public int Value { get; set; }
            public int DisposeCount { get; private set; }
            public bool IsDisposed => DisposeCount != 0;

            public void Dispose() => DisposeCount++;
        }

        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private Action<TestEnvelope> _callback;

            public List<TestEnvelope> Copies { get; } = new List<TestEnvelope>();
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

            public void Invoke(TestEnvelope borrowed) => _callback(borrowed);

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
