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
        private static readonly FoxRunTransportId Provider =
            new FoxRunTransportId("unity2foxglove.test");
        private static readonly FoxRunDeliveryPolicy Delivery =
            new FoxRunDeliveryPolicy(
                FoxRunDeliveryReliability.Reliable,
                FoxRunDeliveryDurability.Volatile,
                FoxRunDeliveryHistory.KeepLast,
                7);

        [Fact]
        public void InitialStateExposesANonNullDisabledSnapshot()
        {
            var state = new FoxRunSubscriptionSessionState();

            Assert.False(state.Current.SubscriptionsEnabled);
            Assert.Equal(0UL, state.Current.SessionGeneration);
            Assert.Equal(
                FoxgloveWebSocketTransport.TransportId,
                state.Current.DefaultProvider);
        }

        [Fact]
        public void ActiveSnapshotCapturesProviderEncodingDeliveryAndBounds()
        {
            var state = new FoxRunSubscriptionSessionState();

            var policy = state.BeginIfNeeded(
                Provider,
                FoxRunEncoding.MessagePack,
                Delivery,
                transportAdmissionRateLimitHz: 120,
                defaultSubscribeRateHz: 37,
                maxPayloadBytes: 8 * 1024 * 1024);

            Assert.True(policy.SubscriptionsEnabled);
            Assert.Equal(Provider, policy.DefaultProvider);
            Assert.Equal(FoxRunEncoding.MessagePack, policy.WebSocketEncoding);
            Assert.Equal(Delivery, policy.DefaultDeliveryPolicy);
            Assert.Equal(120, policy.TransportAdmissionRateLimitHz);
            Assert.Equal(37, policy.DefaultSubscribeRateHz);
            Assert.Equal(8 * 1024 * 1024, policy.MaxPayloadBytes);
            Assert.All(
                typeof(FoxRunSubscriptionSessionPolicy).GetProperties(
                    BindingFlags.Instance | BindingFlags.Public),
                property => Assert.False(property.CanWrite));
        }

        [Fact]
        public void RepeatedBeginFreezesThenEndRecapturesAndNormalizesBounds()
        {
            var state = new FoxRunSubscriptionSessionState();
            var first = state.BeginIfNeeded(
                Provider,
                FoxRunEncoding.Protobuf,
                Delivery,
                60,
                10,
                4096);
            var repeated = state.BeginIfNeeded(
                FoxgloveWebSocketTransport.TransportId,
                FoxRunEncoding.JSON,
                FoxRunDeliveryPolicy.ProviderDefault,
                1,
                1,
                1);
            Assert.Same(first, repeated);

            state.End();
            var second = state.BeginIfNeeded(
                FoxgloveWebSocketTransport.TransportId,
                FoxRunEncoding.JSON,
                FoxRunDeliveryPolicy.ProviderDefault,
                0,
                0,
                0);

            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
            Assert.Equal(1, second.TransportAdmissionRateLimitHz);
            Assert.Equal(1, second.DefaultSubscribeRateHz);
            Assert.Equal(1, second.MaxPayloadBytes);
        }

        [Fact]
        public void BeginningAfterMaxGenerationFailsClosed()
        {
            var state = new FoxRunSubscriptionSessionState(ulong.MaxValue);

            Assert.Throws<InvalidOperationException>(() =>
                state.BeginIfNeeded(
                    Provider,
                    FoxRunEncoding.Protobuf,
                    Delivery,
                    60,
                    10));
            Assert.False(state.Current.SubscriptionsEnabled);
            Assert.Equal(ulong.MaxValue, state.Current.SessionGeneration);
        }
    }
}
