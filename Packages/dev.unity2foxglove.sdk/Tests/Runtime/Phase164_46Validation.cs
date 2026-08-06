using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_46Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-46 Tests ---");
            _passed = 0;

            VerifySessionOptimizationContracts();
            VerifyValidationOptimizations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-46: " + _passed + " checks passed.\n");
        }

        private static void VerifySessionOptimizationContracts()
        {
            var subscriptions = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var clientPublish = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var graph = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");

            var trySingle = SourceMethod(subscriptions, "TryAddSubscription(uint clientId, uint subscriptionId, uint channelId, out string error)");
            var addSingle = SourceMethod(subscriptions, "AddSubscription(uint clientId, uint subscriptionId, uint channelId)");
            var removeClient = SourceMethod(clientPublish, "RemoveClient(uint clientId)");
            var broadcast = SourceMethod(graph, "BroadcastUpdate()");
            var publishJson = SourceMethod(session, "PublishJson(uint channelId, object message, ulong logTimeNs)");

            Check(!trySingle.Contains("new[]", StringComparison.Ordinal)
                  && !addSingle.Contains("new[]", StringComparison.Ordinal)
                  && subscriptions.Contains("TryAddSubscriptionLocked", StringComparison.Ordinal),
                "164-46A-1: single subscription add path avoids wrapper-array allocation");
            Check(removeClient.Contains("_clientChannelRemovalScratch", StringComparison.Ordinal)
                  && !removeClient.Contains(".Where(", StringComparison.Ordinal)
                  && !removeClient.Contains(".ToList(", StringComparison.Ordinal),
                "164-46A-2: client-published channel removal uses a reusable scratch list instead of LINQ");
            Check(broadcast.Contains("_graph.CopySubscribersTo(_subscriberScratch)", StringComparison.Ordinal)
                  && !broadcast.Contains("_graph.GetSubscribers()", StringComparison.Ordinal),
                "164-46A-3: graph broadcasts reuse subscriber scratch instead of allocating subscriber snapshots");
            Check(publishJson.Contains("JsonSerializerCache.Value.Serialize(jsonWriter, message)", StringComparison.Ordinal),
                "164-46A-4: JSON publish path reuses the cached serializer");
        }

        private static void VerifyValidationOptimizations()
        {
            var phase4 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase4Validation.cs");
            var phase6 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase6Validation.cs");

            Check(phase4.Contains("private static readonly DefaultSchemaRegistry CoreSchemaRegistry", StringComparison.Ordinal)
                  && phase4.Contains("CreateCoreSchemaRegistry()", StringComparison.Ordinal)
                  && !SourceMethod(phase4, "TestCompressedImageSchemaRegistered()").Contains("new DefaultSchemaRegistry", StringComparison.Ordinal)
                  && !SourceMethod(phase4, "TestRegisterSchemaChannelCamera()").Contains("RegisterCoreSchemas", StringComparison.Ordinal),
                "164-46B-1: Phase4 reuses one core schema registry for schema lookup tests");
            Check(phase6.Contains("Thread.Sleep(1)", StringComparison.Ordinal)
                  && phase6.Contains("DateTime.UtcNow + TimeSpan.FromMilliseconds(50)", StringComparison.Ordinal)
                  && !phase6.Contains("Thread.Sleep(20)", StringComparison.Ordinal),
                "164-46B-2: Phase6 service timeout test uses short polling instead of a fixed 20ms wait");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-46\"", StringComparison.Ordinal), "164-46C-1: validation registry exposes Phase164-46");
            Check(project.Contains("Phase164_46Validation.cs", StringComparison.Ordinal), "164-46C-2: runtime validation project compiles Phase164-46");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.RequiredSourceMethod(source, signature);

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
