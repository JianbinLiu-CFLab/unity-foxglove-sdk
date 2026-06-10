// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-10 MCAP replay engine review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for MCAP replay engine defects found in Phase 140-10.
    /// </summary>
    public static class Phase140_10Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-10 MCAP replay engine review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-10: MCAP replay engine review fixes ===");
            _passed = 0;

            TickDoesNotReadFutureChunksIntoPending();
            SeekPastEndClampsToReplayEnd();
            TickOverflowUpdatesBufferingStatusImmediately();
            LoadUsesNullSafeChunkIndexCount();
            PendingDocDoesNotClaimTimestampMutation();
            HistorySkipsRedundantSortForCappedQueries();
            DisposedReplayEngineRejectsRetainedReferences();
            TickHasNoUnreachablePendingBufferingBranch();
            ReplayFiltersBeforeCopyingPayloads();
            SnapshotAvoidsLinqSorting();
            ReplayEngineHasNoDeadCrcWarningHelper();

            Console.WriteLine($"Phase 140-10: {_passed} checks passed.");
        }

        private static void TickDoesNotReadFutureChunksIntoPending()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(
                    Chunk(10, Message(1, 10, "{}")),
                    Chunk(100, Message(2, 100, "{}")),
                    Chunk(200, Message(3, 200, "{}"))));

                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.Play();
                var result = engine.Tick(10);

                Check(result.Count == 1 && result[0].LogTime == 10,
                    "140-10A-1: first tick emits only due chunk message");
                Check(GetPendingCount(engine) == 0,
                    "140-10A-2: first tick does not pre-buffer future chunks");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void SeekPastEndClampsToReplayEnd()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(
                    Chunk(10, Message(1, 10, "{}"), Message(2, 20, "{}"), Message(3, 30, "{}"))));

                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.Play();
                engine.Seek(engine.EndTimeNs + 1);
                var result = engine.Tick(engine.EndTimeNs);

                Check(result.Count == 1 && result[0].LogTime == engine.EndTimeNs,
                    "140-10B-1: seek past end clamps and emits final boundary message");
                Check(engine.CurrentStatus != McapReplayEngine.Status.Ended || result.Count > 0,
                    "140-10B-2: seek past end does not silently skip all messages");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void TickOverflowUpdatesBufferingStatusImmediately()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(
                    Chunk(10, Message(1, 10, "{}"), Message(2, 20, "{}"), Message(3, 30, "{}"))));

                using var engine = new McapReplayEngine { MaxMessagesPerTick = 1 };
                engine.Load(path);
                engine.Play();
                var result = engine.Tick(30);

                Check(result.Count == 1,
                    "140-10C-1: replay tick cap leaves overflow messages pending");
                Check(GetPendingCount(engine) == 2,
                    "140-10C-2: overflow messages are retained in pending");
                Check(engine.CurrentStatus == McapReplayEngine.Status.Buffering,
                    "140-10C-3: overflow pending state is visible in the same tick");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void LoadUsesNullSafeChunkIndexCount()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(!source.Contains("_summary.ChunkIndexes.Count > 0", StringComparison.Ordinal),
                "140-10D-1: Load does not directly dereference ChunkIndexes for CanSeek");
        }

        private static void PendingDocDoesNotClaimTimestampMutation()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(!source.Contains("Dequeues the oldest pending message and updates the last emitted time.", StringComparison.Ordinal),
                "140-10E-1: PopPending XML doc does not claim it mutates last emitted time");
        }

        private static void HistorySkipsRedundantSortForCappedQueries()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(source.Contains("if (maxMessages <= 0 && result.Count > 1)", StringComparison.Ordinal),
                "140-10F-1: capped History queries avoid a redundant final sort");
        }

        private static void DisposedReplayEngineRejectsRetainedReferences()
        {
            var path = TempMcapPath();
            try
            {
                File.WriteAllBytes(path, BuildChunkMcap(Chunk(10, Message(1, 10, "{}"))));

                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                var retained = controller.Engine;
                Check(retained != null && retained.IsLoaded,
                    "140-10G-1: replay controller exposes a loaded engine snapshot");

                controller.Disable();
                CheckThrows<ObjectDisposedException>(
                    () => retained.Play(),
                    "140-10G-2: retained engine reference rejects use after controller disable");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void TickHasNoUnreachablePendingBufferingBranch()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(!source.Contains("PeekPending().LogTime <= clampedNow", StringComparison.Ordinal),
                "140-10H-1: Tick no longer keeps unreachable pending buffering branch");
        }

        private static void ReplayFiltersBeforeCopyingPayloads()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var tick = SourceBetween(source, "public List<McapMessage> Tick(ulong nowNs, List<McapMessage> result)", "public List<McapMessage> Snapshot");
            var snapshot = SourceBetween(source, "public List<McapMessage> Snapshot", "public List<McapMessage> History");
            var history = SourceBetween(source, "public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result, int maxMessages)", "public void Play()");

            Check(AppearsBefore(tick, "if (logNs < emitAfter)", "var data = new byte[dataLen]"),
                "140-10I-1: Tick rejects stale messages before copying payloads");
            Check(AppearsBefore(snapshot, "if (logNs > clampedTime)", "var data = new byte[dataLen]"),
                "140-10I-2: Snapshot rejects future messages before copying payloads");
            Check(AppearsBefore(history, "if (logNs < clampedFrom || logNs > clampedTo)", "var data = new byte[dataLen]"),
                "140-10I-3: History rejects out-of-range messages before copying payloads");
        }

        private static void SnapshotAvoidsLinqSorting()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var snapshot = SourceBetween(source, "public List<McapMessage> Snapshot", "public List<McapMessage> History");
            Check(!snapshot.Contains(".OrderBy(", StringComparison.Ordinal)
                  && !snapshot.Contains(".ThenBy(", StringComparison.Ordinal),
                "140-10J-1: Snapshot sorts its caller-owned result without LINQ sorting");
        }

        private static void ReplayEngineHasNoDeadCrcWarningHelper()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(!source.Contains("private void LogCrcWarning(", StringComparison.Ordinal),
                "140-10K-1: replay engine removes the unused CRC warning helper");
        }

        private static int GetPendingCount(McapReplayEngine engine)
        {
            var pending = (List<McapMessage>)typeof(McapReplayEngine)
                .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(engine);
            var head = (int)typeof(McapReplayEngine)
                .GetField("_pendingHeadIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(engine);
            return pending.Count - head;
        }

        private static byte[] BuildChunkMcap(params ChunkSpec[] chunks)
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase140-10-replay");

                var chunkRecords = new List<ChunkRecord>();
                ulong messageCount = 0;
                ulong startTime = ulong.MaxValue;
                ulong endTime = 0;

                foreach (var chunk in chunks)
                {
                    var raw = BuildChunkMessages(chunk.Messages);
                    var chunkOffset = (ulong)ms.Position;
                    writer.WriteChunk(
                        chunk.StartTime,
                        chunk.EndTime,
                        (ulong)raw.Length,
                        Crc32Helper.Compute(raw),
                        "",
                        (ulong)raw.Length,
                        raw);
                    var chunkLength = (ulong)ms.Position - chunkOffset;
                    chunkRecords.Add(new ChunkRecord(chunk, chunkOffset, chunkLength, raw.Length));

                    messageCount += (ulong)chunk.Messages.Length;
                    if (chunk.StartTime < startTime)
                        startTime = chunk.StartTime;
                    if (chunk.EndTime > endTime)
                        endTime = chunk.EndTime;
                }

                if (messageCount == 0)
                    startTime = 0;

                var summaryStart = (ulong)ms.Position;
                writer.WriteSchema(1, "phase140_10.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase140_10", "json", new Dictionary<string, string>());
                writer.WriteStatistics(
                    messageCount,
                    1,
                    1,
                    0,
                    0,
                    (uint)chunks.Length,
                    startTime,
                    endTime,
                    new Dictionary<ushort, ulong> { [1] = messageCount });

                foreach (var chunk in chunkRecords)
                {
                    writer.WriteChunkIndex(
                        chunk.Spec.StartTime,
                        chunk.Spec.EndTime,
                        chunk.Offset,
                        chunk.Length,
                        new Dictionary<ushort, ulong>(),
                        0,
                        "",
                        (ulong)chunk.RawLength,
                        (ulong)chunk.RawLength);
                }

                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
                writer.Flush();
            }

            return ms.ToArray();
        }

        private static byte[] BuildChunkMessages(params MessageSpec[] messages)
        {
            var stream = new MemoryStream();
            foreach (var message in messages)
            {
                var data = Encoding.UTF8.GetBytes(message.Payload);
                var content = new MemoryStream();
                McapWriter.WriteU16(content, 1);
                McapWriter.WriteU32(content, message.Sequence);
                McapWriter.WriteU64(content, message.LogTime);
                McapWriter.WriteU64(content, message.LogTime);
                content.Write(data, 0, data.Length);

                stream.WriteByte(McapWriter.OpcodeMessage);
                McapWriter.WriteU64(stream, (ulong)content.Length);
                var bytes = content.ToArray();
                stream.Write(bytes, 0, bytes.Length);
            }

            return stream.ToArray();
        }

        private static ChunkSpec Chunk(ulong timeNs, params MessageSpec[] messages)
            => new ChunkSpec(timeNs, timeNs, messages);

        private static MessageSpec Message(uint sequence, ulong logTime, string payload)
            => new MessageSpec(sequence, logTime, payload);

        private static string TempMcapPath()
            => Path.Combine(Path.GetTempPath(), "phase140_10_" + Guid.NewGuid().ToString("N") + ".mcap");

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

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string SourceBetween(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                throw new InvalidOperationException("Could not locate source markers for Phase140-10 validation.");
            return source.Substring(start, end - start);
        }

        private static bool AppearsBefore(string source, string firstMarker, string secondMarker)
        {
            var first = source.IndexOf(firstMarker, StringComparison.Ordinal);
            var second = source.IndexOf(secondMarker, StringComparison.Ordinal);
            return first >= 0 && second >= 0 && first < second;
        }

        private static void CheckThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                _passed++;
                Console.WriteLine("[PASS] " + message);
                return;
            }

            throw new Exception("[FAIL] " + message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private readonly struct ChunkSpec
        {
            public readonly ulong StartTime;
            public readonly ulong EndTime;
            public readonly MessageSpec[] Messages;

            public ChunkSpec(ulong startTime, ulong endTime, MessageSpec[] messages)
            {
                StartTime = startTime;
                EndTime = endTime;
                Messages = messages ?? Array.Empty<MessageSpec>();
            }
        }

        private readonly struct MessageSpec
        {
            public readonly uint Sequence;
            public readonly ulong LogTime;
            public readonly string Payload;

            public MessageSpec(uint sequence, ulong logTime, string payload)
            {
                Sequence = sequence;
                LogTime = logTime;
                Payload = payload ?? "{}";
            }
        }

        private readonly struct ChunkRecord
        {
            public readonly ChunkSpec Spec;
            public readonly ulong Offset;
            public readonly ulong Length;
            public readonly int RawLength;

            public ChunkRecord(ChunkSpec spec, ulong offset, ulong length, int rawLength)
            {
                Spec = spec;
                Offset = offset;
                Length = length;
                RawLength = rawLength;
            }
        }
    }
}
