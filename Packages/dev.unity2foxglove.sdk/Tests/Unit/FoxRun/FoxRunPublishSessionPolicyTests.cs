// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunPublishSessionPolicyTests
    {
        private static readonly FoxRunTransportId WebSocket =
            FoxgloveWebSocketTransport.TransportId;
        private static readonly FoxRunTransportId Provider =
            new FoxRunTransportId("unity2foxglove.test");
        private static readonly FoxRunDeliveryPolicy Delivery =
            new FoxRunDeliveryPolicy(
                FoxRunDeliveryReliability.BestEffort,
                FoxRunDeliveryDurability.TransientLocal,
                FoxRunDeliveryHistory.KeepAll,
                0);

        [Fact]
        public void InitialStateIsAnInertNonNullSnapshot()
        {
            var state = new FoxRunPublishSessionState();

            Assert.False(state.Current.SessionActive);
            Assert.Equal(0UL, state.Current.SessionGeneration);
            Assert.Empty(state.Current.PublishTransportIds);
            Assert.Equal(
                FoxRunDeliveryPolicy.ProviderDefault,
                state.Current.DefaultDeliveryPolicy);
        }

        [Fact]
        public void BeginCapturesCanonicalProviderOrderAndDirectionalDefaults()
        {
            var state = new FoxRunPublishSessionState();

            var policy = state.BeginIfNeeded(
                new[] { Provider, WebSocket },
                FoxRunEncoding.MessagePack,
                defaultPublishRateHz: 25f,
                Delivery);

            Assert.True(policy.SessionActive);
            Assert.Equal(1UL, policy.SessionGeneration);
            Assert.Equal(
                new[] { WebSocket, Provider },
                policy.PublishTransportIds);
            Assert.Equal(
                FoxRunEncoding.MessagePack,
                policy.WebSocketEncoding);
            Assert.Equal(25f, policy.DefaultPublishRateHz);
            Assert.Equal(Delivery, policy.DefaultDeliveryPolicy);
        }

        [Fact]
        public void RepeatedBeginFreezesUntilEndThenRecaptures()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                new[] { WebSocket },
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunDeliveryPolicy.ProviderDefault);

            var repeated = state.BeginIfNeeded(
                new[] { Provider },
                FoxRunEncoding.JSON,
                90f,
                Delivery);
            Assert.Same(first, repeated);

            state.End();
            var second = state.BeginIfNeeded(
                new[] { Provider },
                FoxRunEncoding.JSON,
                90f,
                Delivery);

            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
            Assert.Equal(new[] { Provider }, second.PublishTransportIds);
            Assert.Equal(FoxRunEncoding.JSON, second.WebSocketEncoding);
            Assert.Equal(Delivery, second.DefaultDeliveryPolicy);
        }

        [Fact]
        public void BeginningAfterMaxGenerationFailsClosed()
        {
            var state = new FoxRunPublishSessionState(ulong.MaxValue);

            Assert.Throws<InvalidOperationException>(() =>
                state.BeginIfNeeded(
                    new[] { WebSocket },
                    FoxRunEncoding.Protobuf,
                    10f,
                    FoxRunDeliveryPolicy.ProviderDefault));
            Assert.False(state.Current.SessionActive);
            Assert.Equal(ulong.MaxValue, state.Current.SessionGeneration);
        }
    }
}
