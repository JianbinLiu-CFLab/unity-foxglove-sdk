// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxTopicSinkRouterTests
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


    }
}
