// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Per-sink channel filtering behavior for live WebSocket and MCAP outputs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "148")]
    [Trait("Domain", "Routing")]
    public sealed class SinkChannelFilterTests
    {
        [Fact]
        public void NullFiltersAllowLiveAdvertiseLivePublishAndMcapRecording()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);

            RegisterJsonChannel(session, 1, "/filter/default");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            session.PublishJson(1, new { value = 42 }, 1480);
            session.SetRecorder(null);

            recorder.Dispose();
            Assert.Contains(transport.BroadcastTexts, text => text.Contains("/filter/default"));
            Assert.True(transport.BinaryByClient.TryGetValue(7, out var frames));
            Assert.Single(frames);
            Assert.Equal(new[] { "/filter/default" }, ReadTopics(stream));
        }

        [Fact]
        public void SynchronousTransportPublishReentryKeepsSubscriberSnapshotsIndependent()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            RegisterJsonChannel(session, 1, "/filter/reentrant");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            var reentered = false;
            transport.BeforeSendBinary = () =>
            {
                if (reentered)
                    return;

                reentered = true;
                session.PublishJson(1, new { value = "inner" }, 14801);
            };

            session.PublishJson(1, new { value = "outer" }, 14800);

            Assert.True(reentered);
            Assert.True(transport.BinaryByClient.TryGetValue(7, out var frames));
            Assert.Equal(2, frames.Count);
        }

        [Fact]
        public void LiveFilterHidesChannelFromAdvertiseSubscribePublishAndUnadvertise()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/filter/live-denied"));

            RegisterJsonChannel(session, 1, "/filter/live-denied");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            session.PublishJson(1, new { value = 1 }, 1481);
            session.UnregisterChannel(1);

            Assert.DoesNotContain(transport.AllText, text => text.Contains("/filter/live-denied"));
            Assert.False(transport.BinaryByClient.ContainsKey(7));
            Assert.DoesNotContain(transport.AllText, text => text.Contains("\"unadvertise\""));
        }

        [Fact]
        public void McapFilterSkipsChannelRecordAndMessageRecord()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording,
                new PredicateFilter(context => context.Topic != "/filter/mcap-denied"));

            RegisterJsonChannel(session, 1, "/filter/mcap-denied");
            session.PublishJson(1, new { value = 1 }, 1482);
            session.SetRecorder(null);

            recorder.Dispose();
            Assert.Empty(ReadTopics(stream));
        }

        [Fact]
        public void LiveAndMcapFiltersMakeIndependentRoutingDecisions()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/filter/record-only"));
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording,
                new PredicateFilter(context => context.Topic != "/filter/live-only"));

            RegisterJsonChannel(session, 1, "/filter/live-only");
            RegisterJsonChannel(session, 2, "/filter/record-only");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            transport.SimulateText(7, SubscribeJson(101, 2));
            session.PublishJson(1, new { value = "live" }, 1483);
            session.PublishJson(2, new { value = "record" }, 1484);
            session.SetRecorder(null);

            recorder.Dispose();
            Assert.Contains(transport.AllText, text => text.Contains("/filter/live-only"));
            Assert.DoesNotContain(transport.AllText, text => text.Contains("/filter/record-only"));
            Assert.True(transport.BinaryByClient.TryGetValue(7, out var frames));
            Assert.Single(frames);
            Assert.Equal(new[] { "/filter/record-only" }, ReadTopics(stream));
        }

        [Fact]
        public void HasChannelDemandCountsOnlySinksThatAllowTheChannel()
        {
            var transport = new FilterTransport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, new PredicateFilter(_ => false));

            RegisterJsonChannel(session, 1, "/filter/no-demand");
            Assert.False(session.HasChannelDemand(1));

            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => true));
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            Assert.True(session.HasChannelDemand(1));
        }

        [Fact]
        public void RuntimePassesConfiguredFiltersIntoStartedSession()
        {
            var transport = new FilterTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => false));
            runtime.Start("sink-filter-runtime");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/filter/runtime-live-denied",
                Encoding = "json",
                SchemaName = "Filter.Runtime",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            transport.SimulateConnected(7);

            Assert.NotNull(runtime.GetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket));
            Assert.DoesNotContain(transport.AllText, text => text.Contains("/filter/runtime-live-denied"));
        }

        [Fact]
        public void RuntimeRejectsSinkChannelFilterChangeAfterStart()
        {
            var transport = new FilterTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => true));
            runtime.Start("sink-filter-after-start");

            Assert.Throws<InvalidOperationException>(() =>
                runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => false)));

            // The pre-start policy is left intact after the rejected change.
            Assert.NotNull(runtime.GetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket));
        }

        [Fact]
        public void RuntimeReappliesFilterAfterStopRestart()
        {
            var transport = new FilterTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => false));
            runtime.Start("sink-filter-restart");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/filter/restart-denied",
                Encoding = "json",
                SchemaName = "Filter.Runtime",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            transport.SimulateConnected(7);
            Assert.DoesNotContain(transport.AllText, text => text.Contains("/filter/restart-denied"));
            runtime.Stop();

            // Filter survives Stop and can be reconfigured while stopped.
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => true));
            runtime.Start("sink-filter-restart");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 2,
                Topic = "/filter/restart-allowed",
                Encoding = "json",
                SchemaName = "Filter.Runtime",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            transport.SimulateConnected(8);

            Assert.NotNull(runtime.GetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket));
            Assert.Contains(transport.AllText, text => text.Contains("/filter/restart-allowed"));
        }

        private static FoxgloveSession NewSession(FilterTransport transport)
            => new FoxgloveSession("sink-filter-tests", transport, schemaRegistry: new DefaultSchemaRegistry());

        private static void RegisterJsonChannel(FoxgloveSession session, uint id, string topic)
            => session.RegisterChannel(new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "Filter.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });

        private static IReadOnlyList<string> ReadTopics(MemoryStream stream)
        {
            stream.Position = 0;
            var summary = new McapReader(stream).ReadSummary();
            return summary.Channels.Select(channel => channel.Topic).OrderBy(topic => topic, StringComparer.Ordinal).ToList();
        }

        private static string SubscribeJson(uint subscriptionId, uint channelId)
            => JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = subscriptionId, ChannelId = channelId }
                }
            });

        private sealed class PredicateFilter : ISinkChannelFilter
        {
            private readonly Func<SinkChannelFilterContext, bool> _predicate;

            public PredicateFilter(Func<SinkChannelFilterContext, bool> predicate)
            {
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            }

            public bool AllowChannel(SinkChannelFilterContext context) => _predicate(context);
        }

        private sealed class FilterTransport : IFoxgloveTransport
        {
            public readonly List<string> BroadcastTexts = new List<string>();
            public readonly Dictionary<uint, List<string>> TextByClient = new Dictionary<uint, List<string>>();
            public readonly Dictionary<uint, List<byte[]>> BinaryByClient = new Dictionary<uint, List<byte[]>>();

            public Action BeforeSendBinary { get; set; }

            public IEnumerable<string> AllText => BroadcastTexts.Concat(TextByClient.Values.SelectMany(item => item));
            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) => Add(TextByClient, clientId, json);
            public void SendBinary(uint clientId, byte[] data)
            {
                BeforeSendBinary?.Invoke();
                Add(BinaryByClient, clientId, data);
            }
            public void SimulateConnected(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateDisconnected(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void SimulateBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);

            private static void Add<T>(Dictionary<uint, List<T>> map, uint clientId, T value)
            {
                if (!map.TryGetValue(clientId, out var list))
                {
                    list = new List<T>();
                    map[clientId] = list;
                }

                list.Add(value);
            }
        }
    }
}
