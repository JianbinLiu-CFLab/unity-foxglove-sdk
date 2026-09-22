using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module3ReviewRemediationTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(4)]
        public void InboundFramesAreBoundedBeforeAllocation(int configuredLimit)
        {
            Assert.Equal(4 * 1024 * 1024, new ManagedWebSocketOptions().MaxInboundFrameBytes);
            int limit = configuredLimit > 0 ? configuredLimit : 4 * 1024 * 1024;
            foreach (int length in new[] { limit, limit + 1 })
            {
                using var stream = new MemoryStream();
                stream.WriteByte(0x82);
                if (length <= 125)
                    stream.WriteByte((byte)(0x80 | length));
                else
                {
                    stream.WriteByte(0xFF);
                    for (int shift = 56; shift >= 0; shift -= 8)
                        stream.WriteByte((byte)((ulong)length >> shift));
                }
                long headerLength = stream.Length;
                if (length == limit)
                    stream.Write(new byte[4 + length], 0, 4 + length);
                stream.Position = 0;
                using var connection = new WsConnection(null, stream, 8, 1024, configuredLimit);
                var frame = connection.ReadFrame(out var result);
                Assert.Equal(length == limit ? WsFrameReadResult.Success : WsFrameReadResult.MessageTooBig, result);
                if (length == limit)
                {
                    Assert.NotNull(frame);
                    Assert.Equal(length, frame.Payload.Length);
                    Assert.Equal(stream.Length, stream.Position);
                }
                else
                {
                    Assert.Null(frame);
                    Assert.Equal(headerLength, stream.Position);
                }
            }
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

        [Theory]
        [InlineData("valid", true, "HTTP/1.1 101")]
        [InlineData("http10", false, "")]
        [InlineData("missing-host", false, "HTTP/1.1 400")]
        [InlineData("duplicate-host", false, "")]
        [InlineData("duplicate-protocol", false, "")]
        [InlineData("protocol-case", false, "HTTP/1.1 400")]
        [InlineData("protocol-list", true, "HTTP/1.1 101")]
        public void HandshakeEnforcesHttpHostDuplicateAndSubprotocolGuards(string scenario, bool accepted, string response)
        {
            string request = HandshakeRequest();
            if (scenario == "http10") request = request.Replace("HTTP/1.1", "HTTP/1.0");
            if (scenario == "missing-host") request = request.Replace("Host: localhost\r\n", "");
            if (scenario == "duplicate-host") request = request.Replace("Host: localhost\r\n", "Host: localhost\r\nhOsT: other\r\n");
            if (scenario == "duplicate-protocol") request = request.Replace("Sec-WebSocket-Protocol: ", "sec-websocket-protocol: foxglove.websocket.v1\r\nSec-WebSocket-Protocol: ");
            if (scenario == "protocol-case") request = request.Replace("foxglove.websocket.v1", "FOXGLOVE.WEBSOCKET.V1");
            if (scenario == "protocol-list") request = request.Replace("foxglove.websocket.v1", "other, foxglove.websocket.v1");
            AssertHandshake(request, new ManagedWebSocketOptions(), accepted, response);
        }

        [Theory]
        [InlineData(false, "", "", false, "HTTP/1.1 403")]
        [InlineData(true, "", "", false, "HTTP/1.1 403")]
        [InlineData(false, "secret", "secret", false, "HTTP/1.1 403")]
        [InlineData(true, "secret", "", false, "HTTP/1.1 401")]
        [InlineData(true, "secret", "wrong", false, "HTTP/1.1 401")]
        [InlineData(true, "secret", "secret", true, "HTTP/1.1 101")]
        public void NullOriginRequiresExplicitOptInAndValidToken(bool allow, string configuredToken, string suppliedToken, bool accepted, string response)
        {
            var request = HandshakeRequest("/?token=" + suppliedToken, "Origin: null\r\n");
            AssertHandshake(request, new ManagedWebSocketOptions { AllowOpaqueOrigin = allow, SharedToken = configuredToken }, accepted, response);
        }

        [Fact]
        public void OversizedDataDropsStaleDataWithoutDisconnecting()
        {
            var queue = new WsSendQueue(4, 8);
            var first = queue.Enqueue(new QueuedFrame(2, new byte[] { 1, 2 }, FramePriority.Data));
            var oversized = queue.Enqueue(new QueuedFrame(2, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, FramePriority.Data));
            Assert.True(first.Accepted);
            Assert.False(oversized.Accepted);
            Assert.False(oversized.ShouldDisconnect);
            Assert.Equal(0, queue.GetSnapshot().QueuedDataFrames);
            Assert.Equal(0, queue.QueuedBytes);
            Assert.Equal(2, oversized.DroppedDataFrames);
        }

        [Fact]
        public async Task BinarySendCopiesCallerBuffer()
        {
            using var stream = new ProbeStream(Array.Empty<byte>());
            using var connection = new WsConnection(null, stream, 8, 1024);
            var bytes = new byte[] { 1, 2, 3 };
            Assert.True(connection.SendBinary(bytes, FramePriority.Data).Accepted);
            bytes[0] = 99;
            connection.StartSendLoop(null, CancellationToken.None);
            await stream.Flushed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { 0x82, 3, 1, 2, 3 }, stream.Output.ToArray());
        }

        [Fact]
        public async Task SendFailureCallbackDoesNotWaitForItsOwnSendLoop()
        {
            using var stream = new ProbeStream(Array.Empty<byte>()) { FailWrites = true };
            using var connection = new WsConnection(null, stream, 8, 1024);
            var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.StartSendLoop(() => observed.TrySetResult(connection.WaitForSendLoop(TimeSpan.FromMilliseconds(50))), CancellationToken.None);
            Assert.True(connection.SendBinary(new byte[] { 1 }, FramePriority.Data).Accepted);
            Assert.True(await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(connection.WaitForSendLoop(TimeSpan.FromSeconds(5)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TransportObserversAreInvokedIndividually(bool throwOnConnect)
        {
            using var backend = new ManagedWsBackend();
            var connected = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnected = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var text = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var binary = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            backend.OnClientConnected += _ => { if (throwOnConnect) throw new InvalidOperationException("connect observer"); };
            backend.OnClientConnected += id => connected.TrySetResult(id);
            backend.OnClientDisconnected += _ => throw new InvalidOperationException("disconnect observer");
            backend.OnClientDisconnected += id => disconnected.TrySetResult(id);
            backend.OnTextReceived += (_, _) => throw new InvalidOperationException("text observer");
            backend.OnTextReceived += (_, value) => text.TrySetResult(value);
            backend.OnBinaryReceived += (_, _) => throw new InvalidOperationException("binary observer");
            backend.OnBinaryReceived += (_, value) => binary.TrySetResult(value);
            backend.Start("127.0.0.1", 0);
            var listener = (TcpListener)typeof(ManagedWsBackend).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend);
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var client = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            client.Options.AddSubProtocol("foxglove.websocket.v1");
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), timeout.Token);
            uint id = await connected.Task.WaitAsync(timeout.Token);
            if (!throwOnConnect)
            {
                await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hello")), WebSocketMessageType.Text, true, timeout.Token);
                Assert.Equal("hello", await text.Task.WaitAsync(timeout.Token));
                await client.SendAsync(new ArraySegment<byte>(new byte[] { 7, 8 }), WebSocketMessageType.Binary, true, timeout.Token);
                Assert.Equal(new byte[] { 7, 8 }, await binary.Task.WaitAsync(timeout.Token));
                client.Abort();
            }
            Assert.Equal(id, await disconnected.Task.WaitAsync(timeout.Token));
        }

        private static string HandshakeRequest(string target = "/", string extraHeaders = "") =>
            $"GET {target} HTTP/1.1\r\nHost: localhost\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" +
            "Sec-WebSocket-Protocol: foxglove.websocket.v1\r\n" + extraHeaders + "\r\n";

        private static void AssertHandshake(string request, ManagedWebSocketOptions options, bool accepted, string response)
        {
            using var stream = new ProbeStream(Encoding.ASCII.GetBytes(request));
            var handler = new WsHandshakeHandler(options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new object(), null);
            var result = handler.Handshake(stream);
            Assert.Equal(accepted, result.accepted);
            Assert.Equal(accepted ? "foxglove.websocket.v1" : null, result.subprotocol);
            string actualResponse = Encoding.ASCII.GetString(stream.Output.ToArray());
            if (response.Length == 0) Assert.Equal("", actualResponse);
            else Assert.StartsWith(response, actualResponse);
        }

        private sealed class ProbeStream : Stream
        {
            private readonly MemoryStream _input;
            public readonly MemoryStream Output = new MemoryStream();
            public readonly TaskCompletionSource<bool> Flushed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool FailWrites;
            public ProbeStream(byte[] input) => _input = new MemoryStream(input);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
            public override int ReadByte() => _input.ReadByte();
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (FailWrites) throw new IOException("injected stream failure");
                Output.Write(buffer, offset, count);
            }
            public override void Flush() => Flushed.TrySetResult(true);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) { _input.Dispose(); Output.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
