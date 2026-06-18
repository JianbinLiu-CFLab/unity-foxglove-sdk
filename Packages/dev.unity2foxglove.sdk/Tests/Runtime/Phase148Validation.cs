// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 148 per-sink channel filtering validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase148Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 148 Tests ---");
            _passCount = 0;

            VerifyDefaultFiltersPreserveLiveAndRecording();
            VerifyLiveFilterHidesAdvertiseSubscribeAndPublish();
            VerifyRecordingFilterSkipsMcapChannelAndMessages();
            VerifySinkDecisionsAreIndependent();
            VerifyDemandRespectsFilters();
            VerifyRuntimePersistsFiltersAcrossStart();
            VerifyRuntimeRejectsFilterChangeAfterStart();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 148: " + _passCount + " checks passed.\n");
        }

        private static void VerifyDefaultFiltersPreserveLiveAndRecording()
        {
            var transport = new Phase148Transport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);

            RegisterJsonChannel(session, 1, "/phase148/default");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            session.PublishJson(1, new { value = 42 }, 1480);
            session.SetRecorder(null);

            recorder.Dispose();
            Check(transport.BroadcastTexts.Any(text => text.Contains("/phase148/default")),
                "Null filters allow live advertise");
            Check(transport.BinaryByClient.TryGetValue(7, out var frames) && frames.Count == 1,
                "Null filters allow live data frames");
            Check(ReadTopics(stream).SequenceEqual(new[] { "/phase148/default" }),
                "Null filters allow MCAP channel records");
        }

        private static void VerifyLiveFilterHidesAdvertiseSubscribeAndPublish()
        {
            var transport = new Phase148Transport();
            using var session = NewSession(transport);
            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/phase148/live-denied"));

            RegisterJsonChannel(session, 1, "/phase148/live-denied");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            session.PublishJson(1, new { value = 1 }, 1481);
            session.UnregisterChannel(1);

            Check(!transport.AllText.Any(text => text.Contains("/phase148/live-denied")),
                "Live filter hides denied channels from existing and new clients");
            Check(!transport.BinaryByClient.ContainsKey(7),
                "Live filter ignores subscriptions and data frames for denied channels");
            Check(!transport.AllText.Any(text => text.Contains("\"unadvertise\"")),
                "Live filter does not unadvertise channels that were never advertised");
        }

        private static void VerifyRecordingFilterSkipsMcapChannelAndMessages()
        {
            var transport = new Phase148Transport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording,
                new PredicateFilter(context => context.Topic != "/phase148/mcap-denied"));

            RegisterJsonChannel(session, 1, "/phase148/mcap-denied");
            session.PublishJson(1, new { value = 1 }, 1482);
            session.SetRecorder(null);

            recorder.Dispose();
            Check(ReadTopics(stream).Count == 0,
                "MCAP filter skips denied channel records");
        }

        private static void VerifySinkDecisionsAreIndependent()
        {
            var transport = new Phase148Transport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/phase148/record-only"));
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording,
                new PredicateFilter(context => context.Topic != "/phase148/live-only"));

            RegisterJsonChannel(session, 1, "/phase148/live-only");
            RegisterJsonChannel(session, 2, "/phase148/record-only");
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            transport.SimulateText(7, SubscribeJson(101, 2));
            session.PublishJson(1, new { value = "live" }, 1483);
            session.PublishJson(2, new { value = "record" }, 1484);
            session.SetRecorder(null);

            recorder.Dispose();
            Check(transport.AllText.Any(text => text.Contains("/phase148/live-only"))
                    && !transport.AllText.Any(text => text.Contains("/phase148/record-only")),
                "Live sink advertises only channels allowed by the live filter");
            Check(transport.BinaryByClient.TryGetValue(7, out var frames) && frames.Count == 1,
                "Live sink publishes only channels allowed by the live filter");
            Check(ReadTopics(stream).SequenceEqual(new[] { "/phase148/record-only" }),
                "MCAP sink records only channels allowed by the MCAP filter");
        }

        private static void VerifyDemandRespectsFilters()
        {
            var transport = new Phase148Transport();
            using var session = NewSession(transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, new PredicateFilter(_ => false));

            RegisterJsonChannel(session, 1, "/phase148/no-demand");
            Check(!session.HasChannelDemand(1),
                "Recording demand ignores channels denied by the MCAP filter");

            session.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => true));
            transport.SimulateConnected(7);
            transport.SimulateText(7, SubscribeJson(100, 1));
            Check(session.HasChannelDemand(1),
                "Live subscribers still create demand when the live filter allows the channel");
        }

        private static void VerifyRuntimePersistsFiltersAcrossStart()
        {
            var transport = new Phase148Transport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => false));
            runtime.Start("phase148-runtime");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/phase148/runtime-live-denied",
                Encoding = "json",
                SchemaName = "phase148.Runtime",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            transport.SimulateConnected(7);

            Check(runtime.GetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket) != null,
                "Runtime stores the configured live sink filter");
            Check(!transport.AllText.Any(text => text.Contains("/phase148/runtime-live-denied")),
                "Runtime passes configured sink filters into the started session");
        }

        private static void VerifyRuntimeRejectsFilterChangeAfterStart()
        {
            var transport = new Phase148Transport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => true));
            runtime.Start("phase148-after-start");

            var threw = false;
            try
            {
                runtime.SetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket, new PredicateFilter(_ => false));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Check(threw,
                "Runtime rejects sink filter changes after the session starts");
            Check(runtime.GetSinkChannelFilter(FoxgloveSinkKind.LiveWebSocket) != null,
                "Rejected filter change leaves the pre-start policy intact");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase148"),
                "Phase validation registry exposes the per-sink channel filtering flag");
        }

        private static FoxgloveSession NewSession(Phase148Transport transport)
            => new FoxgloveSession("phase148", transport, schemaRegistry: new DefaultSchemaRegistry());

        private static void RegisterJsonChannel(FoxgloveSession session, uint id, string topic)
            => session.RegisterChannel(new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "phase148.Schema",
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }

        private sealed class PredicateFilter : ISinkChannelFilter
        {
            private readonly Func<SinkChannelFilterContext, bool> _predicate;

            public PredicateFilter(Func<SinkChannelFilterContext, bool> predicate)
            {
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            }

            public bool AllowChannel(SinkChannelFilterContext context) => _predicate(context);
        }

        private sealed class Phase148Transport : IFoxgloveTransport
        {
            public readonly List<string> BroadcastTexts = new List<string>();
            public readonly Dictionary<uint, List<string>> TextByClient = new Dictionary<uint, List<string>>();
            public readonly Dictionary<uint, List<byte[]>> BinaryByClient = new Dictionary<uint, List<byte[]>>();

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
            public void SendBinary(uint clientId, byte[] data) => Add(BinaryByClient, clientId, data);
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
