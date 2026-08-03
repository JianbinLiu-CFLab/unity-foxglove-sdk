// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: RFC 6455 WebSocket frame encoding and decoding helpers shared by
// managed plain and secure WebSocket transports.

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>RFC 6455 WebSocket frame encoding and decoding helpers.</summary>
    internal static class WsFrameCodec
    {
        /// <summary>RFC 6455 FIN bit in the first WebSocket frame header byte.</summary>
        private const byte FinBit = 0x80;
        /// <summary>RFC 6455 MASK bit in the second WebSocket frame header byte.</summary>
        private const byte MaskBit = 0x80;
        /// <summary>RFC 6455 RSV1/RSV2/RSV3 bits. No extensions are negotiated by this server.</summary>
        private const byte ReservedBitsMask = 0x70;
        /// <summary>RFC 6455 low-nibble opcode mask for the first frame header byte.</summary>
        private const byte OpcodeMask = 0x0F;
        /// <summary>RFC 6455 payload-length mask for the second frame header byte.</summary>
        private const byte PayloadLengthMask = 0x7F;
        /// <summary>Maximum WebSocket frame header size: 2 base bytes plus 8 extended length bytes.</summary>
        internal const int MaxFrameHeaderBytes = 10;
        /// <summary>RFC 6455 inline payload-length limit before extended length markers are used.</summary>
        private const int SmallPayloadLimit = 125;
        /// <summary>RFC 6455 marker for the 16-bit extended payload-length field.</summary>
        private const byte Payload16BitLengthMarker = 126;
        /// <summary>RFC 6455 marker for the 64-bit extended payload-length field.</summary>
        private const byte Payload64BitLengthMarker = 127;
        /// <summary>Maximum allowable payload size in bytes (64 MiB).</summary>
        internal const int MaxPayloadBytes = 64 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static int WriteFrameHeader(byte opcode, int payloadLength, Span<byte> destination)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            if (destination.Length < MaxFrameHeaderBytes)
                throw new ArgumentException("WebSocket frame header destination must be at least 10 bytes.", nameof(destination));

            var offset = 0;
            destination[offset++] = (byte)(FinBit | opcode);

            if (payloadLength <= SmallPayloadLimit)
            {
                destination[offset++] = (byte)payloadLength;
                return offset;
            }

            if (payloadLength <= ushort.MaxValue)
            {
                destination[offset++] = Payload16BitLengthMarker;
                destination[offset++] = (byte)(payloadLength >> 8);
                destination[offset++] = (byte)payloadLength;
                return offset;
            }

            destination[offset++] = Payload64BitLengthMarker;
            var len = (ulong)payloadLength;
            for (var i = 7; i >= 0; i--)
                destination[offset++] = (byte)(len >> (i * 8));
            return offset;
        }

        internal static void WriteFrame(Stream stream, byte opcode, byte[] payload)
            => WriteFrame(stream, opcode, payload, flush: true);

        internal static void WriteFrame(Stream stream, byte opcode, byte[] payload, bool flush)
        {
            FoxgloveProfiler.Global.BeginSample("WsFrameCodec.Encode");
            try
            {
                payload ??= Array.Empty<byte>();
                Span<byte> header = stackalloc byte[MaxFrameHeaderBytes];
                var headerLength = WriteFrameHeader(opcode, payload.Length, header);
                stream.Write(header.Slice(0, headerLength));
                if (payload.Length > 0)
                    stream.Write(payload, 0, payload.Length);
                if (flush)
                    stream.Flush();
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        internal static bool TryReadFrame(Stream stream, out WsFrame frame)
            => ReadFrame(stream, out frame) == WsFrameReadResult.Success;

        internal static WsFrameReadResult ReadFrame(Stream stream, out WsFrame frame)
        {
            frame = null;

            Span<byte> header = stackalloc byte[2];
            if (!ReadExact(stream, header))
                return WsFrameReadResult.EndOfStream;

            if ((header[0] & ReservedBitsMask) != 0)
                return WsFrameReadResult.ProtocolError;

            var fin = (header[0] & FinBit) != 0;
            var opcode = header[0] & OpcodeMask;
            var masked = (header[1] & MaskBit) != 0;
            var payloadLen = (int)(header[1] & PayloadLengthMask);

            if (payloadLen == Payload16BitLengthMarker)
            {
                Span<byte> ext = stackalloc byte[2];
                if (!ReadExact(stream, ext))
                    return WsFrameReadResult.EndOfStream;
                payloadLen = (ext[0] << 8) | ext[1];
                if (payloadLen <= SmallPayloadLimit)
                    return WsFrameReadResult.ProtocolError;
            }
            else if (payloadLen == Payload64BitLengthMarker)
            {
                Span<byte> ext = stackalloc byte[8];
                if (!ReadExact(stream, ext))
                    return WsFrameReadResult.EndOfStream;
                ulong len64 = 0;
                for (var i = 0; i < ext.Length; i++)
                    len64 = (len64 << 8) | ext[i];
                if ((len64 & (1UL << 63)) != 0 || len64 <= ushort.MaxValue)
                    return WsFrameReadResult.ProtocolError;
                if (len64 > MaxPayloadBytes)
                    return WsFrameReadResult.MessageTooBig;
                payloadLen = (int)len64;
            }

            if (!masked)
                return WsFrameReadResult.ProtocolError;

            if (!IsKnownDataOpcode(opcode) && !IsKnownControlOpcode(opcode))
                return WsFrameReadResult.ProtocolError;

            if (IsControlOpcode(opcode)
                && (!fin || payloadLen > SmallPayloadLimit))
                return WsFrameReadResult.ProtocolError;

            if (opcode == WsOpcode.Close && payloadLen == 1)
                return WsFrameReadResult.ProtocolError;

            if (payloadLen > MaxPayloadBytes)
                return WsFrameReadResult.MessageTooBig;

            Span<byte> mask = stackalloc byte[4];
            if (!ReadExact(stream, mask))
                return WsFrameReadResult.EndOfStream;

            var payload = new byte[payloadLen];
            if (payloadLen > 0 && !ReadExact(stream, payload, 0, payloadLen))
                return WsFrameReadResult.EndOfStream;

            var maskIndex = 0;
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(payload[i] ^ mask[maskIndex]);
                maskIndex++;
                if (maskIndex == 4)
                    maskIndex = 0;
            }

            if (opcode == WsOpcode.Close && payloadLen >= 2)
            {
                var closeCode = (payload[0] << 8) | payload[1];
                if (!IsValidCloseCode(closeCode))
                    return WsFrameReadResult.ProtocolError;
                if (!TryDecodeUtf8(payload, 2, payload.Length - 2, out _))
                    return WsFrameReadResult.InvalidPayloadData;
            }

            frame = new WsFrame
            {
                Fin = fin,
                Opcode = (byte)opcode,
                Payload = payload
            };
            return WsFrameReadResult.Success;
        }

        internal static bool TryDecodeUtf8(byte[] payload, int index, int count, out string text)
        {
            try
            {
                text = StrictUtf8.GetString(payload ?? Array.Empty<byte>(), index, count);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        private static bool IsControlOpcode(int opcode) => opcode >= WsOpcode.Close;

        private static bool IsKnownDataOpcode(int opcode) =>
            opcode == WsOpcode.Continuation || opcode == WsOpcode.Text || opcode == WsOpcode.Binary;

        private static bool IsKnownControlOpcode(int opcode) =>
            opcode == WsOpcode.Close || opcode == WsOpcode.Ping || opcode == WsOpcode.Pong;

        private static bool IsValidCloseCode(int closeCode)
        {
            return closeCode == 1000
                   || closeCode == 1001
                   || closeCode == 1002
                   || closeCode == 1003
                   || closeCode == 1007
                   || closeCode == 1008
                   || closeCode == 1009
                   || closeCode == 1010
                   || closeCode == 1011
                   || closeCode == 1012
                   || closeCode == 1013
                   || closeCode == 1014
                   || (closeCode >= 3000 && closeCode <= 4999);
        }

        /// <summary>Read exactly <c>count</c> bytes into the buffer, returning <c>false</c> if the stream ends early.</summary>
        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            try
            {
                while (count > 0)
                {
                    var read = stream.Read(buffer, offset, count);
                    if (read == 0)
                        return false;
                    offset += read;
                    count -= read;
                }
            }
            catch (Exception ex) when (IsExpectedStreamShutdown(ex))
            {
                return false;
            }

            return true;
        }

        /// <summary>Read exactly enough bytes to fill the span, returning <c>false</c> if the stream ends early.</summary>
        private static bool ReadExact(Stream stream, Span<byte> buffer)
        {
            try
            {
                while (!buffer.IsEmpty)
                {
                    var read = stream.Read(buffer);
                    if (read == 0)
                        return false;
                    buffer = buffer.Slice(read);
                }
            }
            catch (Exception ex) when (IsExpectedStreamShutdown(ex))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true for stream exceptions produced by normal client disconnects or TLS socket abort races.
        /// </summary>
        private static bool IsExpectedStreamShutdown(Exception ex)
        {
            if (ex is ObjectDisposedException || ex is IOException || ex is SocketException)
                return true;

            if (ex is AggregateException aggregate)
            {
                var inner = aggregate.Flatten().InnerExceptions;
                return inner.Count > 0 && inner.All(IsExpectedStreamShutdown);
            }

            return false;
        }
    }

    internal enum WsFrameReadResult
    {
        Success,
        EndOfStream,
        ProtocolError,
        InvalidPayloadData,
        MessageTooBig
    }

    /// <summary>Decoded WebSocket frame: FIN flag, opcode, and unmasked payload.</summary>
    internal sealed class WsFrame
    {
        /// <summary>Whether this is the final fragment of a message.</summary>
        public bool Fin;
        /// <summary>WebSocket opcode (text, binary, close, ping, pong).</summary>
        public byte Opcode;
        /// <summary>Unmasked payload data.</summary>
        public byte[] Payload;
    }

    /// <summary>RFC 6455 WebSocket opcode constants.</summary>
    internal static class WsOpcode
    {
        /// <summary>Continuation frame opcode (0x0).</summary>
        public const byte Continuation = 0x0;
        /// <summary>Text frame opcode (0x1).</summary>
        public const byte Text = 0x1;
        /// <summary>Binary frame opcode (0x2).</summary>
        public const byte Binary = 0x2;
        /// <summary>Close frame opcode (0x8).</summary>
        public const byte Close = 0x8;
        /// <summary>Ping frame opcode (0x9).</summary>
        public const byte Ping = 0x9;
        /// <summary>Pong frame opcode (0xA).</summary>
        public const byte Pong = 0xA;
    }
}
