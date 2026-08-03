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
        public void RecoverableFaultObserverFailureDoesNotBlockHealthySink()
        {
            var router = new FoxTopicSinkRouter();
            router.SinkFaulted += _ =>
                throw new InvalidOperationException("diagnostic failed");
            var bad = new ThrowingSink("bad");
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(bad);
            router.AddSink(good);
            var contract = Exported("/diagnostic-isolation");
            router.Register(contract);

            router.Publish(contract, 1UL, Bytes("{}"), "source-a");

            Assert.Single(good.Published);
        }

        [Fact]
        public void RecoverableFaultObserverFailureDoesNotBlockLaterObserver()
        {
            var router = new FoxTopicSinkRouter();
            var observed = new List<FoxTopicSinkFault>();
            router.SinkFaulted += _ =>
                throw new InvalidOperationException("diagnostic failed");
            router.SinkFaulted += fault => observed.Add(fault);
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(new ThrowingSink("bad"));
            router.AddSink(good);
            var contract = Exported("/diagnostic-observer-isolation");
            router.Register(contract);

            router.Publish(contract, 1UL, Bytes("{}"), "source-a");

            Assert.Single(observed);
            Assert.Single(good.Published);
        }

        [Fact]
        public void FatalFaultObserverFailurePassesThroughRouter()
        {
            var router = new FoxTopicSinkRouter();
            router.SinkFaulted += _ =>
                throw new OutOfMemoryException("fatal diagnostic");
            router.AddSink(new ThrowingSink("bad"));
            var contract = Exported("/diagnostic-fatal");
            router.Register(contract);

            Assert.Throws<OutOfMemoryException>(() =>
                router.Publish(contract, 1UL, Bytes("{}"), "source-a"));
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

            Assert.Single(good.Registered);
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
        public void FatalAddSinkReplayRollsBackEveryAttemptAndNeverAttachesSink()
        {
            var router = new FoxTopicSinkRouter();
            var first = Exported("/phase184/add-sink/first");
            var fatal = Exported("/phase184/add-sink/fatal");
            router.Register(first);
            router.Register(fatal);
            var sink = new FatalReplaySink(fatal.Topic);

            var thrown = Assert.Throws<OutOfMemoryException>(() =>
                router.AddSink(sink));

            Assert.Equal("fatal replay register", thrown.Message);
            Assert.Equal(
                new[] { first.Topic, fatal.Topic },
                sink.RegisteredTopics);
            Assert.Contains(first.Topic, sink.UnregisteredTopics);
            Assert.Contains(fatal.Topic, sink.UnregisteredTopics);
            Assert.False(router.HasSinks);
            Assert.Equal(0, router.SinkCount);
            Assert.False(router.RemoveSink(sink));
            router.Publish(first, 1UL, Bytes("{}"), "owner");
            Assert.Equal(0, sink.PublishCalls);
        }

        [Fact]
        public void RemoveSinkUnregistersOwnedContractsBeforeDetaching()
        {
            var router = new FoxTopicSinkRouter();
            var sink = new LifecycleRecordingSink("lifecycle");
            router.AddSink(sink);
            router.Register(Exported("/phase184/remove-sink/first"));
            router.Register(Exported("/phase184/remove-sink/second"));

            Assert.True(router.RemoveSink(sink));

            Assert.Equal(2, sink.UnregisterCalls);
            Assert.False(router.HasSinks);
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
        [Trait("Phase", "185-B")]
        public void MessagePackSinkFanoutBorrowsOneArrayAndRetainersMustCopyPastCapture()
        {
            var router = new FoxTopicSinkRouter();
            var healthy = new RecordingSink("healthy", new List<string>());
            router.AddSink(new ThrowingSink("failing"));
            router.AddSink(healthy);
            var contract = new FoxTopicContract(
                "/phase185/msgpack",
                "Demo.MessagePack",
                "msgpack",
                "",
                "phase185-msgpack-v1",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            router.Register(contract);
            var borrowed = new byte[] { 0x81, 0xa1, 0x76, 0x01 };

            router.PublishCompatible(
                contract,
                FoxRunEncoding.MessagePack,
                185UL,
                borrowed,
                "phase185-source");

            var received = Assert.Single(healthy.Published).Payload;
            var wireContract = Assert.Single(healthy.PublishedContracts);
            Assert.Same(
                Assert.Single(healthy.Registered),
                wireContract);
            Assert.Same(borrowed, received);
            Assert.Equal("/phase185/msgpack", wireContract.Topic);
            Assert.Equal("msgpack", wireContract.Encoding);
            Assert.Equal(string.Empty, wireContract.SchemaName);
            Assert.Equal(contract.CanonicalType, wireContract.CanonicalType);
            Assert.Equal(contract.StableFingerprint, wireContract.StableFingerprint);
            var retainedCopy = (byte[])received.Clone();
            borrowed[3] = 0x02;
            Assert.Equal(0x02, received[3]);
            Assert.Equal(0x01, retainedCopy[3]);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ProtobufLogicalContractRegistersAndPublishesOneJsonWireView()
        {
            var router = new FoxTopicSinkRouter();
            var initial =
                new RecordingSink("initial", new List<string>());
            router.AddSink(initial);
            var logical = new FoxTopicContract(
                "/phase185f/protobuf-json-view",
                "Demo.Payload",
                "protobuf",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(router.Register(logical));
            var late =
                new RecordingSink("late", new List<string>());
            router.AddSink(late);
            router.Publish(
                logical,
                1UL,
                Bytes("{}"),
                "owner");

            var registered = Assert.Single(initial.Registered);
            Assert.Equal("json", registered.Encoding);
            Assert.Same(
                registered,
                Assert.Single(late.Registered));
            Assert.Same(
                registered,
                Assert.Single(initial.PublishedContracts));
            Assert.Same(
                registered,
                Assert.Single(late.PublishedContracts));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedMessagePackRegistersOneWireViewForInitialAndLateSinks()
        {
            var router = new FoxTopicSinkRouter();
            var initial =
                new RecordingSink("initial", new List<string>());
            router.AddSink(initial);
            var logical = new FoxTopicContract(
                "/phase185f/inherited-msgpack",
                "Demo.Payload",
                "inherit",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(
                router.Register(
                    logical,
                    FoxRunEncoding.MessagePack));
            var late =
                new RecordingSink("late", new List<string>());
            router.AddSink(late);
            router.PublishCompatible(
                logical,
                FoxRunEncoding.MessagePack,
                2UL,
                new byte[] { 0x80 },
                "owner");

            var registered = Assert.Single(initial.Registered);
            Assert.Equal("msgpack", registered.Encoding);
            Assert.Equal(string.Empty, registered.SchemaName);
            Assert.Same(
                registered,
                Assert.Single(late.Registered));
            Assert.Same(
                registered,
                Assert.Single(initial.PublishedContracts));
            Assert.Same(
                registered,
                Assert.Single(late.PublishedContracts));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void RegisteredWireEncodingMismatchFailsClosedWithoutDelivery()
        {
            var router = new FoxTopicSinkRouter();
            var sink =
                new RecordingSink("sink", new List<string>());
            router.AddSink(sink);
            var logical = new FoxTopicContract(
                "/phase185f/wire-mismatch",
                "Demo.Payload",
                "inherit",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(
                router.Register(
                    logical,
                    FoxRunEncoding.MessagePack));
            Assert.False(
                router.Register(
                    logical,
                    FoxRunEncoding.JSON));
            Assert.Throws<InvalidOperationException>(
                () => router.PublishCompatible(
                    logical,
                    FoxRunEncoding.JSON,
                    3UL,
                    Bytes("{}"),
                    "owner"));
            Assert.Empty(sink.Published);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void FreshEquivalentLogicalContractUsesCanonicalRegisteredWireView()
        {
            var router = new FoxTopicSinkRouter();
            var sink =
                new RecordingSink("sink", new List<string>());
            router.AddSink(sink);
            var first = new FoxTopicContract(
                "/phase185f/equivalent",
                "Demo.Payload",
                "protobuf",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            var equivalent = new FoxTopicContract(
                first.Topic,
                first.SchemaName,
                first.Encoding,
                first.CanonicalType,
                first.StableFingerprint,
                first.Visibility,
                first.WriterPolicy);

            Assert.True(router.Register(first));
            router.PublishCompatible(
                equivalent,
                FoxRunEncoding.JSON,
                4UL,
                Bytes("{}"),
                "owner");

            Assert.Same(
                Assert.Single(sink.Registered),
                Assert.Single(sink.PublishedContracts));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ThrowingDiagnosticGettersDoNotInterruptAdditiveFanout()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            router.AddSink(new ThrowingDiagnosticGetterSink());
            var healthy =
                new RecordingSink("healthy", new List<string>());
            router.AddSink(healthy);
            var contract =
                Exported("/phase185f/diagnostic-getters");

            Assert.True(router.Register(contract));
            router.Publish(
                contract,
                5UL,
                Bytes("{}"),
                "owner");

            Assert.Single(healthy.Published);
            var fault = Assert.Single(faults);
            Assert.Contains(
                nameof(ThrowingDiagnosticGetterSink),
                fault.SinkName,
                StringComparison.Ordinal);
            Assert.Equal("publish", fault.Operation);
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
        public void FatalUnregisterStillCleansLaterSinksAndRemovesRoute()
        {
            var router = new FoxTopicSinkRouter();
            var fatal = new FatalUnregisterSink("fatal");
            var good = new LifecycleRecordingSink("good");
            router.AddSink(fatal);
            router.AddSink(good);
            var contract = Exported("/phase184/fatal-unregister");
            router.Register(contract);

            Assert.Throws<OutOfMemoryException>(() =>
                router.Unregister(contract.Topic));

            Assert.Equal(1, fatal.UnregisterCalls);
            Assert.Equal(1, good.UnregisterCalls);
            var late = new RecordingSink("late", new List<string>());
            router.AddSink(late);
            Assert.Empty(late.Registered);
        }

        [Fact]
        public void FatalRegisterRollsBackRouteAndAttemptedLifecycleSinks()
        {
            var router = new FoxTopicSinkRouter();
            var good = new LifecycleRecordingSink("good");
            var fatal = new FatalRegisterSink("fatal");
            router.AddSink(good);
            router.AddSink(fatal);
            var contract = Exported("/phase184/fatal-register");

            var failure = Assert.Throws<OutOfMemoryException>(() =>
                router.Register(contract));

            Assert.Equal("fatal register", failure.Message);
            Assert.Equal(1, good.UnregisterCalls);
            Assert.Equal(1, fatal.UnregisterCalls);
            Assert.True(router.RemoveSink(fatal));
            Assert.True(router.Register(contract));
            Assert.Equal(2, good.RegisterCalls);
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
        public void FatalDisposeStillDisposesLaterSinksAndClearsRouter()
        {
            var router = new FoxTopicSinkRouter();
            var fatal = new FatalDisposeSink("fatal");
            var good = new RecordingSink("good", new List<string>());
            router.AddSink(fatal);
            router.AddSink(good);

            Assert.Throws<OutOfMemoryException>(() => router.Dispose());

            Assert.True(good.Disposed);
            Assert.Equal(0, router.SinkCount);
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

        private static FoxTopicContract Exported(
            string topic,
            FoxTopicWriterPolicy writerPolicy =
                FoxTopicWriterPolicy.SingleWriter)
            => new FoxTopicContract(
                topic,
                "foxrun.Test",
                "json",
                "foxrun.Test",
                "abc123",
                FoxTopicVisibility.Exported,
                writerPolicy);

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
            public List<FoxTopicContract> PublishedContracts { get; } =
                new List<FoxTopicContract>();
            public List<(string Topic, ulong TimestampNs, byte[] Payload, string Origin)> Published { get; } = new();
            public bool Disposed { get; private set; }

            public void Register(FoxTopicContract contract) => Registered.Add(contract);

            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
            {
                _order.Add(Name);
                PublishedContracts.Add(contract);
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

        private sealed class ThrowingDiagnosticGetterSink : IFoxTopicSink
        {
            public string Name =>
                throw new InvalidOperationException(
                    "name getter failed");

            public FoxTopicSinkCapabilities Capabilities =>
                throw new InvalidOperationException(
                    "capabilities getter must not be consulted");

            public void Register(FoxTopicContract contract) { }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
                => throw new InvalidOperationException(
                    "publish failed");

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
            public int RegisterCalls { get; private set; }
            public int UnregisterCalls { get; private set; }

            public void Register(FoxTopicContract contract) => RegisterCalls++;
            public void Unregister(string topic) => UnregisterCalls++;
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalUnregisterSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public FatalUnregisterSink(string name) => Name = name;
            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public int UnregisterCalls { get; private set; }
            public void Register(FoxTopicContract contract) { }
            public void Unregister(string topic)
            {
                UnregisterCalls++;
                throw new OutOfMemoryException("fatal unregister");
            }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalRegisterSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public FatalRegisterSink(string name) => Name = name;
            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public int UnregisterCalls { get; private set; }
            public void Register(FoxTopicContract contract)
                => throw new OutOfMemoryException("fatal register");
            public void Unregister(string topic) => UnregisterCalls++;
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalReplaySink :
            IFoxTopicSink,
            IFoxTopicSinkContractLifecycle
        {
            private readonly string _fatalTopic;

            public FatalReplaySink(string fatalTopic)
            {
                _fatalTopic = fatalTopic;
            }

            public string Name => "fatal-replay";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.Test;
            public List<string> RegisteredTopics { get; } = new List<string>();
            public List<string> UnregisteredTopics { get; } = new List<string>();
            public int PublishCalls { get; private set; }

            public void Register(FoxTopicContract contract)
            {
                RegisteredTopics.Add(contract.Topic);
                if (string.Equals(
                        contract.Topic,
                        _fatalTopic,
                        StringComparison.Ordinal))
                {
                    throw new OutOfMemoryException(
                        "fatal replay register");
                }
            }

            public void Unregister(string topic)
                => UnregisteredTopics.Add(topic);

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
                => PublishCalls++;

            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalDisposeSink : IFoxTopicSink
        {
            public FatalDisposeSink(string name) => Name = name;
            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;
            public void Register(FoxTopicContract contract) { }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() => throw new OutOfMemoryException("fatal dispose");
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
