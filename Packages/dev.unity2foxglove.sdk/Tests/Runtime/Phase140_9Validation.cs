// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-9 MCAP reader and indexing review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for MCAP reader and indexing defects found in Phase 140-9.
    /// </summary>
    public static class Phase140_9Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-9 MCAP reader and indexing review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-9: MCAP readers and indexing review fixes ===");
            _passed = 0;

            LinearFallbackReevaluatesWhenChunkSizeLimitChanges();
            StreamingReaderDeduplicatesBodyAndSummaryIndexes();
            ReaderRejectsOffsetsBeyondSeekableRange();
            ReaderReportsNonSeekableStreamGuidance();
            ReaderXmlDocDoesNotContainDuplicateSummaryTag();
            CompressedChunkReadersPreserveMessages();
            ReaderOptimizationShapesAvoidKnownCopies();

            Console.WriteLine($"Phase 140-9: {_passed} checks passed.");
        }

        private static void LinearFallbackReevaluatesWhenChunkSizeLimitChanges()
        {
            var bytes = CreateFixture(new McapWriterOptions
            {
                UseChunking = true,
                IndexTypes = McapIndexTypes.None,
                RepeatSchemas = false,
                RepeatChannels = false,
                UseStatistics = false,
                UseSummaryOffsets = false
            });

            using var indexed = new McapIndexedReader(new MemoryStream(bytes), leaveOpen: false);
            var messages = indexed.ReadMessages(new McapReadOptions
            {
                ChunkUncompressedSizeLimit = 0
            });

            Check(messages.Count == 3,
                "140-9A-1: linear fallback reads with unlimited chunk size");
            CheckThrowsWith<InvalidDataException>(
                () => indexed.ReadMessages(new McapReadOptions { ChunkUncompressedSizeLimit = 1 }),
                "exceeds limit",
                "140-9A-2: tighter chunk size limit is enforced on a later linear fallback read");
        }

        private static void StreamingReaderDeduplicatesBodyAndSummaryIndexes()
        {
            var bytes = CreateFixture(new McapWriterOptions
            {
                UseChunking = false,
                IndexTypes = McapIndexTypes.All,
                RepeatSchemas = true,
                RepeatChannels = true,
                UseStatistics = true,
                UseSummaryOffsets = true
            }, recorder =>
            {
                recorder.WriteMetadata("phase140-9.metadata", "{\"ok\":true}");
                recorder.AddAttachment(
                    "phase140-9.bin",
                    "application/octet-stream",
                    new byte[] { 1, 2, 3, 4 },
                    50);
            });

            using var reader = new McapStreamingReader(new MemoryStream(bytes), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests);
            var result = reader.Read();

            Check(result.Summary.MetadataIndexes.Count == 1,
                "140-9B-1: streaming reader returns one metadata index for indexed files");
            Check(result.Summary.AttachmentIndexes.Count == 1,
                "140-9B-2: streaming reader returns one attachment index for indexed files");
            Check(result.Metadata.Count == 1 && result.Attachments.Count == 1,
                "140-9B-3: streaming reader still returns metadata and attachment bodies");
        }

        private static void ReaderRejectsOffsetsBeyondSeekableRange()
        {
            using var stream = new MemoryStream(CreateFixture(new McapWriterOptions { UseChunking = false }));
            var reader = new McapReader(stream);

            CheckThrowsWith<InvalidDataException>(
                () => reader.ReadChunkRecords(ulong.MaxValue, 0, out _),
                "exceeds seekable range",
                "140-9C-1: chunk record offset overflow reports InvalidDataException");
            CheckThrowsWith<InvalidDataException>(
                () => reader.ReadAttachmentAt(ulong.MaxValue),
                "exceeds seekable range",
                "140-9C-2: attachment offset overflow reports InvalidDataException");
            CheckThrowsWith<InvalidDataException>(
                () => reader.ReadMetadataAt(ulong.MaxValue),
                "exceeds seekable range",
                "140-9C-3: metadata offset overflow reports InvalidDataException");
        }

        private static void ReaderReportsNonSeekableStreamGuidance()
        {
            using (var stream = new NonSeekableReadStream(new MemoryStream(CreateFixture(new McapWriterOptions { UseChunking = false }))))
            {
                var reader = new McapReader(stream);
                CheckThrowsWith<NotSupportedException>(
                    () => reader.ReadSummary(),
                    "McapStreamingReader",
                    "140-9D-1: McapReader points non-seekable callers to McapStreamingReader");
            }
        }

        private static void ReaderXmlDocDoesNotContainDuplicateSummaryTag()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            Check(!source.Contains("/// <summary>\r\n        /// <summary>", StringComparison.Ordinal)
                  && !source.Contains("/// <summary>\n        /// <summary>", StringComparison.Ordinal),
                "140-9E-1: McapReader XML docs do not contain duplicate summary opening tags");
        }

        private static void CompressedChunkReadersPreserveMessages()
        {
            foreach (var compression in new[] { "lz4", "zstd" })
            {
                var bytes = CreateFixture(new McapWriterOptions { Compression = compression });
                using var indexed = new McapIndexedReader(new MemoryStream(bytes), leaveOpen: false);
                Check(indexed.ReadMessages().Select(message => message.LogTime).SequenceEqual(new ulong[] { 10, 20, 30 }),
                    $"140-9F: indexed reader preserves {compression} chunk messages");

                using var streaming = new McapStreamingReader(
                    new MemoryStream(bytes),
                    leaveOpen: false,
                    McapSequentialReadLimits.UnlimitedForTests);
                Check(streaming.Read().Messages.Select(message => message.LogTime).SequenceEqual(new ulong[] { 10, 20, 30 }),
                    $"140-9F: streaming reader preserves {compression} chunk messages");
            }
        }

        private static void ReaderOptimizationShapesAvoidKnownCopies()
        {
            var decoder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapRecordDecoder.cs");
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            var streaming = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapStreamingReader.cs");
            var indexed = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapIndexedReader.cs");

            Check(!decoder.Contains("var compressed = new byte[(int)compSize]", StringComparison.Ordinal)
                  && decoder.Contains("new ArraySegment<byte>(content, off, (int)compSize)", StringComparison.Ordinal),
                "140-9G-1: chunk decoder passes the compressed source segment without a full copy");
            Check(!reader.Contains("var crcInput = new byte[", StringComparison.Ordinal),
                "140-9G-2: summary CRC validation avoids a concatenated copy");
            Check(streaming.Contains("private readonly byte[] _recordHeaderBuffer", StringComparison.Ordinal)
                  && !streaming.Contains("headerBytes = new byte[McapWriter.RecordHeaderLength]", StringComparison.Ordinal),
                "140-9G-3: streaming reader reuses its record header buffer");
            Check(indexed.Contains("VisitSequentialMessages", StringComparison.Ordinal)
                  && !indexed.Contains("_linearMessagesCache", StringComparison.Ordinal)
                  && !indexed.Contains("new List<McapMessage>(ReadLinearMessages", StringComparison.Ordinal),
                "140-9G-4: latest-before linear fallback does not retain or sort a full reader-wide message cache");
        }

        private static byte[] CreateFixture(McapWriterOptions options, Action<McapRecorder> extra = null)
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(stream, null, options, leaveOpen: true))
            {
                recorder.AddChannel(
                    1,
                    "/phase140_9/topic",
                    "json",
                    "phase140_9.Sample",
                    "jsonschema",
                    "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"n\":10}"));
                recorder.WriteMessage(1, 20, Encoding.UTF8.GetBytes("{\"n\":20}"));
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("{\"n\":30}"));
                extra?.Invoke(recorder);
            }

            return stream.ToArray();
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void CheckThrowsWith<TException>(Action action, string expectedMessagePart, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex) when (ex.Message.IndexOf(expectedMessagePart, StringComparison.OrdinalIgnoreCase) >= 0)
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

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableReadStream(Stream inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
