// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Core.Session
{
    [Trait("Phase", "170A")]
    [Trait("Domain", "Session")]
    public sealed class SessionHelperExtractionTests
    {
        [Fact]
        public void TimeBroadcasterSuppressesDuplicateBroadcastsInsideRateWindow()
        {
            var broadcaster = new SessionTimeBroadcaster();
            var startTicks = TimeSpan.TicksPerSecond * 100L;

            Assert.True(broadcaster.TryReserveBroadcast(startTicks, 10f));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10 - 1, 10f));
            Assert.True(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10, 10f));
        }

        [Fact]
        public void TimeBroadcasterFallsBackToDefaultRateForInvalidRates()
        {
            var broadcaster = new SessionTimeBroadcaster();
            var startTicks = TimeSpan.TicksPerSecond * 100L;

            Assert.True(broadcaster.TryReserveBroadcast(startTicks, 0f));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10 - 1, 0f));

            broadcaster.Reset();
            Assert.True(broadcaster.TryReserveBroadcast(startTicks, -1f));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10 - 1, -1f));

            broadcaster.Reset();
            Assert.True(broadcaster.TryReserveBroadcast(startTicks, float.NaN));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10 - 1, float.NaN));

            broadcaster.Reset();
            Assert.True(broadcaster.TryReserveBroadcast(startTicks, float.PositiveInfinity));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + TimeSpan.TicksPerSecond / 10 - 1, float.PositiveInfinity));
        }

        [Fact]
        public void TimeBroadcasterResetLetsNextBroadcastThroughImmediately()
        {
            var broadcaster = new SessionTimeBroadcaster();
            var startTicks = TimeSpan.TicksPerSecond * 100L;

            Assert.True(broadcaster.TryReserveBroadcast(startTicks, 10f));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + 1, 10f));

            broadcaster.Reset();

            Assert.True(broadcaster.TryReserveBroadcast(startTicks + 1, 10f));
        }

        [Fact]
        public void ChannelFilterKeepsLiveAndMcapSinkDecisionsIndependent()
        {
            var filter = new SessionChannelFilter();
            var liveOnly = Channel(1, "/live-only");
            var recordOnly = Channel(2, "/record-only");

            filter.SetSinkChannelFilter(
                FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/record-only"));
            filter.SetSinkChannelFilter(
                FoxgloveSinkKind.McapRecording,
                new PredicateFilter(context => context.Topic != "/live-only"));

            Assert.True(filter.AllowLiveWebSocket(liveOnly));
            Assert.False(filter.AllowMcapRecording(liveOnly));
            Assert.False(filter.AllowLiveWebSocket(recordOnly));
            Assert.True(filter.AllowMcapRecording(recordOnly));
        }

        [Fact]
        public void ChannelFilterReturnsOnlyLiveAllowedChannels()
        {
            var filter = new SessionChannelFilter();
            var channels = new List<AdvertiseChannel>
            {
                Channel(1, "/allowed"),
                Channel(2, "/denied"),
                null
            };

            filter.SetSinkChannelFilter(
                FoxgloveSinkKind.LiveWebSocket,
                new PredicateFilter(context => context.Topic != "/denied"));

            var filtered = filter.FilterLiveChannels(channels);

            Assert.Collection(
                filtered,
                channel => Assert.Equal("/allowed", channel.Topic));
        }

        [Fact]
        public void ChannelFilterRejectsUnknownSink()
        {
            var filter = new SessionChannelFilter();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                filter.SetSinkChannelFilter((FoxgloveSinkKind)999, new PredicateFilter(_ => true)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                filter.GetSinkChannelFilter((FoxgloveSinkKind)999));
        }

        private static AdvertiseChannel Channel(uint id, string topic)
            => new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "Test.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            };

        private sealed class PredicateFilter : ISinkChannelFilter
        {
            private readonly Func<SinkChannelFilterContext, bool> _predicate;

            public PredicateFilter(Func<SinkChannelFilterContext, bool> predicate)
            {
                _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            }

            public bool AllowChannel(SinkChannelFilterContext context) => _predicate(context);
        }
    }
}
