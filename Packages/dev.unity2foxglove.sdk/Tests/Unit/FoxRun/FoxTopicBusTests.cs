// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxTopicBusTests
    {
        [Fact]
        public void ContractNormalizesNullInputsAndRejectsMissingTopic()
        {
            Assert.Throws<ArgumentException>(() => new FoxTopicContract(null, "", "json", "", "", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter));

            var contract = new FoxTopicContract("/pose", null, null, null, null, FoxTopicVisibility.LocalOnly, FoxTopicWriterPolicy.SingleWriter);

            Assert.Equal("/pose", contract.Topic);
            Assert.Equal(string.Empty, contract.SchemaName);
            Assert.Equal("json", contract.Encoding);
            Assert.Equal(string.Empty, contract.CanonicalType);
            Assert.Equal(string.Empty, contract.StableFingerprint);
            Assert.Equal(FoxTopicVisibility.LocalOnly, contract.Visibility);
            Assert.Equal(FoxTopicWriterPolicy.SingleWriter, contract.WriterPolicy);
        }

        [Fact]
        public void BusDispatchesTypedPayloadWithoutObjectEnvelope()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/scalar");
            var received = new List<int>();

            bus.Register(contract, "source-a");
            bus.Subscribe<int>("/scalar", envelope => received.Add(envelope.Payload));
            var payload = 42;

            bus.Publish(contract, 123UL, in payload, "source-a");

            Assert.Equal(new[] { 42 }, received);
        }

        [Fact]
        public void SingleWriterRejectsSecondOriginAndKeepsFirstActive()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/pose");

            var first = bus.Register(contract, "source-a");
            var second = bus.Register(contract, "source-b");

            Assert.True(first.Accepted);
            Assert.False(second.Accepted);
            Assert.Contains("single writer", second.Diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("source-a", bus.GetRegisteredOrigin("/pose"));
        }

        [Fact]
        public void SameOriginCannotReplaceItsAcceptedContract()
        {
            var bus = new FoxTopicBus();
            var accepted = Contract("/pose");
            var conflicting = new FoxTopicContract(
                accepted.Topic,
                "foxrun.Other",
                "json",
                "foxrun.Other",
                "other",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(bus.Register(accepted, "source-a").Accepted);
            var result = bus.Register(conflicting, "source-a");

            Assert.False(result.Accepted);
            Assert.True(bus.IsRegistered(accepted, "source-a"));
            Assert.False(bus.IsRegistered(conflicting, "source-a"));
        }

        [Fact]
        public void UnregisterReleasesSingleWriterTopicForReplacementOrigin()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/pose");

            Assert.True(bus.Register(contract, "source-a").Accepted);

            Assert.True(bus.Unregister("/pose", "source-a"));
            var replacement = bus.Register(contract, "source-b");

            Assert.True(replacement.Accepted);
            Assert.Equal("source-b", bus.GetRegisteredOrigin("/pose"));
        }

        [Fact]
        public void MultiWriterPolicyAcceptsMultipleOrigins()
        {
            var bus = new FoxTopicBus();
            var contract = new FoxTopicContract("/multi", "", "json", "", "", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.MultiWriter);

            Assert.True(bus.Register(contract, "source-a").Accepted);
            Assert.True(bus.Register(contract, "source-b").Accepted);
        }

        [Fact]
        public void MultiWriterRejectsMismatchedContract()
        {
            var bus = new FoxTopicBus();
            var first = new FoxTopicContract("/multi", "foxrun.A", "json", "foxrun.A", "a", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.MultiWriter);
            var second = new FoxTopicContract("/multi", "foxrun.B", "json", "foxrun.B", "b", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.MultiWriter);

            Assert.True(bus.Register(first, "source-a").Accepted);
            var result = bus.Register(second, "source-b");

            Assert.False(result.Accepted);
            Assert.Contains("contract", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SubscriberExceptionIsBoundedAndDoesNotStopRemainingSubscribers()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/health");
            var received = 0;
            var faults = new List<FoxTopicSubscriberFault>();

            bus.Register(contract, "source-a");
            bus.SubscriberFaulted += fault => faults.Add(fault);
            bus.Subscribe<int>("/health", _ => throw new InvalidOperationException("boom"));
            bus.Subscribe<int>("/health", _ => received++);

            var payload = 1;
            bus.Publish(contract, 1UL, in payload, "source-a");
            bus.Publish(contract, 2UL, in payload, "source-a");

            Assert.Equal(2, received);
            Assert.Single(faults);
            Assert.Equal("/health", faults[0].Topic);
        }

        [Fact]
        public void UnsubscribeRemovesMatchingTypedCallbackOnly()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/health");
            var received = 0;
            Action<FoxTopicEnvelope<int>> callback = _ => received++;

            bus.Subscribe("/health", callback);
            Assert.True(bus.HasSubscribers("/health"));

            var payload = 1;
            bus.Publish(contract, 1UL, in payload, "source-a");
            Assert.True(bus.Unsubscribe("/health", callback));
            bus.Publish(contract, 2UL, in payload, "source-a");

            Assert.Equal(1, received);
            Assert.False(bus.HasSubscribers("/health"));
        }

        [Fact]
        public void TypeMismatchedSubscriberIsReportedInsteadOfSilentlySkipped()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/health");
            var faults = new List<FoxTopicSubscriberFault>();
            bus.SubscriberFaulted += fault => faults.Add(fault);

            bus.Subscribe<string>("/health", _ => { });
            var payload = 1;

            bus.Publish(contract, 1UL, in payload, "source-a");
            bus.Publish(contract, 2UL, in payload, "source-a");

            Assert.Single(faults);
            Assert.Equal("/health", faults[0].Topic);
            Assert.Contains("incompatible subscriber type", faults[0].Exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void UnsubscribeClearsReportedFaultForNewSubscription()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/health");
            var faults = new List<FoxTopicSubscriberFault>();
            Action<FoxTopicEnvelope<int>> throwing = _ => throw new InvalidOperationException("boom");
            bus.SubscriberFaulted += fault => faults.Add(fault);

            bus.Subscribe("/health", throwing);
            var payload = 1;
            bus.Publish(contract, 1UL, in payload, "source-a");
            Assert.True(bus.Unsubscribe("/health", throwing));
            bus.Subscribe("/health", throwing);
            bus.Publish(contract, 2UL, in payload, "source-a");

            Assert.Equal(2, faults.Count);
        }

        [Fact]
        public void TransportResultsAndOrdinaryObserversUseIndependentPublishPaths()
        {
            var bus = new FoxTopicBus();
            var contract = Contract("/native/result");
            var observerCalls = 0;
            var transportCalls = 0;
            var faults = new List<FoxTopicSubscriberFault>();
            bus.SubscriberFaulted += faults.Add;
            bus.Subscribe<int>(
                contract.Topic,
                _ =>
                {
                    observerCalls++;
                    throw new InvalidOperationException("observer failure");
                });
            bus.Subscribe<string>(contract.Topic, _ => { });
            bus.SubscribeResult<int>(
                contract.Topic,
                "source",
                _ =>
                {
                    transportCalls++;
                    return true;
                });
            var payload = 7;

            var result = bus.PublishToResultSubscribers(
                contract,
                184UL,
                in payload,
                "source",
                9UL);

            Assert.Equal(1, result.Matched);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.True(result.AllSucceeded);
            Assert.Equal(0, observerCalls);
            Assert.Equal(1, transportCalls);

            bus.PublishToObservers(
                contract,
                184UL,
                in payload,
                "source",
                9UL);

            Assert.Equal(1, observerCalls);
            Assert.Equal(1, transportCalls);
            Assert.Equal(2, faults.Count);
            Assert.Contains(
                faults,
                fault => fault.Exception.Message.Contains(
                    "observer failure",
                    StringComparison.Ordinal));
            Assert.Contains(
                faults,
                fault => fault.Exception.Message.Contains(
                    "incompatible subscriber type",
                    StringComparison.Ordinal));
        }

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
