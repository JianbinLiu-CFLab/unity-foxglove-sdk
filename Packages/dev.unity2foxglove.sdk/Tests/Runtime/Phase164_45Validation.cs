using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_45Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-45 Tests ---");
            _passed = 0;

            VerifyRuntimeSessionAllocationOptimizations();
            VerifyExistingSubscriptionAndGraphOptimizations();
            VerifyPhase5UsesDirectLinkXmlPath();
            VerifyRegistry();

            Console.WriteLine("Phase 164-45: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimeSessionAllocationOptimizations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var channelFilter = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionChannelFilter.cs");
            var publishJson = SourceMethod(source, "PublishJson(uint channelId, object message, ulong logTimeNs)");
            var filterLive = SourceMethod(channelFilter, "FilterLiveChannels(IReadOnlyCollection<AdvertiseChannel> channels)");

            Check(source.Contains("ThreadLocal<JsonSerializer> JsonSerializerCache", StringComparison.Ordinal)
                  && source.Contains("new ThreadLocal<JsonSerializer>(JsonSerializer.CreateDefault)", StringComparison.Ordinal),
                "164-45A-1: FoxgloveSession caches JsonSerializer per thread");
            Check(publishJson.Contains("JsonSerializerCache.Value.Serialize(jsonWriter, message)", StringComparison.Ordinal)
                  && !publishJson.Contains("JsonSerializer.CreateDefault().Serialize", StringComparison.Ordinal),
                "164-45A-2: PublishJson avoids allocating a JsonSerializer for every message");
            Check(source.Contains("=> _channelFilter.FilterLiveChannels(channels)", StringComparison.Ordinal)
                  && filterLive.Contains("var filter = Volatile.Read(ref _liveWebSocketChannelFilter)", StringComparison.Ordinal)
                  && filterLive.Contains("if (filter == null)")
                  && filterLive.Contains("return channels;", StringComparison.Ordinal)
                  && filterLive.Contains("filter.AllowChannel(CreateFilterContext(FoxgloveSinkKind.LiveWebSocket, channel))", StringComparison.Ordinal),
                "164-45A-3: live channel filtering returns the existing snapshot when no filter is configured");
        }

        private static void VerifyExistingSubscriptionAndGraphOptimizations()
        {
            var subscriptions = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var trySingle = SourceMethod(subscriptions, "TryAddSubscription(uint clientId, uint subscriptionId, uint channelId, out string error)");
            var addSingle = SourceMethod(subscriptions, "AddSubscription(uint clientId, uint subscriptionId, uint channelId)");
            var graph = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var broadcast = SourceMethod(graph, "BroadcastUpdate()");

            Check(!trySingle.Contains("new[]", StringComparison.Ordinal)
                  && !addSingle.Contains("new[]", StringComparison.Ordinal)
                  && subscriptions.Contains("TryAddSubscriptionLocked", StringComparison.Ordinal),
                "164-45B-1: single subscription path already avoids wrapper-array allocation");
            Check(broadcast.Contains("_graph.CopySubscribersTo(_subscriberScratch)", StringComparison.Ordinal)
                  && broadcast.Contains("if (_subscriberScratch.Count == 0 && !hasDirtyRecorder)", StringComparison.Ordinal)
                  && !broadcast.Contains("_graph.GetSubscribers()", StringComparison.Ordinal),
                "164-45B-2: connection graph broadcast reuses subscriber scratch and skips idle snapshots");
        }

        private static void VerifyPhase5UsesDirectLinkXmlPath()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase5Validation.cs");
            var method = SourceMethod(source, "private static void TestAssetsLinkXmlDuplicateAbsent()");

            Check(method.Contains("Path.Combine(root, \"Unity2Foxglove\", \"Assets\", \"link.xml\")", StringComparison.Ordinal)
                  && method.Contains("!File.Exists(path)", StringComparison.Ordinal)
                  && !method.Contains("Directory.GetFiles", StringComparison.Ordinal)
                  && !method.Contains("SearchOption.AllDirectories", StringComparison.Ordinal),
                "164-45C-1: Phase5 validates the stable Assets link.xml absence without recursive scanning");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-45\"", StringComparison.Ordinal), "164-45D-1: validation registry exposes Phase164-45");
            Check(project.Contains("Phase164_45Validation.cs", StringComparison.Ordinal), "164-45D-2: runtime validation project compiles Phase164-45");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
