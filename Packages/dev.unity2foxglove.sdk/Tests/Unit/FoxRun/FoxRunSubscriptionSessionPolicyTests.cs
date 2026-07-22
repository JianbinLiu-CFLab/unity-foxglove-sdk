// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunSubscriptionSessionPolicyTests
    {
        [Fact]
        public void InitialStateExposesANonNullDisabledSnapshot()
        {
            var state = new FoxRunSubscriptionSessionState();

            Assert.NotNull(state.Current);
            Assert.False(state.Current.SubscriptionsEnabled);
            Assert.Equal(0UL, state.Current.SessionGeneration);
        }

        [Fact]
        public void ActiveSnapshotCapturesIndependentAdmissionAndDefaultApplyRates()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.SensorData,
                8 * 1024 * 1024,
                120,
                37);

            Assert.Equal(1UL, policy.SessionGeneration);
            Assert.True(policy.SubscriptionsEnabled);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, policy.DefaultProvider);
            Assert.Equal(FoxRunWireEncoding.Json, policy.WebSocketSubscriptionEncoding);
            Assert.Equal(FoxRunRos2QosPreset.SensorData, policy.DefaultRos2Qos);
            Assert.Equal(8 * 1024 * 1024, policy.NativeCopyBudgetBytes);
            Assert.Equal(120, policy.TransportAdmissionRateLimitHz);
            Assert.Equal(37, policy.DefaultMainThreadApplyRateHz);

            var properties = typeof(FoxRunSubscriptionSessionPolicy)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Assert.Equal(8, properties.Length);
            Assert.All(properties, property => Assert.False(property.CanWrite));
        }

        [Fact]
        public void NativeProviderRetainsAnIndependentWebSocketEncoding()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                4 * 1024 * 1024,
                120,
                60);

            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, policy.DefaultProvider);
            Assert.Equal(FoxRunWireEncoding.Protobuf, policy.WebSocketSubscriptionEncoding);
        }

        [Fact]
        public void BeginNormalizesSourceOnlyDefaultsAndResourceLimits()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Inherit,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.Inherit,
                0,
                0,
                0);

            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, policy.DefaultProvider);
            Assert.Equal(FoxRunRos2QosPreset.Default, policy.DefaultRos2Qos);
            Assert.Equal(4 * 1024 * 1024, policy.NativeCopyBudgetBytes);
            Assert.Equal(1, policy.TransportAdmissionRateLimitHz);
            Assert.Equal(1, policy.DefaultMainThreadApplyRateHz);
        }

        [Fact]
        public void RepeatedBeginFreezesTheOriginalSnapshotAndGeneration()
        {
            var state = new FoxRunSubscriptionSessionState();
            var first = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.Reliable,
                2 * 1024 * 1024,
                90,
                25);

            var repeated = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.TransientLocal,
                16 * 1024 * 1024,
                240,
                120);

            Assert.Same(first, repeated);
            Assert.Equal(1UL, repeated.SessionGeneration);
            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, repeated.DefaultProvider);
            Assert.Equal(FoxRunWireEncoding.Json, repeated.WebSocketSubscriptionEncoding);
            Assert.Equal(FoxRunRos2QosPreset.Reliable, repeated.DefaultRos2Qos);
            Assert.Equal(2 * 1024 * 1024, repeated.NativeCopyBudgetBytes);
            Assert.Equal(90, repeated.TransportAdmissionRateLimitHz);
            Assert.Equal(25, repeated.DefaultMainThreadApplyRateHz);
        }

        [Fact]
        public void EndIsIdempotentAndKeepsTheCurrentGeneration()
        {
            var state = new FoxRunSubscriptionSessionState();
            var active = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                4 * 1024 * 1024,
                120,
                60);

            var disabled = state.End();
            var repeated = state.End();

            Assert.False(disabled.SubscriptionsEnabled);
            Assert.Equal(active.SessionGeneration, disabled.SessionGeneration);
            Assert.Same(disabled, repeated);
        }

        [Fact]
        public void ReenableAdvancesTheSessionGeneration()
        {
            var state = new FoxRunSubscriptionSessionState();
            var first = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                4 * 1024 * 1024,
                120,
                60);
            state.End();

            var second = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.SensorData,
                8 * 1024 * 1024,
                90,
                30);

            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
        }

        [Fact]
        public void BeginningAfterMaxGenerationFailsClosedWithoutMutatingState()
        {
            var state = new FoxRunSubscriptionSessionState(ulong.MaxValue);
            var before = state.Current;

            Assert.Throws<InvalidOperationException>(() => state.BeginIfNeeded(
                    FoxRunSubscriptionProvider.FoxgloveWebSocket,
                    FoxRunWireEncoding.Protobuf,
                    FoxRunRos2QosPreset.Default,
                    4 * 1024 * 1024,
                    120,
                    60));

            Assert.Same(before, state.Current);
            Assert.False(state.Current.SubscriptionsEnabled);
            Assert.Equal(ulong.MaxValue, state.Current.SessionGeneration);
        }
    }
}
