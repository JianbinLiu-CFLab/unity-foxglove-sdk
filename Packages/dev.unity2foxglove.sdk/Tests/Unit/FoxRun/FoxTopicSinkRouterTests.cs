// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxTopicSinkRouterTests
    {
        [Fact]
        public void FanoutDeliversToEverySinkInRegistrationOrder()
        {
            var router = new FoxTopicSinkRouter();
            var order = new List<string>();
            var a = new RecordingSink("a", order);
            var b = new RecordingSink("b", order);
            router.AddSink(a);
            router.AddSink(b);

            var contract = Exported("/telemetry");
            router.Register(contract);
            router.Publish(contract, 7UL, Bytes("{\"v\":1}"), "source-a");

            Assert.Equal(new[] { "a", "b" }, order);
            Assert.Single(a.Published);
            Assert.Equal("/telemetry", a.Published[0].Topic);
            Assert.Equal(7UL, a.Published[0].TimestampNs);
            Assert.Equal("{\"v\":1}", Encoding.UTF8.GetString(a.Published[0].Payload));
            Assert.Single(a.Registered);
        }

        [Fact]
        public void LocalOnlyContractIsNeverExported()
        {
            var router = new FoxTopicSinkRouter();
            var sink = new RecordingSink("a", new List<string>());
            router.AddSink(sink);

            var contract = new FoxTopicContract("/local", "", "json", "", "", FoxTopicVisibility.LocalOnly, FoxTopicWriterPolicy.SingleWriter);
            router.Register(contract);
            router.Publish(contract, 1UL, Bytes("{}"), "source-a");

            Assert.Empty(sink.Registered);
            Assert.Empty(sink.Published);
        }

        [Fact]
        public void FailingSinkIsIsolatedAndReportedOnce()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            var bad = new ThrowingSink("bad");
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(bad);
            router.AddSink(good);

            var contract = Exported("/telemetry");
            router.Register(contract);
            router.Publish(contract, 1UL, Bytes("{}"), "source-a");
            router.Publish(contract, 2UL, Bytes("{}"), "source-a");

            Assert.Equal(2, good.Published.Count);
            Assert.Single(faults);
            Assert.Equal("bad", faults[0].SinkName);
            Assert.Equal("/telemetry", faults[0].Topic);
            Assert.Equal("publish", faults[0].Operation);
        }

        [Fact]
        public void FailingRegisterSinkIsIsolatedAndReportedOnce()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            var bad = new ThrowingRegisterSink("bad");
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(bad);
            router.AddSink(good);

            var contract = Exported("/telemetry");
            router.Register(contract);
            router.Register(contract);

            Assert.Equal(2, good.Registered.Count);
            Assert.Single(faults);
            Assert.Equal("bad", faults[0].SinkName);
            Assert.Equal("/telemetry", faults[0].Topic);
            Assert.Equal("register", faults[0].Operation);
        }

        [Fact]
        public void AddSinkWithThrowingRegisterIsolatesAndContinues()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            var contract = Exported("/telemetry");
            router.Register(contract);

            var bad = new ThrowingRegisterSink("bad");
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(bad);
            router.AddSink(good);

            Assert.Single(good.Registered);
            Assert.Single(faults);
            Assert.Equal("bad", faults[0].SinkName);
            Assert.Equal("register", faults[0].Operation);
        }

        [Fact]
        public void EverySinkSharesOneSerializedPayloadBuffer()
        {
            var router = new FoxTopicSinkRouter();
            var a = new RecordingSink("a", new List<string>());
            var b = new RecordingSink("b", new List<string>());
            router.AddSink(a);
            router.AddSink(b);

            var contract = Exported("/telemetry");
            router.Register(contract);
            var payload = Bytes("{\"v\":1}");
            router.Publish(contract, 1UL, payload, "source-a");

            Assert.Same(payload, a.Published[0].Payload);
            Assert.Same(payload, b.Published[0].Payload);
        }

        [Fact]
        public void RemovedSinkStopsReceivingPayloads()
        {
            var router = new FoxTopicSinkRouter();
            var sink = new RecordingSink("a", new List<string>());
            router.AddSink(sink);
            Assert.True(router.RemoveSink(sink));

            router.Publish(Exported("/telemetry"), 1UL, Bytes("{}"), "source-a");

            Assert.Empty(sink.Published);
            Assert.False(router.HasSinks);
        }

        [Fact]
        public void UnregisteredContractIsNotReplayedToLaterSinks()
        {
            var router = new FoxTopicSinkRouter();
            var first = Exported("/telemetry");
            router.Register(first);

            Assert.True(router.Unregister(first.Topic));

            var late = new RecordingSink("late", new List<string>());
            router.AddSink(late);

            Assert.Empty(late.Registered);
        }

        [Fact]
        public void UnregisteredContractCannotPublishWithStaleReference()
        {
            var router = new FoxTopicSinkRouter();
            var sink = new RecordingSink("a", new List<string>());
            router.AddSink(sink);
            var contract = Exported("/telemetry");
            router.Register(contract);

            Assert.True(router.Unregister(contract.Topic));
            router.Publish(contract, 1UL, Bytes("{}"), "source-a");

            Assert.Empty(sink.Published);
        }

        [Fact]
        public void UnregisterNotifiesEveryOptionalLifecycleSinkAndIsolatesFailure()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            var bad = new ThrowingUnregisterSink("bad");
            var good = new LifecycleRecordingSink("good");
            router.AddSink(bad);
            router.AddSink(good);
            var contract = Exported("/telemetry");
            router.Register(contract);

            Assert.True(router.Unregister(contract.Topic));

            Assert.Equal(1, bad.UnregisterCalls);
            Assert.Equal(1, good.UnregisterCalls);
            Assert.Single(faults);
            Assert.Equal("bad", faults[0].SinkName);
            Assert.Equal("/telemetry", faults[0].Topic);
            Assert.Equal("unregister", faults[0].Operation);
        }

        [Fact]
        public void DisposeDisposesEverySinkAndClearsRouter()
        {
            var router = new FoxTopicSinkRouter();
            var a = new RecordingSink("a", new List<string>());
            var b = new RecordingSink("b", new List<string>());
            router.AddSink(a);
            router.AddSink(b);

            router.Dispose();

            Assert.True(a.Disposed);
            Assert.True(b.Disposed);
            Assert.False(router.HasSinks);
        }

        [Fact]
        public void DisposePreventsReusingRouter()
        {
            var router = new FoxTopicSinkRouter();
            router.Dispose();

            Assert.Throws<ObjectDisposedException>(() => router.AddSink(new RecordingSink("late", new List<string>())));
            Assert.False(router.HasSinks);
            Assert.Equal(0, router.SinkCount);
        }

        private static FoxTopicContract Exported(string topic)
            => new FoxTopicContract(topic, "foxrun.Test", "json", "foxrun.Test", "abc123", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);

        private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

        private sealed class RecordingSink : IFoxTopicSink
        {
            private readonly List<string> _order;

            public RecordingSink(string name, List<string> order)
            {
                Name = name;
                _order = order;
            }

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public List<FoxTopicContract> Registered { get; } = new List<FoxTopicContract>();
            public List<(string Topic, ulong TimestampNs, byte[] Payload, string Origin)> Published { get; } = new();
            public bool Disposed { get; private set; }

            public void Register(FoxTopicContract contract) => Registered.Add(contract);

            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
            {
                _order.Add(Name);
                Published.Add((contract.Topic, timestampNs, payload, origin));
            }

            public void Flush() { }
            public void Dispose() => Disposed = true;
        }

        private sealed class ThrowingSink : IFoxTopicSink
        {
            public ThrowingSink(string name) => Name = name;

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;

            public void Register(FoxTopicContract contract) { }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
                => throw new InvalidOperationException("boom");
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class ThrowingRegisterSink : IFoxTopicSink
        {
            public ThrowingRegisterSink(string name) => Name = name;

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;

            public void Register(FoxTopicContract contract)
                => throw new InvalidOperationException("register boom");
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class LifecycleRecordingSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public LifecycleRecordingSink(string name) => Name = name;

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public int UnregisterCalls { get; private set; }

            public void Register(FoxTopicContract contract) { }
            public void Unregister(string topic) => UnregisterCalls++;
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class ThrowingUnregisterSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public ThrowingUnregisterSink(string name) => Name = name;

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public int UnregisterCalls { get; private set; }

            public void Register(FoxTopicContract contract) { }
            public void Unregister(string topic)
            {
                UnregisterCalls++;
                throw new InvalidOperationException("unregister boom");
            }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }
    }
}
