// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed class RemoteMcapHttpServerBoundaryTests
    {
        private const long SlowResponseLength = 64L * 1024L * 1024L;

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.42.1.9", true)]
        [InlineData("::1", true)]
        [InlineData("[::1]", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("", true)]
        [InlineData("   ", true)]
        [InlineData("localhost", false)]
        [InlineData("0.0.0.0", false)]
        [InlineData("::", false)]
        [InlineData("+", false)]
        [InlineData("*", false)]
        [InlineData("192.0.2.1", false)]
        public void LoopbackClassificationRejectsExternallyReachableHosts(
            string host,
            bool expected)
        {
            Assert.Equal(expected, RemoteMcapHttpOptions.IsLoopbackHost(host));
        }

        [Fact]
        public void Ipv6BaseUrlUsesUriBrackets()
        {
            var options = new RemoteMcapHttpOptions
            {
                Host = "::1",
                Port = 8891
            };

            Assert.Equal("http://[::1]:8891", options.BaseUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void NonLoopbackStartWithoutBearerTokenFailsBeforeBinding(
            string token)
        {
            var path = Path.GetTempFileName();
            RemoteMcapHttpServer server = null;
            try
            {
                var options = new RemoteMcapHttpOptions
                {
                    Host = "0.0.0.0",
                    Port = 1,
                    McapPath = path,
                    RequiredBearerToken = token
                };

                var error = Record.Exception(() =>
                    server = RemoteMcapHttpServer.Start(options));

                var argument = Assert.IsType<ArgumentException>(error);
                Assert.Equal("options", argument.ParamName);
                Assert.Contains("non-loopback", argument.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("bearer token", argument.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                server?.Dispose();
                File.Delete(path);
            }
        }

        [Fact]
        public async Task SlowFileResponseDoesNotBlockNextAcceptedRequest()
        {
            var path = Path.GetTempFileName();
            RemoteMcapHttpServer server = null;
            TcpClient slowClient = null;
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                    file.SetLength(SlowResponseLength);

                var options = new RemoteMcapHttpOptions
                {
                    Host = "127.0.0.1",
                    McapPath = path,
                    SourceId = "slow"
                };
                server = StartLoopbackServerWithRetry(options);

                slowClient = new TcpClient { ReceiveBufferSize = 256 };
                await slowClient.ConnectAsync(IPAddress.Loopback, options.Port);
                var slowRequest = Encoding.ASCII.GetBytes(
                    "GET /v1/files/slow.mcap HTTP/1.1\r\n"
                    + "Host: 127.0.0.1:" + options.Port + "\r\n"
                    + "Connection: close\r\n\r\n");
                var slowStream = slowClient.GetStream();
                await slowStream.WriteAsync(slowRequest, 0, slowRequest.Length);
                await ReadResponseHeadersAsync(slowStream, TimeSpan.FromSeconds(2));
                await Task.Delay(100);

                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage(
                           HttpMethod.Options,
                           server.BaseUrl + "/v1/manifest"))
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                using (var response = await client.SendAsync(request, timeout.Token))
                {
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                }
            }
            finally
            {
                slowClient?.Dispose();
                server?.Dispose();
                DeleteTempFileWithRetry(path);
            }
        }

        private static RemoteMcapHttpServer StartLoopbackServerWithRetry(
            RemoteMcapHttpOptions options)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                options.Port = FindFreeLoopbackPort();
                try
                {
                    return RemoteMcapHttpServer.Start(options);
                }
                catch (Exception error) when (IsAddressAlreadyInUse(error))
                {
                    lastError = error;
                }
            }

            throw new InvalidOperationException(
                "Could not bind a loopback Remote MCAP test server.",
                lastError);
        }

        private static int FindFreeLoopbackPort()
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

        private static bool IsAddressAlreadyInUse(Exception error)
            => error is SocketException socket
               && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
               || error is HttpListenerException listener
               && (listener.ErrorCode == 183 || listener.ErrorCode == 10_048);

        private static async Task ReadResponseHeadersAsync(
            Stream stream,
            TimeSpan timeout)
        {
            var marker = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var buffer = new byte[1];
            using (var cancellation = new CancellationTokenSource(timeout))
            {
                while (matched < marker.Length)
                {
                    var read = await stream.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        cancellation.Token);
                    if (read == 0)
                        throw new EndOfStreamException("Remote MCAP response ended before its HTTP headers.");

                    matched = buffer[0] == marker[matched]
                        ? matched + 1
                        : buffer[0] == marker[0] ? 1 : 0;
                }
            }
        }

        private static void DeleteTempFileWithRetry(string path)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }
}
