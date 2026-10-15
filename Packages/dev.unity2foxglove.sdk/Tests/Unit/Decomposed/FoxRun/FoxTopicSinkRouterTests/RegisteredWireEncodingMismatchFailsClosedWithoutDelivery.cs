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
