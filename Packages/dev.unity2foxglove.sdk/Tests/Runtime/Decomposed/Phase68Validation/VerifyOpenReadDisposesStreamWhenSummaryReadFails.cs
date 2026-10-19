// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 68 validation for the MCAP indexed reader surface.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the public MCAP indexed reader API and its local query
    /// behavior.
    /// </summary>
    public static partial class Phase68Validation
    {
        private static void VerifyOpenReadDisposesStreamWhenSummaryReadFails()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase68_invalid_" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not an mcap"));

            try
            {
                var openError = CatchException(() =>
                {
                    using var _ = McapIndexedReader.OpenRead(path);
                });
                Check(openError != null,
                    "68E-6: OpenRead invalid file throws");
                try
                {
                    File.Delete(path);
                }
                catch (IOException ex)
                {
                    throw new Exception("[FAIL] 68E-7: OpenRead releases invalid file handle after failure: " + ex.Message, ex);
                }
                Check(!File.Exists(path), "68E-7: OpenRead releases invalid file handle after failure");
            }
            finally
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch { }
            }
        }

        private static byte[] CreateTwoChunkFixtureBytes(out ulong firstChunkEndTime, out ulong secondChunkStartTime)
        {
            using var clean = new MemoryStream();
            using (var recorder = new McapRecorder(clean, chunkSizeBytes: 80))
            {
                recorder.AddChannel(1, "/phase68/corrupt", "json", "phase68.Corrupt", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes(new string('a', 64)));
                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes(new string('b', 64)));
                recorder.Close();
            }

            clean.Position = 0;
            var summary = new McapReader(clean).ReadSummary();
            Check(summary.ChunkIndexes.Count >= 2,
                "68F-0: malformed chunk fixture produces two indexed chunks");

            var firstChunk = summary.ChunkIndexes[0];
            var secondChunk = summary.ChunkIndexes[1];
            firstChunkEndTime = firstChunk.MessageEndTime;
            secondChunkStartTime = secondChunk.MessageStartTime;

            var bytes = clean.ToArray();
            const int mcapRecordHeaderLength = 1 + 8;
            const int chunkUncompressedCrcOffsetInContent = 8 + 8 + 8;
            var corruptOffset = checked((int)(secondChunk.ChunkStartOffset
                + mcapRecordHeaderLength
                + chunkUncompressedCrcOffsetInContent));
            bytes[corruptOffset] ^= 0x01;
            return bytes;
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static Exception CatchException(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
