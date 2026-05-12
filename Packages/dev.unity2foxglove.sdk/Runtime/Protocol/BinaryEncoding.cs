// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Encodes/decodes binary WebSocket frames for the Foxglove protocol v1.

using System;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Encodes binary WebSocket frames for the Foxglove protocol v1.</summary>
    public static class BinaryEncoding
    {
        /// <summary>
        /// Server→client MessageData frame.
        /// Wire format: opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload
        /// </summary>
        public static byte[] EncodeServerMessageData(uint subscriptionId, ulong logTimeNs, byte[] payload)
        {
            var frame = new byte[1 + 4 + 8 + payload.Length];
            frame[0] = ServerOpcode.MessageData;
            WriteU32LE(frame, 1, subscriptionId);
            WriteU64LE(frame, 5, logTimeNs);
            Buffer.BlockCopy(payload, 0, frame, 13, payload.Length);
            return frame;
        }

        /// <summary>Encode a Time frame: opcode(1) + timestamp(8 bytes LE, nanoseconds).</summary>
        public static byte[] EncodeTime(ulong nsecs)
        {
            var frame = new byte[1 + 8];
            frame[0] = ServerOpcode.Time;
            WriteU64LE(frame, 1, nsecs);
            return frame;
        }

        /// <summary>
        /// Decode a server→client MessageData frame (for roundtrip testing only).
        /// Format: opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload
        /// </summary>
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

            if (data.Length < 13 + encodingLength)
                return false;

            encoding = System.Text.Encoding.UTF8.GetString(data, 13, (int)encodingLength);
            var payloadOffset = 13 + (int)encodingLength;
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
            var encBytes = System.Text.Encoding.UTF8.GetBytes(encoding ?? "");
            var frame = new byte[1 + 4 + 4 + 4 + encBytes.Length + (payload?.Length ?? 0)];
            frame[0] = ServerOpcode.ServiceCallResponse;
            WriteU32LE(frame, 1, serviceId);
            WriteU32LE(frame, 5, callId);
            WriteU32LE(frame, 9, (uint)encBytes.Length);
            Buffer.BlockCopy(encBytes, 0, frame, 13, encBytes.Length);
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, 13 + encBytes.Length, payload.Length);
            return frame;
        }

        // ── Little-endian helpers ──

        /// <summary>Write a 32-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Write a 64-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU64LE(byte[] buf, int offset, ulong value)
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
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }

        /// <summary>Read a 64-bit unsigned integer in little-endian byte order.</summary>
        public static ulong ReadU64LE(byte[] buf, int offset)
        {
            return (ulong)buf[offset]
                 | ((ulong)buf[offset + 1] << 8)
                 | ((ulong)buf[offset + 2] << 16)
                 | ((ulong)buf[offset + 3] << 24)
                 | ((ulong)buf[offset + 4] << 32)
                 | ((ulong)buf[offset + 5] << 40)
                 | ((ulong)buf[offset + 6] << 48)
                 | ((ulong)buf[offset + 7] << 56);
        }
        // ── Phase 9: fetchAssetResponse ──

        /// <summary>Encode a successful fetchAssetResponse.</summary>
        public static byte[] EncodeFetchAssetResponseSuccess(uint requestId, byte[] payload)
        {
            var frame = new byte[1 + 4 + 1 + 4 + (payload?.Length ?? 0)];
            frame[0] = ServerOpcode.FetchAssetResponse;
            WriteU32LE(frame, 1, requestId);
            frame[5] = 0; // status: success
            // errorMessageLen = 0
            frame[6] = 0; frame[7] = 0; frame[8] = 0; frame[9] = 0;
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
            WriteU32LE(frame, 1, requestId);
            frame[5] = 1; // status: error
            WriteU32LE(frame, 6, (uint)errBytes.Length);
            Buffer.BlockCopy(errBytes, 0, frame, 10, errBytes.Length);
            return frame;
        }

        // ── Phase 9: PlaybackControl ──

        /// <summary>Write a 32-bit float in little-endian byte order.</summary>
        public static void WriteF32LE(byte[] buf, int offset, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buf, offset, 4);
        }

        /// <summary>Read a 32-bit float in little-endian byte order.</summary>
        public static float ReadF32LE(byte[] buf, int offset)
        {
            var bytes = new byte[4];
            Buffer.BlockCopy(buf, offset, bytes, 0, 4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
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
            if (data.Length < 19 + idLen) return false;
            requestId = idLen > 0 ? System.Text.Encoding.UTF8.GetString(data, 19, (int)idLen) : null;
            return true;
        }

        /// <summary>Encode a PlaybackState binary frame.</summary>
        public static byte[] EncodePlaybackState(byte status, ulong currentTimeNs, float speed,
            bool didSeek, string requestId)
        {
            var idBytes = requestId != null ? System.Text.Encoding.UTF8.GetBytes(requestId) : new byte[0];
            var frame = new byte[1 + 1 + 8 + 4 + 1 + 4 + idBytes.Length];
            frame[0] = ServerOpcode.PlaybackState;
            frame[1] = status;
            WriteU64LE(frame, 2, currentTimeNs);
            WriteF32LE(frame, 10, speed);
            frame[14] = didSeek ? (byte)1 : (byte)0;
            WriteU32LE(frame, 15, (uint)idBytes.Length);
            Buffer.BlockCopy(idBytes, 0, frame, 19, idBytes.Length);
            return frame;
        }
    }
}
