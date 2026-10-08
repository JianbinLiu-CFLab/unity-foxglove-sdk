// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Replay
// Purpose: Locks replay cursor endpoint worker generations across restart.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{


public sealed partial class UnityReplayCursorEndpointGenerationTests
    {
        [Fact]
        public async Task StartRetriesWhenTheReservedPortIsTakenBeforeTheBind()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            var squattedPort = ReserveFreeLoopbackPort();
            var squatter = new TcpListener(IPAddress.Loopback, squattedPort);
            squatter.Start();
            try
            {
                var bound = StartEndpointWithRetry(
                    endpoint,
                    "/retry",
                    "retry-token",
                    _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted."),
                    PortsStartingWith(squattedPort));

                Assert.NotEqual(squattedPort, bound);
                Assert.True(endpoint.IsRunning);
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(bound, "/retry", "retry-token"));
            }
            finally
            {
                squatter.Stop();
            }
        }

        [Fact]
        public async Task AbortedResponseDoesNotRetireTheListenerWorker()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var queueEntered = new ManualResetEventSlim();
            using var releaseQueue = new ManualResetEventSlim();
            var port = StartEndpointWithRetry(
                endpoint,
                "/abort",
                "abort-token",
                _ =>
                {
                    queueEntered.Set();
                    releaseQueue.Wait(TimeSpan.FromSeconds(5));
                    return new UnityReplayCursorEndpointQueueResult(
                        true,
                        new string('x', 2 * 1024 * 1024));
                });

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var request = Encoding.ASCII.GetBytes(
                    "POST /abort HTTP/1.1\r\n"
                    + "Host: 127.0.0.1\r\n"
                    + "Authorization: Bearer abort-token\r\n"
                    + "Content-Type: application/json\r\n"
                    + "Content-Length: " + Encoding.UTF8.GetByteCount(CursorJson) + "\r\n"
                    + "Connection: close\r\n\r\n"
                    + CursorJson);
                await client.GetStream().WriteAsync(request, 0, request.Length);
                Assert.True(queueEntered.Wait(TimeSpan.FromSeconds(5)), "Worker never reached the queue callback.");

                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
                releaseQueue.Set();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.Equal(
                HttpStatusCode.Accepted,
                await PostCursorAsync(port, "/abort", "abort-token"));
        }

        private static UnityReplayCursorEndpointOptions Options(int port, string path, string bearerToken)
            => new UnityReplayCursorEndpointOptions(
                enabled: true,
                host: "127.0.0.1",
                port: port,
                path: path,
                bearerToken: bearerToken,
                maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes);

        private static Task<HttpStatusCode> PostCursorAsync(int port, string path, string bearerToken)
            => PostCursorAsync(port, path, bearerToken, CursorJson);

        private static async Task<HttpStatusCode> PostCursorAsync(
            int port,
            string path,
            string bearerToken,
            string json)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{port}{path}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                bearerToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request);
            return response.StatusCode;
        }

        private static string CursorJsonFor(long sequence, long sec, int nsec)
            => "{\"source\":\"phase187\",\"sequence\":"
               + sequence
               + ",\"mode\":\"seek\",\"time\":{\"sec\":"
               + sec
               + ",\"nsec\":"
               + nsec
               + "}}";

        private static async Task ObserveRetiredRequestAsync(Task<HttpStatusCode> request)
        {
            try
            {
                await request;
            }
            catch (HttpRequestException)
            {
                // Closing the retired listener may abort its in-flight response.
            }
            catch (TaskCanceledException)
            {
                // A closed listener can surface as a bounded client timeout.
            }
        }

        /// <summary>
        /// Starts the endpoint on a free loopback port, retrying when the reserved port was taken
        /// between the reservation probe and the listener bind. Returns the port actually bound.
        /// </summary>
        private static int StartEndpointWithRetry(
            UnityReplayCursorEndpoint endpoint,
            string path,
            string bearerToken,
            Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue,
            IEnumerable<int> candidatePorts = null)
        {
            Exception lastError = null;
            var attempts = 0;
            foreach (var port in candidatePorts ?? FreeLoopbackPorts(5))
            {
                attempts++;
                try
                {
                    endpoint.Start(Options(port, path, bearerToken), queue);
                    return port;
                }
                catch (Exception error) when (IsBindContention(error))
                {
                    lastError = error;
                }
            }

            throw new InvalidOperationException(
                "Could not bind a loopback replay cursor endpoint after " + attempts + " attempts.",
                lastError);
        }

        // Losing the port between the probe and the bind surfaces as an HttpListenerException whose
        // code depends on the winner: 183 when another HTTP prefix owns it, 32 when a raw socket does.
        private static bool IsBindContention(Exception error)
            => error is HttpListenerException
               || error is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse;

        private static IEnumerable<int> FreeLoopbackPorts(int count)
        {
            for (var attempt = 0; attempt < count; attempt++)
            {
                yield return ReserveFreeLoopbackPort();
            }
        }

        private static IEnumerable<int> PortsStartingWith(int firstPort)
        {
            yield return firstPort;
            foreach (var port in FreeLoopbackPorts(4))
            {
                yield return port;
            }
        }

        private static int ReserveFreeLoopbackPort()
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
