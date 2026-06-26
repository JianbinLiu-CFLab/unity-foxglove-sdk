// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Transport observability stats snapshots (pure-logic checks migrated
//          from Phase36Validation). The two real-socket checks
//          (TestDisconnectedClientDropsRetained, TestRuntimeAccessorLifecycle)
//          intentionally remain in the console runner as integration tests.

using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Transport stats snapshot counters, immutability, and unsupported fallback.
    /// Ported from Phase36Validation (pure-logic subset: 36A, 36B-1..6).
    /// </summary>
    [Trait("Phase", "36")]
    [Trait("Domain", "Transport")]
    public class TransportStatsSnapshotTests
    {
        [Fact]
        public void UnsupportedTransportReturnsUnsupported()
        {
            var fake = new Phase36FakeTransport();
            var runtime = new FoxgloveRuntime(fake, new SystemClock(), new DefaultSchemaRegistry());
            var snap = runtime.GetTransportStatsSnapshot();

            Assert.True(!snap.Supported, "36A-1: unsupported transport returns Supported=false");
            Assert.True(!snap.IsRunning, "36A-1b: unsupported transport reports not running");
            Assert.True(snap.ActiveClientCount == 0, "36A-1c: unsupported transport has 0 clients");
            Assert.True(snap.Clients.Count == 0, "36A-1d: unsupported transport has empty client list");
        }

        [Fact]
        public void EmptyBackendHasZeroCounters()
        {
            var backend = new ManagedWsBackend();
            var snap = backend.GetStatsSnapshot();

            Assert.True(snap.Supported, "36B-1: managed backend snapshot is supported");
            Assert.True(!snap.IsRunning, "36B-1b: not running before start");
            Assert.True(snap.ActiveClientCount == 0, "36B-1c: zero active clients");
            Assert.True(snap.TotalAcceptedClients == 0, "36B-1d: zero accepted");
            Assert.True(snap.TotalDisconnectedClients == 0, "36B-1e: zero disconnected");
            Assert.True(snap.TotalDroppedDataFrames == 0, "36B-1f: zero dropped data");
            Assert.True(snap.ControlOverflowDisconnects == 0, "36B-1g: zero control overflow disconnects");
            Assert.True(snap.TotalQueuedFrames == 0, "36B-1h: zero queued frames");
            Assert.True(snap.TotalQueuedBytes == 0, "36B-1i: zero queued bytes");
        }

        [Fact]
        public void QueueSnapshotCounts()
        {
            var q = new WsSendQueue(maxFrames: 10, maxQueuedBytes: 1024 * 1024);

            q.Enqueue(C(1));
            q.Enqueue(C(2));
            q.Enqueue(D(10));
            q.Enqueue(D(11));
            q.Enqueue(D(12));

            var snap = q.GetSnapshot();
            Assert.True(snap.QueuedFrames == 5, "36B-2: total queued frames");
            Assert.True(snap.QueuedControlFrames == 2, "36B-2b: control frame count");
            Assert.True(snap.QueuedDataFrames == 3, "36B-2c: data frame count");
            Assert.True(snap.QueuedBytes == 5, "36B-2d: queued bytes");
            Assert.True(snap.DroppedDataFrames == 0, "36B-2e: no drops yet");
        }

        [Fact]
        public void DataOverflowIncrementsDropped()
        {
            var q = new WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(D(1));
            q.Enqueue(D(2));
            q.Enqueue(D(3));
            q.Enqueue(D(4)); // overflow

            var snap = q.GetSnapshot();
            Assert.True(snap.QueuedFrames <= 3, "36B-3: queue stays bounded after data overflow");
            Assert.True(snap.DroppedDataFrames >= 1, "36B-3b: dropped count incremented");
        }

        [Fact]
        public void ControlOverflowDisconnectObservable()
        {
            var q = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(C(1));
            q.Enqueue(C(2));
            var result = q.Enqueue(C(3));

            Assert.True(!result.Accepted, "36B-4: control overflow frame not accepted");
            Assert.True(result.ShouldDisconnect, "36B-4b: control overflow requests disconnect");
            Assert.True(result.DroppedDataFrames == 0, "36B-4c: no data frames were available to drop");
        }

        [Fact]
        public void SnapshotImmutability()
        {
            var q = new WsSendQueue(maxFrames: 10, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(D(1));
            q.Enqueue(D(2));

            var snap1 = q.GetSnapshot();
            Assert.True(snap1.QueuedFrames == 2, "36B-5: snapshot 1 has 2 frames");

            q.Enqueue(D(3));
            q.Enqueue(D(4));

            Assert.True(snap1.QueuedFrames == 2, "36B-5b: old snapshot unchanged after enqueue");

            var snap2 = q.GetSnapshot();
            Assert.True(snap2.QueuedFrames == 4, "36B-5c: new snapshot reflects current state");
        }

        [Fact]
        public void QueueByteCapacityCheckDoesNotOverflowNearIntMax()
        {
            var q = new WsSendQueue(maxFrames: 10, maxQueuedBytes: int.MaxValue);
            var queuedBytes = typeof(WsSendQueue).GetField(
                "_queuedBytes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            queuedBytes.SetValue(q, int.MaxValue);

            var result = q.Enqueue(D(1));

            Assert.False(result.Accepted);
            Assert.False(result.ShouldDisconnect);
            Assert.Equal(1, result.DroppedDataFrames);
            Assert.Equal(int.MaxValue, q.QueuedBytes);
        }

        [Fact]
        public void SnapshotClientsNotMutable()
        {
            var backend = new ManagedWsBackend();
            var snap = backend.GetStatsSnapshot();
            Assert.True(snap.Supported, "36B-6: managed backend snapshot is supported");
            Assert.True(!(snap.Clients is System.Collections.Generic.List<TransportClientStats>),
                "36B-6b: Clients is not a mutable List");
        }

        [Fact]
        public void AllowedOriginsReturnsIndependentSnapshot()
        {
            using var backend = new ManagedWsBackend();
            backend.AddAllowedOrigin("https://first.example");

            var snapshot = backend.AllowedOrigins;
            backend.AddAllowedOrigin("https://second.example");

            Assert.Single(snapshot);
            Assert.Contains("https://first.example", snapshot);
            Assert.DoesNotContain("https://second.example", snapshot);
        }

        private static QueuedFrame D(byte b) =>
            new(WsOpcode.Binary, new[] { b }, FramePriority.Data);

        private static QueuedFrame C(byte b) =>
            new(WsOpcode.Text, new[] { b }, FramePriority.Control);

        private sealed class Phase36FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event System.Action<uint> OnClientConnected;
            public event System.Action<uint> OnClientDisconnected;
            public event System.Action<uint, string> OnTextReceived;
            public event System.Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) { }
            public void Stop() { }
            public void SendText(uint clientId, string json) { }
            public void BroadcastText(string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastBinary(byte[] data) { }
            public void Dispose() { }
        }
    }
}
