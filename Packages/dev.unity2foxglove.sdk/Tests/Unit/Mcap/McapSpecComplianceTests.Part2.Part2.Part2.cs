// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP specification conformance regressions for writer policy,
//          durable chunk recovery, record extensions, and reader structure.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{


public partial class McapSpecComplianceTests
    {
        private static void WriteU64LittleEndian(byte[] bytes, int offset, ulong value)
        {
            for (var i = 0; i < sizeof(ulong); i++)
                bytes[offset + i] = (byte)(value >> (8 * i));
        }

        private static void WriteU32LittleEndian(byte[] bytes, int offset, uint value)
        {
            for (var i = 0; i < sizeof(uint); i++)
                bytes[offset + i] = (byte)(value >> (8 * i));
        }

        private static void WriteUncheckedRecord(McapWriter writer, byte opcode, byte[] content)
        {
            var streamField = typeof(McapWriter).GetField("_stream", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var stream = (Stream)streamField?.GetValue(writer)
                ?? throw new InvalidOperationException("Could not access the test writer stream.");
            stream.WriteByte(opcode);
            McapWriter.WriteU64(stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0)
                stream.Write(content, 0, content.Length);
        }

        private static byte[] SummaryBytes(Action<McapWriter> writeSummary)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
                writeSummary(writer);
            return stream.ToArray();
        }

        private static byte[] CreateFaultMatrixFixture()
        {
            using var stream = new MemoryStream();
            WriteFaultMatrixFixture(stream, out _);
            return stream.ToArray();
        }

        private static AbsoluteFaultStream RecordFaultMatrixFixture(long target, out Exception writeError)
        {
            var stream = new AbsoluteFaultStream(target);
            WriteFaultMatrixFixture(stream, out writeError);
            return stream;
        }

        private static AbsoluteFaultStream RecordCloseFaultMatrixFixture(
            long target,
            bool throwOnFlush,
            out Exception closeError)
        {
            var stream = new AbsoluteFaultStream(target) { ThrowOnNextFlush = throwOnFlush };
            var recorder = CreateFaultMatrixRecorder(stream);
            recorder.WriteMessage(1, 10, new byte[64]);
            recorder.WriteMessage(1, 20, new byte[64]);
            recorder.WriteMessage(1, 30, new byte[64]);
            closeError = RecordException(recorder.Close);
            recorder.Dispose();
            stream.Position = 0;
            return stream;
        }

        private static void WriteFaultMatrixFixture(Stream stream, out Exception writeError)
        {
            var recorder = CreateFaultMatrixRecorder(stream);
            writeError = null;
            try
            {
                recorder.WriteMessage(1, 10, new byte[64]);
                recorder.WriteMessage(1, 20, new byte[64]);
                recorder.WriteMessage(1, 30, new byte[64]);
            }
            catch (Exception ex)
            {
                writeError = ex;
            }

            recorder.Close();
            recorder.Dispose();
            stream.Position = 0;
        }

        private static McapRecorder CreateFaultMatrixRecorder(Stream stream)
        {
            var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions
                {
                    ChunkSizeBytes = 32,
                    Compression = "",
                    UseChunking = true,
                    IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message,
                    RepeatSchemas = true,
                    RepeatChannels = true,
                    UseStatistics = true,
                    UseSummaryOffsets = true,
                    EnableCrcs = true
                },
                leaveOpen: true);
            recorder.AddChannel(1, "/mcap/fault-matrix", "json", "mcap.FaultMatrix", "jsonschema", "{}");
            return recorder;
        }

        private static List<McapMessage> ReadMessages(Stream stream)
        {
            stream.Position = 0;
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            return reader.ReadMessages();
        }

        private static byte[] SnapshotBytes(Stream stream)
        {
            var originalPosition = stream.Position;
            stream.Position = 0;
            var bytes = new byte[stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Could not snapshot the complete MCAP stream.");
                offset += read;
            }
            stream.Position = originalPosition;
            return bytes;
        }

        private static List<long> FindRecordOffsets(byte[] bytes, byte opcode)
        {
            var result = new List<long>();
            var off = McapWriter.MagicLength;
            var bodyEnd = bytes.Length - McapWriter.MagicLength;
            while (off < bodyEnd)
            {
                var recordStart = off;
                var recordOpcode = bytes[off++];
                var length = McapBinaryReader.ReadU64LE(bytes, ref off);
                if (recordOpcode == opcode)
                    result.Add(recordStart);
                off = checked(off + (int)length);
            }
            return result;
        }

        private static long FindFirstSummaryRecordOffset(byte[] bytes)
        {
            var dataEnd = FindRecordOffsets(bytes, McapWriter.OpcodeDataEnd).Single();
            var off = checked((int)dataEnd + McapWriter.RecordHeaderLength + McapWriter.Crc32SizeBytes);
            if (bytes[off] == McapWriter.OpcodeFooter)
                throw new InvalidOperationException("Fault fixture unexpectedly has no summary records.");
            return off;
        }

        private static Exception RecordException(Action action)
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

        private static (uint Sequence, ulong MessageCount)
            ReadOnlyServerChannelCounters(McapRecorder recorder)
        {
            var field = typeof(McapRecorder).GetField(
                "_serverChannelWriteStates",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            var states = Assert.IsAssignableFrom<IDictionary>(field?.GetValue(recorder));
            var state = Assert.Single(states.Values.Cast<object>());
            var type = state.GetType();
            var sequence = Assert.IsType<uint>(type.GetField("Seq")?.GetValue(state));
            var messageCount = Assert.IsType<ulong>(
                type.GetField("MsgCount")?.GetValue(state));
            return (sequence, messageCount);
        }

        private sealed class AbsoluteFaultStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();
            private long _faultPosition;

            public AbsoluteFaultStream(long faultPosition)
            {
                _faultPosition = faultPosition;
            }

            public bool ThrowOnNextFlush { get; set; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }

            public override void Flush()
            {
                if (ThrowOnNextFlush)
                {
                    ThrowOnNextFlush = false;
                    throw new IOException("Injected MCAP flush failure.");
                }
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_faultPosition >= Position && _faultPosition < Position + count)
                {
                    var writable = checked((int)(_faultPosition - Position));
                    if (writable > 0)
                        _inner.Write(buffer, offset, writable);
                    _faultPosition = -1;
                    throw new IOException("Injected MCAP write failure.");
                }
                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (_faultPosition == Position)
                {
                    _faultPosition = -1;
                    throw new IOException("Injected MCAP write failure.");
                }
                _inner.WriteByte(value);
            }
        }

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            internal readonly List<string> Warnings = new List<string>();

            public void LogWarning(string message) => Warnings.Add(message);

            public void LogError(string message)
            {
            }
        }

        private sealed class RestoreExercisingFileOperations : IMcapAmendmentFileOperations
        {
            private readonly string _destination;
            private bool _failedTempPromotion;

            public RestoreExercisingFileOperations(string destination)
            {
                _destination = Path.GetFullPath(destination);
            }

            public bool RestoreAttempted { get; private set; }

            public void Replace(string source, string destination, string backup)
                => throw new PlatformNotSupportedException("Exercise portable replacement fallback.");

            public void Move(string source, string destination)
            {
                var fullSource = Path.GetFullPath(source);
                var fullDestination = Path.GetFullPath(destination);
                if (!_failedTempPromotion && fullDestination == _destination && fullSource.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    _failedTempPromotion = true;
                    throw new IOException("Injected temp promotion failure.");
                }
                if (_failedTempPromotion && fullDestination == _destination && fullSource.EndsWith(".bak", StringComparison.Ordinal))
                    RestoreAttempted = true;
                File.Move(source, destination);
            }

            public bool Exists(string path) => File.Exists(path);
        }

        private sealed class WriteCallTrackingStream : MemoryStream
        {
            public int BulkWriteCalls { get; private set; }
            public int ByteWriteCalls { get; private set; }

            public override void Write(byte[] buffer, int offset, int count)
            {
                BulkWriteCalls++;
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                BulkWriteCalls++;
                var copy = buffer.ToArray();
                base.Write(copy, 0, copy.Length);
            }

            public override void WriteByte(byte value)
            {
                ByteWriteCalls++;
                base.WriteByte(value);
            }
        }

        private sealed class FailOnceWriteStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();
            private long _remainingBeforeThrow = -1;

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

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_remainingBeforeThrow >= 0 && count > _remainingBeforeThrow)
                {
                    var writable = (int)_remainingBeforeThrow;
                    if (writable > 0)
                        _inner.Write(buffer, offset, writable);
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow >= 0)
                    _remainingBeforeThrow -= count;
                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (_remainingBeforeThrow == 0)
                {
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow > 0)
                    _remainingBeforeThrow--;
                _inner.WriteByte(value);
            }
        }
    }
}
