// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 171 mirror sink routing behavior for optional remote gateway output.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "171")]
    [Trait("Domain", "Routing")]
    public sealed class MirrorSinkRoutingTests
    {
        [Fact]
        public void SessionMirrorSinkReceivesRegistrationDemandAndPublish()
        {
            var transport = new MirrorTransport();
            using var session = new FoxgloveSession("mirror-session", transport, schemaRegistry: new DefaultSchemaRegistry());
            var sink = new RecordingMirrorSink();
            session.SetMirrorSink(sink);

            RegisterJsonChannel(session, 1, "/mirror/session");
            Assert.True(session.HasChannelDemand(1));

            var payload = new byte[] { 1, 2, 3 };
            session.Publish(1, payload, 1710UL);

            Assert.Single(sink.Registered);
            Assert.Equal("/mirror/session", sink.Registered[0].Topic);
            Assert.Single(sink.Published);
            Assert.Equal(1U, sink.Published[0].Channel.Id);
            Assert.Equal(1710UL, sink.Published[0].LogTimeNs);
            Assert.Same(payload, sink.Published[0].Payload);
        }

        [Fact]
        public void RuntimeCanAttachMirrorSinkAfterChannelsExist()
        {
            var transport = new MirrorTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("mirror-runtime");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 7,
                Topic = "/mirror/runtime",
                Encoding = "json",
                SchemaName = "Mirror.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            Assert.False(runtime.HasChannelDemand(7));

            var sink = new RecordingMirrorSink();
            runtime.SetMirrorSink(sink);

            Assert.True(runtime.HasChannelDemand(7));
            Assert.Single(sink.Registered);
            runtime.Publish(7, new byte[] { 9 }, 1711UL);
            Assert.Single(sink.Published);

            runtime.SetMirrorSink(null);
            Assert.False(runtime.HasChannelDemand(7));
            Assert.Single(sink.Unregistered);
            Assert.Equal(7U, sink.Unregistered[0]);
            runtime.Publish(7, new byte[] { 10 }, 1712UL);
            Assert.Single(sink.Published);
        }

        private static void RegisterJsonChannel(FoxgloveSession session, uint id, string topic)
            => session.RegisterChannel(new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "Mirror.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });

        private sealed class RecordingMirrorSink : IFoxgloveMirrorSink
        {
            public readonly List<AdvertiseChannel> Registered = new List<AdvertiseChannel>();
            public readonly List<uint> Unregistered = new List<uint>();
            public readonly List<PublishRecord> Published = new List<PublishRecord>();

            public bool HasChannelDemand(AdvertiseChannel channel) => channel != null;
            public void RegisterChannel(AdvertiseChannel channel) => Registered.Add(channel);
            public void UnregisterChannel(uint channelId) => Unregistered.Add(channelId);
            public void Publish(AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
                => Published.Add(new PublishRecord(channel, logTimeNs, payload));
        }

        private readonly struct PublishRecord
        {
            public PublishRecord(AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
            {
                Channel = channel;
                LogTimeNs = logTimeNs;
                Payload = payload;
            }

            public AdvertiseChannel Channel { get; }
            public ulong LogTimeNs { get; }
            public byte[] Payload { get; }
        }

        private sealed class MirrorTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
        }
    }
}
