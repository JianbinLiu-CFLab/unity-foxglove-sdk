using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_9Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-9 Tests ---");
            _passed = 0;

            VerifyCrc32SlicingByEight();
            VerifyChunkMessageHeaderSingleWrite();
            VerifyCompressionStateReuse();
            VerifyRegistry();

            Console.WriteLine("Phase 164-9: " + _passed + " checks passed.\n");
        }

        private static void VerifyCrc32SlicingByEight()
        {
            var crcSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/Crc32Helper.cs");
            var update = SourceMethod(crcSource, "public static uint Update");
            var standardVector = Encoding.ASCII.GetBytes("123456789");

            Check(Crc32Helper.Compute(standardVector) == 0xCBF43926u,
                "164-9A-1: CRC32 still matches the standard IEEE test vector");
            Check(crcSource.Contains("_slicingTables", StringComparison.Ordinal)
                  && crcSource.Contains("BuildSlicingTables()", StringComparison.Ordinal)
                  && update.Contains("while (offset <= data.Length - 8)", StringComparison.Ordinal)
                  && update.Contains("_slicingTables[7]", StringComparison.Ordinal),
                "164-9A-2: CRC32 span updates use slicing-by-8 tables");
        }

        private static void VerifyChunkMessageHeaderSingleWrite()
        {
            var recorder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");
            var writeMessage = SourceMethod(recorder, "private void WriteMessageToChannelWriteState");

            Check(recorder.Contains("new MemoryStream(_chunkSz)", StringComparison.Ordinal),
                "164-9B-1: recorder chunk buffer is pre-sized to the configured chunk size");
            Check(recorder.Contains("private readonly byte[] _messageRecordHeader", StringComparison.Ordinal)
                  && writeMessage.Contains("var header = _messageRecordHeader", StringComparison.Ordinal)
                  && writeMessage.Contains("_chunkBuf.Write(header, 0, header.Length)", StringComparison.Ordinal)
                  && !writeMessage.Contains("_chunkBuf.WriteByte(McapWriter.OpcodeMessage)", StringComparison.Ordinal)
                  && !writeMessage.Contains("McapWriter.WriteU64(_chunkBuf, (ulong)contentLength)", StringComparison.Ordinal),
                "164-9B-2: chunked message records write a reusable header buffer once per message");
            Check(writer.Contains("internal static void WriteU16(byte[] buffer, int offset, ushort v)", StringComparison.Ordinal)
                  && writer.Contains("internal static void WriteU32(byte[] buffer, int offset, uint v)", StringComparison.Ordinal)
                  && writer.Contains("internal static void WriteU64(byte[] buffer, int offset, ulong v)", StringComparison.Ordinal),
                "164-9B-3: MCAP writer exposes byte-array little-endian helpers for reusable record headers");
        }

        private static void VerifyCompressionStateReuse()
        {
            var recorder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var compression = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common/McapCompression.cs");
            var flush = SourceMethod(recorder, "void FlushChunk()");
            var compress = SourceMethod(compression, "internal static ArraySegment<byte> Compress");

            Check(recorder.Contains("private Compressor _zstdCompressor", StringComparison.Ordinal)
                  && recorder.Contains("private byte[] _zstdCompressionBuffer", StringComparison.Ordinal)
                  && flush.Contains("_zstdCompressor ??= new Compressor()", StringComparison.Ordinal)
                  && flush.Contains("ref _zstdCompressionBuffer", StringComparison.Ordinal),
                "164-9C-1: recorder reuses zstd compressor state and output storage across chunk flushes");
            Check(compress.Contains("Compressor zstdCompressor", StringComparison.Ordinal)
                  && compress.Contains("ref byte[] zstdOutputBuffer", StringComparison.Ordinal)
                  && compress.Contains("zstdOutputBuffer == null || zstdOutputBuffer.Length < outputBound", StringComparison.Ordinal)
                  && compress.Contains("new ArraySegment<byte>(zstdOutputBuffer, 0, zstdOutputBuffer.Length)", StringComparison.Ordinal)
                  && compress.Contains("if (ownsCompressor)", StringComparison.Ordinal),
                "164-9C-2: MCAP compression supports caller-owned zstd state without changing public API semantics");
            Check(recorder.Contains("_zstdCompressor?.Dispose()", StringComparison.Ordinal)
                  && recorder.Contains("_zstdCompressionBuffer = null", StringComparison.Ordinal),
                "164-9C-3: recorder releases reusable zstd state during disposal");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-9\"", StringComparison.Ordinal), "164-9D-1: validation registry exposes Phase164-9");
            Check(project.Contains("Phase164_9Validation.cs", StringComparison.Ordinal), "164-9D-2: runtime validation project compiles Phase164-9");
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
