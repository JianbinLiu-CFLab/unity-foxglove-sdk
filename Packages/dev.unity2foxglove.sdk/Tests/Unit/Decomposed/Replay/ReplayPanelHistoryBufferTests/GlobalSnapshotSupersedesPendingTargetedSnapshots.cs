// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{


public sealed partial class ReplayPanelHistoryBufferTests
    {
        [Fact]
        public void GlobalSnapshotSupersedesPendingTargetedSnapshots()
        {
            var state = new ReplaySnapshotStateMachine();
            state.RequestPanelSnapshot(100, 100, 1);
            state.RequestPanelSnapshot(900, 200);
            state.RequestPanelSnapshot(300, 0, 2);

            Assert.False(state.TryConsumePanelSnapshot(199, out _, out _));
            Assert.True(state.TryConsumePanelSnapshot(200, out var time, out var clientId));
            Assert.Equal(900UL, time);
            Assert.Null(clientId);
            Assert.False(state.TryConsumePanelSnapshot(200, out _, out _));
        }

        [Fact]
        public void ReplaySnapshotHistoryIsFilteredPerClientSubscription()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase187-replay-history-filter-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/history-filter/a", "json", "", "", "");
                    recorder.AddChannel(2, "/history-filter/b", "json", "", "", "");
                    recorder.WriteMessage(1, 1, new byte[] { 1 });
                    recorder.WriteMessage(2, 2, new byte[] { 2 });
                    recorder.Close();
                }

                using var transport = new StatsTransport();
                using var session = new FoxgloveSession("history-filter", transport);
                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.RegisterChannels(session);
                var channelAId = (uint)McapReplayEngine.ReplayChannelIdBase | 1u;
                var channelBId = (uint)McapReplayEngine.ReplayChannelIdBase | 2u;
                transport.ReceiveText(
                    1,
                    string.Format(
                        "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":11,\"channelId\":{0}}}]}}",
                        channelAId));
                transport.ReceiveText(
                    2,
                    string.Format(
                        "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":21,\"channelId\":{0}}}]}}",
                        channelBId));

                controller.PublishSnapshot(session, 2);

                var decoded = new List<(uint clientId, uint subscriptionId, byte[] payload)>();
                foreach (var frame in transport.SentFrames)
                {
                    if (BinaryEncoding.TryDecodeServerMessageData(
                        frame.data,
                        out var subscriptionId,
                        out _,
                        out var payload))
                        decoded.Add((frame.clientId, subscriptionId, payload));
                }

                Assert.Equal(2, decoded.Count);
                Assert.Equal(new byte[] { 1 }, decoded.Find(item => item.clientId == 1).payload);
                Assert.Equal(new byte[] { 2 }, decoded.Find(item => item.clientId == 2).payload);
                Assert.Equal((uint)11, decoded.Find(item => item.clientId == 1).subscriptionId);
                Assert.Equal((uint)21, decoded.Find(item => item.clientId == 2).subscriptionId);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void ReplaySnapshotSkipsHistoryForClientWithoutReplaySubscription(
            bool targeted,
            bool subscribeLive)
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase192-replay-history-no-subscription-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        Compression = "lz4",
                        IndexTypes = McapIndexTypes.Chunk
                    }))
                {
                    recorder.AddChannel(1, "/history-no-subscription", "json", "", "", "");
                    recorder.WriteMessage(1, 1, new byte[] { 1 });
                    recorder.Close();
                }

                using var transport = new StatsTransport();
                using var session = new FoxgloveSession("history-no-subscription", transport);
                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.RegisterChannels(session);
                Assert.True(controller.Engine.CanSeek);
                Assert.All(controller.Engine.Summary.ChunkIndexes,
                    chunk => Assert.Empty(chunk.MessageIndexOffsets));
                if (subscribeLive)
                {
                    session.RegisterChannel(new AdvertiseChannel
                    {
                        Id = 2,
                        Topic = "/live-only",
                        Encoding = "json"
                    });
                    transport.ReceiveText(1,
                        "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":11,\"channelId\":2}]}");
                }

                controller.PublishSnapshot(session, 1, targetClientId: targeted ? 1u : (uint?)null);

                Assert.Equal(0, controller.Engine.LastHistoryMetrics.CandidateCount);
                Assert.Equal(0, controller.Engine.LastHistoryMetrics.DecompressedChunkReads);
                Assert.DoesNotContain(transport.SentFrames,
                    frame => BinaryEncoding.TryDecodeServerMessageData(frame.data, out _, out _, out _));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReplaySnapshotReusesHistoryQueryForEquivalentClientSubscriptions()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase192-replay-history-query-cache-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/history-query-cache", "json", "", "", "");
                    recorder.WriteMessage(1, 1, new byte[] { 1 });
                    recorder.Close();
                }

                using var transport = new StatsTransport
                {
                    Snapshot = new TransportStatsSnapshot
                    {
                        Supported = true,
                        MaxQueuedFramesPerClient = 4,
                        MaxQueuedBytesPerClient = 4096,
                        Clients = new[]
                        {
                            new TransportClientStats { ClientId = 1, QueuedFrames = 4 },
                            new TransportClientStats { ClientId = 2, QueuedFrames = 4 }
                        }
                    }
                };
                using var session = new FoxgloveSession("history-query-cache", transport);
                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.RegisterChannels(session);
                var replayChannelId = (uint)McapReplayEngine.ReplayChannelIdBase | 1u;
                transport.ReceiveText(1, string.Format(
                    "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":11,\"channelId\":{0}}}]}}",
                    replayChannelId));
                transport.ReceiveText(2, string.Format(
                    "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":21,\"channelId\":{0}}}]}}",
                    replayChannelId));

                controller.PublishSnapshot(session, 1);

                var panelHistory = (ReplayPanelHistoryBuffer)typeof(ReplayController)
                    .GetField("_panelHistory", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(controller);
                Assert.Same(
                    panelHistory.DebugGetClientBuffer(1),
                    panelHistory.DebugGetClientBuffer(2));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReplaySnapshotHistoryCapIsAppliedPerClientSubscription()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase192-replay-history-per-client-cap-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/history-cap/a", "json", "", "", "");
                    recorder.AddChannel(2, "/history-cap/b", "json", "", "", "");
                    recorder.WriteMessage(2, 1, new byte[] { 0xB });
                    for (ulong timeNs = 2; timeNs <= 5002; timeNs++)
                        recorder.WriteMessage(1, timeNs, new byte[] { 0xA });
                    recorder.Close();
                }

                using var transport = new StatsTransport();
                using var session = new FoxgloveSession("history-cap", transport);
                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.RegisterChannels(session);
                var channelAId = (uint)McapReplayEngine.ReplayChannelIdBase | 1u;
                var channelBId = (uint)McapReplayEngine.ReplayChannelIdBase | 2u;
                transport.ReceiveText(
                    1,
                    string.Format(
                        "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":11,\"channelId\":{0}}}]}}",
                        channelAId));
                transport.ReceiveText(
                    2,
                    string.Format(
                        "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":21,\"channelId\":{0}}}]}}",
                        channelBId));

                controller.PublishSnapshot(session, 5002);

                var clientBData = new List<byte[]>();
                foreach (var frame in transport.SentFrames)
                {
                    if (frame.clientId == 2
                        && BinaryEncoding.TryDecodeServerMessageData(
                            frame.data,
                            out var subscriptionId,
                            out _,
                            out var payload))
                    {
                        Assert.Equal((uint)21, subscriptionId);
                        clientBData.Add(payload);
                    }
                }

                Assert.Single(clientBData);
                Assert.Equal(new byte[] { 0xB }, clientBData[0]);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void CompleteClientDrain(
            ReplayPanelHistoryBuffer buffer,
            uint clientId,
            ulong parkTimeNs)
        {
            using var session = new FoxgloveSession("history-watermark", new StatsTransport());
            buffer.BeginClientDrains(
                parkTimeNs,
                new Dictionary<uint, List<McapMessage>>
                {
                    [clientId] = new List<McapMessage>()
                });
            buffer.DrainClientsLocked(
                session,
                new Dictionary<ushort, string>(),
                new ConsoleLogger(),
                maxMessagesPerTick: 256,
                queueReserveFrames: 0,
                queueReserveBytes: 0);
        }

        private sealed class StatsTransport : IFoxgloveTransport, IFoxgloveTransportStatsProvider, IFoxgloveTransportCapacityProvider
        {
            public TransportStatsSnapshot Snapshot { get; set; } = TransportStatsSnapshot.Unsupported;
            public int MaxClients { get; set; }
            public List<uint> SentClientIds { get; } = new();
            public List<(uint clientId, byte[] data)> SentFrames { get; } = new();
            public bool IsRunning => false;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data)
            {
                SentClientIds.Add(clientId);
                SentFrames.Add((clientId, data));
            }
            public TransportStatsSnapshot GetStatsSnapshot() => Snapshot;
            public void Dispose() { }
            public void ReceiveText(uint clientId, string text) => OnTextReceived?.Invoke(clientId, text);
        }
    }
}
