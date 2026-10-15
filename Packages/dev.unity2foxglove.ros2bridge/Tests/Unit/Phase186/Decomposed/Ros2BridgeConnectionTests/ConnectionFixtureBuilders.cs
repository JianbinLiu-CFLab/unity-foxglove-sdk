// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first real-stream checks for the bounded duplex connection.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeConnectionTests
    {
        private static U2R2Message Parse(byte[] wireBytes)
            => U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(wireBytes));

        private static JObject HelloAck(
            ulong requestId,
            bool includeSubscribe = false)
            => new JObject
            {
                ["capabilities"] =
                    includeSubscribe
                        ? new JArray("publish", "subscribe")
                        : new JArray("publish"),
                ["connectionGeneration"] = 19,
                ["op"] = "hello_ack",
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = "phase186-session",
                ["status"] = "ok",
            };

        private static JObject Response(
            string operation,
            ulong requestId,
            string sessionId,
            ulong connectionGeneration)
            => new JObject
            {
                ["connectionGeneration"] = connectionGeneration,
                ["op"] = operation,
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = sessionId,
                ["status"] = "ok",
            };

        private static JObject MessageHeader()
            => new JObject
            {
                ["connectionGeneration"] = 19,
                ["contractId"] = 11,
                ["encoding"] = "cdr",
                ["messageId"] = 1,
                ["op"] = "message",
                ["protocolVersion"] = 2,
                ["receiveTimeNs"] = 2,
                ["representation"] = "xcdr1-le",
                ["schemaName"] = "phase186_msgs/msg/Inbound",
                ["sequence"] = 1,
                ["sessionId"] = "phase186-session",
                ["topic"] = "/phase186/inbound",
            };

        private static JObject ContractResponse(
            string operation,
            ulong requestId,
            ulong contractId)
            => new JObject
            {
                ["connectionGeneration"] = 19,
                ["contractId"] = contractId,
                ["op"] = operation,
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = "phase186-session",
                ["status"] = "ok",
            };

        private static void WriteFrame(
            Stream stream,
            JObject header)
            => WriteFrame(
                stream,
                header,
                Array.Empty<byte>());

        private static void WriteFrame(
            Stream stream,
            JObject header,
            byte[] payload)
        {
            var wire = U2R2ProtocolCodec.EncodeFrame(
                header,
                payload);
            stream.Write(wire, 0, wire.Length);
            stream.Flush();
        }

        private static void WriteFragmented(
            Stream stream,
            JObject header,
            int fragmentBytes)
        {
            var wire = U2R2ProtocolCodec.EncodeFrame(
                header,
                Array.Empty<byte>());
            for (var offset = 0; offset < wire.Length;)
            {
                var count = Math.Min(
                    fragmentBytes,
                    wire.Length - offset);
                stream.Write(wire, offset, count);
                stream.Flush();
                offset += count;
            }
        }

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExactly(stream, 16);
            var headerBytes = ReadUInt32(fixedHeader, 8);
            var payloadBytes = ReadUInt32(fixedHeader, 12);
            var remainder = ReadExactly(
                stream,
                checked((int)(headerBytes + payloadBytes)));
            var frame = new byte[
                checked(fixedHeader.Length + remainder.Length)];
            Buffer.BlockCopy(
                fixedHeader,
                0,
                frame,
                0,
                fixedHeader.Length);
            Buffer.BlockCopy(
                remainder,
                0,
                frame,
                fixedHeader.Length,
                remainder.Length);
            return frame;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bytes, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(
                bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));

        private static void WriteUInt32(
            byte[] bytes,
            int offset,
            uint value)
        {
            bytes[offset] = (byte)(value & 0xff);
            bytes[offset + 1] =
                (byte)((value >> 8) & 0xff);
            bytes[offset + 2] =
                (byte)((value >> 16) & 0xff);
            bytes[offset + 3] =
                (byte)((value >> 24) & 0xff);
        }

        private sealed class LoopbackPeer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _done =
                new ManualResetEventSlim(false);
            private Exception _error;
            private int _connections;

            private LoopbackPeer(Action<Stream> behavior, int maxConnections)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(1);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(() =>
                {
                    try
                    {
                        for (var attempt = 1; ; attempt++)
                        {
                            using var client = _listener.AcceptTcpClient();
                            Interlocked.Increment(ref _connections);
                            try
                            {
                                using var stream = client.GetStream();
                                behavior(stream);
                                break;
                            }
                            catch (IOException) when (attempt < maxConnections)
                            {
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        _error = exception;
                    }
                    finally
                    {
                        _done.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Phase186E loopback peer",
                };
                _thread.Start();
            }

            internal int Port { get; }

            internal int Connections => Volatile.Read(ref _connections);

            internal static LoopbackPeer Start(
                Action<Stream> behavior)
                => new LoopbackPeer(behavior, maxConnections: 1);

            /// <summary>
            /// Restarts the scripted conversation on a new connection when the client
            /// drops the current one, as an auto-reconnecting runtime does after a
            /// handshake timeout.
            /// </summary>
            internal static LoopbackPeer StartAcceptingReconnects(
                Action<Stream> behavior)
                => new LoopbackPeer(behavior, maxConnections: 5);

            internal void AssertCompleted()
            {
                Assert.True(_done.Wait(TimeSpan.FromSeconds(3)));
                _thread.Join(TimeSpan.FromSeconds(1));
                Assert.Null(_error);
            }

            public void Dispose()
            {
                _listener.Stop();
                _done.Wait(TimeSpan.FromSeconds(1));
                _thread.Join(TimeSpan.FromSeconds(1));
                _done.Dispose();
            }
        }
    }
}
