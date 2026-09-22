using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module1ReviewRemediationTests
    {
        [Fact]
        public void ComponentSessionCaptureUsesActiveRuntimeGeneration()
        {
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            Assert.True(server.IndexOf("_runtime.StartWithSessionSetup(", StringComparison.Ordinal)
                        < server.IndexOf("CaptureComponentPublisherSession();", StringComparison.Ordinal));
        }

        [Fact]
        public void ComponentSessionResolvesAutoManagerPublishers()
        {
            var contracts = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ComponentPublishContracts.cs");
            Assert.Contains("ResolveManagerForComponentSession", contracts, StringComparison.Ordinal);
        }

        [Fact]
        public void ComponentSessionIsClearedDuringManagerStop()
        {
            var contracts = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ComponentPublishContracts.cs");
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            Assert.Contains("ClearActiveComponentPublisherSession", contracts, StringComparison.Ordinal);
            Assert.Contains("ClearActiveComponentPublisherSession", server, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerReplayForwardersIsolatePublicSubscribers()
        {
            var server = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            Assert.Contains("InvokeReplayMessageSubscribers", server, StringComparison.Ordinal);
            Assert.Contains("InvokeReplayMessageContextSubscribers", server, StringComparison.Ordinal);
            Assert.Contains("InvokeReplayBatchSubscribers", server, StringComparison.Ordinal);
        }

        [Fact]
        public void EndpointStartFailuresUseRetryBackoff()
        {
            var remote = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.RemoteMcap.cs");
            var cursor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.ReplayCursor.cs");
            Assert.Contains("_remoteMcapFileServerRetryAt", remote, StringComparison.Ordinal);
            Assert.Contains("_replayCursorEndpointRetryAt", cursor, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplaySuppressionTracksPublisherOwnership()
        {
            var publisher = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var setup = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            Assert.Contains("TryDisableForReplay", publisher, StringComparison.Ordinal);
            Assert.Contains("RestoreAfterReplay", publisher, StringComparison.Ordinal);
            Assert.Contains("pub.TryDisableForReplay()", setup, StringComparison.Ordinal);
            Assert.Contains("RestoreAfterReplay", setup, StringComparison.Ordinal);
            Assert.DoesNotContain("pub.enabled = true", setup, StringComparison.Ordinal);
            Assert.DoesNotContain("pub.enabled = false", setup, StringComparison.Ordinal);
        }
    }
}
