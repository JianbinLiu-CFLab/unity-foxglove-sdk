// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 36 transport observability — stats snapshots,
// counters, immutability, and unsupported fallback.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase36Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 36 Tests ---");
            _passCount = 0;

            TestUnsupportedTransportReturnsUnsupported();
            TestEmptyBackendHasZeroCounters();
            TestQueueSnapshotCounts();
            TestDataOverflowIncrementsDropped();
            TestControlOverflowDisconnectObservable();
            TestSnapshotImmutability();
            TestSnapshotClientsNotMutable();
            TestDisconnectedClientDropsRetained();
            TestRuntimeAccessorLifecycle();

            Console.WriteLine("Phase 36: All checks passed.");
        }

        private static void TestUnsupportedTransportReturnsUnsupported()
        {
            var fake = new Phase36FakeTransport();
            var runtime = new FoxgloveRuntime(fake, new SystemClock(), new DefaultSchemaRegistry());
            var snap = runtime.GetTransportStatsSnapshot();

            Check(!snap.Supported, "36A-1: unsupported transport returns Supported=false");
            Check(!snap.IsRunning, "36A-1b: unsupported transport reports not running");
            Check(snap.ActiveClientCount == 0, "36A-1c: unsupported transport has 0 clients");
            Check(snap.Clients.Count == 0, "36A-1d: unsupported transport has empty client list");
        }

        private static void TestEmptyBackendHasZeroCounters()
        {
            var backend = new ManagedWsBackend();
            var snap = backend.GetStatsSnapshot();

            Check(snap.Supported, "36B-1: managed backend snapshot is supported");
            Check(!snap.IsRunning, "36B-1b: not running before start");
            Check(snap.ActiveClientCount == 0, "36B-1c: zero active clients");
            Check(snap.TotalAcceptedClients == 0, "36B-1d: zero accepted");
            Check(snap.TotalDisconnectedClients == 0, "36B-1e: zero disconnected");
            Check(snap.TotalDroppedDataFrames == 0, "36B-1f: zero dropped data");
            Check(snap.ControlOverflowDisconnects == 0, "36B-1g: zero control overflow disconnects");
            Check(snap.TotalQueuedFrames == 0, "36B-1h: zero queued frames");
            Check(snap.TotalQueuedBytes == 0, "36B-1i: zero queued bytes");
        }

        private static void TestQueueSnapshotCounts()
        {
            var q = new WsSendQueue(maxFrames: 10, maxQueuedBytes: 1024 * 1024);

            q.Enqueue(C(1));
            q.Enqueue(C(2));
            q.Enqueue(D(10));
            q.Enqueue(D(11));
            q.Enqueue(D(12));

            var snap = q.GetSnapshot();
            Check(snap.QueuedFrames == 5, "36B-2: total queued frames");
            Check(snap.QueuedControlFrames == 2, "36B-2b: control frame count");
            Check(snap.QueuedDataFrames == 3, "36B-2c: data frame count");
            Check(snap.QueuedBytes == 5, "36B-2d: queued bytes");
            Check(snap.DroppedDataFrames == 0, "36B-2e: no drops yet");
        }

        private static void TestDataOverflowIncrementsDropped()
        {
            var q = new WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(D(1));
            q.Enqueue(D(2));
            q.Enqueue(D(3));
            q.Enqueue(D(4)); // overflow

            var snap = q.GetSnapshot();
            Check(snap.QueuedFrames <= 3, "36B-3: queue stays bounded after data overflow");
            Check(snap.DroppedDataFrames >= 1, "36B-3b: dropped count incremented");
        }

        private static void TestControlOverflowDisconnectObservable()
        {
            var q = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(C(1));
            q.Enqueue(C(2));
            var result = q.Enqueue(C(3));

            Check(!result.Accepted, "36B-4: control overflow frame not accepted");
            Check(result.ShouldDisconnect, "36B-4b: control overflow requests disconnect");
            Check(result.DroppedDataFrames == 0, "36B-4c: no data frames were available to drop");
        }

        private static void TestSnapshotImmutability()
        {
            var q = new WsSendQueue(maxFrames: 10, maxQueuedBytes: 1024 * 1024);
            q.Enqueue(D(1));
            q.Enqueue(D(2));

            var snap1 = q.GetSnapshot();
            Check(snap1.QueuedFrames == 2, "36B-5: snapshot 1 has 2 frames");

            q.Enqueue(D(3));
            q.Enqueue(D(4));

            // snap1 must NOT change
            Check(snap1.QueuedFrames == 2, "36B-5b: old snapshot unchanged after enqueue");

            var snap2 = q.GetSnapshot();
            Check(snap2.QueuedFrames == 4, "36B-5c: new snapshot reflects current state");
        }

        private static void TestSnapshotClientsNotMutable()
        {
            var backend = new ManagedWsBackend();
            var snap = backend.GetStatsSnapshot();
            Check(snap.Supported, "36B-6: managed backend snapshot is supported");
            Check(!(snap.Clients is System.Collections.Generic.List<TransportClientStats>),
                "36B-6b: Clients is not a mutable List");
        }

        private static void TestDisconnectedClientDropsRetained()
        {
            var port = GetFreeTcpPort();
            using var runtime = new FoxgloveRuntime();
            runtime.Start("phase36-retained-drops", "127.0.0.1", port);
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/phase36/drop",
                Encoding = "json",
                SchemaName = string.Empty,
                Schema = string.Empty
            });

            TcpClient client = null;
            try
            {
                client = ConnectSubscribedRawClient(port, channelId: 1, subscriptionId: 7001);
                Check(WaitFor(() => runtime.GetTransportStatsSnapshot().ActiveClientCount == 1, 2000),
                    "36B-7: raw subscribed client connected");

                var payload = new byte[256 * 1024];
                for (var i = 0; i < payload.Length; i++)
                    payload[i] = (byte)(i & 0xff);

                long droppedBeforeDisconnect = 0;
                for (var i = 0; i < 2000; i++)
                {
                    runtime.Publish(1, payload, (ulong)i);
                    if ((i & 15) != 15)
                        continue;

                    droppedBeforeDisconnect = runtime.GetTransportStatsSnapshot().TotalDroppedDataFrames;
                    if (droppedBeforeDisconnect > 0)
                        break;
                }

                Check(droppedBeforeDisconnect > 0, "36B-7b: slow client triggered data drops");

                client.Close();
                client.Dispose();
                client = null;

                Check(WaitFor(() => runtime.GetTransportStatsSnapshot().ActiveClientCount == 0, 3000),
                    "36B-7c: disconnected client removed from active stats");

                var after = runtime.GetTransportStatsSnapshot();
                Check(after.TotalDroppedDataFrames >= droppedBeforeDisconnect,
                    "36B-7d: aggregate dropped frames retained after disconnect");
            }
            finally
            {
                try { client?.Close(); } catch { }
                try { client?.Dispose(); } catch { }
                runtime.Stop();
            }
        }
        private static void TestRuntimeAccessorLifecycle()
        {
            var runtime = new FoxgloveRuntime();

            // Before start
            var snap = runtime.GetTransportStatsSnapshot();
            Check(!snap.IsRunning, "36C-1: transport not running before start");

            // Start + stop
            runtime.Start("phase36-test", "127.0.0.1", GetFreeTcpPort());
            snap = runtime.GetTransportStatsSnapshot();
            Check(snap.IsRunning, "36C-1b: transport running after start");
            Check(snap.Supported, "36C-1c: managed backend is supported");

            runtime.Stop();

            snap = runtime.GetTransportStatsSnapshot();
            Check(!snap.IsRunning, "36C-1d: transport not running after stop");
            // No exception thrown = pass
        }

        private static QueuedFrame D(byte b) =>
            new(WsOpcode.Binary, new[] { b }, FramePriority.Data);
        private static QueuedFrame C(byte b) =>
            new(WsOpcode.Text, new[] { b }, FramePriority.Control);

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }
            throw new Exception($"[FAIL] {label}");
        }

        private sealed class Phase36FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) { }
            public void Stop() { }
            public void SendText(uint clientId, string json) { }
            public void BroadcastText(string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastBinary(byte[] data) { }
            public void Dispose() { }
        }

        private static TcpClient ConnectSubscribedRawClient(int port, uint channelId, uint subscriptionId)
        {
            var client = new TcpClient
            {
                SendTimeout = 2000,
                ReceiveTimeout = 2000
            };
            client.Connect("127.0.0.1", port);

            var stream = client.GetStream();
            var key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var request =
                "GET / HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {key}\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                $"Sec-WebSocket-Protocol: {Subprotocol.SdkV1}\r\n" +
                "\r\n";

            var requestBytes = Encoding.ASCII.GetBytes(request);
            stream.Write(requestBytes, 0, requestBytes.Length);

            var response = ReadHttpResponse(stream);
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
                throw new Exception("Phase36 raw WebSocket handshake failed: " + response);

            var subscribe =
                "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":" +
                subscriptionId +
                ",\"channelId\":" +
                channelId +
                "}]}";
            SendMaskedTextFrame(stream, subscribe);

            Thread.Sleep(100);
            return client;
        }

        private static string ReadHttpResponse(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var b = stream.ReadByte();
                if (b < 0)
                    break;

                buffer[offset++] = (byte)b;
                if (offset >= 4
                    && buffer[offset - 4] == '\r'
                    && buffer[offset - 3] == '\n'
                    && buffer[offset - 2] == '\r'
                    && buffer[offset - 1] == '\n')
                    break;
            }

            return Encoding.ASCII.GetString(buffer, 0, offset);
        }

        private static void SendMaskedTextFrame(NetworkStream stream, string text)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            var header = new byte[payload.Length <= 125 ? 6 : 8];
            var offset = 0;
            header[offset++] = 0x81;
            if (payload.Length <= 125)
            {
                header[offset++] = (byte)(0x80 | payload.Length);
            }
            else
            {
                header[offset++] = 0xFE;
                header[offset++] = (byte)(payload.Length >> 8);
                header[offset++] = (byte)payload.Length;
            }

            Array.Copy(mask, 0, header, offset, mask.Length);

            var masked = new byte[payload.Length];
            for (var i = 0; i < payload.Length; i++)
                masked[i] = (byte)(payload[i] ^ mask[i % 4]);

            stream.Write(header, 0, header.Length);
            stream.Write(masked, 0, masked.Length);
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;

                Thread.Sleep(10);
            }

            return condition();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
