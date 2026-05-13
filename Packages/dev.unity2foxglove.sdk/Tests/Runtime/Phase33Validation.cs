// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 33 transport backpressure queueing and WebSocket
// frame header encoding behavior.

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase33Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 33 Tests ---");
            _passCount = 0;

            TestDataOverflowDropsOldestData();
            TestControlOverflowDropsDataAndSendsControlFirst();
            TestControlOnlyOverflowRequestsDisconnect();
            TestClearDataFramesPreservesControlFrames();
            TestWebSocketHeaderLengths();
            TestSendQueueCompleteIsIdempotent();
            TestLivePublishUsesDataPrioritySend();

            Console.WriteLine("Phase 33: All checks passed.");
        }

        private static void TestDataOverflowDropsOldestData()
        {
            var queue = new WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024);

            Check(queue.Enqueue(Data(1)).Accepted, "33A-1: data frame 1 accepted");
            Check(queue.Enqueue(Data(2)).Accepted, "33A-1b: data frame 2 accepted");
            Check(queue.Enqueue(Data(3)).Accepted, "33A-1c: data frame 3 accepted");

            var result = queue.Enqueue(Data(4));
            Check(result.Accepted, "33A-1d: overflow data frame accepted after dropping old data");
            Check(result.DroppedDataFrames == 1, "33A-1e: one stale data frame dropped");
            Check(queue.Count == 3, "33A-1f: queue stays within frame bound");

            Check(queue.TryDequeue(out var first) && first.Payload[0] == 2, "33A-1g: oldest remaining data is frame 2");
            Check(queue.TryDequeue(out var second) && second.Payload[0] == 3, "33A-1h: next data is frame 3");
            Check(queue.TryDequeue(out var third) && third.Payload[0] == 4, "33A-1i: newest data is frame 4");
        }

        private static void TestControlOverflowDropsDataAndSendsControlFirst()
        {
            var queue = new WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024);
            queue.Enqueue(Data(1));
            queue.Enqueue(Data(2));
            queue.Enqueue(Data(3));

            var result = queue.Enqueue(Control(9));
            Check(result.Accepted, "33A-2: control accepted into full data queue");
            Check(result.DroppedDataFrames == 1, "33A-2b: oldest data dropped to preserve control");
            Check(queue.Count == 3, "33A-2c: queue remains bounded");
            Check(queue.TryDequeue(out var first) && first.Priority == FramePriority.Control,
                "33A-2d: control frame dequeues before older data");
            Check(first.Payload[0] == 9, "33A-2e: dequeued control frame is the inserted frame");
        }

        private static void TestControlOnlyOverflowRequestsDisconnect()
        {
            var queue = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024);
            queue.Enqueue(Control(1));
            queue.Enqueue(Control(2));

            var result = queue.Enqueue(Control(3));
            Check(!result.Accepted, "33A-3: overflowing control frame is not accepted");
            Check(result.ShouldDisconnect, "33A-3b: control-only overflow requests disconnect");
            Check(result.DroppedDataFrames == 0, "33A-3c: no data frames were available to drop");
            Check(queue.Count == 2, "33A-3d: existing control frames remain queued");
        }

        private static void TestClearDataFramesPreservesControlFrames()
        {
            var queue = new WsSendQueue(maxFrames: 4, maxQueuedBytes: 1024);
            queue.Enqueue(Data(1));
            queue.Enqueue(Control(9));
            queue.Enqueue(Data(2));

            var dropped = queue.ClearDataFrames();
            Check(dropped == 2, "33A-3e: seek reset drops queued data frames");
            Check(queue.Count == 1, "33A-3f: seek reset preserves queued control frames");
            Check(queue.TryDequeue(out var frame) && frame.Priority == FramePriority.Control,
                "33A-3g: preserved control frame still dequeues");
            Check(frame.Payload[0] == 9, "33A-3h: preserved control frame payload is unchanged");
        }

        private static void TestWebSocketHeaderLengths()
        {
            CheckHeader(125, new byte[] { 0x82, 125 }, "33B-1: header length 125");
            CheckHeader(126, new byte[] { 0x82, 126, 0, 126 }, "33B-2: header length 126");
            CheckHeader(65535, new byte[] { 0x82, 126, 255, 255 }, "33B-3: header length 65535");
            CheckHeader(65536, new byte[] { 0x82, 127, 0, 0, 0, 0, 0, 1, 0, 0 }, "33B-4: header length 65536");
        }

        private static void TestSendQueueCompleteIsIdempotent()
        {
            var queue = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024);
            queue.Enqueue(Data(1));
            queue.Complete();
            queue.Complete();

            Check(queue.IsCompleted, "33C-1: queue completion is recorded");
            Check(!queue.Enqueue(Data(2)).Accepted, "33C-1b: completed queue rejects new frames");
            Check(queue.TryDequeue(out var frame) && frame.Payload[0] == 1, "33C-1c: already queued frame can drain after complete");
            Check(!queue.TryDequeue(out _), "33C-1d: completed empty queue has no more frames");
        }

        private static void TestLivePublishUsesDataPrioritySend()
        {
            var transport = new Phase33PriorityTransport();
            var session = new FoxgloveSession("phase33-priority", transport);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/phase33/data",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });

            transport.SimulateConnect(42);
            transport.SimulateText(42, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":7,\"channelId\":1}]}");
            session.Publish(1, new byte[] { 1, 2, 3 }, 123);

            Check(transport.DataBinaryCount == 1, "33A-4: live MessageData uses data-priority send path");
            Check(transport.ControlBinaryCount == 0, "33A-4b: live MessageData does not use control SendBinary path");
        }

        private static void CheckHeader(int payloadLength, byte[] expected, string label)
        {
            var actual = new byte[10];
            var written = WsFrameCodec.WriteFrameHeader(WsOpcode.Binary, payloadLength, actual);
            Check(written == expected.Length, $"{label} byte count");
            for (var i = 0; i < expected.Length; i++)
                Check(actual[i] == expected[i], $"{label} byte {i}");
        }

        private static QueuedFrame Data(byte marker, int length = 1)
        {
            var payload = new byte[length];
            payload[0] = marker;
            return new QueuedFrame(WsOpcode.Binary, payload, FramePriority.Data);
        }

        private static QueuedFrame Control(byte marker)
        {
            return new QueuedFrame(WsOpcode.Text, new[] { marker }, FramePriority.Control);
        }

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

        private sealed class Phase33PriorityTransport : IFoxgloveTransport, IPrioritizedFoxgloveTransport
        {
            public bool IsRunning => true;
            public int ControlBinaryCount { get; private set; }
            public int DataBinaryCount { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void BroadcastDataBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) => ControlBinaryCount++;
            public void SendDataBinary(uint clientId, byte[] data) => DataBinaryCount++;

            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
