// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxTopicBusTests
    {
        [Fact]
        public void RecoverableFaultObserverDoesNotBlockLaterTransportSubscriberOrFaultObserver()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/native/fault-observer");
            var transportCalls = 0;
            var reportedFaults = 0;
            bus.SubscriberFaulted += _ => throw new InvalidOperationException("diagnostic observer failed");
            bus.SubscriberFaulted += _ => reportedFaults++;
            bus.Subscribe<int>(
                contract.Topic,
                _ => throw new InvalidOperationException("payload observer failed"));
            bus.SubscribeResult<int>(
                contract.Topic,
                "source",
                _ =>
                {
                    transportCalls++;
                    return true;
                });
            var payload = 184;

            bus.PublishToObservers(
                contract,
                184UL,
                in payload,
                "source");
            var result = bus.PublishToResultSubscribers(
                contract,
                184UL,
                in payload,
                "source");

            Assert.Equal(1, result.Matched);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.True(result.AllSucceeded);
            Assert.Equal(1, transportCalls);
            Assert.Equal(1, reportedFaults);
        }

        [Fact]
        public void FatalFaultObserverPropagates()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/native/fatal-fault-observer");
            bus.SubscriberFaulted += _ => throw new OutOfMemoryException("fatal diagnostic");
            bus.Subscribe<int>(
                contract.Topic,
                _ => throw new InvalidOperationException("payload observer failed"));
            var payload = 184;

            Assert.Throws<OutOfMemoryException>(
                () => bus.Publish(contract, 184UL, in payload, "source"));
        }

        [Fact]
        public void ResultReadinessRequiresExactPayloadType()
        {
            var bus = new FoxTopicBus();
            bus.SubscribeResult<string>(
                "/native/typed",
                "source-a",
                _ => true);

            Assert.True(bus.HasResultSubscribers<string>(
                "/native/typed",
                "source-a"));
            Assert.False(bus.HasResultSubscribers<string>(
                "/native/typed",
                "source-b"));
            Assert.False(bus.HasResultSubscribers<int>(
                "/native/typed",
                "source-a"));
        }

        [Fact]
        public void ResultSubscribersAreIsolatedByExactOrigin()
        {
            var bus = new FoxTopicBus();
            var contract = new FoxTopicContract(
                "/native/multi",
                "foxrun.Test",
                "json",
                "foxrun.Test",
                "abc123",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.MultiWriter);
            var firstCalls = 0;
            var secondCalls = 0;
            Assert.True(bus.Register(contract, "source-a").Accepted);
            Assert.True(bus.Register(contract, "source-b").Accepted);
            bus.SubscribeResult<int>(
                contract.Topic,
                "source-a",
                _ =>
                {
                    firstCalls++;
                    return true;
                });
            bus.SubscribeResult<int>(
                contract.Topic,
                "source-b",
                _ =>
                {
                    secondCalls++;
                    return true;
                });
            var payload = 184;

            var first = bus.PublishToResultSubscribers(
                contract,
                1UL,
                in payload,
                "source-a");
            var second = bus.PublishToResultSubscribers(
                contract,
                2UL,
                in payload,
                "source-b");

            Assert.Equal(1, first.Matched);
            Assert.Equal(1, second.Matched);
            Assert.Equal(1, firstCalls);
            Assert.Equal(1, secondCalls);
        }

        private static FoxTopicContract Contract(string topic)
            => new FoxTopicContract(topic, "foxrun.Test", "json", "foxrun.Test", "abc123", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);
    }
}
