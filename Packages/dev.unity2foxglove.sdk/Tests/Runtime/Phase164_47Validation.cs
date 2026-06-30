using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_47Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-47 Tests ---");
            _passed = 0;

            VerifyReplayEngineScratchState();
            VerifyReplayPropertyCacheConcurrentHits();
            VerifyMcapTestFixtureReuse();
            VerifyRegistry();

            Console.WriteLine("Phase 164-47: " + _passed + " checks passed.\n");
        }

        private static void VerifyReplayEngineScratchState()
        {
            var engine = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var tick = SourceMethod(engine, "public List<McapMessage> Tick(ulong nowNs)");
            var snapshot = SourceMethod(engine, "Snapshot(ulong timeNs, List<McapMessage> result)");

            Check(engine.Contains("private readonly List<McapMessage> _defaultTickBuffer = new();", StringComparison.Ordinal)
                  && tick.Contains("return Tick(nowNs, _defaultTickBuffer);", StringComparison.Ordinal)
                  && !tick.Contains("new List<McapMessage>()", StringComparison.Ordinal),
                "164-47A-1: no-argument Tick reuses an engine-owned result buffer");
            Check(engine.Contains("private readonly Dictionary<ushort, McapMessage> _snapshotLatestByChannel = new();", StringComparison.Ordinal)
                  && snapshot.Contains("var latestByChannel = _snapshotLatestByChannel;", StringComparison.Ordinal)
                  && snapshot.Contains("latestByChannel.Clear();", StringComparison.Ordinal)
                  && !snapshot.Contains("new Dictionary<ushort, McapMessage>()", StringComparison.Ordinal),
                "164-47A-2: Snapshot reuses the latest-by-channel dictionary");
        }

        private static void VerifyReplayPropertyCacheConcurrentHits()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPropertyCache.cs");
            var resolve = SourceMethod(source, "Resolve(Type type, string propertyName, BindingFlags bindingFlags)");

            Check(source.Contains("ConcurrentDictionary<PropertyCacheKey, PropertyInfo>", StringComparison.Ordinal)
                  && resolve.Contains("Cache.GetOrAdd(key", StringComparison.Ordinal)
                  && !resolve.Contains("lock (Gate)", StringComparison.Ordinal)
                  && !source.Contains("new Dictionary<PropertyCacheKey, PropertyInfo>", StringComparison.Ordinal),
                "164-47B-1: ReplayPropertyCache uses ConcurrentDictionary for cache-hit reads");
        }

        private static void VerifyMcapTestFixtureReuse()
        {
            var indexing = Read("Packages/dev.unity2foxglove.sdk/Tests/Unit/Mcap/McapReaderIndexingTests.cs");
            var helper = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/TempMcapHelper.cs");

            Check(indexing.Contains("private static readonly byte[] SimpleFiveMessageMcap", StringComparison.Ordinal)
                  && indexing.Contains("OpenSimpleMessageMcap(SimpleFiveMessageMcap)", StringComparison.Ordinal)
                  && indexing.Contains("CreateSimpleMessageMcapBytes(int messageCount)", StringComparison.Ordinal),
                "164-47C-1: deterministic MCAP reader fixture bytes are built once and reopened per test");
            Check(helper.Contains("private static readonly List<string> _paths = new(16);", StringComparison.Ordinal)
                  && helper.Contains("_paths.Clear();", StringComparison.Ordinal),
                "164-47C-2: TempMcapHelper has a small capacity hint and preserves cleanup");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-47\"", StringComparison.Ordinal), "164-47D-1: validation registry exposes Phase164-47");
            Check(project.Contains("Phase164_47Validation.cs", StringComparison.Ordinal), "164-47D-2: runtime validation project compiles Phase164-47");
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
