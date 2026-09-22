using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module2ReviewRemediationTests
    {
        [Fact]
        public void ChannelRegistryRejectsConflictingActiveDescriptor()
        {
            var registry = new ChannelRegistry();
            registry.Register(new AdvertiseChannel { Id = 7, Topic = "/a", Encoding = "json" });
            Assert.Throws<InvalidOperationException>(() => registry.Register(
                new AdvertiseChannel { Id = 7, Topic = "/b", Encoding = "json" }));
            Assert.Equal("/a", registry.Get(7).Topic);
        }

        [Fact]
        public void SubscriptionRegistryRejectsActiveIdRebindAndDuplicateChannel()
        {
            var registry = new SubscriptionRegistry();
            Assert.True(registry.TryAddSubscription(1, 10, 20, out _));
            Assert.False(registry.TryAddSubscription(1, 10, 21, out var rebindError));
            Assert.Contains("active subscription", rebindError, StringComparison.OrdinalIgnoreCase);
            Assert.False(registry.TryAddSubscription(1, 11, 20, out var duplicateError));
            Assert.Contains("already subscribed", duplicateError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParameterSubscriptionsHaveBoundedBudgets()
        {
            var registry = new ParameterSubscriptionRegistry();
            var names = Enumerable.Range(0, 1024).Select(i => "p" + i).ToArray();
            for (uint client = 1; client <= 8; client++)
                Assert.True(registry.TrySubscribe(client, names, out _));
            Assert.False(registry.TrySubscribe(1, new[] { "overflow" }, out _));
            Assert.False(registry.IsSubscribed(1, "overflow"));
            Assert.False(registry.TrySubscribe(9, new[] { "p0" }, out _));
            Assert.False(registry.TrySubscribe(9, null, out _));
            registry.RemoveClient(8);
            Assert.True(registry.TrySubscribe(9, names, out _));
            Assert.True(registry.IsSubscribed(9, "p1023"));
        }

        [Fact]
        public void SessionSinksAreOutsideLifecycleLock()
        {
            using var transport = new ProbeTransport();
            using var session = new FoxgloveSession("review", transport);
            session.RegisterChannel(new AdvertiseChannel { Id = 7, Topic = "/a", Encoding = "json" });
            transport.Connect(1);
            transport.Text(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":10,\"channelId\":7}]}");
            var gate = GetField(session, "_channelLifecycleLock");
            var sink = new ProbeSink();
            session.SetMirrorSink(sink);
            bool mirrorUnlocked = false, transportUnlocked = false;
            sink.OnPublish = () => mirrorUnlocked = LockAvailableOnAnotherThread(gate);
            transport.Binary = (_, _) => transportUnlocked = LockAvailableOnAnotherThread(gate);
            session.Publish(7, new byte[] { 1 }, 100);
            Assert.True(mirrorUnlocked);
            Assert.True(transportUnlocked);
            Assert.Equal(1, sink.PublishCount);
            Assert.Equal(1, transport.BinaryCount);
        }

        [Theory]
        [InlineData("json", true)]
        [InlineData("unsupported", false)]
        public void ClientPublishValidatesEncodingAndIsolatesRecorderFailure(string encoding, bool accepted)
        {
            using var transport = new ProbeTransport();
            var graph = new SessionGraphHandler(transport, null, null);
            using var stream = new FailingWriteStream();
            using var recorder = new McapRecorder(stream, chunkSizeBytes: 1);
            var calls = 0;
            var handler = new SessionClientPublishHandler(() => recorder, new SystemClock(), null, graph,
                (client, channel, topic, actualEncoding, payload) => calls++);
            handler.Advertise(1, JsonConvert.SerializeObject(new Advertise
            {
                Channels = new List<AdvertiseChannel>
                {
                    new AdvertiseChannel { Id = 7, Topic = "/input", Encoding = encoding }
                }
            }));
            stream.Fail = true;
            try
            {
                handler.RouteBinary(1, 7, new byte[] { 1, 2, 3 });
                Assert.Equal(accepted ? 1 : 0, calls);
                Assert.Equal(accepted, stream.FailedWrites > 0);
            }
            finally { stream.Fail = false; }
        }

        [Fact]
        public void GraphBroadcastSnapshotsRecipientsAndContinuesAfterSendFailure()
        {
            using var transport = new ProbeTransport();
            var graph = new SessionGraphHandler(transport, null, null);
            graph.Subscribe(1);
            graph.Subscribe(2);
            var sent = new List<uint>();
            var unlocked = true;
            transport.TextSend = (id, _) =>
            {
                unlocked &= LockAvailableOnAnotherThread(GetField(graph, "_subscriberScratchLock"));
                sent.Add(id);
                if (sent.Count == 1)
                {
                    graph.Unsubscribe(1);
                    graph.Unsubscribe(2);
                    throw new IOException("injected send failure");
                }
            };
            Assert.Throws<IOException>(() => graph.BroadcastUpdate());
            Assert.True(unlocked);
            Assert.Equal(new uint[] { 1, 2 }, sent.OrderBy(id => id));
            sent.Clear();
            graph.BroadcastUpdate();
            Assert.Empty(sent);
        }

        [Fact]
        public void ConnectionGraphSnapshotsSortTopologyAndIdentifiers()
        {
            var graph = new ConnectionGraphRegistry();
            foreach (var name in new[] { "/z", "/a" })
                foreach (var id in new[] { "z", "a" })
                {
                    graph.AddPublishedTopic(name, id);
                    graph.AddSubscribedTopic(name, id);
                    graph.AddAdvertisedService(name, id);
                }
            var snapshot = graph.GetSnapshot();
            Assert.Equal(new[] { "/a", "/z" }, snapshot.PublishedTopics.Select(t => t.Name));
            Assert.Equal(new[] { "/a", "/z" }, snapshot.SubscribedTopics.Select(t => t.Name));
            Assert.Equal(new[] { "/a", "/z" }, snapshot.AdvertisedServices.Select(t => t.Name));
            Assert.All(snapshot.PublishedTopics, t => Assert.Equal(new[] { "a", "z" }, t.PublisherIds));
            Assert.All(snapshot.SubscribedTopics, t => Assert.Equal(new[] { "a", "z" }, t.SubscriberIds));
            Assert.All(snapshot.AdvertisedServices, t => Assert.Equal(new[] { "a", "z" }, t.ProviderIds));
        }

        [Fact]
        public void ParameterObserversAreIsolatedFromEachOther()
        {
            var store = new FoxgloveParameterStore();
            var values = new List<int>();
            store.OnParameterChanged += (_, _, _) => throw new IOException("injected observer failure");
            store.OnParameterChanged += (_, value, _) => values.Add((int)value);
            store.Register("value", new JValue(1), "number", true);
            Assert.True(store.TrySetFromClient("value", new JValue(2)));
            Assert.Equal(new[] { 1, 2 }, values);
        }

        [Fact]
        public void ServiceRegistryGuardsIdExhaustionAndJsonEncoding()
        {
            var registry = new FoxgloveServiceRegistry();
            typeof(FoxgloveServiceRegistry).GetField("_nextServiceId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(registry, uint.MaxValue);
            Assert.Equal(uint.MaxValue, registry.Register(new ServiceDescriptor { Name = "/last" }));
            Assert.Throws<InvalidOperationException>(() => registry.Register(new ServiceDescriptor { Name = "/overflow" }));
            Assert.Single(registry.GetAll());
            foreach (var requestSide in new[] { true, false })
            {
                var invalid = new ServiceDescriptor { Name = "/encoding" };
                var schema = new ServiceSchemaDescriptor { Encoding = "protobuf" };
                if (requestSide) invalid.Request = schema; else invalid.Response = schema;
                Assert.Throws<ArgumentException>(() => new FoxgloveServiceRegistry().Register(invalid));
            }
            var valid = new FoxgloveServiceRegistry();
            Assert.Equal(1u, valid.Register(new ServiceDescriptor { Name = "/json",
                Request = new ServiceSchemaDescriptor { Encoding = "json" },
                Response = new ServiceSchemaDescriptor { Encoding = "json" } }));
        }

        private static object GetField(object value, string name)
            => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);

        private static bool LockAvailableOnAnotherThread(object gate)
        {
            var available = false;
            var thread = new Thread(() =>
            {
                if (!Monitor.TryEnter(gate, 500)) return;
                try { available = true; }
                finally { Monitor.Exit(gate); }
            });
            thread.Start();
            Assert.True(thread.Join(3000));
            return available;
        }

        private sealed class FailingWriteStream : MemoryStream
        {
            public bool Fail;
            public int FailedWrites;
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Fail) { FailedWrites++; throw new IOException("injected recorder failure"); }
                base.Write(buffer, offset, count);
            }
        }

        private sealed class ProbeSink : IFoxgloveMirrorSink
        {
            public Action OnPublish;
            public int PublishCount;
            public bool HasChannelDemand(AdvertiseChannel channel) => true;
            public void RegisterChannel(AdvertiseChannel channel) { }
            public void UnregisterChannel(uint channelId) { }
            public void Publish(AdvertiseChannel channel, ulong time, byte[] payload)
            { PublishCount++; OnPublish?.Invoke(); }
        }

        private sealed class ProbeTransport : IFoxgloveTransport
        {
            public Action<uint, string> TextSend;
            public Action<uint, byte[]> Binary;
            public int BinaryCount;
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Connect(uint id) => OnClientConnected?.Invoke(id);
            public void Text(uint id, string text) => OnTextReceived?.Invoke(id, text);
            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void BroadcastText(string text) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint id, string text) => TextSend?.Invoke(id, text);
            public void SendBinary(uint id, byte[] data) { BinaryCount++; Binary?.Invoke(id, data); }
        }
    }
}
