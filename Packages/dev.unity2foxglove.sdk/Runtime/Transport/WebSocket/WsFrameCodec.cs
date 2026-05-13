// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: RFC 6455 WebSocket frame encoding and decoding helpers shared by
// managed plain and secure WebSocket transports.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>RFC 6455 WebSocket frame encoding and decoding helpers.</summary>
    internal static class WsFrameCodec
    {
        /// <summary>RFC 6455 FIN bit in the first WebSocket frame header byte.</summary>
        private const byte FinBit = 0x80;
        /// <summary>RFC 6455 MASK bit in the second WebSocket frame header byte.</summary>
        private const byte MaskBit = 0x80;
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
        {
            payload ??= Array.Empty<byte>();
            Span<byte> header = stackalloc byte[MaxFrameHeaderBytes];
            var headerLength = WriteFrameHeader(opcode, payload.Length, header);
            stream.Write(header.Slice(0, headerLength));
            if (payload.Length > 0)
                stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        internal static bool TryReadFrame(Stream stream, out WsFrame frame)
        {
            frame = null;

            var header = new byte[2];
            if (!ReadExact(stream, header, 0, 2))
                return false;

            var fin = (header[0] & FinBit) != 0;
            var opcode = header[0] & OpcodeMask;
            var masked = (header[1] & MaskBit) != 0;
            var payloadLen = (int)(header[1] & PayloadLengthMask);

            if (payloadLen == Payload16BitLengthMarker)
            {
                var ext = new byte[2];
                if (!ReadExact(stream, ext, 0, 2))
                    return false;
                payloadLen = (ext[0] << 8) | ext[1];
            }
            else if (payloadLen == Payload64BitLengthMarker)
            {
                var ext = new byte[8];
                if (!ReadExact(stream, ext, 0, 8))
                    return false;
                var len64 = (long)(((long)ext[0] << 56) | ((long)ext[1] << 48) | ((long)ext[2] << 40)
                                 | ((long)ext[3] << 32) | ((long)ext[4] << 24) | ((long)ext[5] << 16)
                                 | ((long)ext[6] << 8)  | (long)ext[7]);
                if (len64 < 0 || len64 > int.MaxValue)
                    return false;
                payloadLen = (int)len64;
            }

            if (!masked)
                return false;

            if (IsControlOpcode(opcode)
                && (!IsKnownControlOpcode(opcode) || !fin || payloadLen > SmallPayloadLimit))
                return false;

            var mask = new byte[4];
            if (!ReadExact(stream, mask, 0, 4))
                return false;

            if (payloadLen > MaxPayloadBytes)
                return false;

            var payload = new byte[payloadLen];
            if (payloadLen > 0 && !ReadExact(stream, payload, 0, payloadLen))
                return false;

            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(payload[i] ^ mask[i % 4]);

            frame = new WsFrame
            {
                Fin = fin,
                Opcode = (byte)opcode,
                Payload = payload
            };
            return true;
        }

        private static bool IsControlOpcode(int opcode) => opcode >= WsOpcode.Close;

        private static bool IsKnownControlOpcode(int opcode) =>
            opcode == WsOpcode.Close || opcode == WsOpcode.Ping || opcode == WsOpcode.Pong;

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

        private static bool IsExpectedStreamShutdown(Exception ex)
        {
            if (ex is ObjectDisposedException)
                return true;

            if (ex is AggregateException aggregate)
            {
                var inner = aggregate.Flatten().InnerExceptions;
                return inner.Count > 0 && inner.All(IsExpectedStreamShutdown);
            }

            return false;
        }
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
