// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free construction helpers for foxglove.CompressedVideo
// protobuf camera payloads.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Builds official <c>foxglove.CompressedVideo</c> protobuf messages
    /// from one complete compressed video access unit.
    /// </summary>
    public static class CameraCompressedVideoBuilder
    {
        /// <summary>Foxglove format label for H.264 Annex B video.</summary>
        public const string H264Format = "h264";

        /// <summary>Foxglove format label for H.265/HEVC Annex B video.</summary>
        public const string H265Format = "h265";

        /// <summary>
        /// Create a protobuf CompressedVideo using a single video access unit.
        /// </summary>
        public static Foxglove.CompressedVideo Create(
            ulong unixNs,
            string frameId,
            byte[] h264AccessUnit,
            string format = H264Format)
        {
            return new Foxglove.CompressedVideo
            {
                Timestamp = new Timestamp
                {
                    Seconds = (long)(unixNs / 1_000_000_000UL),
                    Nanos = (int)(unixNs % 1_000_000_000UL)
                },
                FrameId = frameId ?? "",
                Data = ByteString.CopyFrom(h264AccessUnit ?? new byte[0]),
                Format = string.IsNullOrEmpty(format) ? H264Format : format
            };
        }

        /// <summary>
        /// Create and serialize a protobuf CompressedVideo payload.
        /// </summary>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            byte[] h264AccessUnit,
            string format = H264Format)
        {
            return Create(unixNs, frameId, h264AccessUnit, format).ToByteArray();
        }
    }
}
