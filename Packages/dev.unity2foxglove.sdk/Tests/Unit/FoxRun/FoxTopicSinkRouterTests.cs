// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
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
        public void TargetAwarePublishSkipsUnselectedSinksAndReturnsPerTargetFailure()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            var bridge = new TargetRecordingSink(
                "bridge",
                FoxRunEndpoint.Ros2Bridge,
                ready: true,
                succeeds: false);
            router.AddSink(native);
            router.AddSink(bridge);
            var contract = Exported("/phase184/target-aware");
            router.Register(contract);

            var result = router.PublishTarget(
                FoxRunEndpoint.Ros2Bridge,
                contract,
                41UL,
                Bytes("{}"),
                "source-a");

            Assert.True(result.HadReadySink);
            Assert.False(result.Succeeded);
            Assert.Equal(0, native.PublishCalls);
            Assert.Equal(1, bridge.PublishCalls);
        }

        [Fact]
        public void FreshButIdenticalGeneratedContractCanUseTheRegisteredTargetRoute()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(native);
            var registered = Exported("/phase184/fresh-contract");
            Assert.True(router.RegisterTargets(
                FoxRunEndpoint.Ros2Native,
                registered));

            var freshGeneratedInstance = Exported("/phase184/fresh-contract");
            var result = router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                freshGeneratedInstance,
                184UL,
                Bytes("{}"),
                "generated-source");

            Assert.True(result.HadReadySink);
            Assert.True(result.Succeeded);
            Assert.Equal(1, native.PublishCalls);
        }

        [Fact]
        public void ConflictingRegistrationCannotOverwriteTheAcceptedRoute()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            var bridge = new TargetRecordingSink(
                "bridge",
                FoxRunEndpoint.Ros2Bridge,
                ready: true,
                succeeds: true);
            router.AddSink(native);
            router.AddSink(bridge);
            var accepted = Exported("/phase184/route-conflict");
            var conflicting = new FoxTopicContract(
                accepted.Topic,
                "foxrun.Other",
                "json",
                "foxrun.Other",
                "different-fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(router.RegisterTargets(
                FoxRunEndpoint.Ros2Native,
                accepted));
            Assert.False(router.RegisterTargets(
                FoxRunEndpoint.Ros2Bridge,
                conflicting));

            Assert.True(router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                Exported(accepted.Topic),
                185UL,
                Bytes("{}"),
                "accepted").Succeeded);
            Assert.False(router.PublishTarget(
                FoxRunEndpoint.Ros2Bridge,
                conflicting,
                186UL,
                Bytes("{}"),
                "rejected").Succeeded);
            Assert.Equal(1, native.RegisterCalls);
            Assert.Equal(0, bridge.RegisterCalls);
        }

        [Fact]
        public void IdenticalMultiWritersShareOneStableSinkRoute()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(native);
            var first = Exported(
                "/phase184/multiwriter",
                FoxTopicWriterPolicy.MultiWriter);
            var second = Exported(
                "/phase184/multiwriter",
                FoxTopicWriterPolicy.MultiWriter);

            Assert.True(router.RegisterTargets(FoxRunEndpoint.Ros2Native, first));
            Assert.True(router.RegisterTargets(FoxRunEndpoint.Ros2Native, second));
            Assert.True(router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                second,
                187UL,
                Bytes("{}"),
                "writer-b").Succeeded);
            Assert.Equal(1, native.RegisterCalls);
            Assert.Equal(1, native.PublishCalls);
        }

        [Fact]
        public void RemovingOneIdenticalMultiWriterKeepsSharedSinkRouteAlive()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(native);
            var first = Exported(
                "/phase184/multiwriter-removal",
                FoxTopicWriterPolicy.MultiWriter);
            var second = Exported(
                "/phase184/multiwriter-removal",
                FoxTopicWriterPolicy.MultiWriter);
            Assert.True(router.RegisterTargets(FoxRunEndpoint.Ros2Native, first));
            Assert.True(router.RegisterTargets(FoxRunEndpoint.Ros2Native, second));

            Assert.True(router.Unregister(first.Topic));
            Assert.True(router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                second,
                188UL,
                Bytes("{}"),
                "writer-b").Succeeded);
            Assert.Equal(1, native.RegisterCalls);
            Assert.Equal(1, native.PublishCalls);

            Assert.True(router.Unregister(second.Topic));
            Assert.False(router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                second,
                189UL,
                Bytes("{}"),
                "writer-b").Succeeded);
        }

        [Fact]
        public void LegacyPublishHonorsBridgeOnlyResolvedTargetSelection()
        {
            var router = new FoxTopicSinkRouter();
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            var bridge = new TargetRecordingSink(
                "bridge",
                FoxRunEndpoint.Ros2Bridge,
                ready: true,
                succeeds: true);
            var legacyNative = new RecordingSink("legacy-native", new List<string>());
            router.AddSink(native);
            router.AddSink(bridge);
            router.AddSink(legacyNative);
            var contract = Exported("/phase184/bridge-only-legacy");
            Assert.True(router.RegisterTargets(FoxRunEndpoint.Ros2Bridge, contract));

            router.Publish(contract, 190UL, Bytes("{}"), "writer-a");

            Assert.Equal(0, native.PublishCalls);
            Assert.Equal(1, bridge.PublishCalls);
            Assert.Empty(legacyNative.Published);
        }

        [Fact]
        public void TargetReadinessPreventsPayloadPreparationAndLegacySinkAdaptsToNative()
        {
            var router = new FoxTopicSinkRouter();
            var unavailableBridge = new TargetRecordingSink(
                "bridge",
                FoxRunEndpoint.Ros2Bridge,
                ready: false,
                succeeds: true);
            var legacy = new RecordingSink(
                "legacy",
                new List<string>(),
                FoxTopicSinkCapabilities.External);
            router.AddSink(unavailableBridge);
            router.AddSink(legacy);
            var contract = Exported("/phase184/readiness");
            router.Register(contract);

            Assert.False(router.HasReadyTarget(FoxRunEndpoint.Ros2Bridge, contract));
            Assert.True(router.HasReadyTarget(FoxRunEndpoint.Ros2Native, contract));
            Assert.Equal(0, unavailableBridge.PublishCalls);
            Assert.Empty(legacy.Published);
        }

        [Fact]
        public void FatalTargetReadinessExceptionPassesThroughTheRouter()
        {
            var router = new FoxTopicSinkRouter();
            router.AddSink(new FatalReadinessSink());
            var contract = Exported("/phase184/fatal-readiness");
            router.Register(contract);

            Assert.Throws<OutOfMemoryException>(() =>
                router.HasReadyTarget(FoxRunEndpoint.Ros2Bridge, contract));
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
            var native = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(new ThrowingSink("failing"));
            router.AddSink(healthy);
            router.AddSink(native);
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
            Assert.Equal(0, native.PublishCalls);
            var retainedCopy = (byte[])received.Clone();
            borrowed[3] = 0x02;
            Assert.Equal(0x02, received[3]);
            Assert.Equal(0x01, retainedCopy[3]);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void CompatibleFanoutReusesOneBoundedWireContractPerRegistration()
        {
            var router = new FoxTopicSinkRouter();
            var sink = new RecordingSink("recording", new List<string>());
            router.AddSink(sink);
            var contract = new FoxTopicContract(
                "/phase185f/stable-wire-contract",
                "Demo.Logical",
                "msgpack",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            Assert.True(router.Register(contract));

            router.PublishCompatible(
                contract,
                FoxRunEncoding.MessagePack,
                1UL,
                new byte[] { 0x80 },
                "owner");
            router.PublishCompatible(
                contract,
                FoxRunEncoding.MessagePack,
                2UL,
                new byte[] { 0x80 },
                "owner");

            Assert.Equal(2, sink.PublishedContracts.Count);
            Assert.Same(
                sink.PublishedContracts[0],
                sink.PublishedContracts[1]);
            Assert.Same(
                Assert.Single(sink.Registered),
                sink.PublishedContracts[0]);
            Assert.Equal(
                string.Empty,
                sink.PublishedContracts[0].SchemaName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void WireEncodingViewChainsConvergeOnOneLogicalContractCache()
        {
            var logical = new FoxTopicContract(
                "/phase185f/wire-view-chain",
                "Demo.Logical",
                "json",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            var messagePack = logical.ForWireEncoding(
                FoxRunEncoding.MessagePack);
            var protobuf = messagePack.ForWireEncoding(
                FoxRunEncoding.Protobuf);
            var json = protobuf.ForWireEncoding(
                FoxRunEncoding.JSON);

            Assert.Same(logical, json);
            Assert.Same(
                messagePack,
                json.ForWireEncoding(
                    FoxRunEncoding.MessagePack));
            Assert.Same(
                protobuf,
                messagePack.ForWireEncoding(
                    FoxRunEncoding.Protobuf));
            Assert.Equal(string.Empty, messagePack.SchemaName);
            Assert.Equal("Demo.Logical", protobuf.SchemaName);
            Assert.Equal("Demo.Logical", json.SchemaName);
        }

        [Theory]
        [InlineData("json")]
        [InlineData("protobuf")]
        [Trait("Phase", "185-F")]
        public void CompatibleNonMessagePackFanoutExcludesTargetSinks(
            string declaredEncoding)
        {
            var router = new FoxTopicSinkRouter();
            var additive = new RecordingSink(
                "additive",
                new List<string>());
            var target = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(additive);
            router.AddSink(target);
            var contract = new FoxTopicContract(
                "/phase185f/compatible-" + declaredEncoding,
                "Demo.Payload",
                declaredEncoding,
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            Assert.True(router.Register(contract));

            router.PublishCompatible(
                contract,
                FoxRunEncoding.JSON,
                3UL,
                new byte[] { 0x7b, 0x7d },
                "owner");

            Assert.Single(additive.Published);
            Assert.Same(
                Assert.Single(additive.Registered),
                Assert.Single(additive.PublishedContracts));
            Assert.Equal(
                "json",
                additive.Registered[0].Encoding);
            Assert.Same(
                contract,
                Assert.Single(target.Registered));
            Assert.Equal(0, target.PublishCalls);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ResolvedInheritedMessagePackRegistersOneWireViewForInitialAndLateAdditiveSinks()
        {
            var router = new FoxTopicSinkRouter();
            var initial = new RecordingSink(
                "initial",
                new List<string>());
            var target = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(initial);
            router.AddSink(target);
            var logical = new FoxTopicContract(
                "/phase185f/inherited-wire-registration",
                "Demo.Payload",
                "json",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            var resolved = ResolvedPublish(
                FoxRunEncoding.MessagePack);

            Assert.True(router.RegisterTargets(resolved, logical));
            var late = new RecordingSink("late", new List<string>());
            router.AddSink(late);
            router.PublishCompatible(
                logical,
                FoxRunEncoding.MessagePack,
                4UL,
                new byte[] { 0x80 },
                "owner");

            var initialRegistration = Assert.Single(initial.Registered);
            var lateRegistration = Assert.Single(late.Registered);
            Assert.Equal("msgpack", initialRegistration.Encoding);
            Assert.Equal(string.Empty, initialRegistration.SchemaName);
            Assert.Same(
                initialRegistration,
                Assert.Single(initial.PublishedContracts));
            Assert.Same(
                lateRegistration,
                Assert.Single(late.PublishedContracts));
            Assert.Same(
                initialRegistration,
                lateRegistration);
            Assert.Same(
                resolved,
                Assert.Single(initial.ResolvedRegistrations).Resolved);
            Assert.Same(
                initialRegistration,
                Assert.Single(initial.ResolvedRegistrations).Contract);
            Assert.Same(
                resolved,
                Assert.Single(late.ResolvedRegistrations).Resolved);
            Assert.Same(
                lateRegistration,
                Assert.Single(late.ResolvedRegistrations).Contract);
            Assert.Same(
                logical,
                Assert.Single(target.Registered));
            Assert.Equal(0, target.PublishCalls);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void PlainProtobufPublishUsesRegisteredJsonViewAndExcludesTargetSinks()
        {
            var router = new FoxTopicSinkRouter();
            var initial = new RecordingSink(
                "initial",
                new List<string>());
            var target = new TargetRecordingSink(
                "native",
                FoxRunEndpoint.Ros2Native,
                ready: true,
                succeeds: true);
            router.AddSink(initial);
            router.AddSink(target);
            var logical = new FoxTopicContract(
                "/phase185f/plain-protobuf",
                "Demo.Payload",
                "protobuf",
                "canonical",
                "fingerprint",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            Assert.True(router.Register(logical));
            var late = new RecordingSink("late", new List<string>());
            router.AddSink(late);
            router.Publish(
                logical,
                5UL,
                new byte[] { 0x7b, 0x7d },
                "legacy-json-owner");

            Assert.Equal(
                "json",
                Assert.Single(initial.Registered).Encoding);
            Assert.Equal(
                "json",
                Assert.Single(late.Registered).Encoding);
            Assert.Same(
                Assert.Single(initial.Registered),
                Assert.Single(initial.PublishedContracts));
            Assert.Same(
                Assert.Single(late.Registered),
                Assert.Single(late.PublishedContracts));
            Assert.Same(
                Assert.Single(initial.Registered),
                Assert.Single(late.Registered));
            Assert.Same(logical, Assert.Single(target.Registered));
            Assert.Equal(0, target.PublishCalls);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ThrowingCapabilitiesIsIsolatedFromCompatibleFanout()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            router.AddSink(new ThrowingCapabilitiesSink("bad-capabilities"));
            var healthy = new RecordingSink(
                "healthy",
                new List<string>());
            router.AddSink(healthy);
            var contract = Exported("/phase185f/capabilities-fanout");

            Assert.True(router.Register(contract));
            router.PublishCompatible(
                contract,
                FoxRunEncoding.JSON,
                6UL,
                Bytes("{}"),
                "owner");

            Assert.Single(healthy.Registered);
            Assert.Single(healthy.Published);
            Assert.Contains(
                faults,
                fault => fault.Operation == "register");
            Assert.Contains(
                faults,
                fault => fault.Operation == "publish");
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ThrowingCapabilitiesIsIsolatedFromTargetReadinessAndPublish()
        {
            var router = new FoxTopicSinkRouter();
            var faults = new List<FoxTopicSinkFault>();
            router.SinkFaulted += fault => faults.Add(fault);
            router.AddSink(new ThrowingCapabilitiesSink("bad-capabilities"));
            var healthy = new RecordingSink(
                "healthy-legacy",
                new List<string>(),
                FoxTopicSinkCapabilities.External);
            router.AddSink(healthy);
            var contract = Exported("/phase185f/capabilities-target");

            Assert.True(router.RegisterTargets(
                FoxRunEndpoint.Ros2Native,
                contract));
            Assert.True(router.HasReadyTarget(
                FoxRunEndpoint.Ros2Native,
                contract));
            var result = router.PublishTarget(
                FoxRunEndpoint.Ros2Native,
                contract,
                7UL,
                Bytes("{}"),
                "owner");

            Assert.True(result.HadReadySink);
            Assert.True(result.Succeeded);
            Assert.Single(healthy.Published);
            Assert.Contains(
                faults,
                fault => fault.Operation == "readiness");
            Assert.Contains(
                faults,
                fault => fault.Operation == "publish");
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

        private static FoxRunResolvedPublishContract ResolvedPublish(
            FoxRunEncoding encoding)
        {
            var constructor = typeof(FoxRunResolvedPublishContract)
                .GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    new[]
                    {
                        typeof(FoxRunEndpoint),
                        typeof(FoxRunEncoding),
                        typeof(FoxRunResolvedQos),
                        typeof(FoxRunResolvedQos)
                    },
                    modifiers: null);
            Assert.NotNull(constructor);
            return (FoxRunResolvedPublishContract)constructor.Invoke(
                new object[]
                {
                    FoxRunEndpoint.Foxglove
                    | FoxRunEndpoint.Ros2Native,
                    encoding,
                    FoxRunResolvedQos.Default,
                    FoxRunResolvedQos.Default
                });
        }

        private sealed class RecordingSink :
            IFoxTopicSink,
            IFoxTopicResolvedContractSink
        {
            private readonly List<string> _order;
            private readonly FoxTopicSinkCapabilities _capabilities;

            public RecordingSink(
                string name,
                List<string> order,
                FoxTopicSinkCapabilities capabilities =
                    FoxTopicSinkCapabilities.Test)
            {
                Name = name;
                _order = order;
                _capabilities = capabilities;
            }

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => _capabilities;
            public List<FoxTopicContract> Registered { get; } = new List<FoxTopicContract>();
            public List<FoxTopicContract> PublishedContracts { get; } =
                new List<FoxTopicContract>();
            public List<(
                FoxTopicContract Contract,
                FoxRunResolvedPublishContract Resolved)>
                ResolvedRegistrations { get; } = new();
            public List<(string Topic, ulong TimestampNs, byte[] Payload, string Origin)> Published { get; } = new();
            public bool Disposed { get; private set; }

            public void Register(FoxTopicContract contract) => Registered.Add(contract);
            public void Register(
                FoxTopicContract contract,
                FoxRunResolvedPublishContract resolved)
            {
                Registered.Add(contract);
                ResolvedRegistrations.Add((contract, resolved));
            }

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

        private sealed class ThrowingCapabilitiesSink : IFoxTopicSink
        {
            public ThrowingCapabilitiesSink(string name) => Name = name;

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities =>
                throw new InvalidOperationException("capabilities boom");
            public void Register(FoxTopicContract contract) { }
            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
            }
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

        private sealed class TargetRecordingSink : IFoxTopicSink, IFoxTopicTargetSink
        {
            private readonly bool _ready;
            private readonly bool _succeeds;

            public TargetRecordingSink(
                string name,
                FoxRunEndpoint target,
                bool ready,
                bool succeeds)
            {
                Name = name;
                Target = target;
                _ready = ready;
                _succeeds = succeeds;
            }

            public string Name { get; }
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.External;
            public FoxRunEndpoint Target { get; }
            public int RegisterCalls { get; private set; }
            public int PublishCalls { get; private set; }
            public List<FoxTopicContract> Registered { get; } =
                new List<FoxTopicContract>();
            public void Register(FoxTopicContract contract)
            {
                RegisterCalls++;
                Registered.Add(contract);
            }
            public bool IsReady(FoxTopicContract contract, out string reason)
            {
                reason = _ready ? string.Empty : "unavailable";
                return _ready;
            }
            public bool TryPublish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin,
                out string reason)
            {
                PublishCalls++;
                reason = _succeeds ? string.Empty : "failed";
                return _succeeds;
            }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
            {
                TryPublish(contract, timestampNs, payload, origin, out _);
            }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalReadinessSink : IFoxTopicSink, IFoxTopicTargetSink
        {
            public string Name => "fatal-readiness";
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.External;
            public FoxRunEndpoint Target => FoxRunEndpoint.Ros2Bridge;
            public void Register(FoxTopicContract contract) { }
            public bool IsReady(FoxTopicContract contract, out string reason)
            {
                reason = string.Empty;
                throw new OutOfMemoryException("fatal");
            }
            public bool TryPublish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin,
                out string reason)
            {
                reason = string.Empty;
                return true;
            }
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }
    }
}
