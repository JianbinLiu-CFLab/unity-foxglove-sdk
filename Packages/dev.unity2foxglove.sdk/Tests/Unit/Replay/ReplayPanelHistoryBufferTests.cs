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
    [Trait("Phase", "174-001")]
    [Trait("Domain", "Replay")]
    public sealed partial class ReplayPanelHistoryBufferTests
    {
        [Fact]
        public void HistoryStartUsesCompletedClientWatermarkUntilDebounceReset()
        {
            var buffer = new ReplayPanelHistoryBuffer();

            Assert.Equal(70UL, buffer.GetHistoryFromTime(1, 0, 100, 30));

            CompleteClientDrain(buffer, 1, 100);

            Assert.Equal(101UL, buffer.GetHistoryFromTime(1, 0, 150, 30));

            buffer.ResetDebounce(1);

            Assert.Equal(120UL, buffer.GetHistoryFromTime(1, 0, 150, 30));
        }

        [Fact]
        public void CancelClientDrainRetiresOnlyRequestedClient()
        {
            var buffer = new ReplayPanelHistoryBuffer();
            var clientOneMessages = new List<McapMessage>
            {
                new McapMessage { ChannelId = 1, LogTime = 140, Data = new byte[] { 1 } }
            };
            var clientTwoMessages = new List<McapMessage>
            {
                new McapMessage { ChannelId = 1, LogTime = 150, Data = new byte[] { 2 } }
            };

            buffer.BeginClientDrains(
                150,
                new Dictionary<uint, List<McapMessage>>
                {
                    [1] = clientOneMessages,
                    [2] = clientTwoMessages
                });
            buffer.CancelDrain(1);

            Assert.Equal(1, buffer.DebugClientDrainCount);
            Assert.Null(buffer.DebugGetClientBuffer(1));
            Assert.Same(clientTwoMessages, buffer.DebugGetClientBuffer(2));
        }

        [Fact]
        public void CompletedMaximumClientWatermarkDoesNotOverflowHistoryStart()
        {
            var buffer = new ReplayPanelHistoryBuffer();
            CompleteClientDrain(buffer, 1, ulong.MaxValue);

            Assert.Equal(
                ulong.MaxValue,
                buffer.GetHistoryFromTime(1, 0, ulong.MaxValue, 30));
        }

        [Fact]
        public void WindowFallbackDoesNotPrecedeReplayStart()
        {
            var buffer = new ReplayPanelHistoryBuffer();

            Assert.Equal(40UL, buffer.GetHistoryFromTime(1, 40, 50, 100));
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
                    Clients = new[] { new TransportClientStats { ClientId = 1 } }
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
            buffer.BeginClientDrains(
                10,
                new Dictionary<uint, List<McapMessage>>
                {
                    [1] = new List<McapMessage>
                    {
                        new McapMessage
                        {
                            ChannelId = 1,
                            LogTime = 10,
                            Data = new byte[200]
                        }
                    }
                });

            buffer.DrainClientsLocked(
                session,
                new Dictionary<ushort, string> { [1] = "/history" },
                new ConsoleLogger(),
                maxMessagesPerTick: 256,
                queueReserveFrames: 0,
                queueReserveBytes: 0);

            Assert.Null(buffer.DebugGetClientBuffer(1));
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
        public void BeginClientDrainsTransfersHistoryListOwnership()
        {
            var buffer = new ReplayPanelHistoryBuffer();
            var messages = new List<McapMessage>
            {
                new McapMessage { ChannelId = 1, LogTime = 10, Data = new byte[] { 1 } }
            };

            buffer.BeginClientDrains(
                10,
                new Dictionary<uint, List<McapMessage>> { [1] = messages });

            Assert.Same(messages, buffer.DebugGetClientBuffer(1));
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
        public void ReplaySnapshotCapacityFollowsConfiguredTransportMaxClients()
        {
            using var transport = new ManagedWsBackend(new ManagedWebSocketOptions { MaxClients = 128 });
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            Assert.Equal(128, transport.GetStatsSnapshot().MaxClients);

            var coordinator = (TickCoordinator)typeof(FoxgloveRuntime)
                .GetField("_tickCoordinator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(runtime);
            var state = (ReplaySnapshotStateMachine)typeof(TickCoordinator)
                .GetField("_replaySnapshots", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(coordinator);

            for (uint clientId = 1; clientId <= 65; clientId++)
                state.RequestPanelSnapshot(clientId, clientId, clientId);

            for (uint clientId = 1; clientId <= 65; clientId++)
            {
                Assert.True(state.TryConsumePanelSnapshot(65, out var timeNs, out var consumedClientId));
                Assert.Equal(clientId, timeNs);
                Assert.Equal(clientId, consumedClientId);
            }

            Assert.False(state.TryConsumePanelSnapshot(65, out _, out _));
        }

        [Fact]
        public void ReplayCapacityRefreshesWhenTransportAuthorityChanges()
        {
            var transport = new StatsTransport
            {
                Snapshot = new TransportStatsSnapshot { MaxClients = 64 },
                MaxClients = 64
            };
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            transport.MaxClients = 128;
            runtime.Tick();

            var coordinator = (TickCoordinator)typeof(FoxgloveRuntime)
                .GetField("_tickCoordinator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(runtime);
            var state = (ReplaySnapshotStateMachine)typeof(TickCoordinator)
                .GetField("_replaySnapshots", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(coordinator);
            for (uint clientId = 1; clientId <= 65; clientId++)
                state.RequestPanelSnapshot(clientId, clientId, clientId);

            for (uint clientId = 1; clientId <= 65; clientId++)
            {
                Assert.True(state.TryConsumePanelSnapshot(65, out var timeNs, out var consumedClientId));
                Assert.Equal(clientId, timeNs);
                Assert.Equal(clientId, consumedClientId);
            }
        }

        [Fact]
        public void ReplayCapacityShrinkDoesNotDropAlreadyAcceptedClientBackfills()
        {
            var state = new ReplaySnapshotStateMachine(128);
            for (uint clientId = 1; clientId <= 65; clientId++)
                state.RequestPanelSnapshot(clientId, clientId, clientId);

            state.UpdateCapacity(64);

            for (uint clientId = 1; clientId <= 65; clientId++)
            {
                Assert.True(state.TryConsumePanelSnapshot(65, out var timeNs, out var consumedClientId));
                Assert.Equal(clientId, timeNs);
                Assert.Equal(clientId, consumedClientId);
            }

            Assert.False(state.TryConsumePanelSnapshot(65, out _, out _));
        }

    }
}
