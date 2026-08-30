// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Encodes/decodes binary WebSocket frames for the Foxglove protocol v1.

using System;
using System.ComponentModel;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Encodes binary WebSocket frames for the Foxglove protocol v1.</summary>
    public static class BinaryEncoding
    {
        public const int ServerMessageDataHeaderLength = 13;
        public const int TimeFrameLength = 9;

        private static readonly byte[] EmptyEncodingBytes = Array.Empty<byte>();
        private static readonly byte[] JsonEncodingBytes = System.Text.Encoding.UTF8.GetBytes("json");
        private static readonly byte[] ProtobufEncodingBytes = System.Text.Encoding.UTF8.GetBytes("protobuf");
        private static readonly byte[] Ros1EncodingBytes = System.Text.Encoding.UTF8.GetBytes("ros1");
        private static readonly System.Text.UTF8Encoding StrictUtf8 =
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// Server→client MessageData frame.
        /// Wire format: opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload
        /// </summary>
        public static byte[] EncodeServerMessageData(uint subscriptionId, ulong logTimeNs, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            var frame = new byte[GetServerMessageDataFrameLength(payload.Length)];
            EncodeServerMessageData(frame, 0, subscriptionId, logTimeNs, payload);
            return frame;
        }

        public static int GetServerMessageDataFrameLength(int payloadLength)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            if (payloadLength > int.MaxValue - ServerMessageDataHeaderLength)
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength),
                    "The payload is too large to fit in an Int32 MessageData frame length.");
            return ServerMessageDataHeaderLength + payloadLength;
        }

        public static void EncodeServerMessageData(byte[] destination, int offset, uint subscriptionId, ulong logTimeNs, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            ValidateBufferRange(destination, offset, GetServerMessageDataFrameLength(payload.Length));
            destination[offset] = ServerOpcode.MessageData;
            WriteU32LEUnchecked(destination, offset + 1, subscriptionId);
            WriteU64LEUnchecked(destination, offset + 5, logTimeNs);
            Buffer.BlockCopy(payload, 0, destination, offset + ServerMessageDataHeaderLength, payload.Length);
        }

        /// <summary>Encode a Time frame: opcode(1) + timestamp(8 bytes LE, nanoseconds).</summary>
        public static byte[] EncodeTime(ulong nsecs)
        {
            var frame = new byte[TimeFrameLength];
            EncodeTime(frame, 0, nsecs);
            return frame;
        }

        public static void EncodeTime(byte[] destination, int offset, ulong nsecs)
        {
            ValidateBufferRange(destination, offset, TimeFrameLength);
            destination[offset] = ServerOpcode.Time;
            WriteU64LEUnchecked(destination, offset + 1, nsecs);
        }

        /// <summary>
        /// Decode a server→client MessageData frame (for roundtrip testing only).
        /// Format: opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool TryDecodeServerMessageData(byte[] data, out uint subscriptionId, out ulong logTimeNs, out byte[] payload)
        {
            subscriptionId = 0;
            logTimeNs = 0;
            payload = null;

            if (data == null || data.Length < 13 || data[0] != ServerOpcode.MessageData)
                return false;

            subscriptionId = ReadU32LE(data, 1);
            logTimeNs = ReadU64LE(data, 5);
            payload = new byte[data.Length - 13];
            Buffer.BlockCopy(data, 13, payload, 0, payload.Length);
            return true;
        }

        /// <summary>
        /// Decode a client→server MessageData binary frame.
        /// Wire format: opcode(1) + channelId(u32 LE) + payload (NO logTime).
        /// </summary>
        public static bool TryDecodeClientMessageData(byte[] data, out uint channelId, out byte[] payload)
        {
            channelId = 0;
            payload = null;

            if (data == null || data.Length < 5 || data[0] != ClientOpcode.MessageData)
                return false;

            channelId = ReadU32LE(data, 1);
            payload = new byte[data.Length - 5];
            Buffer.BlockCopy(data, 5, payload, 0, payload.Length);
            return true;
        }

        /// <summary>
        /// Decode a client→server ServiceCallRequest binary frame.
        /// Wire format: opcode(1) + serviceId(u32 LE) + callId(u32 LE) + encodingLength(u32 LE) + encoding bytes + payload
        /// </summary>
        public static bool TryDecodeClientServiceCallRequest(
            byte[] data, out uint serviceId, out uint callId, out string encoding, out byte[] payload)
        {
            serviceId = 0;
            callId = 0;
            encoding = null;
            payload = null;

            if (data == null || data.Length < 13 || data[0] != ClientOpcode.ServiceCallRequest)
                return false;

            serviceId = ReadU32LE(data, 1);
            callId = ReadU32LE(data, 5);
            var encodingLength = ReadU32LE(data, 9);

            if (encodingLength > int.MaxValue)
                return false;

            var encodingLengthInt = (int)encodingLength;
            if (encodingLengthInt > data.Length - 13)
                return false;

            if (!TryDecodeStrictUtf8(data, 13, encodingLengthInt, out encoding))
                return false;
            var payloadOffset = 13 + encodingLengthInt;
            payload = new byte[data.Length - payloadOffset];
            Buffer.BlockCopy(data, payloadOffset, payload, 0, payload.Length);
            return true;
        }

        /// <summary>
        /// Encode a server→client ServiceCallResponse binary frame.
        /// Wire format: opcode(1) + serviceId(u32 LE) + callId(u32 LE) + encodingLength(u32 LE) + encoding bytes + payload
        /// </summary>
        public static byte[] EncodeServerServiceCallResponse(
            uint serviceId, uint callId, string encoding, byte[] payload)
        {
            var encBytes = GetCachedServiceEncodingBytes(encoding);
            var frame = new byte[1 + 4 + 4 + 4 + encBytes.Length + (payload?.Length ?? 0)];
            frame[0] = ServerOpcode.ServiceCallResponse;
            WriteU32LEUnchecked(frame, 1, serviceId);
            WriteU32LEUnchecked(frame, 5, callId);
            WriteU32LEUnchecked(frame, 9, (uint)encBytes.Length);
            Buffer.BlockCopy(encBytes, 0, frame, 13, encBytes.Length);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, 13 + encBytes.Length, payload.Length);
            return frame;
        }

        private static byte[] GetCachedServiceEncodingBytes(string encoding)
        {
            if (string.IsNullOrEmpty(encoding))
                return EmptyEncodingBytes;
            if (string.Equals(encoding, "json", StringComparison.Ordinal))
                return JsonEncodingBytes;
            if (string.Equals(encoding, "protobuf", StringComparison.Ordinal))
                return ProtobufEncodingBytes;
            if (string.Equals(encoding, "ros1", StringComparison.Ordinal))
                return Ros1EncodingBytes;
            return System.Text.Encoding.UTF8.GetBytes(encoding);
        }

        // ── Little-endian helpers ──

        /// <summary>Write a 32-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            ValidateBufferRange(buf, offset, 4);
            WriteU32LEUnchecked(buf, offset, value);
        }

        private static void WriteU32LEUnchecked(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Write a 64-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU64LE(byte[] buf, int offset, ulong value)
        {
            ValidateBufferRange(buf, offset, 8);
            WriteU64LEUnchecked(buf, offset, value);
        }

        private static void WriteU64LEUnchecked(byte[] buf, int offset, ulong value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
            buf[offset + 4] = (byte)((value >> 32) & 0xFF);
            buf[offset + 5] = (byte)((value >> 40) & 0xFF);
            buf[offset + 6] = (byte)((value >> 48) & 0xFF);
            buf[offset + 7] = (byte)((value >> 56) & 0xFF);
        }

        /// <summary>Read a 32-bit unsigned integer in little-endian byte order.</summary>
        public static uint ReadU32LE(byte[] buf, int offset)
        {
            ValidateBufferRange(buf, offset, 4);
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }

        /// <summary>Read a 64-bit unsigned integer in little-endian byte order.</summary>
        public static ulong ReadU64LE(byte[] buf, int offset)
        {
            ValidateBufferRange(buf, offset, 8);
            return (ulong)buf[offset]
                 | ((ulong)buf[offset + 1] << 8)
                 | ((ulong)buf[offset + 2] << 16)
                 | ((ulong)buf[offset + 3] << 24)
                 | ((ulong)buf[offset + 4] << 32)
                 | ((ulong)buf[offset + 5] << 40)
                 | ((ulong)buf[offset + 6] << 48)
                 | ((ulong)buf[offset + 7] << 56);
        }
        private const byte FetchAssetStatusSuccess = 0;
        private const byte FetchAssetStatusError = 1;

        // fetchAssetResponse frames

        /// <summary>Encode a successful fetchAssetResponse.</summary>
        public static byte[] EncodeFetchAssetResponseSuccess(uint requestId, byte[] payload)
        {
            var frame = new byte[1 + 4 + 1 + 4 + (payload?.Length ?? 0)];
            frame[0] = ServerOpcode.FetchAssetResponse;
            WriteU32LEUnchecked(frame, 1, requestId);
            frame[5] = FetchAssetStatusSuccess;
            WriteU32LEUnchecked(frame, 6, 0u);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, 10, payload.Length);
            return frame;
        }

        /// <summary>Encode a failed fetchAssetResponse.</summary>
        public static byte[] EncodeFetchAssetResponseError(uint requestId, string message)
        {
            var errBytes = System.Text.Encoding.UTF8.GetBytes(message ?? "");
            var frame = new byte[1 + 4 + 1 + 4 + errBytes.Length];
            frame[0] = ServerOpcode.FetchAssetResponse;
            WriteU32LEUnchecked(frame, 1, requestId);
            frame[5] = FetchAssetStatusError;
            WriteU32LEUnchecked(frame, 6, (uint)errBytes.Length);
            Buffer.BlockCopy(errBytes, 0, frame, 10, errBytes.Length);
            return frame;
        }

        // PlaybackControl frames

        /// <summary>Maximum UTF-8 byte length accepted for PlaybackControl request ids.</summary>
        public const int MaxPlaybackRequestIdBytes = 256;

        /// <summary>Write a 32-bit float in little-endian byte order.</summary>
        public static void WriteF32LE(byte[] buf, int offset, float value)
        {
            WriteU32LE(buf, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static void WriteF32LEUnchecked(byte[] buf, int offset, float value)
        {
            WriteU32LEUnchecked(buf, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        /// <summary>Read a 32-bit float in little-endian byte order.</summary>
        public static float ReadF32LE(byte[] buf, int offset)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadU32LE(buf, offset)));
        }

        /// <summary>Decode a PlaybackControlRequest binary frame.</summary>
        public static bool TryDecodePlaybackControlRequest(byte[] data, out byte command, out float speed,
            out bool hasSeek, out ulong seekTimeNs, out string requestId)
        {
            command = 0; speed = 1f; hasSeek = false; seekTimeNs = 0; requestId = null;
            if (data == null || data.Length < 1 + 1 + 4 + 1 + 8 + 4 || data[0] != ClientOpcode.PlaybackControlRequest)
                return false;
            command = data[1];
            speed = ReadF32LE(data, 2);
            hasSeek = data[6] != 0;
            seekTimeNs = ReadU64LE(data, 7);
            var idLen = ReadU32LE(data, 15);
            if (idLen > int.MaxValue) return false;
            if (idLen > MaxPlaybackRequestIdBytes) return false;
            var idLenInt = (int)idLen;
            if (idLenInt != data.Length - 19) return false;
            if (idLenInt > 0 && !TryDecodeStrictUtf8(data, 19, idLenInt, out requestId))
                return false;
            return true;
        }

        private static bool TryDecodeStrictUtf8(byte[] data, int offset, int length, out string value)
        {
            try
            {
                value = StrictUtf8.GetString(data, offset, length);
                return true;
            }
            catch (System.Text.DecoderFallbackException)
            {
                value = null;
                return false;
            }
        }

        /// <summary>Encode a PlaybackState binary frame.</summary>
        public static byte[] EncodePlaybackState(byte status, ulong currentTimeNs, float speed,
            bool didSeek, string requestId)
        {
            var idBytes = requestId != null ? System.Text.Encoding.UTF8.GetBytes(requestId) : Array.Empty<byte>();
            if (idBytes.Length > MaxPlaybackRequestIdBytes)
                throw new ArgumentOutOfRangeException(
                    nameof(requestId),
                    $"PlaybackState request id must be at most {MaxPlaybackRequestIdBytes} UTF-8 bytes.");

            var frame = new byte[1 + 1 + 8 + 4 + 1 + 4 + idBytes.Length];
            frame[0] = ServerOpcode.PlaybackState;
            frame[1] = status;
            WriteU64LEUnchecked(frame, 2, currentTimeNs);
            WriteF32LEUnchecked(frame, 10, speed);
            frame[14] = didSeek ? (byte)1 : (byte)0;
            WriteU32LEUnchecked(frame, 15, (uint)idBytes.Length);
            Buffer.BlockCopy(idBytes, 0, frame, 19, idBytes.Length);
            return frame;
        }

        private static void ValidateBufferRange(byte[] buf, int offset, int byteCount)
        {
            if (buf == null)
                throw new ArgumentNullException(nameof(buf));

            if (offset < 0 || byteCount < 0 || offset > buf.Length - byteCount)
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Buffer length {buf.Length} cannot fit {byteCount} bytes at offset {offset}.");
        }
    }
}
