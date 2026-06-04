// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Camera
// Purpose: Core-SDK raw image frame DTO for optional native raw camera bridge output.

using System;

namespace Unity.FoxgloveSDK.Schemas.Camera
{
    /// <summary>
    /// Unity-free raw camera image frame produced by the core camera publisher.
    /// Optional native adapters translate it to transport-specific ROS messages.
    /// </summary>
    public sealed class SensorRawImageFrame
    {
        public SensorRawImageFrame(ulong unixNs, string frameId, int width, int height, byte[] data, string encoding = "rgb8")
            : this(unixNs, frameId, width, height, data, encoding, 0)
        {
        }

        public SensorRawImageFrame(ulong unixNs, string frameId, int width, int height, byte[] data, string encoding = "rgb8", int? isBigendian = null)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "height must be positive.");

            var fixedEncoding = string.IsNullOrWhiteSpace(encoding) ? "rgb8" : encoding.Trim().ToLowerInvariant();
            if (!string.Equals(fixedEncoding, "rgb8", StringComparison.Ordinal))
                throw new ArgumentException("Only rgb8 is supported in this phase.", nameof(encoding));

            var step = width * 3;
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length != step * height)
                throw new ArgumentException("Data length must be exactly step * height.", nameof(data));

            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Width = width;
            Height = height;
            Step = step;
            Encoding = fixedEncoding;
            IsBigendian = isBigendian.GetValueOrDefault() == 0 ? (byte)0 : (byte)1;
            Data = data;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public int Width { get; }
        public int Height { get; }
        public int Step { get; }
        public string Encoding { get; }
        public byte IsBigendian { get; }
        public byte[] Data { get; }
    }
}
