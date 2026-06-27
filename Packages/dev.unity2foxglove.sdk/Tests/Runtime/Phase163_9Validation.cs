// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-9 review regression checks for MCAP writer internals.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_9Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-9: MCAP Writer Internals Review ===");
            _passed = 0;

            FinalChunkPartialWriteFailureLeavesRecoverableMcap();
            DuplicateServerChannelIdIsIgnoredWithoutPhantomChannel();
            Lz4CompressionReturnsOwnedRoundTrippableBuffer();
            RawWriterRejectsReservedOpcodes();
            AmendmentReplacementFallbackReportsBackupPath();
            SourceKeepsPartialFlushRecoveryBoundedToSeekableStreams();
            PhaseRegistryWiresPhase163_9();

            Console.WriteLine($"Phase 163-9: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void FinalChunkPartialWriteFailureLeavesRecoverableMcap()
        {
            using var stream = new FaultingStream();
            using (var recorder = new McapRecorder(
                       stream,
                       null,
                       new McapWriterOptions { ChunkSizeBytes = 4096 },
                       leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase163_9/fault", "json", "phase163_9.Fault", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"value\":10}"));
                stream.ThrowOnceAfterWrittenBytes(3);
                recorder.Close();
            }

            Check(stream.ThrowCount == 1,
                "163-9A-1: validation injected one partial stream write failure during final chunk flush");
            Check(HasMagicPrefixAndSuffix(stream.ToArray()),
                "163-9A-2: partial final chunk failure still leaves valid MCAP magic prefix and suffix");

            stream.Position = 0;
            var summary = new McapReader(stream).ReadSummary();
            Check(summary.Channels.Count == 1
                  && summary.Channels[0].Topic == "/phase163_9/fault"
                  && summary.Statistics == null,
                "163-9A-3: recovered trailer keeps prior channel indexes without misleading final statistics");
        }

        private static void DuplicateServerChannelIdIsIgnoredWithoutPhantomChannel()
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(stream, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase163_9/first", "json", "phase163_9.First", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"value\":\"first\"}"));
                recorder.AddChannel(1, "/phase163_9/second", "json", "phase163_9.Second", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 20, Encoding.UTF8.GetBytes("{\"value\":\"still-first\"}"));
                recorder.Close();
            }

            stream.Position = 0;
            var summary = new McapReader(stream).ReadSummary();
            Check(summary.Channels.Count == 1 && summary.Channels[0].Topic == "/phase163_9/first",
                "163-9B-1: duplicate server channel id does not append a phantom summary channel");

            stream.Position = 0;
            using var indexed = new McapIndexedReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var messages = indexed.ReadMessages();
            var channelId = summary.Channels[0].Id;
            Check(messages.Count == 2 && messages.All(message => message.ChannelId == channelId),
                "163-9B-2: messages after duplicate server channel id continue using the original channel mapping");
        }

        private static void Lz4CompressionReturnsOwnedRoundTrippableBuffer()
        {
            var payload = Encoding.UTF8.GetBytes(string.Join("|", Enumerable.Range(0, 256).Select(i => "phase163_9-" + i)));
            var compressed = McapCompression.Compress("lz4", new ArraySegment<byte>(payload));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var restored = McapCompression.Decompress("lz4", compressed, payload.Length, maxOutputBytes: payload.Length);

            Check(compressed.Array != null
                  && compressed.Offset == 0
                  && compressed.Count == compressed.Array.Length,
                "163-9C-1: LZ4 compression returns an owned compact buffer segment");
            Check(payload.SequenceEqual(restored),
                "163-9C-2: LZ4 compressed buffer round-trips after GC pressure");
        }

        private static void RawWriterRejectsReservedOpcodes()
        {
            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);

            Check(Throws<ArgumentOutOfRangeException>(() => writer.WriteRecord(0x10, Array.Empty<byte>())),
                "163-9D-1: raw MCAP writer rejects reserved opcode 0x10");
            writer.WriteRecord(0x80, new byte[] { 1, 2, 3 });
            Check(stream.Length == McapWriter.RecordHeaderLength + 3,
                "163-9D-2: raw MCAP writer still allows private opcode records");
        }

        private static void AmendmentReplacementFallbackReportsBackupPath()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs");
            Check(source.Contains("var backupPath = CreateBackupPath(_filePath);", StringComparison.Ordinal)
                  && source.Contains("AggregateException", StringComparison.Ordinal)
                  && source.Contains("restoring that backup also failed", StringComparison.Ordinal)
                  && source.Contains("original file was restored from backup", StringComparison.Ordinal)
                  && source.Contains("'{backupPath}'", StringComparison.Ordinal),
                "163-9E: amendment replacement fallback reports the unique backup path on replace/restore failures");
        }

        private static void SourceKeepsPartialFlushRecoveryBoundedToSeekableStreams()
        {
            var recorder = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var writer = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");

            Check(recorder.Contains("TryRecoverAfterFailedFinalChunkFlush", StringComparison.Ordinal)
                  && recorder.Contains("_w.TruncateToPosition(flushStartPosition)", StringComparison.Ordinal)
                  && recorder.Contains("if (!_w.CanSeek)", StringComparison.Ordinal)
                  && writer.Contains("internal void TruncateToPosition(long position)", StringComparison.Ordinal)
                  && writer.Contains("_stream.SetLength(position)", StringComparison.Ordinal),
                "163-9F: partial final chunk recovery is seekable-stream gated and truncates before writing the trailer");
        }

        private static void PhaseRegistryWiresPhase163_9()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase163-9\", \"Phase 163-9\", Phase163_9Validation.Validate", StringComparison.Ordinal),
                "163-9G: PhaseValidationRegistry wires --phase163-9");
        }

        private static bool HasMagicPrefixAndSuffix(byte[] bytes)
        {
            var magic = McapWriter.Magic;
            if (bytes.Length < magic.Length * 2)
                return false;

            for (var i = 0; i < magic.Length; i++)
            {
                if (bytes[i] != magic[i])
                    return false;
                if (bytes[bytes.Length - magic.Length + i] != magic[i])
                    return false;
            }

            return true;
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class FaultingStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();
            private long _remainingBeforeThrow = -1;

            public int ThrowCount { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public void ThrowOnceAfterWrittenBytes(long byteCount)
            {
                _remainingBeforeThrow = byteCount;
            }

            public byte[] ToArray() => _inner.ToArray();

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ShouldThrow(count, out var writableBeforeThrow))
                {
                    if (writableBeforeThrow > 0)
                        _inner.Write(buffer, offset, writableBeforeThrow);
                    ThrowCount++;
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow >= 0)
                    _remainingBeforeThrow -= count;
                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (ShouldThrow(1, out _))
                {
                    ThrowCount++;
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow >= 0)
                    _remainingBeforeThrow--;
                _inner.WriteByte(value);
            }

            private bool ShouldThrow(int count, out int writableBeforeThrow)
            {
                writableBeforeThrow = 0;
                if (_remainingBeforeThrow < 0)
                    return false;
                if (count <= _remainingBeforeThrow)
                    return false;

                writableBeforeThrow = (int)Math.Max(0, _remainingBeforeThrow);
                return true;
            }
        }
    }
}
