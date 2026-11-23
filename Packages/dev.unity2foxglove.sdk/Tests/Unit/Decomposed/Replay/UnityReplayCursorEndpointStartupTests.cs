using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{
    public sealed class UnityReplayCursorEndpointStartupTests
    {
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        [Fact]
        public async Task StartWaiterAcceptsImmediateRequestBeforeStop()
        {
            var logger = new RecordingLogger();
            using var endpoint = new UnityReplayCursorEndpoint(logger);
            var port = ReserveFreeLoopbackPort();
            endpoint.Start(
                Options(port),
                _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted."));

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"http://127.0.0.1:{port}/start-ready");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    "start-ready-token");
                request.Content = new StringContent(
                    "{\"source\":\"phase187\",\"sequence\":1,\"mode\":\"seek\",\"time\":{\"sec\":2,\"nsec\":3}}",
                    Encoding.UTF8,
                    "application/json");

                using var response = await Client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
            finally
            {
                endpoint.Stop();
            }

            Assert.Empty(logger.Warnings);
            Assert.False(endpoint.IsRunning);
            Assert.Equal(0, endpoint.RetiringGenerationCount);
        }

        private static UnityReplayCursorEndpointOptions Options(int port)
            => new UnityReplayCursorEndpointOptions(
                enabled: true,
                host: "127.0.0.1",
                port: port,
                path: "/start-ready",
                bearerToken: "start-ready-token",
                maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes);

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

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            internal readonly System.Collections.Generic.List<string> Warnings = new System.Collections.Generic.List<string>();

            public void LogWarning(string message) => Warnings.Add(message);

            public void LogError(string message) { }
        }
    }
}
