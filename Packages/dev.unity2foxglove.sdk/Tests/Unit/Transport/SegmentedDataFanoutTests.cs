// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Transport
// Purpose: Verify shared-body fanout and segmented WebSocket frame behavior.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{
    public sealed class SegmentedDataFanoutTests
    {
        [Fact]
        public void SessionFanoutUsesOneSharedBodyAndDistinctHeaders()
        {
            var transport = new Plan199SegmentedCaptureTransport();
            using var session = Plan199BuildSession(transport);
            var payload = new byte[] { 1, 2, 3, 4 };

            Plan199ConnectSubscriber(transport, 11, 101);
            Plan199ConnectSubscriber(transport, 12, 202);
            session.Publish(1, payload, 123456789UL);

            Assert.Equal(2, transport.SegmentedFrames.Count);
            var first = transport.SegmentedFrames[0];
            var second = transport.SegmentedFrames[1];
            Assert.NotSame(first.Header, second.Header);
            Assert.Same(first.Body, second.Body);
            Assert.Equal(payload, first.Body);
            Assert.Equal(
                BinaryEncoding.EncodeServerMessageData(101, 123456789UL, Array.Empty<byte>()),
                first.Header);
            Assert.Equal(
                BinaryEncoding.EncodeServerMessageData(202, 123456789UL, Array.Empty<byte>()),
                second.Header);
            Assert.Equal(
                BinaryEncoding.EncodeServerMessageData(101, 123456789UL, payload),
                Plan199JoinFrame(first.Header, first.Body));
            Assert.Equal(
                BinaryEncoding.EncodeServerMessageData(202, 123456789UL, payload),
                Plan199JoinFrame(second.Header, second.Body));
        }

        [Fact]
        public void SessionFanoutCopiesPayloadBeforeReturningToCaller()
        {
            var transport = new Plan199SegmentedCaptureTransport();
            using var session = Plan199BuildSession(transport);
            var payload = new byte[] { 5, 6, 7, 8 };

            Plan199ConnectSubscriber(transport, 11, 101);
            session.Publish(1, payload, 20UL);
            payload[0] = 99;
            payload[1] = 98;

            var frame = Assert.Single(transport.SegmentedFrames);
            Assert.Equal(new byte[] { 5, 6, 7, 8 }, frame.Body);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(126)]
        [InlineData(70000)]
        public void SegmentedWebSocketFrameMatchesSinglePayloadFrame(int payloadLength)
        {
            var header = new byte[] { 0xA1, 0xB2, 0xC3 };
            var body = Enumerable.Range(0, payloadLength - header.Length)
                .Select(value => (byte)(value % 251))
                .ToArray();
            using var segmented = new MemoryStream();
            using var single = new MemoryStream();

            WsFrameCodec.WriteFrame(segmented, WsOpcode.Binary, header, body, flush: false);
            WsFrameCodec.WriteFrame(single, WsOpcode.Binary, Plan199JoinFrame(header, body), flush: false);

            Assert.Equal(Plan199ExpectedFrame(WsOpcode.Binary, header, body), segmented.ToArray());
            Assert.Equal(single.ToArray(), segmented.ToArray());
        }

        [Theory]
        [InlineData(13, 2)]
        [InlineData(33, 3)]
        public void SegmentedCodecUsesBoundedWriteCount(int prefixLength, int expectedWriteCount)
        {
            var prefix = Enumerable.Repeat((byte)0xA1, prefixLength).ToArray();
            var payload = new byte[] { 1, 2, 3, 4 };
            using var stream = new Plan199FlushStream();

            WsFrameCodec.WriteFrame(stream, WsOpcode.Binary, prefix, payload, flush: false);

            Assert.Equal(expectedWriteCount, stream.WriteCount);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(126)]
        [InlineData(70000)]
        public async Task WsConnectionWritesSegmentedFrameBytes(int payloadLength)
        {
            var header = new byte[] { 0xD1, 0xE2, 0xF3 };
            var body = Enumerable.Range(0, payloadLength - header.Length)
                .Select(value => (byte)(value % 239))
                .ToArray();
            using var stream = new Plan199FlushStream();
            using var connection = new WsConnection(null, stream, 8, 1024 * 1024);

            Assert.True(connection.SendSegmentedBinary(header, body, FramePriority.Data).Accepted);
            connection.StartSendLoop(null, CancellationToken.None);
            await stream.FlushCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(connection.WaitForPendingSends(TimeSpan.FromSeconds(5)));
            Assert.Equal(Plan199ExpectedFrame(WsOpcode.Binary, header, body), stream.ToArray());
        }

        [Fact]
        public void SegmentedQueueUsesCombinedSizeAndPreservesPriorityAndDrops()
        {
            var queue = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 10);
            var first = new QueuedFrame(WsOpcode.Binary, new byte[] { 1, 2 }, new byte[] { 3, 4, 5 }, FramePriority.Data);
            var second = new QueuedFrame(WsOpcode.Binary, new byte[] { 6 }, new byte[] { 7, 8, 9 }, FramePriority.Data);
            var control = new QueuedFrame(WsOpcode.Text, new byte[] { 10 }, new byte[] { 11 }, FramePriority.Control);

            Assert.Equal(5, first.SizeBytes);
            Assert.True(queue.Enqueue(first).Accepted);
            Assert.True(queue.Enqueue(second).Accepted);
            var controlResult = queue.Enqueue(control);

            Assert.True(controlResult.Accepted);
            Assert.Equal(1, controlResult.DroppedDataFrames);
            Assert.Equal(6, queue.QueuedBytes);
            Assert.True(queue.TryDequeue(out var dequeued));
            Assert.Equal(FramePriority.Control, dequeued.Priority);
            Assert.Equal(2, dequeued.SizeBytes);
            Assert.Equal(1, queue.DroppedDataFramesSnapshot);
        }

        [Fact]
        public void SessionFallsBackToSinglePayloadFrameWithoutSegmentedCapability()
        {
            var transport = new Plan199PlainCaptureTransport();
            using var session = Plan199BuildSession(transport);
            var payload = new byte[] { 12, 13, 14 };

            Plan199ConnectSubscriber(transport, 11, 101);
            session.Publish(1, payload, 99UL);

            var frame = Assert.Single(transport.FullFrames);
            Assert.Equal(BinaryEncoding.EncodeServerMessageData(101, 99UL, payload), frame);
        }

        [Fact]
        public void ReplayToClientWithoutMatchingSubscriptionDoesNotQueueSegments()
        {
            var transport = new Plan199SegmentedCaptureTransport();
            using var session = Plan199BuildSession(transport);
            session.RegisterSchemaChannel(2, "/plan199/other", "test.Plan199Payload", "json");
            Plan199ConnectSubscriberForChannel(transport, 11, 202, 2);

            session.PublishReplayToClient(11, 1, new byte[] { 31, 32, 33 }, 100UL);

            Assert.Empty(transport.SegmentedFrames);
        }

        [Fact]
        public void ManagedBackendExposesSegmentedDataCapability()
        {
            using var backend = new ManagedWsBackend();

            Assert.IsAssignableFrom<ISegmentedDataFoxgloveTransport>(backend);
        }

        private static FoxgloveSession Plan199BuildSession(IFoxgloveTransport transport)
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "test.Plan199Payload",
                Encoding = "jsonschema",
                Content = "{}"
            });
            var session = new FoxgloveSession("plan199-segmented", transport, schemaRegistry: registry);
            session.RegisterSchemaChannel(1, "/plan199/segmented", "test.Plan199Payload", "json");
            return session;
        }

        private static void Plan199ConnectSubscriber(
            Plan199PlainCaptureTransport transport,
            uint clientId,
            uint subscriptionId)
        {
            Plan199ConnectSubscriberForChannel(transport, clientId, subscriptionId, 1);
        }

        private static void Plan199ConnectSubscriberForChannel(
            Plan199PlainCaptureTransport transport,
            uint clientId,
            uint subscriptionId,
            uint channelId)
        {
            transport.Plan199ConnectClient(clientId);
            transport.Plan199ReceiveText(
                clientId,
                $"{{\"op\":\"subscribe\",\"subscriptions\":[{{\"id\":{subscriptionId},\"channelId\":{channelId}}}]}}");
        }

        private static byte[] Plan199JoinFrame(byte[] header, byte[] body)
        {
            var result = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(body, 0, result, header.Length, body.Length);
            return result;
        }

        private static byte[] Plan199ExpectedFrame(byte opcode, byte[] prefix, byte[] body)
        {
            var payloadLength = checked(prefix.Length + body.Length);
            using var stream = new MemoryStream();
            stream.WriteByte((byte)(0x80 | opcode));
            if (payloadLength <= 125)
            {
                stream.WriteByte((byte)payloadLength);
            }
            else if (payloadLength <= ushort.MaxValue)
            {
                stream.WriteByte(126);
                stream.WriteByte((byte)(payloadLength >> 8));
                stream.WriteByte((byte)payloadLength);
            }
            else
            {
                stream.WriteByte(127);
                for (var shift = 56; shift >= 0; shift -= 8)
                    stream.WriteByte((byte)((ulong)payloadLength >> shift));
            }

            stream.Write(prefix, 0, prefix.Length);
            stream.Write(body, 0, body.Length);
            return stream.ToArray();
        }

        private sealed class Plan199FlushStream : MemoryStream
        {
            public TaskCompletionSource<bool> FlushCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int WriteCount { get; private set; }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                WriteCount++;
                base.Write(buffer.ToArray(), 0, buffer.Length);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                WriteCount++;
                base.Write(buffer, offset, count);
            }

            public override void Flush()
            {
                base.Flush();
                FlushCompleted.TrySetResult(true);
            }
        }

        private sealed class Plan199CapturedSegment
        {
            public Plan199CapturedSegment(byte[] header, byte[] body)
            {
                Header = header;
                Body = body;
            }

            public byte[] Header { get; }
            public byte[] Body { get; }
        }

        private class Plan199PlainCaptureTransport : IFoxgloveTransport, IPrioritizedFoxgloveTransport
        {
            public bool IsRunning => true;
            public readonly List<byte[]> FullFrames = new();
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) => FullFrames.Add(data);
            public void SendDataBinary(uint clientId, byte[] data) => FullFrames.Add(data);
            public void BroadcastDataBinary(byte[] data) { }
            public void Dispose() { }

            public void Plan199ConnectClient(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void Plan199ReceiveText(uint clientId, string text) => OnTextReceived?.Invoke(clientId, text);
        }

        private sealed class Plan199SegmentedCaptureTransport : Plan199PlainCaptureTransport, ISegmentedDataFoxgloveTransport
        {
            public readonly List<Plan199CapturedSegment> SegmentedFrames = new();

            public void SendDataBinary(uint clientId, byte[] header, byte[] sharedBody)
            {
                SegmentedFrames.Add(new Plan199CapturedSegment(header, sharedBody));
            }
        }
    }
}
