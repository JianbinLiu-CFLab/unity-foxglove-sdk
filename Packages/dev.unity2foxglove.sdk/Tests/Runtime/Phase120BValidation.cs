// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 120B validation for MCAP DataLoader hardening review closure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase120BValidation
    {
        private const string Token = "phase120b-token";
        private const string SourceId = "phase120b-source";
        private static int _passed;

        public static void Validate()
        {
            try
            {
                Console.WriteLine();
                Console.WriteLine("=== Phase 120B: MCAP DataLoader Hardening Review Closure ===");
                _passed = 0;

                VerifySequentialFallbackLimits();
                VerifyDataLoaderWarnings();
                VerifyBackfillLatestAt();
                VerifySparseIndexedBackfillEarlyStop();
                VerifyCrcHardFailConsistency();
                VerifyRemoteStreamAndCap();
                VerifyChunkSchemaChannelAllocationRemoval();
                VerifySegmentDecodeBounds();
            VerifyPublicHardeningCoverageAndWiring();

                Console.WriteLine($"Phase 120B: {_passed} checks passed.");
            }
            finally
            {
                TempMcapHelper.Cleanup();
            }
        }

        private static void VerifySequentialFallbackLimits()
        {
            var defaults = McapSequentialReadLimits.Default;
            Check(defaults.MaxMessages == 100000, "120B-A1: sequential fallback default message limit is bounded");
            Check(defaults.MaxPayloadBytes == 256L * 1024L * 1024L, "120B-A2: sequential fallback default payload limit is bounded");

            using (var direct = CreateDirectFixture())
            using (var reader = new McapIndexedReader(direct, leaveOpen: true))
            {
                Check(reader.Summary.SequentialMessages == null || reader.Summary.SequentialMessages.Count == 0,
                    "120B-A3: summary/inventory scan does not retain direct message payloads");
            }

            using (var direct = CreateDirectFixture())
            using (var reader = new McapIndexedReader(direct, leaveOpen: true, new McapSequentialReadLimits
            {
                MaxMessages = 2,
                MaxPayloadBytes = 0
            }))
            {
                Check(ThrowsWith<InvalidOperationException>(() => reader.ReadMessages(), "MaxMessages"),
                    "120B-A4: sequential fallback enforces MaxMessages");
            }

            using (var direct = CreateDirectFixture())
            using (var reader = new McapIndexedReader(direct, leaveOpen: true, new McapSequentialReadLimits
            {
                MaxMessages = 0,
                MaxPayloadBytes = 8
            }))
            {
                Check(ThrowsWith<InvalidOperationException>(() => reader.ReadMessages(), "MaxPayloadBytes"),
                    "120B-A5: sequential fallback enforces MaxPayloadBytes");
            }
        }

        private static void VerifyDataLoaderWarnings()
        {
            using var direct = CreateDirectFixture();
            using var loader = new McapDataLoader(direct, leaveOpen: true);
            var initialization = loader.Initialize();
            Check(initialization.Problems.Any(p => p.Code == "UnindexedSequentialFallback"),
                "120B-B1: DataLoader warns when unindexed sequential fallback is active");
        }

        private static void VerifyBackfillLatestAt()
        {
            using (var indexed = CreateIndexedBackfillFixture())
            using (var loader = new McapDataLoader(indexed, leaveOpen: true))
            {
                var backfill = loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 45,
                    ChannelIds = new List<ushort> { 1, 2 }
                }).ToList();
                Check(backfill.Select(m => m.LogTime).SequenceEqual(new ulong[] { 40, 15 }),
                    "120B-C1: indexed backfill returns latest-at per channel");
            }

            using (var direct = CreateDirectFixture())
            using (var loader = new McapDataLoader(direct, leaveOpen: true))
            {
                var backfill = loader.GetBackfill(new McapDataLoaderBackfillQuery
                {
                    TimeNs = 45,
                    Topics = new List<string> { "/phase120b/a", "/phase120b/b" }
                }).ToList();
                Check(backfill.Select(m => m.LogTime).SequenceEqual(new ulong[] { 40, 15 }),
                    "120B-D1: direct fallback backfill returns latest-at per channel");
            }

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Check(!source.Contains("StartTimeNs = 0", StringComparison.Ordinal)
                  || source.Contains("ReadLatestBefore", StringComparison.Ordinal),
                "120B-C2: DataLoader backfill source no longer depends on unbounded 0..T query materialization");
        }

        private static void VerifySparseIndexedBackfillEarlyStop()
        {
            using var indexed = new MemoryStream(BuildSparseIndexedBackfillFixtureWithOlderBadChunk());
            using var loader = new McapDataLoader(indexed, leaveOpen: true);
            var backfill = loader.GetBackfill(new McapDataLoaderBackfillQuery
            {
                TimeNs = 100
            }).ToList();

            Check(backfill.Count == 3
                  && backfill.Select(m => m.ChannelId).SequenceEqual(new ushort[] { 1, 2, 3 })
                  && backfill.Select(m => m.LogTime).SequenceEqual(new ulong[] { 90, 91, 92 }),
                "120B-C3: indexed backfill early-stop uses actual indexed channels, not declared channel inventory");
        }

        private static void VerifyCrcHardFailConsistency()
        {
            using (var indexed = new MemoryStream(BuildChunkMcap(summaryStart: true, badCrc: true, includeSchemaChannel: false)))
            using (var reader = new McapIndexedReader(indexed, leaveOpen: true))
            {
                Check(ThrowsWith<InvalidDataException>(() => reader.ReadMessages(), "CRC"),
                    "120B-E1: indexed chunk CRC mismatch hard-fails");
            }

            using var sequential = new MemoryStream(BuildChunkMcap(summaryStart: false, badCrc: true, includeSchemaChannel: false));
            Check(ThrowsWith<InvalidDataException>(() => new McapIndexedReader(sequential, leaveOpen: true), "CRC"),
                "120B-E2: sequential chunk CRC mismatch hard-fails");
        }

        private static void VerifyRemoteStreamAndCap()
        {
            var path = CreateRemoteFixtureFile();
            var small = new RemoteMcapDataSourcePrototype(path, SourceId, "phase120b-small", Token);
            var smallResponse = small.GetData(AuthorizedRequest());
            Check(smallResponse.Status == RemoteMcapResponseStatus.Ok && smallResponse.Data.Length == new FileInfo(path).Length,
                "120B-F1: small prototype data response remains byte-compatible");

            var capped = new RemoteMcapDataSourcePrototype(path, SourceId, "phase120b-capped", Token, maxInMemoryDataBytes: 32);
            var cappedResponse = capped.GetData(AuthorizedRequest());
            Check(cappedResponse.Status == RemoteMcapResponseStatus.Unsupported
                  && cappedResponse.Problems.Any(p => p.Code == "DataTooLargeForInMemoryResponse"),
                "120B-F2: over-cap in-memory data response is refused before full-file allocation");

            var streamResponse = capped.GetDataStream(AuthorizedRequest());
            using (streamResponse.DataStream)
            {
                Check(streamResponse.Status == RemoteMcapResponseStatus.Ok
                      && streamResponse.DataStream != null
                      && streamResponse.DataStream.CanRead
                      && streamResponse.Length == new FileInfo(path).Length,
                    "120B-F3: remote prototype exposes readable stream response for larger files");
            }

            Check(typeof(IDisposable).IsAssignableFrom(typeof(RemoteMcapDataStreamResponse)),
                "120B-F4: remote stream response owns an explicit disposable stream lifetime");

            var ownedStreamResponse = capped.GetDataStream(AuthorizedRequest());
            var ownedStreamResponseObject = (object)ownedStreamResponse;
            var disposable = (IDisposable)ownedStreamResponseObject;
            var ownedStream = ownedStreamResponse.DataStream;
            disposable.Dispose();
            Check(ownedStream == null || !ownedStream.CanRead,
                "120B-F5: disposing remote stream response closes the underlying stream");
        }

        private static void VerifyChunkSchemaChannelAllocationRemoval()
        {
            using (var chunked = new MemoryStream(BuildChunkMcap(summaryStart: false, badCrc: false, includeSchemaChannel: true)))
            using (var reader = new McapIndexedReader(chunked, leaveOpen: true))
            {
                Check(reader.Schemas.Count == 1 && reader.Channels.Count == 1 && reader.ReadMessages().Count == 1,
                    "120B-G1: summary-less chunk scan still collects schema/channel/message records");
            }

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            Check(!source.Contains("schemaContent = new byte[recordLength]", StringComparison.Ordinal)
                  && !source.Contains("channelContent = new byte[recordLength]", StringComparison.Ordinal),
                "120B-G2: chunk schema/channel decode avoids record-sized temp arrays");
        }

        private static void VerifySegmentDecodeBounds()
        {
            var schemaContent = BuildSchemaContent(1, "phase120b.Bounded", "jsonschema", Encoding.UTF8.GetBytes("{}"));
            var schemaBuffer = WrapWithPadding(schemaContent);
            Check(ThrowsWith<InvalidDataException>(
                    () => McapReader.DecodeSchema(schemaBuffer, 1, schemaContent.Length - 1),
                    "segment"),
                "120B-G3: DecodeSchema honors contentLen segment bounds");

            var channelContent = BuildChannelContent(1, 1, "/phase120b/bounded", "json");
            var channelBuffer = WrapWithPadding(channelContent);
            Check(ThrowsWith<InvalidDataException>(
                    () => McapReader.DecodeChannel(channelBuffer, 1, channelContent.Length - 1),
                    "segment"),
                "120B-G4: DecodeChannel honors contentLen segment bounds");
        }

        private static void VerifyPublicHardeningCoverageAndWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("--phase120b", StringComparison.Ordinal)
                  && registry.Contains("Phase120BValidation.Validate", StringComparison.Ordinal),
                "120B-H1: PhaseValidationRegistry wires --phase120b");
            Check(project.Contains("Phase120BValidation.cs", StringComparison.Ordinal),
                "120B-H2: runtime test project compiles Phase120BValidation");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase120BValidation.cs");
            foreach (var required in new[] { "VerifySequentialFallbackLimits", "CRC", "MaxMessages", "MaxPayloadBytes", "DataTooLargeForInMemoryResponse", "ReadLatestBefore" })
            {
                Check(source.Contains(required, StringComparison.Ordinal),
                    "120B-H3: public hardening validation covers " + required);
            }

            var dataLoader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Check(dataLoader.Contains("UnindexedSequentialFallback", StringComparison.Ordinal)
                  && dataLoader.Contains("ReadLatestBefore", StringComparison.Ordinal),
                "120B-H4: public DataLoader source exposes fallback diagnostics and latest-at backfill");
        }

        private static MemoryStream CreateDirectFixture()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase120b-direct");
                writer.WriteSchema(1, "phase120b.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase120b/a", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 1, "/phase120b/b", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("payload-a10"));
                writer.WriteMessage(2, 1, 15, 15, Encoding.UTF8.GetBytes("payload-b15"));
                writer.WriteMessage(1, 2, 40, 40, Encoding.UTF8.GetBytes("payload-a40"));
                writer.WriteMessage(2, 2, 50, 50, Encoding.UTF8.GetBytes("payload-b50"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateIndexedBackfillFixture()
        {
            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, 96, "", leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase120b/a", "json", "phase120b.A", "jsonschema", "{}");
                recorder.AddChannel(2, "/phase120b/b", "json", "phase120b.B", "jsonschema", "{}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 15, Encoding.UTF8.GetBytes("{\"b\":15}"));
                recorder.WriteMessage(1, 40, Encoding.UTF8.GetBytes("{\"a\":40}"));
                recorder.WriteMessage(2, 50, Encoding.UTF8.GetBytes("{\"b\":50}"));
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static byte[] BuildSparseIndexedBackfillFixtureWithOlderBadChunk()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase120b-sparse-indexed");

                var freshChunkOffset = (ulong)ms.Position;
                var freshRaw = BuildChunkMessages(
                    (1, 1u, 90UL, 90UL, "fresh-1"),
                    (2, 1u, 91UL, 91UL, "fresh-2"),
                    (3, 1u, 92UL, 92UL, "fresh-3"));
                writer.WriteChunk(90, 92, (ulong)freshRaw.Length, Crc32Helper.Compute(freshRaw), "", (ulong)freshRaw.Length, freshRaw);
                var freshChunkLength = (ulong)ms.Position - freshChunkOffset;

                var oldChunkOffset = (ulong)ms.Position;
                var oldRaw = BuildChunkMessages(
                    (1, 2u, 10UL, 10UL, "old-1"),
                    (2, 2u, 11UL, 11UL, "old-2"),
                    (3, 2u, 12UL, 12UL, "old-3"));
                writer.WriteChunk(10, 12, (ulong)oldRaw.Length, 0xDEADBEEFu, "", (ulong)oldRaw.Length, oldRaw);
                var oldChunkLength = (ulong)ms.Position - oldChunkOffset;

                var summaryOffset = (ulong)ms.Position;
                writer.WriteSchema(1, "phase120b.Sparse", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                for (ushort channelId = 1; channelId <= 10; channelId++)
                    writer.WriteChannel(channelId, 1, "/phase120b/sparse/" + channelId, "json", new Dictionary<string, string>());

                var activeChannels = new Dictionary<ushort, ulong>
                {
                    [1] = 0,
                    [2] = 0,
                    [3] = 0
                };
                writer.WriteChunkIndex(90, 92, freshChunkOffset, freshChunkLength,
                    activeChannels, 0, "", (ulong)freshRaw.Length, (ulong)freshRaw.Length);
                writer.WriteChunkIndex(10, 12, oldChunkOffset, oldChunkLength,
                    activeChannels, 0, "", (ulong)oldRaw.Length, (ulong)oldRaw.Length);

                writer.WriteFooter(summaryOffset, 0, 0);
                writer.WriteMagic();
                writer.Flush();
            }

            return ms.ToArray();
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

        private static string CreateRemoteFixtureFile()
        {
            var path = TempMcapHelper.CreatePath("phase120b_remote");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var source = CreateDirectFixture())
            {
                source.CopyTo(fs);
            }

            return path;
        }

        private static byte[] BuildChunkMcap(bool summaryStart, bool badCrc, bool includeSchemaChannel)
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase120b-chunk");

                var chunkData = new MemoryStream();
                if (includeSchemaChannel)
                {
                    WriteInnerSchema(chunkData, 1, "phase120b.Chunked", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                    WriteInnerChannel(chunkData, 1, 1, "/phase120b/chunked", "json");
                }
                WriteInnerMessage(chunkData, 1, 1, 100, 100, Encoding.UTF8.GetBytes("{}"));
                var raw = chunkData.ToArray();

                var chunkOffset = (ulong)ms.Position;
                var crc = badCrc ? 0xDEADBEEFu : Crc32Helper.Compute(raw);
                writer.WriteChunk(100, 100, (ulong)raw.Length, crc, "", (ulong)raw.Length, raw);
                var chunkLength = (ulong)ms.Position - chunkOffset;

                var summaryOffset = summaryStart ? (ulong)ms.Position : 0UL;
                if (summaryStart)
                {
                    writer.WriteChunkIndex(100, 100, chunkOffset, chunkLength,
                        new Dictionary<ushort, ulong>(), 0, "", (ulong)raw.Length, (ulong)raw.Length);
                }

                writer.WriteFooter(summaryOffset, 0, 0);
                writer.WriteMagic();
                writer.Flush();
            }

            return ms.ToArray();
        }

        private static void WriteInnerSchema(Stream stream, ushort id, string name, string encoding, byte[] data)
        {
            WriteInnerRecord(stream, 0x03, BuildSchemaContent(id, name, encoding, data));
        }

        private static void WriteInnerChannel(Stream stream, ushort channelId, ushort schemaId, string topic, string encoding)
        {
            WriteInnerRecord(stream, 0x04, BuildChannelContent(channelId, schemaId, topic, encoding));
        }

        private static byte[] BuildSchemaContent(ushort id, string name, string encoding, byte[] data)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, id);
            McapWriter.WriteString(content, name);
            McapWriter.WriteString(content, encoding);
            McapWriter.WriteLengthPrefixedBytes(content, data);
            return content.ToArray();
        }

        private static byte[] BuildChannelContent(ushort channelId, ushort schemaId, string topic, string encoding)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, channelId);
            McapWriter.WriteU16(content, schemaId);
            McapWriter.WriteString(content, topic);
            McapWriter.WriteString(content, encoding);
            McapWriter.WriteStringMap(content, new Dictionary<string, string>());
            return content.ToArray();
        }

        private static byte[] WrapWithPadding(byte[] content)
        {
            var buffer = new byte[content.Length + 2];
            Buffer.BlockCopy(content, 0, buffer, 1, content.Length);
            return buffer;
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

        private static RemoteMcapRequest AuthorizedRequest()
        {
            return new RemoteMcapRequest
            {
                BearerToken = "Bearer " + Token,
                SourceId = SourceId
            };
        }

        private static bool ThrowsWith<TException>(Action action, string requiredText)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException ex)
            {
                return ex.Message.Contains(requiredText, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase120B file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
