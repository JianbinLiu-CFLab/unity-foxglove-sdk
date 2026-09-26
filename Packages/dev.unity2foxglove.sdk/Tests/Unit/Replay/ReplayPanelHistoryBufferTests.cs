// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    [Trait("Phase", "174-001")]
    [Trait("Domain", "Replay")]
    public sealed class ReplayPanelHistoryBufferTests
    {
        [Fact]
        public void HistoryStartUsesCompletedWatermarkUntilDebounceReset()
        {
            var buffer = new ReplayPanelHistoryBuffer();

            Assert.Equal(70UL, buffer.GetHistoryFromTime(startNs: 0, clampedToNs: 100, windowNs: 30));

            buffer.BeginDrain(100);
            buffer.MarkDrainComplete();

            Assert.Equal(101UL, buffer.GetHistoryFromTime(startNs: 0, clampedToNs: 150, windowNs: 30));

            buffer.ResetDebounce();

            Assert.Equal(120UL, buffer.GetHistoryFromTime(startNs: 0, clampedToNs: 150, windowNs: 30));
        }

        [Fact]
        public void CancelClearsDrainWithoutForgettingCompletedWatermark()
        {
            var buffer = new ReplayPanelHistoryBuffer();
            buffer.BeginDrain(100);
            buffer.MarkDrainComplete();

            buffer.Buffer.Add(new McapMessage { ChannelId = 1, LogTime = 140, Data = new byte[] { 1 } });
            buffer.BeginDrain(150);
            buffer.CancelDrain();

            Assert.False(buffer.DebugActive);
            Assert.Equal(0, buffer.DebugBufferedCount);
            Assert.Equal(101UL, buffer.GetHistoryFromTime(startNs: 0, clampedToNs: 130, windowNs: 30));
        }

        [Fact]
        public void CompletedMaximumWatermarkDoesNotOverflowHistoryStart()
        {
            var buffer = new ReplayPanelHistoryBuffer();
            buffer.BeginDrain(ulong.MaxValue);
            buffer.MarkDrainComplete();

            Assert.Equal(
                ulong.MaxValue,
                buffer.GetHistoryFromTime(startNs: 0, clampedToNs: ulong.MaxValue, windowNs: 30));
        }

        [Fact]
        public void WindowFallbackDoesNotPrecedeReplayStart()
        {
            var buffer = new ReplayPanelHistoryBuffer();

            Assert.Equal(40UL, buffer.GetHistoryFromTime(startNs: 40, clampedToNs: 50, windowNs: 100));
        }

        [Fact]
        public void OversizedHistoryMessageIsSkippedWhenItCannotFitTransportCapacity()
        {
            var transport = new StatsTransport
            {
                Snapshot = new TransportStatsSnapshot
                {
                    Supported = true,
                    MaxQueuedFramesPerClient = 4,
                    MaxQueuedBytesPerClient = 100,
                    Clients = new[] { new TransportClientStats() }
                }
            };
            using var session = new FoxgloveSession("history-oversized", transport);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = (uint)McapReplayEngine.ReplayChannelIdBase | 1u,
                Topic = "/history",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });

            var buffer = new ReplayPanelHistoryBuffer();
            buffer.Buffer.Add(new McapMessage
            {
                ChannelId = 1,
                LogTime = 10,
                Data = new byte[200]
            });
            buffer.BeginDrain(10);

            buffer.DrainLocked(
                session,
                new Dictionary<ushort, string> { [1] = "/history" },
                new ConsoleLogger(),
                maxMessagesPerTick: 256,
                queueReserveFrames: 0,
                queueReserveBytes: 0);

            Assert.False(buffer.DebugActive);
            Assert.Equal(0, buffer.DebugBufferedCount);
        }

        [Fact]
        public void SlowClientDoesNotThrottleAnotherClientsHistoryDrain()
        {
            var transport = new StatsTransport
            {
                Snapshot = new TransportStatsSnapshot
                {
                    Supported = true,
                    MaxQueuedFramesPerClient = 4,
                    MaxQueuedBytesPerClient = 4096,
                    Clients = new[]
                    {
                        new TransportClientStats { ClientId = 1, QueuedFrames = 4 },
                        new TransportClientStats { ClientId = 2, QueuedFrames = 0 }
                    }
                }
            };
            using var session = new FoxgloveSession("history-per-client", transport);
            var replayChannelId = (uint)McapReplayEngine.ReplayChannelIdBase | 1u;
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = replayChannelId,
                Topic = "/history-per-client",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });
            transport.ReceiveText(1, string.Format(
                "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":11,\"channelId\":{0}}}]}}",
                replayChannelId));
            transport.ReceiveText(2, string.Format(
                "{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":21,\"channelId\":{0}}}]}}",
                replayChannelId));

            var buffer = new ReplayPanelHistoryBuffer();
            buffer.BeginClientDrains(
                10,
                new Dictionary<uint, List<McapMessage>>
                {
                    [1] = new List<McapMessage>
                    {
                        new McapMessage { ChannelId = 1, LogTime = 10, Data = new byte[] { 1 } }
                    },
                    [2] = new List<McapMessage>
                    {
                        new McapMessage { ChannelId = 1, LogTime = 10, Data = new byte[] { 2 } }
                    }
                });

            buffer.DrainClientsLocked(
                session,
                new Dictionary<ushort, string> { [1] = "/history-per-client" },
                new ConsoleLogger(),
                maxMessagesPerTick: 4,
                queueReserveFrames: 0,
                queueReserveBytes: 0);

            Assert.DoesNotContain((uint)1, transport.SentClientIds);
            Assert.Contains((uint)2, transport.SentClientIds);
        }

        [Fact]
        public void DisconnectedClientHistoryDrainCanBeRetiredWithoutAffectingOthers()
        {
            using var controller = new ReplayController(new ConsoleLogger(), null, null);
            var panelHistory = (ReplayPanelHistoryBuffer)typeof(ReplayController)
                .GetField("_panelHistory", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
            panelHistory.BeginClientDrains(
                10,
                new Dictionary<uint, List<McapMessage>>
                {
                    [1] = new List<McapMessage>
                    {
                        new McapMessage { ChannelId = 1, LogTime = 10, Data = new byte[] { 1 } }
                    },
                    [2] = new List<McapMessage>
                    {
                        new McapMessage { ChannelId = 1, LogTime = 10, Data = new byte[] { 2 } }
                    }
                });

            controller.CancelPanelHistory(1);

            Assert.Equal(1, panelHistory.DebugClientDrainCount);
            controller.CancelPanelHistory(2);
            Assert.Equal(0, panelHistory.DebugClientDrainCount);
        }

        [Fact]
        public void TwoTargetedSnapshotsWithinDebounceAreBothConsumed()
        {
            var state = new ReplaySnapshotStateMachine();
            state.RequestPanelSnapshot(100, 250, 1);
            state.RequestPanelSnapshot(200, 350, 2);

            Assert.False(state.TryConsumePanelSnapshot(249, out _, out _));
            Assert.True(state.TryConsumePanelSnapshot(250, out var firstTime, out var firstClient));
            Assert.Equal(100UL, firstTime);
            Assert.Equal((uint)1, firstClient);
            Assert.True(state.TryConsumePanelSnapshot(350, out var secondTime, out var secondClient));
            Assert.Equal(200UL, secondTime);
            Assert.Equal((uint)2, secondClient);
            Assert.False(state.TryConsumePanelSnapshot(350, out _, out _));
        }

        [Fact]
        public void ClearingOnePendingTargetedSnapshotPreservesTheOther()
        {
            var state = new ReplaySnapshotStateMachine();
            state.RequestPanelSnapshot(100, 250, 1);
            state.RequestPanelSnapshot(200, 250, 2);
            state.ClearPanelSnapshot(1);

            Assert.True(state.TryConsumePanelSnapshot(250, out var time, out var clientId));
            Assert.Equal(200UL, time);
            Assert.Equal((uint)2, clientId);
            Assert.False(state.TryConsumePanelSnapshot(250, out _, out _));
        }

        [Fact]
        public void RepeatedTargetedSnapshotForOneClientCoalescesToLatestRequest()
        {
            var state = new ReplaySnapshotStateMachine();
            state.RequestPanelSnapshot(100, 250, 1);
            state.RequestPanelSnapshot(200, 350, 1);

            Assert.False(state.TryConsumePanelSnapshot(250, out _, out _));
            Assert.True(state.TryConsumePanelSnapshot(350, out var time, out var clientId));
            Assert.Equal(200UL, time);
            Assert.Equal((uint)1, clientId);
            Assert.False(state.TryConsumePanelSnapshot(350, out _, out _));
        }

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

        private sealed class StatsTransport : IFoxgloveTransport, IFoxgloveTransportStatsProvider
        {
            public TransportStatsSnapshot Snapshot { get; set; } = TransportStatsSnapshot.Unsupported;
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
