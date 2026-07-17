using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_51Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-51 Tests ---");
            _passed = 0;

            VerifyDataLoaderBuildsChannelIndexesInOnePass();
            VerifyDataLoaderUsesNullFilterSentinels();
            VerifyDecodedIteratorUsesCachedRegistry();
            VerifyR2fuRuntimeSelectionCachesPackageScans();
            VerifyRegistry();

            Console.WriteLine("Phase 164-51: " + _passed + " checks passed.\n");
        }

        private static void VerifyDataLoaderBuildsChannelIndexesInOnePass()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            var initialize = SourceMethod(source, "public McapDataLoaderInitialization Initialize()");
            var builder = SourceMethod(source, "private static void BuildChannelAndQueryMaps(");

            Check(initialize.Contains("BuildChannelAndQueryMaps(_reader.Channels, out _channelMap, out _topicChannelMap, out _knownChannelIds);", StringComparison.Ordinal)
                  && !initialize.Contains("BuildChannelMap(_reader.Channels)", StringComparison.Ordinal)
                  && !initialize.Contains("BuildQueryMaps(_reader.Channels", StringComparison.Ordinal),
                "164-51A-1: DataLoader initialization builds channel and query maps together");
            Check(!source.Contains("private static Dictionary<ushort, McapChannel> BuildChannelMap", StringComparison.Ordinal)
                  && !source.Contains("private static void BuildQueryMaps", StringComparison.Ordinal),
                "164-51A-2: DataLoader no longer keeps duplicate channel-map passes");
            Check(builder.Contains("out Dictionary<ushort, McapChannel> channelMap", StringComparison.Ordinal)
                  && builder.Contains("channelMap = new Dictionary<ushort, McapChannel>();", StringComparison.Ordinal)
                  && builder.Contains("channelMap[channel.Id] = channel;", StringComparison.Ordinal)
                  && builder.Contains("knownChannelIds.Add(channel.Id);", StringComparison.Ordinal)
                  && builder.Contains("topicChannelMap[topic] = ids;", StringComparison.Ordinal),
                "164-51A-3: combined builder populates all channel indexes in one loop");
        }

        private static void VerifyDataLoaderUsesNullFilterSentinels()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            var backfill = SourceMethod(source, "public IReadOnlyList<McapDataLoaderMessage> GetBackfill(");
            var copyUShorts = SourceMethod(source, "private static List<ushort> CopyUShorts(List<ushort> source)");
            var copyStrings = SourceMethod(source, "private static List<string> CopyStrings(List<string> source)");

            Check(backfill.Contains("EndTimeNs = query?.TimeNs ?? ulong.MaxValue", StringComparison.Ordinal)
                  && !backfill.Contains("query = query ?? new McapDataLoaderBackfillQuery", StringComparison.Ordinal),
                "164-51B-1: DataLoader backfill keeps null query as the no-filter sentinel");
            Check(copyUShorts.Contains("source == null || source.Count == 0 ? null : new List<ushort>(source)", StringComparison.Ordinal)
                  && copyStrings.Contains("source == null || source.Count == 0 ? null : new List<string>(source)", StringComparison.Ordinal),
                "164-51B-2: empty DataLoader filters use null sentinels without empty List allocations");
        }

        private static void VerifyDecodedIteratorUsesCachedRegistry()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            var decodedIterator = SourceMethod(source, "public IEnumerable<McapDecodedMessage> CreateDecodedIterator(");
            var tryDecode = SourceMethod(source, "public bool TryDecodeMessage(");

            Check(decodedIterator.Contains("var registry = GetDecodeRegistry(options);", StringComparison.Ordinal)
                  && !decodedIterator.Contains("CreateDecodeRegistry(options)", StringComparison.Ordinal),
                "164-51C-1: eager decoded iterator reuses the cached decoder registry");
            Check(tryDecode.Contains("return GetDecodeRegistry(options).TryDecode(message, out decoded);", StringComparison.Ordinal),
                "164-51C-2: single-message decode shares the cached decoder registry");
        }

        private static void VerifyR2fuRuntimeSelectionCachesPackageScans()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(source.Contains("private static string _cachedCandidatesProjectDirectory;", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> _cachedCandidates;", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<string> _cachedManifestRuntimePackages;", StringComparison.Ordinal),
                "164-51D-1: R2FU runtime selection caches candidate and manifest scans");
            Check(source.Contains("public static IReadOnlyList<string> ReadManifestRuntimePackages(string projectDirectory)", StringComparison.Ordinal)
                  && source.Contains("var manifestInfo = new FileInfo(manifestPath);", StringComparison.Ordinal)
                  && source.Contains("_cachedManifestWriteTimeUtc == manifestInfo.LastWriteTimeUtc", StringComparison.Ordinal)
                  && source.Contains("_cachedManifestLength == manifestInfo.Length", StringComparison.Ordinal)
                  && source.Contains("ReadManifestDependencies(File.ReadAllText(manifestPath), manifestPath)", StringComparison.Ordinal)
                  && !source.Contains("Regex.Matches", StringComparison.Ordinal),
                "164-51D-2: manifest runtime package discovery is file-version cached and JSON-based");
            Check(source.Contains("private static readonly Dictionary<string, string> ZenohPayloadDiagnostics", StringComparison.Ordinal)
                  && source.Contains("private static string GetZenohPayloadDiagnostic(string packageDirectory, bool manifestDeclaresZenoh)", StringComparison.Ordinal)
                  && source.Contains("ZenohPayloadDiagnostics.TryGetValue(cacheKey, out var cached)", StringComparison.Ordinal)
                  && source.Contains("ZenohPayloadDiagnostics[cacheKey] = diagnostic;", StringComparison.Ordinal)
                  && source.Contains("ZenohPayloadDiagnostics.Clear();", StringComparison.Ordinal),
                "164-51D-3: Zenoh payload diagnostics are cached and invalidated with status cache");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-51\"", StringComparison.Ordinal), "164-51E-1: validation registry exposes Phase164-51");
            Check(project.Contains("Phase164_51Validation.cs", StringComparison.Ordinal), "164-51E-2: runtime validation project compiles Phase164-51");
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
