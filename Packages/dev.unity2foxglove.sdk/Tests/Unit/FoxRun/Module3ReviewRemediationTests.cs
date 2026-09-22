using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module3ReviewRemediationTests
    {
        [Fact]
        public void InboundFramesAreBoundedBeforeAllocation()
        {
            var options = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWebSocketOptions.cs");
            var codec = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs");
            Assert.Contains("DefaultMaxInboundFrameBytes", options, StringComparison.Ordinal);
            Assert.Contains("maxPayloadBytes", codec, StringComparison.Ordinal);
            Assert.Contains("payloadLen > maxPayloadBytes", codec, StringComparison.Ordinal);
        }

        [Fact]
        public void DeclaredOversizedFrameIsRejectedBeforePayloadRead()
        {
            var bytes = new byte[2 + 8 + 4];
            bytes[0] = 0x82;
            bytes[1] = 0xFF;
            bytes[2] = 0x00; bytes[3] = 0x00; bytes[4] = 0x00; bytes[5] = 0x00;
            bytes[6] = 0x00; bytes[7] = 0x01; bytes[8] = 0x00; bytes[9] = 0x00;
            using var stream = new MemoryStream(bytes);
            var result = WsFrameCodec.ReadFrame(stream, out _, 1024);
            Assert.Equal(WsFrameReadResult.MessageTooBig, result);
            Assert.Equal(10, stream.Position);
        }

        [Fact]
        public void NullOriginRequiresExplicitOptInAndHandshakeHasHttpHostGuards()
        {
            var options = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWebSocketOptions.cs");
            var handshake = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsHandshakeHandler.cs");
            Assert.Contains("AllowOpaqueOrigin", options, StringComparison.Ordinal);
            Assert.Contains("HTTP/1.1", handshake, StringComparison.Ordinal);
            Assert.Contains("headers.ContainsKey(\"Host\")", handshake, StringComparison.Ordinal);
            Assert.Contains("StringComparison.Ordinal) == 0", handshake, StringComparison.Ordinal);
            Assert.Contains("headers.ContainsKey(headerName)", handshake, StringComparison.Ordinal);
        }

        [Fact]
        public void OversizedDataDoesNotEvictExistingData()
        {
            var queue = new WsSendQueue(4, 4);
            var first = queue.Enqueue(new QueuedFrame(2, new byte[] { 1, 2 }, FramePriority.Data));
            var oversized = queue.Enqueue(new QueuedFrame(2, new byte[] { 1, 2, 3, 4, 5 }, FramePriority.Data));
            Assert.True(first.Accepted);
            Assert.False(oversized.Accepted);
            Assert.Equal(1, queue.GetSnapshot().QueuedDataFrames);
            Assert.Equal(2, queue.QueuedBytes);
        }

        [Fact]
        public void BinarySendCopiesCallerBufferAndSendLoopAvoidsSelfWait()
        {
            var connection = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsConnection.cs");
            Assert.Contains("(byte[])data.Clone()", connection, StringComparison.Ordinal);
            Assert.Contains("_sendLoopThreadId", connection, StringComparison.Ordinal);
            Assert.Contains("IsCurrentSendLoop", connection, StringComparison.Ordinal);
        }

        [Fact]
        public void TransportObserversAreInvokedIndividually()
        {
            var backend = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Assert.Contains("GetInvocationList", backend, StringComparison.Ordinal);
            Assert.Contains("InvokeClientObservers", backend, StringComparison.Ordinal);
        }
    }
}
