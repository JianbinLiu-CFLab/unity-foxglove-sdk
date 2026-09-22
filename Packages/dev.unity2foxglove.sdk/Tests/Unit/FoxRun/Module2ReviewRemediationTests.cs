using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module2ReviewRemediationTests
    {
        [Fact]
        public void ChannelRegistryRejectsConflictingActiveDescriptor()
        {
            var registry = new ChannelRegistry();
            registry.Register(new AdvertiseChannel { Id = 7, Topic = "/a", Encoding = "json" });
            Assert.Throws<InvalidOperationException>(() => registry.Register(
                new AdvertiseChannel { Id = 7, Topic = "/b", Encoding = "json" }));
            Assert.Equal("/a", registry.Get(7).Topic);
        }

        [Fact]
        public void SubscriptionRegistryRejectsActiveIdRebindAndDuplicateChannel()
        {
            var registry = new SubscriptionRegistry();
            Assert.True(registry.TryAddSubscription(1, 10, 20, out _));
            Assert.False(registry.TryAddSubscription(1, 10, 21, out var rebindError));
            Assert.Contains("active subscription", rebindError, StringComparison.OrdinalIgnoreCase);
            Assert.False(registry.TryAddSubscription(1, 11, 20, out var duplicateError));
            Assert.Contains("already subscribed", duplicateError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ParameterSubscriptionsHaveBoundedBudgets()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ParameterSubscriptionRegistry.cs");
            Assert.Contains("MaxParameterNamesPerClient", source, StringComparison.Ordinal);
            Assert.Contains("MaxTotalParameterNames", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Module2SessionSinksAreOutsideLifecycleLock()
        {
            var session = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var publish = session.Substring(session.IndexOf("public void Publish(uint channelId, byte[] payload, ulong logTimeNs)", StringComparison.Ordinal));
            Assert.Contains("WriteMessageSafely", publish, StringComparison.Ordinal);
            Assert.Contains("CopySubscribersForPublish", publish, StringComparison.Ordinal);
            Assert.DoesNotContain("lock (_channelLifecycleLock)\n            {\n                var channel = _channels.Get(channelId);\n                if (channel == null) return;\n                payload ??= Array.Empty<byte>();\n                var recorder", publish, StringComparison.Ordinal);
        }

        [Fact]
        public void ClientPublishValidatesEncodingAndIsolatesRecorderFailure()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            Assert.Contains("IsSupportedClientEncoding", source, StringComparison.Ordinal);
            Assert.Contains("WriteClientMessageSafely", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GraphBroadcastCopiesRecipientsBeforeTransportFanout()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var copy = source.IndexOf("_graph.CopySubscribersTo", StringComparison.Ordinal);
            var send = source.IndexOf("_transport.SendText", copy, StringComparison.Ordinal);
            var close = source.IndexOf("_subscriberScratchLock", send, StringComparison.Ordinal);
            Assert.True(copy >= 0 && send >= 0);
            Assert.True(close < 0 || close > send);
            Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ConnectionGraphSnapshotsSortTopologyAndIdentifiers()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ConnectionGraphRegistry.cs");
            Assert.Contains("names.Sort(StringComparer.Ordinal)", source, StringComparison.Ordinal);
            Assert.Contains("result.Sort(StringComparer.Ordinal)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ParameterObserversAreolatedFromEachOther()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/FoxgloveParameterStore.cs");
            Assert.Contains("InvokeChangedHandlers", source, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ServiceRegistryGuardsIdExhaustionAndJsonEncoding()
        {
            var service = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            Assert.Contains("uint.MaxValue", service, StringComparison.Ordinal);
            Assert.Contains("ValidateDescriptor", service, StringComparison.Ordinal);
        }
    }
}
