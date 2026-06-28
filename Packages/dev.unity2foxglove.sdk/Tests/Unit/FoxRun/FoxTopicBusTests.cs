// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
            Assert.DoesNotContain("object Payload", TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicEnvelope.cs"), StringComparison.Ordinal);
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

        private static FoxTopicContract Contract(string topic)
            => new FoxTopicContract(topic, "foxrun.Test", "json", "foxrun.Test", "abc123", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);
    }
}
