// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
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

        private sealed class StatsTransport : IFoxgloveTransport, IFoxgloveTransportStatsProvider
        {
            public TransportStatsSnapshot Snapshot { get; set; } = TransportStatsSnapshot.Unsupported;
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
            public void SendBinary(uint clientId, byte[] data) { }
            public TransportStatsSnapshot GetStatsSnapshot() => Snapshot;
            public void Dispose() { }
        }
    }
}
