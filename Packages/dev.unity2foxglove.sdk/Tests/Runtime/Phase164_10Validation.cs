using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_10Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-10 Tests ---");
            _passed = 0;

            VerifySummaryReaderUsesSingleSummaryBuffer();
            VerifyMcapReaderInternalRecordBufferReuse();
            VerifyStreamingReaderContentBufferReuse();
            VerifySegmentDecodersProtectReusableBuffers();
            VerifyReplayOptimizationsRemainInPlace();
            VerifyRegistry();

            Console.WriteLine("Phase 164-10: " + _passed + " checks passed.\n");
        }

        private static void VerifySummaryReaderUsesSingleSummaryBuffer()
        {
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            var summaryBuilder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapSummaryBuilder.cs");
            var readSummary = SourceMethod(reader, "public McapFileSummary ReadSummary");
            var readTrailerInfo = SourceMethod(reader, "internal McapTrailerInfo ReadTrailerInfo");

            Check(readSummary.Contains("ReadExact(_buf, 0, _buf.Length)", StringComparison.Ordinal)
                  && readSummary.Contains("_buf is reused; leading magic was already validated above", StringComparison.Ordinal)
                  && !readSummary.Contains("new byte[8]", StringComparison.Ordinal),
                "164-10A-1: McapReader reuses the instance 8-byte buffer for magic probes");
            Check(readSummary.Contains("var summaryBytes = new byte[(int)summaryLen]", StringComparison.Ordinal)
                  && readSummary.Contains("ReadExact(summaryBytes, 0, summaryBytes.Length)", StringComparison.Ordinal)
                  && readSummary.Contains("McapSummaryBuilder.FromSummarySection", StringComparison.Ordinal)
                  && summaryBuilder.Contains("var summaryOffset = 0", StringComparison.Ordinal)
                  && summaryBuilder.Contains("McapBinaryReader.ReadU64LE(summaryBytes, ref summaryOffset)", StringComparison.Ordinal)
                  && !readSummary.Contains("while ((ulong)_stream.Position < summaryEnd)", StringComparison.Ordinal)
                  && !readSummary.Contains("ReadOneRecord(recordSizeLimit)", StringComparison.Ordinal),
                "164-10A-2: ReadSummary reads the summary section once and parses it from memory");
            Check(summaryBuilder.Contains("crc = Crc32Helper.Update(crc, summaryBytes)", StringComparison.Ordinal)
                  && CountOccurrences(readSummary, "new byte[(int)summaryLen]") == 1,
                "164-10A-3: summary CRC reuses the parsed summary buffer instead of reading it again");
            Check(readTrailerInfo.Contains("var summaryBytes = ReadSummaryBytes(footer.SummaryStart, footerOffset)", StringComparison.Ordinal)
                  && readTrailerInfo.Contains("ValidateSummaryCrc(\n                summaryBytes", StringComparison.Ordinal)
                  && CountOccurrences(reader, "new byte[(int)summaryLen]") == 2,
                "164-10A-4: ReadTrailerInfo reads summary bytes once for CRC validation");
        }

        private static void VerifyMcapReaderInternalRecordBufferReuse()
        {
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            var publicReadOne = SourceMethod(reader, "public (byte opcode, byte[] content) ReadOneRecord");
            var segmentedReadOne = SourceMethod(reader, "private (byte opcode, byte[] content, int contentLength) ReadOneRecordSegment");
            var ensure = SourceMethod(reader, "private byte[] EnsureRecordContentBuffer");

            Check(reader.Contains("private byte[] _recordContentBuffer", StringComparison.Ordinal)
                  && segmentedReadOne.Contains("EnsureRecordContentBuffer(contentLengthInt)", StringComparison.Ordinal)
                  && !segmentedReadOne.Contains("new byte[contentLength]", StringComparison.Ordinal),
                "164-10B-1: internal MCAP record scans reuse a grow-only content buffer");
            Check(reader.Contains("is invalidated by the next call to this method", StringComparison.Ordinal)
                  && reader.Contains("callers that need to retain", StringComparison.Ordinal),
                "164-10B-1b: internal reusable record buffer contract is documented");
            Check(publicReadOne.Contains("ReadOneRecordSegment(sizeLimit)", StringComparison.Ordinal)
                  && publicReadOne.Contains("CloneBytes(content, contentLength)", StringComparison.Ordinal),
                "164-10B-2: public ReadOneRecord keeps ownership-safe byte[] semantics");
            Check(ensure.Contains("_recordContentBuffer == null || _recordContentBuffer.Length < count", StringComparison.Ordinal)
                  && ensure.Contains("_recordContentBuffer = new byte[count]", StringComparison.Ordinal),
                "164-10B-3: reusable reader content buffer only grows when a larger record appears");
            Check(!reader.Contains("ReadOneRecord(recordSizeLimit)", StringComparison.Ordinal)
                  && CountOccurrences(reader, "ReadOneRecord();") == 0,
                "164-10B-4: internal reader paths no longer call the allocating public ReadOneRecord overload");
        }

        private static void VerifyStreamingReaderContentBufferReuse()
        {
            var streaming = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapStreamingReader.cs");
            var read = SourceMethod(streaming, "public McapStreamingReadResult Read");
            var processRecord = SourceMethod(streaming, "private void ProcessRecord");
            var readExactContent = SourceMethod(streaming, "private byte[] ReadExactContent");

            Check(streaming.Contains("private byte[] _contentBuffer", StringComparison.Ordinal)
                  && read.Contains("var content = ReadExactContent(contentLengthInt)", StringComparison.Ordinal)
                  && read.Contains("new ReadOnlySpan<byte>(content, 0, contentLengthInt)", StringComparison.Ordinal),
                "164-10C-1: streaming reader reuses a content buffer and CRCs only valid bytes");
            Check(processRecord.Contains("int contentLength", StringComparison.Ordinal)
                  && processRecord.Contains("DecodeMessage(content, 0, contentLength)", StringComparison.Ordinal)
                  && processRecord.Contains("ValidateDataEnd(content, contentLength", StringComparison.Ordinal),
                "164-10C-2: streaming record processing carries explicit content length with reusable buffers");
            Check(readExactContent.Contains("_contentBuffer == null || _contentBuffer.Length < count", StringComparison.Ordinal)
                  && readExactContent.Contains("_contentBuffer = new byte[count]", StringComparison.Ordinal),
                "164-10C-3: streaming content buffer only grows when needed");
            Check(!streaming.Contains("private byte[] ReadExact(int count)", StringComparison.Ordinal)
                  && !streaming.Contains("Slice(uncompressedRecords", StringComparison.Ordinal)
                  && streaming.Contains("McapWriter.MagicSpan", StringComparison.Ordinal),
                "164-10C-4: streaming reader avoids per-record content slices and defensive magic copies");
        }

        private static void VerifySegmentDecodersProtectReusableBuffers()
        {
            var decoder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapRecordDecoder.cs");

            Check(decoder.Contains("DecodeChunkRecordsContent(\n            byte[] content,\n            int offset,\n            int contentLen", StringComparison.Ordinal)
                  && decoder.Contains("DecodeHeader(byte[] content, int offset, int contentLen)", StringComparison.Ordinal)
                  && decoder.Contains("DecodeFooter(byte[] content, int offset, int contentLen)", StringComparison.Ordinal),
                "164-10D-1: decoder exposes segment overloads for chunk/header/footer records");
            Check(decoder.Contains("DecodeMetadata(byte[] content, int offset, int contentLen)", StringComparison.Ordinal)
                  && decoder.Contains("DecodeAttachment(byte[] content, int offset, int contentLen)", StringComparison.Ordinal)
                  && decoder.Contains("DecodeAttachmentIndex(byte[] content, int offset, int contentLen)", StringComparison.Ordinal)
                  && decoder.Contains("DecodeStatistics(byte[] content, int offset, int contentLen)", StringComparison.Ordinal),
                "164-10D-2: decoder exposes segment overloads for retained object records");
        }

        private static void VerifyReplayOptimizationsRemainInPlace()
        {
            var replay = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var pendingQueue = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayPendingQueue.cs");
            var controller = PhaseValidationSourceHelpers.ReadReplayControllerSources();

            Check(controller.Contains("_replayTickBuffer", StringComparison.Ordinal)
                  && controller.Contains("_replayEngine.Tick(nowNs, _replayTickBuffer)", StringComparison.Ordinal),
                "164-10E-1: ReplayController uses caller-owned tick buffers instead of per-frame Tick allocations");
            Check(replay.Contains("_snapshotLatestByChannel", StringComparison.Ordinal)
                  && replay.Contains("var latestByChannel = _snapshotLatestByChannel", StringComparison.Ordinal),
                "164-10E-2: replay snapshots reuse the latest-by-channel dictionary");
            Check(replay.Contains("private readonly McapReplayPendingQueue _pending = new()", StringComparison.Ordinal)
                  && pendingQueue.Contains("private int _headIndex", StringComparison.Ordinal)
                  && !pendingQueue.Contains("RemoveAt(0)", StringComparison.Ordinal)
                  && !replay.Contains("private static void AddHistoryMessage", StringComparison.Ordinal),
                "164-10E-3: replay pending queue avoids front RemoveAt shifts");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-10\"", StringComparison.Ordinal), "164-10F-1: validation registry exposes Phase164-10");
            Check(project.Contains("Phase164_10Validation.cs", StringComparison.Ordinal), "164-10F-2: runtime validation project compiles Phase164-10");
        }

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

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                throw new InvalidOperationException("Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(
                        dir.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "Runtime",
                        "FoxgloveSdk.Tests.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
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
