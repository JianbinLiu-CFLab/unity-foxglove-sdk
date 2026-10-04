// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Unit
// Purpose: Locks the absolute deadline of the health probe response exchange.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    public sealed class Ros2BridgeHealthProbeDeadlineTests
    {
        [Fact]
        public async Task PingUsesOneDeadlineAcrossFixedAndJsonHeaders()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var fixture = new DripFixture(listener);
            var server = Task.Run(fixture.Run);

            var stopwatch = Stopwatch.StartNew();
            var result = new Ros2BridgeU2R2HealthProbe().Ping("127.0.0.1", port, 500);
            stopwatch.Stop();

            Assert.False(result.Succeeded);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1.2),
                "The probe exceeded its absolute deadline: " + stopwatch.Elapsed);
            await fixture.RequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.FixedHeaderSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.BodyChunkSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(fixture.UnexpectedFailure);
        }

        private sealed class DripFixture
        {
            private readonly TcpListener _listener;

            public DripFixture(TcpListener listener)
            {
                _listener = listener;
            }

            public TaskCompletionSource<bool> RequestReceived { get; } = NewSignal();
            public TaskCompletionSource<bool> FixedHeaderSent { get; } = NewSignal();
            public TaskCompletionSource<bool> BodyChunkSent { get; } = NewSignal();
            public Exception UnexpectedFailure { get; private set; }

            public void Run()
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    using var stream = client.GetStream();
                    var requestFixedHeader = ReadExact(stream, 16);
                    var requestHeaderLength = ReadUInt32LE(requestFixedHeader, 8);
                    var requestHeader = ReadExact(stream, checked((int)requestHeaderLength));
                    var requestJson = JObject.Parse(Encoding.UTF8.GetString(requestHeader));
                    var requestId = requestJson.Value<string>("requestId");
                    RequestReceived.TrySetResult(true);
                    var response = U2R2ProtocolCodec.EncodeFrame(
                        new JObject
                        {
                            ["op"] = "health_pong",
                            ["requestId"] = requestId,
                            ["protocolVersion"] = Ros2BridgeU2R2HealthCodec.ProtocolVersion,
                            ["status"] = "ok",
                            ["sidecarName"] = "test-sidecar",
                            ["sidecarVersion"] = "1.0.0"
                        },
                        Array.Empty<byte>());
                    var fixedHeader = new byte[16];
                    Array.Copy(response, fixedHeader, fixedHeader.Length);
                    Thread.Sleep(300);
                    stream.Write(fixedHeader, 0, fixedHeader.Length);
                    stream.Flush();
                    FixedHeaderSent.TrySetResult(true);

                    var bodyLength = response.Length - 16;
                    var chunkSize = (bodyLength + 3) / 4;
                    for (var offset = 0; offset < bodyLength; offset += chunkSize)
                    {
                        if (offset != 0)
                            Thread.Sleep(100);
                        var count = Math.Min(chunkSize, bodyLength - offset);
                        BodyChunkSent.TrySetResult(true);
                        stream.Write(response, 16 + offset, count);
                        stream.Flush();
                    }
                }
                catch (IOException) when (BodyChunkSent.Task.IsCompleted)
                {
                }
                catch (ObjectDisposedException) when (BodyChunkSent.Task.IsCompleted)
                {
                }
                catch (SocketException) when (BodyChunkSent.Task.IsCompleted)
                {
                }
                catch (Exception ex)
                {
                    UnexpectedFailure = ex;
                }
                finally
                {
                    _listener.Stop();
                }
            }

            private static TaskCompletionSource<bool> NewSignal()
            {
                return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                    throw new IOException("The health probe closed the request stream.");
                offset += read;
            }
            return bytes;
        }

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));
    }
}
