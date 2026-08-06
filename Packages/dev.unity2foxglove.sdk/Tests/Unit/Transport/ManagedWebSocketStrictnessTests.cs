// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Strict RFC 6455 frame validation and restart-generation ownership.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Transport")]
    public sealed class ManagedWebSocketStrictnessTests
    {
        [Fact]
        public void NullTextIsSentAsAnEmptyTextFrame()
        {
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(Array.Empty<byte>());
            using var connection = new WsConnection(tcpClient, stream, 8, 1024);
            connection.StartSendLoop(null, CancellationToken.None);

            var result = connection.SendText(null, FramePriority.Control);

            Assert.True(result.Accepted);
            Assert.True(
                SpinWait.SpinUntil(() => stream.WrittenBytes.Length >= 2, TimeSpan.FromSeconds(1)),
                "The empty text frame was not written before the test deadline.");
            Assert.Equal(new byte[] { 0x81, 0x00 }, stream.WrittenBytes);
        }

        [Fact]
        public void CodecClassifiesNonMinimalLengthsAndMalformedClosePayloads()
        {
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildNonMinimalMaskedFrame(WsOpcode.Binary, 126, 1), out _));
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildNonMinimalMaskedFrame(WsOpcode.Binary, 127, 126), out _));
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildMaskedFrame(WsOpcode.Close, new byte[] { 0x03 }), out _));
            Assert.Equal(
                WsFrameReadResult.InvalidPayloadData,
                ReadResult(
                    BuildMaskedFrame(
                        WsOpcode.Close,
                        new byte[] { 0x03, 0xE8, 0xC3, 0x28 }),
                    out _));
        }

        [Fact]
        public void InvalidTextSendsInvalidPayloadCloseWithoutApplicationCallback()
        {
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(
                BuildMaskedFrame(WsOpcode.Text, new byte[] { 0xC3, 0x28 }));
            var connection = new WsConnection(
                tcpClient,
                stream,
                ManagedWebSocketOptions.DefaultMaxQueuedFrames,
                ManagedWebSocketOptions.DefaultMaxQueuedBytes);
            var clients = Clients(backend);
            clients[1] = connection;
            var textCallbacks = 0;
            backend.OnTextReceived += (_, _) => textCallbacks++;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 1, connection);

            var written = stream.WrittenBytes;
            Assert.Equal(0, textCallbacks);
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1007, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

        [Fact]
        public void MalformedCloseSendsProtocolErrorCloseBeforeDisconnect()
        {
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(
                BuildMaskedFrame(WsOpcode.Close, new byte[] { 0x03 }));
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[2] = connection;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 2, connection);

            var written = stream.WrittenBytes;
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1002, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

        [Fact]
        public void StrictUtf8DecodeAllowsCodePointAcrossFragmentBoundary()
        {
            var first = BuildMaskedFrame(WsOpcode.Text, new byte[] { 0xE2 }, fin: false);
            var second = BuildMaskedFrame(WsOpcode.Continuation, new byte[] { 0x82, 0xAC });
            var input = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, input, 0, first.Length);
            Buffer.BlockCopy(second, 0, input, first.Length, second.Length);
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(input);
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[3] = connection;
            string receivedText = null;
            backend.OnTextReceived += (_, text) => receivedText = text;

            InvokeReceiveLoop(backend, 3, connection);

            Assert.Equal("€", receivedText);
            Assert.Empty(clients);
        }

        [Fact]
        public void ClientIdsRemainMonotonicAcrossStop()
        {
            using var backend = new ManagedWsBackend();
            var allocate = typeof(ManagedWsBackend).GetMethod(
                "AllocateClientId",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(allocate);
            Assert.Equal(1u, (uint)allocate.Invoke(backend, null));

            backend.Stop();

            Assert.Equal(2u, (uint)allocate.Invoke(backend, null));
        }

        [Fact]
        public void StaleDisconnectCannotRemoveAReplacementConnection()
        {
            using var backend = new ManagedWsBackend();
            using var oldTcpClient = new TcpClient();
            using var newTcpClient = new TcpClient();
            var oldStream = new DuplexBufferStream(Array.Empty<byte>());
            var newStream = new DuplexBufferStream(Array.Empty<byte>());
            var oldConnection = new WsConnection(oldTcpClient, oldStream, 8, 1024);
            var newConnection = new WsConnection(newTcpClient, newStream, 8, 1024);
            var clients = Clients(backend);
            clients[7] = newConnection;
            var disconnectCallbacks = 0;
            backend.OnClientDisconnected += _ => disconnectCallbacks++;

            InvokeDisconnect(backend, 7, oldConnection);

            Assert.True(clients.TryGetValue(7, out var retained));
            Assert.Same(newConnection, retained);
            Assert.Equal(0, disconnectCallbacks);
            Assert.True(oldStream.IsDisposed);
            Assert.False(newStream.IsDisposed);

            InvokeDisconnect(backend, 7, newConnection);
            Assert.Empty(clients);
            Assert.Equal(1, disconnectCallbacks);
            Assert.True(newStream.IsDisposed);
        }

        private static WsFrameReadResult ReadResult(byte[] bytes, out WsFrame frame)
        {
            using var stream = new MemoryStream(bytes);
            return WsFrameCodec.ReadFrame(stream, out frame);
        }

        private static ConcurrentDictionary<uint, WsConnection> Clients(ManagedWsBackend backend)
        {
            var field = typeof(ManagedWsBackend).GetField(
                "_clients",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<ConcurrentDictionary<uint, WsConnection>>(field.GetValue(backend));
        }

        private static void InvokeReceiveLoop(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "ReceiveLoop",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection, CancellationToken.None });
        }

        private static void InvokeDisconnect(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "DisconnectClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection });
        }

        private static byte[] BuildMaskedFrame(byte opcode, byte[] payload, bool fin = true)
        {
            payload ??= Array.Empty<byte>();
            if (payload.Length > 125)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var frame = new byte[2 + 4 + payload.Length];
            frame[0] = (byte)((fin ? 0x80 : 0x00) | opcode);
            frame[1] = (byte)(0x80 | payload.Length);
            WriteMaskedPayload(frame, 2, payload);
            return frame;
        }

        private static byte[] BuildNonMinimalMaskedFrame(byte opcode, byte marker, ulong payloadLength)
        {
            var extendedLengthBytes = marker == 126 ? 2 : 8;
            var payload = new byte[checked((int)payloadLength)];
            var frame = new byte[2 + extendedLengthBytes + 4 + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            frame[1] = (byte)(0x80 | marker);
            for (var i = 0; i < extendedLengthBytes; i++)
            {
                var shift = (extendedLengthBytes - i - 1) * 8;
                frame[2 + i] = (byte)(payloadLength >> shift);
            }

            WriteMaskedPayload(frame, 2 + extendedLengthBytes, payload);
            return frame;
        }

        private static void WriteMaskedPayload(byte[] frame, int maskOffset, byte[] payload)
        {
            var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            Buffer.BlockCopy(mask, 0, frame, maskOffset, mask.Length);
            for (var i = 0; i < payload.Length; i++)
                frame[maskOffset + mask.Length + i] = (byte)(payload[i] ^ mask[i % mask.Length]);
        }

        private sealed class DuplexBufferStream : Stream
        {
            private readonly MemoryStream _input;
            private readonly MemoryStream _output = new MemoryStream();
            private readonly object _outputLock = new object();

            public DuplexBufferStream(byte[] input)
            {
                _input = new MemoryStream(input ?? Array.Empty<byte>(), writable: false);
            }

            public byte[] WrittenBytes
            {
                get
                {
                    lock (_outputLock)
                    {
                        return _output.ToArray();
                    }
                }
            }

            public bool IsDisposed { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
                => _input.Read(buffer, offset, count);

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_outputLock)
                {
                    _output.Write(buffer, offset, count);
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
