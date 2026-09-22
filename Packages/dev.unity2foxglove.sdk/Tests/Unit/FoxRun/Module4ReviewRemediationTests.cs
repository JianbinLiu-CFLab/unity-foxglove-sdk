using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module4ReviewRemediationTests
    {
        [Fact]
        public void ReplayTransportFanoutOccursOutsideEngineLock()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var tick = source.Substring(source.IndexOf("public void Tick(FoxgloveSession session, ulong nowNs, bool deferCallbacks)", StringComparison.Ordinal));
            var publish = tick.IndexOf("PublishMessages(session", StringComparison.Ordinal);
            var close = tick.IndexOf("if (!deferCallbacks)", StringComparison.Ordinal);
            Assert.True(publish >= 0 && close > publish);
            Assert.Contains("Engine state is captured under its lock", tick, StringComparison.Ordinal);
            Assert.DoesNotContain("lock (_replayEngineLock)\n            {\n                if (!Volatile.Read(ref _replayEnabled) || _replayEngine == null) return;\n                var messages = _replayEngine.Tick(nowNs, _replayTickBuffer);\n                if (messages == null || messages.Count == 0) return;\n                PublishMessages", tick, StringComparison.Ordinal);
        }

        [Fact]
        public void ExistingMcapReviewFixesRemainPresent()
        {
            var loader = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            var amendment = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs");
            var history = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPanelHistoryBuffer.cs");
            Assert.Contains("QueryCanMatchChannelOrTopic", loader, StringComparison.Ordinal);
            Assert.Contains("AttachmentCount = checked(statistics.AttachmentCount + attachmentCount)", amendment, StringComparison.Ordinal);
            Assert.Contains("_offset++", history, StringComparison.Ordinal);
        }
    }
}
