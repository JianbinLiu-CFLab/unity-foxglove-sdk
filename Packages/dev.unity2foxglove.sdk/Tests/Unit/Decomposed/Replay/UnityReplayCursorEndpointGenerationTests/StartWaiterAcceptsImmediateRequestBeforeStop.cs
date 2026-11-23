using System.Net;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{
    public sealed partial class UnityReplayCursorEndpointGenerationTests
    {
        [Fact]
        public async Task StartWaiterAcceptsImmediateRequestBeforeStop()
        {
            var logger = new RecordingLogger();
            using var endpoint = new UnityReplayCursorEndpoint(logger);
            var port = StartEndpointWithRetry(
                endpoint,
                "/start-ready",
                "start-ready-token",
                _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted."));

            Assert.Equal(
                HttpStatusCode.Accepted,
                await PostCursorAsync(port, "/start-ready", "start-ready-token"));

            endpoint.Stop();

            Assert.Empty(logger.Warnings);
            Assert.False(endpoint.IsRunning);
            Assert.Equal(0, endpoint.RetiringGenerationCount);
        }
    }
}
