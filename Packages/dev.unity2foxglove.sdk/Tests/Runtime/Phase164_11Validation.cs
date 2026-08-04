using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_11Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-11 Tests ---");
            _passed = 0;

            VerifyReplayEngineHotPathCaches();
            VerifyRemoteManifestRequestStampReuse();
            VerifyReplayControllerChannelContextCache();
            VerifyExistingCursorEndpointOptimizationsRemainInPlace();
            VerifyRegistry();

            Console.WriteLine("Phase 164-11: " + _passed + " checks passed.\n");
        }

        private static void VerifyReplayEngineHotPathCaches()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var pendingQueue = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayPendingQueue.cs");
            var tick = PhaseValidationSourceHelpers.SourceMethod(source, "public List<McapMessage> Tick(ulong nowNs)");
            var sortPending = PhaseValidationSourceHelpers.SourceMethod(source, "private void SortPending");
            var addPending = PhaseValidationSourceHelpers.SourceMethod(source, "private void AddPending");
            var seek = PhaseValidationSourceHelpers.SourceMethod(source, "public void Seek");

            Check(source.Contains("private readonly List<McapMessage> _defaultTickBuffer = new()", StringComparison.Ordinal)
                  && tick.Contains("return Tick(nowNs, _defaultTickBuffer)", StringComparison.Ordinal)
                  && !tick.Contains("new List<McapMessage>()", StringComparison.Ordinal),
                "164-11A-1: public replay Tick overload reuses an engine-owned result buffer");
            Check(source.Contains("private readonly McapReplayPendingQueue _pending = new()", StringComparison.Ordinal)
                  && addPending.Contains("=> _pending.Add(message)", StringComparison.Ordinal)
                  && sortPending.Contains("=> _pending.Sort(CompareMessages)", StringComparison.Ordinal)
                  && pendingQueue.Contains("private bool _isSorted = true", StringComparison.Ordinal)
                  && pendingQueue.Contains("_isSorted = false", StringComparison.Ordinal)
                  && pendingQueue.Contains("if (!_isSorted && _messages.Count > 1)", StringComparison.Ordinal)
                  && pendingQueue.Contains("_isSorted = true", StringComparison.Ordinal),
                "164-11A-2: pending replay messages use a dirty sorted flag to avoid redundant sorting");
            Check(seek.Contains("_pending.Clear()", StringComparison.Ordinal)
                  && CountOccurrences(pendingQueue, "_isSorted = true") >= 3,
                "164-11A-3: pending sorted state resets with seek and loaded-state cleanup");
        }

        private static void VerifyRemoteManifestRequestStampReuse()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs");
            var getManifest = PhaseValidationSourceHelpers.SourceMethod(source, "private RemoteMcapManifest GetCachedManifest()");
            var getManifestCore = PhaseValidationSourceHelpers.SourceMethod(source, "private RemoteMcapManifest GetCachedManifestCore");
            var getBytes = PhaseValidationSourceHelpers.SourceMethod(source, "private byte[] GetCachedManifestBytes");

            Check(source.Contains("private struct FileStamp", StringComparison.Ordinal)
                  && source.Contains("private FileStamp ReadFileStamp()", StringComparison.Ordinal)
                  && source.Contains("private bool MatchesCachedStamp(FileStamp stamp)", StringComparison.Ordinal),
                "164-11B-1: remote manifest cache uses a request-local file stamp");
            Check(getManifest.Contains("return CloneManifest(GetCachedManifestCore(ReadFileStamp(), out _))", StringComparison.Ordinal)
                  && getManifestCore.Contains("if (!loadStamp.Exists)", StringComparison.Ordinal)
                  && getManifestCore.Contains("MatchesCachedStamp(loadStamp)", StringComparison.Ordinal)
                  && getManifestCore.Contains("storeStamp = ReadFileStamp()", StringComparison.Ordinal),
                "164-11B-2: manifest path passes the file stamp through cache checks");
            Check(getBytes.Contains("var stamp = ReadFileStamp()", StringComparison.Ordinal)
                  && getBytes.Contains("var manifest = GetCachedManifestCore(stamp, out var storeStamp)", StringComparison.Ordinal)
                  && !getBytes.Contains("new FileInfo(_mcapPath)", StringComparison.Ordinal),
                "164-11B-3: manifest byte path avoids creating an extra FileInfo before manifest loading");
            Check(getManifestCore.Contains("_cachedManifest = manifest", StringComparison.Ordinal)
                  && getManifest.Contains("CloneManifest(GetCachedManifestCore", StringComparison.Ordinal)
                  && !getManifestCore.Contains("_cachedManifest = CloneManifest(manifest)", StringComparison.Ordinal),
                "164-11B-4: manifest miss stores the owned manifest and returns one external clone");
        }

        private static void VerifyReplayControllerChannelContextCache()
        {
            var source = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var publishMessages = PhaseValidationSourceHelpers.SourceMethod(source, "private void PublishMessages");
            var tryGetReplayTopic = PhaseValidationSourceHelpers.SourceMethod(source, "private bool TryGetReplayTopic");
            var createContext = PhaseValidationSourceHelpers.SourceMethod(source, "private ReplayMessageContext CreateReplayMessageContext");
            var channelContext = PhaseValidationSourceHelpers.SourceType(source, "private readonly struct ReplayChannelContext");

            Check(source.Contains("private Dictionary<ushort, ReplayChannelContext> _channelContextMap", StringComparison.Ordinal)
                  && source.Contains("_channelContextMap = new Dictionary<ushort, ReplayChannelContext>()", StringComparison.Ordinal)
                  && source.Contains("_channelContextMap[c.Id] = new ReplayChannelContext(c, s)", StringComparison.Ordinal),
                "164-11C-1: replay enable builds a combined channel/schema context cache");
            Check(publishMessages.Contains("TryGetReplayTopic(msg.ChannelId, out var topic)", StringComparison.Ordinal)
                  && tryGetReplayTopic.Contains("_channelContextMap.TryGetValue(channelId, out var replayContext)", StringComparison.Ordinal)
                  && tryGetReplayTopic.Contains("topic = replayContext.Topic", StringComparison.Ordinal),
                "164-11C-2: replay publish hot path reads topic from the combined context cache");
            Check(createContext.Contains("_channelContextMap.TryGetValue(message.ChannelId, out channelContext)", StringComparison.Ordinal)
                  && !createContext.Contains("_channelMap?.TryGetValue", StringComparison.Ordinal)
                  && !createContext.Contains("_summarySchemas.TryGetValue", StringComparison.Ordinal),
                "164-11C-3: scene replay context creation no longer performs two dictionaries lookups per message");
            Check(channelContext.Contains("public ReplayChannelContext(McapChannel channel, McapSchema schema)", StringComparison.Ordinal)
                  && channelContext.Contains("Topic = channel?.Topic ?? string.Empty", StringComparison.Ordinal)
                  && channelContext.Contains("SchemaEncoding = schema?.Encoding ?? string.Empty", StringComparison.Ordinal),
                "164-11C-4: combined replay context preserves empty-string fallback semantics");
        }

        private static void VerifyExistingCursorEndpointOptimizationsRemainInPlace()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var readBody = PhaseValidationSourceHelpers.SourceMethod(source, "private string ReadBody");
            var handle = PhaseValidationSourceHelpers.SourceMethod(source, "private void Handle");

            Check(source.Contains("private static readonly byte[] AcceptedCursorResponseBytes", StringComparison.Ordinal)
                  && handle.Contains("TryWrite(context, 202, AcceptedCursorResponseBytes, cors)", StringComparison.Ordinal),
                "164-11D-1: cursor endpoint keeps cached accepted response bytes");
            Check(readBody.Contains("var maxBodyBytes = generation.Options.MaxBodyBytes", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Rent(maxBodyBytes + 1)", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Return(buffer)", StringComparison.Ordinal)
                  && !readBody.Contains("new char[maxBodyBytes + 1]", StringComparison.Ordinal),
                "164-11D-2: cursor endpoint keeps request body reads on pooled byte buffers");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-11\"", StringComparison.Ordinal), "164-11E-1: validation registry exposes Phase164-11");
            Check(project.Contains("Phase164_11Validation.cs", StringComparison.Ordinal), "164-11E-2: runtime validation project compiles Phase164-11");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var offset = 0;
            while (true)
            {
                var index = text.IndexOf(value, offset, StringComparison.Ordinal);
                if (index < 0)
                    return count;
                count++;
                offset = index + value.Length;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
