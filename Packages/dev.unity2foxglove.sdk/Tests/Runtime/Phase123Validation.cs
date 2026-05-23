// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 123 validation for MCAP streaming reader and query parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase123Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 123: MCAP Streaming Reader And Query Parity ===");
            _passed = 0;

            VerifyOptionSurface();
            VerifyStreamingReader();
            VerifyQuerySemantics();
            VerifyIndexedFallbackControls();
            VerifyMetadataAttachmentFallback();
            VerifyCrcControls();
            VerifyConformanceWiring();

            Console.WriteLine($"Phase 123: {_passed} checks passed.");
        }

        private static void VerifyOptionSurface()
        {
            var options = new McapReadOptions();
            Check(options.Order == McapReadOrder.LogTimeAscending
                  && !options.UseOfficialEndTimeSemantics
                  && options.AllowLinearFallback
                  && options.ValidateCrcs,
                "123-A1: McapReadOptions defaults preserve existing product behavior");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapReadOptions.cs");
            foreach (var required in new[] { "McapReadOrder", "FileOrder", "LogTimeDescending", "UseOfficialEndTimeSemantics", "AllowLinearFallback", "ValidateCrcs" })
                Check(source.Contains(required, StringComparison.Ordinal), "123-A2: read option source exposes " + required);
        }

        private static void VerifyStreamingReader()
        {
            var direct = CreateTimedSample(new McapWriterOptions
            {
                UseChunking = false,
                EnableDataCrcs = true
            });

            using (var stream = new NonSeekableReadStream(new MemoryStream(direct)))
            using (var reader = new McapStreamingReader(stream, leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var result = reader.Read(new McapReadOptions { Order = McapReadOrder.FileOrder });
                Check(result.Messages.Count == 3
                      && Payload(result.Messages[0]) == "thirty"
                      && Payload(result.Messages[1]) == "ten"
                      && Payload(result.Messages[2]) == "twenty",
                    "123-B1: streaming reader reads non-seekable direct-message streams in file order");
                Check(result.Summary.Channels.Count == 1 && result.Summary.Schemas.Count == 1,
                    "123-B2: streaming reader tracks schema and channel inventory");
            }

            var chunked = CreateTimedSample(new McapWriterOptions { ChunkSizeBytes = 96 });
            using (var stream = new NonSeekableReadStream(new MemoryStream(chunked)))
            using (var reader = new McapStreamingReader(stream, leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var result = reader.Read();
                Check(result.Messages.Count == 3 && Payload(result.Messages[0]) == "ten",
                    "123-B3: streaming reader expands chunked messages and applies default ascending order");
            }
        }

        private static void VerifyQuerySemantics()
        {
            var direct = CreateTimedSample(new McapWriterOptions
            {
                UseChunking = false,
                EnableDataCrcs = true
            });

            using (var reader = new McapStreamingReader(new MemoryStream(direct), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var asc = reader.Read(new McapReadOptions { Order = McapReadOrder.LogTimeAscending }).Messages;
                Check(Payloads(asc) == "ten,twenty,thirty",
                    "123-C1: ascending ordering uses log time");
            }

            using (var reader = new McapStreamingReader(new MemoryStream(direct), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var desc = reader.Read(new McapReadOptions { Order = McapReadOrder.LogTimeDescending }).Messages;
                Check(Payloads(desc) == "thirty,twenty,ten",
                    "123-C2: descending ordering uses log time");
            }

            using (var reader = new McapStreamingReader(new MemoryStream(direct), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var inclusive = reader.Read(new McapReadOptions { StartTimeNs = 10, EndTimeNs = 20 }).Messages;
                Check(Payloads(inclusive) == "ten,twenty",
                    "123-C3: default EndTimeNs remains inclusive");
            }

            using (var reader = new McapStreamingReader(new MemoryStream(direct), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var exclusive = reader.Read(new McapReadOptions { StartTimeNs = 10, EndTimeNs = 20, UseOfficialEndTimeSemantics = true }).Messages;
                Check(Payloads(exclusive) == "ten",
                    "123-C4: official mode makes EndTimeNs exclusive");
            }

            using (var reader = new McapStreamingReader(new MemoryStream(direct), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var filtered = reader.Read(new McapReadOptions { Topics = new List<string> { "/phase123/topic" }, ChannelIds = new List<ushort> { 999 } }).Messages;
                Check(filtered.Count == 3,
                    "123-C5: topic and channel filters are OR-compatible with existing indexed behavior");
            }
        }

        private static void VerifyIndexedFallbackControls()
        {
            var direct = CreateTimedSample(new McapWriterOptions
            {
                UseChunking = false,
                EnableDataCrcs = true
            });
            var path = TempMcap(direct);
            using (var indexed = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests))
            {
                Check(indexed.ReadMessages().Count == 3,
                    "123-D1: indexed reader still allows product linear fallback by default");
                var threw = false;
                try
                {
                    indexed.ReadMessages(new McapReadOptions { AllowLinearFallback = false });
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                Check(threw, "123-D2: AllowLinearFallback=false preserves strict indexed behavior");
            }

            var chunked = TempMcap(CreateTimedSample(new McapWriterOptions { ChunkSizeBytes = 96 }));
            using (var indexed = McapIndexedReader.OpenRead(chunked, McapSequentialReadLimits.UnlimitedForTests))
            {
                var desc = indexed.ReadMessages(new McapReadOptions { Order = McapReadOrder.LogTimeDescending });
                Check(Payloads(desc) == "thirty,twenty,ten",
                    "123-D3: indexed reader honors descending order option");
                var exclusive = indexed.ReadMessages(new McapReadOptions { EndTimeNs = 20, UseOfficialEndTimeSemantics = true });
                Check(Payloads(exclusive) == "ten",
                    "123-D4: indexed reader honors official exclusive end-time option");
            }
        }

        private static void VerifyMetadataAttachmentFallback()
        {
            var bytes = CreateTimedSample(new McapWriterOptions
            {
                UseChunking = false,
                IndexTypes = McapIndexTypes.None,
                RepeatSchemas = false,
                RepeatChannels = false,
                UseStatistics = false,
                UseSummaryOffsets = false,
                EnableDataCrcs = true
            }, recorder =>
            {
                recorder.WriteMetadata("phase123.metadata", "{\"ok\":true}");
                recorder.AddAttachment("phase123.bin", "application/octet-stream", new byte[] { 1, 2, 3 }, 40);
            });

            using (var reader = new McapStreamingReader(new NonSeekableReadStream(new MemoryStream(bytes)), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                var result = reader.Read();
                Check(result.Metadata.Count == 1 && result.Attachments.Count == 1,
                    "123-E1: streaming reader returns metadata and attachment bodies without summary indexes");
            }

            using (var indexed = McapIndexedReader.OpenRead(TempMcap(bytes), McapSequentialReadLimits.UnlimitedForTests))
            {
                Check(indexed.MetadataIndexes.Count == 1 && indexed.AttachmentIndexes.Count == 1,
                    "123-E2: seekable summaryless inventory scan exposes metadata and attachment fallback indexes");
                Check(indexed.ReadMetadata(indexed.MetadataIndexes[0]).Name == "phase123.metadata"
                      && indexed.ReadAttachment(indexed.AttachmentIndexes[0]).Name == "phase123.bin",
                    "123-E3: metadata and attachment fallback indexes can be dereferenced");
            }
        }

        private static void VerifyCrcControls()
        {
            var bytes = CreateTimedSample(new McapWriterOptions
            {
                UseChunking = false,
                EnableDataCrcs = true
            });
            CorruptDataEndCrc(bytes);

            var threw = false;
            try
            {
                using var strict = new McapStreamingReader(new MemoryStream(bytes), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests);
                strict.Read(new McapReadOptions { ValidateCrcs = true });
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Check(threw, "123-F1: streaming reader validates non-zero DataEnd CRC");

            using (var relaxed = new McapStreamingReader(new MemoryStream(bytes), leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                Check(relaxed.Read(new McapReadOptions { ValidateCrcs = false }).Messages.Count == 3,
                    "123-F2: ValidateCrcs=false allows diagnostic reads through DataEnd CRC mismatch");
            }
        }

        private static void VerifyConformanceWiring()
        {
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceReader.cs");
            var indexed = ReadRepoText("Scripts/mcap/conformance/csharp-runners/CsharpIndexedReaderTestRunner.ts");
            var productIndexedReader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapIndexedReader.cs");
            Check(reader.Contains("McapStreamingReader", StringComparison.Ordinal)
                  && reader.Contains("NonSeekableReadStream", StringComparison.Ordinal)
                  && reader.Contains("AllowLinearFallback = false", StringComparison.Ordinal),
                "123-G1: conformance reader exercises streaming and strict indexed paths");
            Check(indexed.Contains("TestFeatures.UseChunkIndex", StringComparison.Ordinal)
                  && indexed.Contains("TestFeatures.UseMessageIndex", StringComparison.Ordinal)
                  && indexed.Contains("TestFeatures.UseRepeatedChannelInfos", StringComparison.Ordinal),
                "123-G2: C# indexed conformance runner only advertises strict indexed variants");
            Check(productIndexedReader.Contains("new McapStreamingReader", StringComparison.Ordinal),
                "123-G3: product indexed fallback delegates to streaming reader path");
        }

        private static byte[] CreateTimedSample(McapWriterOptions options, Action<McapRecorder> extra = null)
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, options, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase123/topic", "json", "phase123.Sample", "jsonschema", "{\"type\":\"string\"}");
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("thirty"));
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("ten"));
                recorder.WriteMessage(1, 20, Encoding.UTF8.GetBytes("twenty"));
                extra?.Invoke(recorder);
            }
            return ms.ToArray();
        }

        private static void CorruptDataEndCrc(byte[] bytes)
        {
            var offset = McapWriter.MagicLength;
            while (offset + McapWriter.RecordHeaderLength <= bytes.Length)
            {
                if (McapBinaryReader.MatchesMagic(bytes, offset))
                    return;
                var opcode = bytes[offset++];
                var len = McapBinaryReader.ReadU64LE(bytes, ref offset);
                if (opcode == McapWriter.OpcodeDataEnd)
                {
                    bytes[offset] ^= 0x5A;
                    return;
                }
                offset += (int)len;
            }

            throw new InvalidDataException("DataEnd record not found.");
        }

        private static string Payload(McapMessage message)
            => Encoding.UTF8.GetString(message.Data ?? Array.Empty<byte>());

        private static string Payloads(IReadOnlyList<McapMessage> messages)
            => string.Join(",", messages.Select(Payload));

        private static string TempMcap(byte[] bytes)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase123-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase123 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);
            _passed++;
            Console.WriteLine("[PASS] " + name);
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
        }
    }
}
