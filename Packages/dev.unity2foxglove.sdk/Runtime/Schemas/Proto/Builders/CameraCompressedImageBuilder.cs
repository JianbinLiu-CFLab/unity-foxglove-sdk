// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free construction helpers for foxglove.CompressedImage
// protobuf camera payloads.

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Builds official <c>foxglove.CompressedImage</c> protobuf messages
    /// from encoded image bytes.
    /// </summary>
    public static class CameraCompressedImageBuilder
    {
        /// <summary>
        /// Create a protobuf CompressedImage using raw compressed image bytes.
        /// </summary>
        public static Foxglove.CompressedImage Create(ulong unixNs, string frameId, byte[] encodedBytes, string format)
        {
            return new Foxglove.CompressedImage
            {
                Timestamp = new Timestamp
                {
                    Seconds = (long)(unixNs / 1_000_000_000UL),
                    Nanos = (int)(unixNs % 1_000_000_000UL)
                },
                FrameId = frameId ?? "",
                Data = ByteString.CopyFrom(encodedBytes ?? new byte[0]),
                Format = format ?? ""
            };
        }

        /// <summary>
        /// Create and serialize a protobuf CompressedImage payload.
        /// </summary>
        public static byte[] Serialize(ulong unixNs, string frameId, byte[] encodedBytes, string format)
        {
            return Create(unixNs, frameId, encodedBytes, format).ToByteArray();
        }
    }
}
