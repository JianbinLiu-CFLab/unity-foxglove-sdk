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
        public void ActiveSnapshotCapturesIndependentMaximumAndDefaultSubscribeRates()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                FoxRunResolvedQos.SensorData,
                8 * 1024 * 1024,
                transportAdmissionRateLimitHz: 120,
                defaultSubscribeRateHz: 37);

            Assert.Equal(1UL, policy.SessionGeneration);
            Assert.True(policy.SubscriptionsEnabled);
            Assert.Equal(FoxRunEndpoint.Ros2Native, policy.DefaultSource);
            Assert.Equal(FoxRunEncoding.JSON, policy.FoxgloveEncoding);
            Assert.Equal(FoxRunResolvedQos.SensorData, policy.DefaultRos2Qos);
            Assert.Equal(8 * 1024 * 1024, policy.NativeCopyBudgetBytes);
            Assert.Equal(120, policy.TransportAdmissionRateLimitHz);
            Assert.Equal(37, policy.DefaultSubscribeRateHz);

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
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.Default,
                4 * 1024 * 1024,
                120,
                60);

            Assert.Equal(FoxRunEndpoint.Ros2Native, policy.DefaultSource);
            Assert.Equal(FoxRunEncoding.Protobuf, policy.FoxgloveEncoding);
        }

        [Fact]
        public void BeginRejectsAnUnspecifiedProfileSource()
        {
            var state = new FoxRunSubscriptionSessionState();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginIfNeeded(
                    (FoxRunEndpoint)0,
                    FoxRunEncoding.JSON,
                    FoxRunResolvedQos.Default,
                    0,
                    0,
                    0));
        }

        [Fact]
        public void BeginNormalizesResourceLimits()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.JSON,
                FoxRunResolvedQos.SystemDefault,
                0,
                0,
                0);

            Assert.Equal(FoxRunEndpoint.Foxglove, policy.DefaultSource);
            Assert.Equal(FoxRunResolvedQos.SystemDefault, policy.DefaultRos2Qos);
            Assert.Equal(4 * 1024 * 1024, policy.NativeCopyBudgetBytes);
            Assert.Equal(1, policy.TransportAdmissionRateLimitHz);
            Assert.Equal(1, policy.DefaultSubscribeRateHz);
        }

        [Fact]
        public void RepeatedBeginFreezesTheOriginalSnapshotAndGeneration()
        {
            var state = new FoxRunSubscriptionSessionState();
            var firstQos = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                3);
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.JSON,
                firstQos,
                2 * 1024 * 1024,
                90,
                25);

            var repeated = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.SensorData,
                16 * 1024 * 1024,
                240,
                120);

            Assert.Same(first, repeated);
            Assert.Equal(1UL, repeated.SessionGeneration);
            Assert.Equal(FoxRunEndpoint.Foxglove, repeated.DefaultSource);
            Assert.Equal(FoxRunEncoding.JSON, repeated.FoxgloveEncoding);
            Assert.Equal(firstQos, repeated.DefaultRos2Qos);
            Assert.Equal(2 * 1024 * 1024, repeated.NativeCopyBudgetBytes);
            Assert.Equal(90, repeated.TransportAdmissionRateLimitHz);
            Assert.Equal(25, repeated.DefaultSubscribeRateHz);
        }

        [Fact]
        public void EndIsIdempotentAndKeepsTheCurrentGeneration()
        {
            var state = new FoxRunSubscriptionSessionState();
            var active = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.Default,
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
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.Default,
                4 * 1024 * 1024,
                120,
                60);
            state.End();

            var second = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                FoxRunResolvedQos.SystemDefault,
                8 * 1024 * 1024,
                90,
                30);

            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
            Assert.Equal(FoxRunResolvedQos.SystemDefault, second.DefaultRos2Qos);
        }

        [Fact]
        public void BeginningAfterMaxGenerationFailsClosedWithoutMutatingState()
        {
            var state = new FoxRunSubscriptionSessionState(ulong.MaxValue);
            var before = state.Current;

            Assert.Throws<InvalidOperationException>(() => state.BeginIfNeeded(
                    FoxRunEndpoint.Foxglove,
                    FoxRunEncoding.Protobuf,
                    FoxRunResolvedQos.Default,
                    4 * 1024 * 1024,
                    120,
                    60));

            Assert.Same(before, state.Current);
            Assert.False(state.Current.SubscriptionsEnabled);
            Assert.Equal(ulong.MaxValue, state.Current.SessionGeneration);
        }
    }
}
