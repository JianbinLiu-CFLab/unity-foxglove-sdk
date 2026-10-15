// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Focused OpenH264 encoder sidecar implementation section.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    public sealed partial class OpenH264EncoderSidecar
    {
        private async Task RunStdinWriter(Process process, CancellationToken token)
        {
            try
            {
                var stream = process.StandardInput.BaseStream;
                var protocolHeader = new byte[OpenH264AllocationPolicy.ProtocolHeaderBytes];
                while (!token.IsCancellationRequested && IsProcessRunning(process))
                {
                    if (TryDequeueInputFrame(process, token, out var frame))
                    {
                        byte[] converted = null;
                        try
                        {
                            var data = frame.Data;
                            var length = frame.Length;
                            if (frame.IsRgb24)
                            {
                                var options = _options;
                                var i420Length = options != null ? options.FrameByteCount : 0;
                                if (i420Length <= 0)
                                {
                                    LastError = "OpenH264 encoder dimensions produce an invalid I420 frame size.";
                                    LastDiagnosticLine = LastError;
                                    Interlocked.Increment(ref _droppedInputFrames);
                                    continue;
                                }

                                converted = ArrayPool<byte>.Shared.Rent(i420Length);
                                string conversionError;
                                if (!Rgb24ToI420Converter.TryConvertRgb24ToI420(
                                        frame.Data,
                                        options.Width,
                                        options.Height,
                                        converted,
                                        flipVertical: true,
                                        out conversionError))
                                {
                                    LastError = conversionError ?? "OpenH264 RGB24 conversion failed.";
                                    LastDiagnosticLine = LastError;
                                    Interlocked.Increment(ref _droppedInputFrames);
                                    continue;
                                }

                                data = converted;
                                length = i420Length;
                            }

                            await WaitForProtocolNegotiation(token).ConfigureAwait(false);
                            var protocolV2 = Volatile.Read(ref _helperProtocolVersion) >= 2;
                            var countedWrittenFrame = false;
                            try
                            {
                                if (protocolV2)
                                {
                                    Interlocked.Increment(ref _writtenFrameCount);
                                    countedWrittenFrame = true;
                                    PopulateProtocolHeader(protocolHeader, frame.TimestampNs, length);
                                    await stream.WriteAsync(protocolHeader, 0, protocolHeader.Length, token).ConfigureAwait(false);
                                }
                                else
                                {
                                    _encodedFrameTimestamps.Enqueue(frame.TimestampNs);
                                }

                                await stream.WriteAsync(data, 0, length, token).ConfigureAwait(false);
                                await stream.FlushAsync(token).ConfigureAwait(false);
                            }
                            catch
                            {
                                if (countedWrittenFrame)
                                    ConsumeWrittenFrame();
                                throw;
                            }
                        }
                        finally
                        {
                            if (converted != null)
                                ArrayPool<byte>.Shared.Return(converted);
                            ReturnInputFrameBuffer(frame);
                            Interlocked.Decrement(ref _processingFrameCount);
                        }
                    }
                    else
                    {
                        await Task.Delay(2, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RetireFailedProcess(process, token, ex.Message);
            }
        }

        private Task RunStdoutReader(Process process, CancellationToken token)
            => RunStdoutReaderForSession(process, token, Volatile.Read(ref _sessionId));

        private async Task RunStdoutReaderForSession(Process process, CancellationToken token, long sessionId)
        {
            try
            {
                await WaitForProtocolNegotiation(token).ConfigureAwait(false);
                var stream = process.StandardOutput.BaseStream;
                if (Volatile.Read(ref _helperProtocolVersion) >= 2)
                {
                    var header = new byte[OpenH264AllocationPolicy.ProtocolHeaderBytes];
                    while (!token.IsCancellationRequested)
                    {
                        if (!await ReadExact(stream, header, token).ConfigureAwait(false))
                        {
                            RetireFailedProcess(process, token, "Encoder stdout ended unexpectedly.");
                            return;
                        }

                        var timestampNs = ReadUInt64LittleEndian(header, 0);
                        var length = ReadInt32LittleEndian(header, 8);
                        if (length < 0 || length > MaxAccessUnitBytes)
                        {
                            LastError = "OpenH264 helper emitted an invalid access-unit length: " + length;
                            RetireFailedProcess(process, token, LastError);
                            return;
                        }

                        if (length == 0)
                        {
                            lock (_outputLock)
                            {
                                if (!IsCurrentSessionForTests(process, sessionId))
                                    return;
                                AcceptHelperSkippedAccessUnit(timestampNs);
                            }
                            continue;
                        }

                        var payload = new byte[length];
                        if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                        {
                            LastError = "OpenH264 helper stdout ended mid access unit.";
                            RetireFailedProcess(process, token, LastError);
                            return;
                        }

                        lock (_outputLock)
                        {
                            if (!IsCurrentSessionForTests(process, sessionId))
                                return;
                            AcceptHelperAccessUnit(payload, timestampNs);
                        }
                    }
                }
                else
                {
                    var header = new byte[4];
                    while (!token.IsCancellationRequested)
                    {
                        var readLength = await ReadLittleEndianLength(stream, header, token).ConfigureAwait(false);
                        if (!readLength.Success)
                        {
                            RetireFailedProcess(process, token, "Encoder stdout ended unexpectedly.");
                            return;
                        }

                        var length = readLength.Length;
                        lock (_outputLock)
                        {
                            if (!IsCurrentSessionForTests(process, sessionId))
                                return;

                            if (length == 0)
                            {
                                AcceptHelperSkippedAccessUnit();
                                continue;
                            }

                            if (length < 0 || length > MaxAccessUnitBytes)
                            {
                                LastError = "OpenH264 helper emitted an invalid access-unit length: " + length;
                                RetireFailedProcess(process, token, LastError);
                                return;
                            }
                        }

                        var payload = new byte[length];
                        if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                        {
                            LastError = "OpenH264 helper stdout ended mid access unit.";
                            RetireFailedProcess(process, token, LastError);
                            return;
                        }

                        lock (_outputLock)
                        {
                            if (!IsCurrentSessionForTests(process, sessionId))
                                return;
                            AcceptHelperAccessUnit(payload);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RetireFailedProcess(process, token, ex.Message);
            }
        }

        private async Task RunStderrReaderForSession(Process process, CancellationToken token, long sessionId)
        {
            try
            {
                await ReadBoundedDiagnosticStream(
                    process.StandardError.BaseStream,
                    line => HandleDiagnosticLine(process, sessionId, line, token),
                    token,
                    Math.Max(1, _options?.MaxStderrLineBytes ?? 8192),
                    Math.Max(1, _options?.MaxStderrRetainedBytes ?? 8192)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    RetireFailedProcess(process, token, ex.Message);
            }
        }

        private void HandleDiagnosticLine(Process process, long sessionId, string line, CancellationToken token)
        {
            if (!IsCurrentSessionForTests(process, sessionId))
                return;

            LastDiagnosticLine = line;
            if (string.Equals(line, ProtocolMarker, StringComparison.Ordinal)
                && !TryAcceptProtocolMarker())
            {
                // Legacy framing was already chosen, but the helper is framing v2.
                // Continuing would desynchronize both pipes, so fail the session.
                RetireFailedProcess(process, token, ProtocolMismatchError);
            }
        }

        /// <summary>
        /// Records the helper's v2 advertisement. Returns false when the negotiation
        /// already fell back to legacy framing, which is a protocol mismatch.
        /// </summary>
        private bool TryAcceptProtocolMarker()
        {
            lock (_protocolNegotiationLock)
            {
                if (_protocolNegotiationState == 1)
                    return false;

                if (_protocolNegotiationState == 0)
                {
                    Volatile.Write(ref _helperProtocolVersion, 2);
                    _protocolNegotiationState = 2;
                    _helperProtocolReady.TrySetResult(true);
                }

                return true;
            }
        }

        private async Task WaitForProtocolNegotiation(CancellationToken token)
        {
            lock (_protocolNegotiationLock)
            {
                if (_protocolNegotiationState != 0)
                    return;
            }

            try
            {
                var completed = await Task.WhenAny(
                    _helperProtocolReady.Task,
                    Task.Delay(Volatile.Read(ref _protocolNegotiationTimeoutMs), token)).ConfigureAwait(false);
                if (completed != _helperProtocolReady.Task
                    && !token.IsCancellationRequested
                    && TrySelectLegacyProtocol())
                {
                    _helperProtocolReady.TrySetResult(false);
                    LastDiagnosticLine = "OpenH264 helper did not advertise protocol 2; using legacy framing. Reinstall or rebuild the helper to enable timestamp framing.";
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private bool TrySelectLegacyProtocol()
        {
            lock (_protocolNegotiationLock)
            {
                if (_protocolNegotiationState != 0)
                    return false;

                Volatile.Write(ref _helperProtocolVersion, 1);
                _protocolNegotiationState = 1;
                return true;
            }
        }

        private static void PopulateProtocolHeader(byte[] header, ulong timestampNs, int length)
        {
            if (header == null || header.Length < OpenH264AllocationPolicy.ProtocolHeaderBytes)
                throw new ArgumentException("The protocol header buffer must contain at least 12 bytes.", nameof(header));

            for (var i = 0; i < 8; i++)
                header[i] = (byte)(timestampNs >> (8 * i));
            header[8] = (byte)length;
            header[9] = (byte)(length >> 8);
            header[10] = (byte)(length >> 16);
            header[11] = (byte)(length >> 24);
        }

        private static ulong ReadUInt64LittleEndian(byte[] buffer, int offset)
        {
            ulong value = 0;
            for (var i = 0; i < 8; i++)
                value |= ((ulong)buffer[offset + i]) << (8 * i);
            return value;
        }

        private static int ReadInt32LittleEndian(byte[] buffer, int offset)
            => buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);

        private static async Task ReadBoundedDiagnosticStream(
            Stream stream,
            Action<string> publishLine,
            CancellationToken token,
            int maxLineBytes,
            int maxRetainedBytes)
        {
            var buffer = new byte[Math.Min(4096, Math.Max(256, maxLineBytes))];
            var lineLimit = Math.Max(1, Math.Min(maxLineBytes, maxRetainedBytes));
            var retained = new byte[lineLimit];
            var retainedCount = 0;
            var truncated = false;

            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];
                    if (value == (byte)'\n')
                    {
                        PublishDiagnosticLine(retained, retainedCount, truncated, publishLine);
                        retainedCount = 0;
                        truncated = false;
                        continue;
                    }

                    if (value == (byte)'\r')
                        continue;

                    if (retainedCount < lineLimit)
                        retained[retainedCount++] = value;
                    else
                        truncated = true;
                }
            }

            if (retainedCount > 0 || truncated)
                PublishDiagnosticLine(retained, retainedCount, truncated, publishLine);
        }

        private static void PublishDiagnosticLine(byte[] retained, int retainedCount, bool truncated, Action<string> publishLine)
        {
            var text = retainedCount == 0
                ? string.Empty
                : Encoding.UTF8.GetString(retained, 0, retainedCount);
            publishLine(truncated ? text + " [truncated]" : text);
        }

    }
}
