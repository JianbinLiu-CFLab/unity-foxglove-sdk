// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-8 MCAP writer and recording pipeline review fixes.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for MCAP writer and recorder shutdown failure paths found in Phase 140-8.
    /// </summary>
    public static class Phase140_8Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-8 MCAP writer and recorder review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-8: MCAP writer and recording pipeline review fixes ===");
            _passed = 0;

            RecoverableChunkFlushFailureStillFinalizesFile();
            StreamWriteFailureDuringChunkFlushRemainsUnrecoverable();
            McapWriterDisposeSuppressesFlushFailure();
            McapRecorderConstructorDisposesOwnedWriterOnHeaderFailure();
            RecorderDefaultChunkSizeAliasesWriterDefault();
            SegmentCompressionRoundTripsWithoutCopyPatterns();
            SummaryCrcUsesIncrementalSegments();
            RecorderReusesChannelScratchAndTopicSignature();

            Console.WriteLine($"Phase 140-8: {_passed} checks passed.");
        }

        private static void RecoverableChunkFlushFailureStillFinalizesFile()
        {
            using var stream = new MemoryStream();
            var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions { UseChunking = true, ChunkSizeBytes = 1024 },
                leaveOpen: true);
            recorder.AddChannel(1, "/phase140_8/recoverable", "json", "", "", "");
            recorder.WriteMessage(1, 100UL, new byte[] { 1, 2, 3 });

            ReplaceChunkBufferWithNonPublicBuffer(recorder);
            recorder.Close();

            var parsed = McapRecordReader.Parse(stream.ToArray());
            Check(parsed.hasLeadingMagic && parsed.hasTrailingMagic,
                "140-8A-1: recoverable chunk flush failure keeps MCAP magic intact");
            Check(parsed.records.Any(record => record.Opcode == McapWriter.OpcodeDataEnd)
                  && parsed.records.Any(record => record.Opcode == McapWriter.OpcodeFooter),
                "140-8A-2: recoverable chunk flush failure still writes DataEnd and Footer");
            Check(!parsed.records.Any(record => record.Opcode == McapWriter.OpcodeChunk),
                "140-8A-3: recoverable chunk flush failure drops the active chunk instead of writing a corrupt chunk");
        }

        private static void StreamWriteFailureDuringChunkFlushRemainsUnrecoverable()
        {
            var stream = new FailingStream();
            var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions { UseChunking = true, ChunkSizeBytes = 1024 },
                leaveOpen: true);
            recorder.AddChannel(1, "/phase140_8/unrecoverable", "json", "", "", "");
            recorder.WriteMessage(1, 100UL, new byte[] { 4, 5, 6 });

            stream.FailWritesAtPosition = stream.Position + 1;
            CheckThrows<IOException>(recorder.Close,
                "140-8B-1: stream write failure during chunk flush is surfaced to caller");
            Check(!HasTrailingMagic(stream.ToArray()),
                "140-8B-2: unrecoverable partial stream write does not append a misleading trailing magic");
        }

        private static void McapWriterDisposeSuppressesFlushFailure()
        {
            var stream = new FailingStream { ThrowOnFlush = true };
            var writer = new McapWriter(stream, leaveOpen: true);

            writer.Dispose();
            writer.Dispose();

            Check(stream.FlushAttempts == 1,
                "140-8C-1: McapWriter Dispose attempts one flush before marking disposed");
            Check(!stream.Disposed,
                "140-8C-2: McapWriter leaveOpen=true still leaves the stream open after flush failure");
        }

        private static void McapRecorderConstructorDisposesOwnedWriterOnHeaderFailure()
        {
            var stream = new FailingStream { ThrowOnWrite = true };
            CheckThrows<IOException>(
                () => new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions { UseChunking = true },
                    leaveOpen: false),
                "140-8D-1: McapRecorder constructor surfaces header write failures");
            Check(stream.Disposed,
                "140-8D-2: McapRecorder constructor disposes its owned writer after header write failure");
        }

        private static void RecorderDefaultChunkSizeAliasesWriterDefault()
        {
            Check(McapRecorder.DefaultChunkSizeBytes == McapWriterOptions.DefaultChunkSizeBytes,
                "140-8E-1: recorder legacy default chunk size stays aligned with writer options");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            Check(source.Contains(
                    "public const int DefaultChunkSizeBytes = McapWriterOptions.DefaultChunkSizeBytes;",
                    StringComparison.Ordinal),
                "140-8E-2: recorder default chunk size is an alias, not an independent literal");
        }

        private static void SegmentCompressionRoundTripsWithoutCopyPatterns()
        {
            var source = Enumerable.Range(0, 1024).Select(i => (byte)(i * 17)).ToArray();
            var segment = new ArraySegment<byte>(source, 73, 811);
            foreach (var compression in new[] { "lz4", "zstd" })
            {
                var compressed = McapCompression.Compress(compression, segment);
                var compact = new byte[compressed.Count];
                Buffer.BlockCopy(compressed.Array, compressed.Offset, compact, 0, compressed.Count);
                var roundTrip = McapCompression.Decompress(compression, compact, segment.Count);
                Check(roundTrip.SequenceEqual(source.Skip(segment.Offset).Take(segment.Count)),
                    $"140-8F: {compression} compression preserves a non-zero-offset source segment");
            }

            var compressionSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common/McapCompression.cs");
            Check(!compressionSource.Contains("ms.ToArray()", StringComparison.Ordinal)
                  && !compressionSource.Contains("var copy = new byte[sourceCount]", StringComparison.Ordinal)
                  && !compressionSource.Contains("compressor.Wrap(copy).ToArray()", StringComparison.Ordinal),
                "140-8F-3: chunk compression avoids full input and output copy patterns");
        }

        private static void SummaryCrcUsesIncrementalSegments()
        {
            var recorderSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var conformanceSource = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceWriter.cs");
            foreach (var source in new[] { recorderSource, conformanceSource })
            {
                Check(!source.Contains("summaryBuilder.ToArray()", StringComparison.Ordinal)
                      && !source.Contains("var crcInput = new byte[", StringComparison.Ordinal),
                    "140-8G: summary writing avoids full summary and concatenated CRC copies");
            }
        }

        private static void RecorderReusesChannelScratchAndTopicSignature()
        {
            var source = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            Check(source.Contains("private readonly HashSet<ushort> _seenChannelIds = new();", StringComparison.Ordinal)
                  && source.Contains("private readonly List<ChannelWriteState> _allChannelWriteStates = new();", StringComparison.Ordinal)
                  && !source.Contains("var seen = new HashSet<ushort>();", StringComparison.Ordinal),
                "140-8H-1: recorder reuses explicit channel-state scratch collections");
            Check(source.Contains("CreateTopicSignature(", StringComparison.Ordinal)
                  && !source.Contains("var incoming = new TopicSignature", StringComparison.Ordinal),
                "140-8H-2: topic routing constructs one reusable signature instead of dead duplicate hashes");
        }

        private static void ReplaceChunkBufferWithNonPublicBuffer(McapRecorder recorder)
        {
            var field = typeof(McapRecorder).GetField("_chunkBuf", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(nameof(McapRecorder), "_chunkBuf");

            var nonPublicBuffer = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 }, 0, 3, writable: true, publiclyVisible: false);
            nonPublicBuffer.Position = nonPublicBuffer.Length;
            field.SetValue(recorder, nonPublicBuffer);
        }

        private static bool HasTrailingMagic(byte[] bytes)
        {
            var magic = McapWriter.Magic;
            if (bytes == null || bytes.Length < magic.Length)
                return false;
            for (var i = 0; i < magic.Length; i++)
                if (bytes[bytes.Length - magic.Length + i] != magic[i])
                    return false;
            return true;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

        private sealed class FailingStream : Stream
        {
            private readonly MemoryStream _inner = new();

            public bool ThrowOnWrite { get; set; }
            public bool ThrowOnFlush { get; set; }
            public long? FailWritesAtPosition { get; set; }
            public bool Disposed { get; private set; }
            public int FlushAttempts { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public byte[] ToArray() => _inner.ToArray();

            public override void Flush()
            {
                FlushAttempts++;
                if (ThrowOnFlush)
                    throw new IOException("phase140-8 flush failure");
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) =>
                _inner.Seek(offset, origin);

            public override void SetLength(long value) =>
                _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase140-8 write failure");

                if (FailWritesAtPosition.HasValue && _inner.Position + count > FailWritesAtPosition.Value)
                {
                    var allowed = (int)Math.Max(0, FailWritesAtPosition.Value - _inner.Position);
                    if (allowed > 0)
                        _inner.Write(buffer, offset, allowed);
                    throw new IOException("phase140-8 partial write failure");
                }

                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase140-8 write failure");

                if (FailWritesAtPosition.HasValue && _inner.Position >= FailWritesAtPosition.Value)
                    throw new IOException("phase140-8 partial write failure");

                _inner.WriteByte(value);
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
