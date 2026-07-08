// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
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
    }
}
