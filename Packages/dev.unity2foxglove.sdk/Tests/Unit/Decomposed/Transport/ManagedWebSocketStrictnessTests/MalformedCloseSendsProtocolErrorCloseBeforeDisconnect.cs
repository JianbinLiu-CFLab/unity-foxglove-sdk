// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Strict RFC 6455 frame validation and restart-generation ownership.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{


public sealed partial class ManagedWebSocketStrictnessTests
    {
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
        public void OversizedFragmentedMessageSendsMessageTooBigClose()
        {
            var frames = new List<byte[]>
            {
                BuildMaskedFrame(WsOpcode.Binary, new byte[ushort.MaxValue], fin: false)
            };
            for (var index = 0; index < 64; index++)
            {
                frames.Add(
                    BuildMaskedFrame(
                        WsOpcode.Continuation,
                        new byte[ushort.MaxValue],
                        fin: index == 63));
            }

            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(JoinFrames(frames));
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[4] = connection;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 4, connection);

            var written = stream.WrittenBytes;
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1009, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

        [Fact]
        public void FragmentedMessageFrameLimitIsIndependentFromSendQueueCapacity()
        {
            var options = new ManagedWebSocketOptions
            {
                MaxQueuedFramesPerClient = 64,
                MaxFragmentedMessageFrames = 2
            };
            var frames = new List<byte[]>
            {
                BuildMaskedFrame(WsOpcode.Binary, new byte[] { 1 }, fin: false),
                BuildMaskedFrame(WsOpcode.Continuation, new byte[] { 2 }, fin: false),
                BuildMaskedFrame(WsOpcode.Continuation, new byte[] { 3 })
            };

            using var backend = new ManagedWsBackend(options);
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(JoinFrames(frames));
            var connection = new WsConnection(tcpClient, stream, options.MaxQueuedFramesPerClient, 1024);
            var clients = Clients(backend);
            clients[5] = connection;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 5, connection);

            var written = stream.WrittenBytes;
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1009, (written[2] << 8) | written[3]);
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

        private static int PendingClientCount(ManagedWsBackend backend)
        {
            var field = typeof(ManagedWsBackend).GetField(
                "_pendingClients",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return 0;

            var value = field.GetValue(backend) as System.Collections.ICollection;
            return value?.Count ?? 0;
        }

        private static int ClientHandlerCount(ManagedWsBackend backend)
        {
            var field = typeof(ManagedWsBackend).GetField(
                "_clientHandlers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return 0;

            var value = field.GetValue(backend) as System.Collections.ICollection;
            return value?.Count ?? 0;
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static string ReadHttpHeaders(NetworkStream stream)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 8192)
            {
                int value;
                try
                {
                    value = stream.ReadByte();
                }
                catch (IOException)
                {
                    return "";
                }
                if (value < 0)
                    break;
                bytes.Add((byte)value);
                var count = bytes.Count;
                if (count >= 4
                    && bytes[count - 4] == '\r'
                    && bytes[count - 3] == '\n'
                    && bytes[count - 2] == '\r'
                    && bytes[count - 1] == '\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static (byte Opcode, byte[] Payload) ReadServerFrame(NetworkStream stream)
        {
            var first = stream.ReadByte();
            var second = stream.ReadByte();
            if (first < 0 || second < 0)
                throw new IOException("The server closed the WebSocket before sending a frame.");

            var payloadLength = second & 0x7F;
            if (payloadLength == 126)
            {
                var extended = new byte[2];
                ReadExact(stream, extended);
                payloadLength = (extended[0] << 8) | extended[1];
            }
            else if (payloadLength == 127)
            {
                var extended = new byte[8];
                ReadExact(stream, extended);
                ulong length = 0;
                foreach (var value in extended)
                    length = (length << 8) | value;
                if (length > int.MaxValue)
                    throw new InvalidOperationException("The test frame is too large.");
                payloadLength = (int)length;
            }

            var payload = new byte[payloadLength];
            if (payload.Length > 0)
                ReadExact(stream, payload);
            return ((byte)(first & 0x0F), payload);
        }

        private static void WriteClientFrame(NetworkStream stream, byte opcode, byte[] payload)
        {
            var frame = BuildMaskedFrame(opcode, payload);
            stream.Write(frame, 0, frame.Length);
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new IOException("The server closed the WebSocket while sending a frame.");
                offset += read;
            }
        }

        private static TcpClient ConnectAndWriteHandshake(int port)
        {
            var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;
            var request =
                "GET / HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                "Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            return client;
        }

        private static string BuildHandshakeRequest(
            string connection = "Upgrade",
            string upgrade = "websocket",
            bool includeKey = true)
        {
            var request = new StringBuilder()
                .Append("GET / HTTP/1.1\r\n")
                .Append("Host: 127.0.0.1\r\n");
            if (connection != null)
                request.Append("Connection: ").Append(connection).Append("\r\n");
            if (upgrade != null)
                request.Append("Upgrade: ").Append(upgrade).Append("\r\n");
            if (includeKey)
                request.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
            request
                .Append("Sec-WebSocket-Version: 13\r\n")
                .Append("Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n\r\n");
            return request.ToString();
        }

    }
}
