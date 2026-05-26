// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-10 regression coverage for MCAP DataLoader query budgets.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_10Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-10: MCAP Replay DataLoader Remote ===");
            _passed = 0;

            DefaultQueryAppliesMessageBudget();
            ExplicitQueryCapStillWins();
            ExplicitUnlimitedQueryRemainsAvailable();
            ReplayTickBudgetPreservesUnlimitedZero();
            ReplayChunkCrcPolicyUsesWithWarningByDefault();
            ReplayChunkIndexesAreSortedOnLoad();
            TryDecodeMessageReusesDecoderRegistry();
            EmptyJsonPayloadHasExplicitDiagnostic();
            DecodedPayloadAvailabilityDistinguishesWarnings();
            RemoteManifestResponsesAreClonedAndTyped();
            OptionalDecoderFactoryDiagnosticsAreExposed();

            Console.WriteLine($"Phase 134-10: {_passed} checks passed.");
        }

        private static void DefaultQueryAppliesMessageBudget()
        {
            const int extraMessages = 3;
            var totalMessages = McapDataLoaderQuery.DefaultMaxMessages + extraMessages;
            using var stream = CreateDirectFixture(totalMessages);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery()).ToList();
            Check(messages.Count == McapDataLoaderQuery.DefaultMaxMessages,
                "134-10A-1: default DataLoader query applies message-count budget");
            Check(messages[0].LogTime == (ulong)(extraMessages + 1),
                "134-10A-2: default DataLoader budget keeps latest chronological messages");
        }

        private static void ExplicitQueryCapStillWins()
        {
            using var stream = CreateDirectFixture(12);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 2 }).ToList();
            Check(messages.Count == 2,
                "134-10B-1: explicit DataLoader MaxMessages cap is honored");
            Check(messages[0].LogTime == 11 && messages[1].LogTime == 12,
                "134-10B-2: explicit DataLoader cap keeps latest chronological messages");
        }

        private static void ExplicitUnlimitedQueryRemainsAvailable()
        {
            var totalMessages = McapDataLoaderQuery.DefaultMaxMessages + 1;
            using var stream = CreateDirectFixture(totalMessages);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var messages = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 0 }).ToList();
            Check(messages.Count == totalMessages,
                "134-10C-1: explicit MaxMessages=0 remains an opt-in unlimited query");
        }

        private static void ReplayTickBudgetPreservesUnlimitedZero()
        {
            var engine = new McapReplayEngine { MaxMessagesPerTick = 0 };
            Check(engine.MaxMessagesPerTick == 0,
                "134-10D-1: replay tick budget preserves zero as unlimited");
            engine.MaxMessagesPerTick = -10;
            Check(engine.MaxMessagesPerTick == 1,
                "134-10D-2: replay tick budget still clamps negative values to one");

            var messages = new List<McapMessage>
            {
                new McapMessage { LogTime = 1 },
                new McapMessage { LogTime = 2 }
            };
            Check(McapReplayEngine.CountTickResultPrefixPreservingLogTimeGroup(messages, 0) == 2,
                "134-10D-3: replay tick cap helper treats zero as unlimited");
            Check(McapReplayEngine.CountTickResultPrefixPreservingLogTimeGroup(messages, -1) == 1,
                "134-10D-4: replay tick cap helper clamps negative values to one");
        }

        private static void ReplayChunkCrcPolicyUsesWithWarningByDefault()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(badCrc: true, reverseChunkIndexOrder: false));

                using (var engine = new McapReplayEngine())
                {
                    engine.Load(path);
                    var result = engine.Snapshot(100, new List<McapMessage>());
                    Check(result.Count == 1,
                        "134-10E-1: replay preserves legacy corrupt-chunk reads by default");
                }

                using (var strict = new McapReplayEngine
                       {
                           CrcMismatchPolicy = McapReplayEngine.CorruptChunkPolicy.Skip
                       })
                {
                    strict.Load(path);
                    var result = strict.Snapshot(100, new List<McapMessage>());
                    Check(result.Count == 0,
                        "134-10E-2: replay can opt into strict corrupt-chunk skip");
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void ReplayChunkIndexesAreSortedOnLoad()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(badCrc: false, reverseChunkIndexOrder: true));
                using var engine = new McapReplayEngine();
                engine.Load(path);
                Check(engine.Summary.ChunkIndexes[0].MessageStartTime == 10
                      && engine.Summary.ChunkIndexes[1].MessageStartTime == 100,
                    "134-10F-1: replay sorts chunk indexes by message time on load");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void TryDecodeMessageReusesDecoderRegistry()
        {
            using var stream = CreateDirectFixture(1);
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var raw = loader.CreateIterator(new McapDataLoaderQuery { MaxMessages = 1 }).First();
            var factory = new CountingDecoderFactory();
            var options = new McapDecodeOptions
            {
                UseBuiltInDecoders = false,
                DecoderFactories = new List<IMcapMessageDecoderFactory> { factory }
            };

            Check(loader.TryDecodeMessage(raw, options, out _),
                "134-10G-1: custom decoder can decode raw DataLoader message");
            Check(loader.TryDecodeMessage(raw, options, out _),
                "134-10G-2: repeated custom decode succeeds");
            Check(factory.TryCreateCount == 1,
                "134-10G-3: TryDecodeMessage reuses per-loader decoder cache");

            var mutableOptions = new McapDecodeOptions
            {
                UseBuiltInDecoders = false,
                DecoderFactories = new List<IMcapMessageDecoderFactory> { new NullDecoderFactory() }
            };
            Check(!loader.TryDecodeMessage(raw, mutableOptions, out _),
                "134-10G-4: unsupported mutable decoder options fail before mutation");
            var addedFactory = new CountingDecoderFactory();
            mutableOptions.DecoderFactories.Add(addedFactory);
            Check(loader.TryDecodeMessage(raw, mutableOptions, out _),
                "134-10G-5: decoder cache refreshes after mutable options change");
            Check(addedFactory.TryCreateCount == 1,
                "134-10G-6: refreshed decoder registry observes newly added factory");
        }

        private static void EmptyJsonPayloadHasExplicitDiagnostic()
        {
            using var stream = CreateDirectFixture(1, payload: Array.Empty<byte>());
            using var loader = new McapDataLoader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);

            var decoded = loader.CreateDecodedIterator(new McapDataLoaderQuery { MaxMessages = 1 }).First();
            Check(decoded.Payload.Kind == McapDecodedPayloadKind.Failed,
                "134-10H-1: empty JSON payload is surfaced as failed decode");
            Check(decoded.Problems.Any(problem => problem.Message.Contains("empty", StringComparison.OrdinalIgnoreCase)),
                "134-10H-2: empty JSON payload diagnostic is explicit");
        }

        private static void DecodedPayloadAvailabilityDistinguishesWarnings()
        {
            var decoded = new McapDecodedMessage
            {
                Payload = new McapDecodedPayload { Kind = McapDecodedPayloadKind.Ros2CdrDiagnostic }
            };
            decoded.Problems.Add(new McapDecodeProblem { Severity = McapDataLoaderProblemSeverity.Warning });

            Check(!decoded.IsDecoded,
                "134-10I-1: IsDecoded still requires a warning-free decode");
            Check(decoded.HasDecodedPayload,
                "134-10I-2: HasDecodedPayload accepts diagnostic fallback payloads with warnings");
        }

        private static void RemoteManifestResponsesAreClonedAndTyped()
        {
            var path = TempMcapPath();
            try
            {
                using (var source = CreateDirectFixture(1))
                using (var destination = new FileStream(path, FileMode.Create, FileAccess.Write))
                    source.CopyTo(destination);

                var prototype = new RemoteMcapDataSourcePrototype(path, "phase134-10", "phase134-10", "token");
                var request = new RemoteMcapRequest
                {
                    BearerToken = "Bearer token",
                    SourceId = "phase134-10"
                };

                var first = prototype.GetManifest(request);
                Check(first.Status == RemoteMcapResponseStatus.Ok && first.Manifest.Sources.Count == 1,
                    "134-10J-1: remote manifest authorizes with fixed-time bearer compare path");
                first.Manifest.Sources.Clear();
                var second = prototype.GetManifest(request);
                Check(second.Manifest.Sources.Count == 1,
                    "134-10J-2: cached remote manifest response is returned as a defensive clone");
                Check(typeof(RemoteMcapProblem).GetField(nameof(RemoteMcapProblem.Severity))?.FieldType
                      == typeof(RemoteMcapProblemSeverity),
                    "134-10J-3: remote MCAP problem severity is typed");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void OptionalDecoderFactoryDiagnosticsAreExposed()
        {
            var diagnostics = McapDecodeRegistry.OptionalFactoryDiagnostics;
            Check(diagnostics != null,
                "134-10K-1: optional decoder factory reflection diagnostics are exposed");
        }

        private static MemoryStream CreateDirectFixture(int messageCount, byte[] payload = null)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-10-dataloader");
                writer.WriteSchema(1, "phase134_10.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_10", "json", new Dictionary<string, string>());
                for (var i = 1; i <= messageCount; i++)
                    writer.WriteMessage(1, (uint)i, (ulong)i, (ulong)i, payload ?? Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] BuildChunkMcap(bool badCrc, bool reverseChunkIndexOrder)
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-10-replay");

                var firstChunkOffset = (ulong)ms.Position;
                var firstRaw = BuildChunkMessages((1, 1u, 10UL, 10UL, "{}"));
                writer.WriteChunk(10, 10, (ulong)firstRaw.Length,
                    badCrc ? 0xDEADBEEFu : Crc32Helper.Compute(firstRaw),
                    "", (ulong)firstRaw.Length, firstRaw);
                var firstChunkLength = (ulong)ms.Position - firstChunkOffset;

                var secondChunkOffset = (ulong)ms.Position;
                var secondRaw = BuildChunkMessages((1, 2u, 100UL, 100UL, "{}"));
                writer.WriteChunk(100, 100, (ulong)secondRaw.Length,
                    badCrc ? 0xDEADBEEFu : Crc32Helper.Compute(secondRaw),
                    "", (ulong)secondRaw.Length, secondRaw);
                var secondChunkLength = (ulong)ms.Position - secondChunkOffset;

                var summaryStart = (ulong)ms.Position;
                writer.WriteSchema(1, "phase134_10.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_10", "json", new Dictionary<string, string>());
                writer.WriteStatistics(2, 1, 1, 0, 0, 2, 10, 100, new Dictionary<ushort, ulong> { [1] = 2 });

                if (reverseChunkIndexOrder)
                {
                    WriteChunkIndex(writer, 100, secondChunkOffset, secondChunkLength, secondRaw.Length);
                    WriteChunkIndex(writer, 10, firstChunkOffset, firstChunkLength, firstRaw.Length);
                }
                else
                {
                    WriteChunkIndex(writer, 10, firstChunkOffset, firstChunkLength, firstRaw.Length);
                    WriteChunkIndex(writer, 100, secondChunkOffset, secondChunkLength, secondRaw.Length);
                }

                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
                writer.Flush();
            }

            return ms.ToArray();
        }

        private static void WriteChunkIndex(McapWriter writer, ulong timeNs, ulong chunkOffset, ulong chunkLength, int rawLength)
        {
            writer.WriteChunkIndex(timeNs, timeNs, chunkOffset, chunkLength,
                new Dictionary<ushort, ulong>(), 0, "", (ulong)rawLength, (ulong)rawLength);
        }

        private static byte[] BuildChunkMessages(params (ushort ChannelId, uint Sequence, ulong LogTime, ulong PublishTime, string Payload)[] messages)
        {
            var chunkData = new MemoryStream();
            for (var i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                WriteInnerMessage(
                    chunkData,
                    message.ChannelId,
                    message.Sequence,
                    message.LogTime,
                    message.PublishTime,
                    Encoding.UTF8.GetBytes(message.Payload));
            }

            return chunkData.ToArray();
        }

        private static void WriteInnerMessage(Stream stream, ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, channelId);
            McapWriter.WriteU32(content, sequence);
            McapWriter.WriteU64(content, logTime);
            McapWriter.WriteU64(content, publishTime);
            if (data != null && data.Length > 0)
                content.Write(data, 0, data.Length);
            WriteInnerRecord(stream, 0x05, content.ToArray());
        }

        private static void WriteInnerRecord(Stream stream, byte opcode, byte[] content)
        {
            stream.WriteByte(opcode);
            McapWriter.WriteU64(stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0)
                stream.Write(content, 0, content.Length);
        }

        private static string TempMcapPath()
            => Path.Combine(Path.GetTempPath(), "phase134_10_" + Guid.NewGuid().ToString("N") + ".mcap");

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for temp validation fixtures.
            }
        }

        private sealed class CountingDecoderFactory : IMcapMessageDecoderFactory
        {
            public int TryCreateCount;

            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
            {
                TryCreateCount++;
                return new CountingDecoder();
            }
        }

        private sealed class NullDecoderFactory : IMcapMessageDecoderFactory
        {
            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
                => null;
        }

        private sealed class CountingDecoder : IMcapMessageDecoder
        {
            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                return new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Json,
                    Text = "{}",
                    RawData = message?.Data ?? Array.Empty<byte>()
                };
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
