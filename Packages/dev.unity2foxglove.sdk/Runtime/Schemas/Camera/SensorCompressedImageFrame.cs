// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Camera
// Purpose: Core-SDK camera frame DTO for optional native ROS2 adapters.

using System;

namespace Unity.FoxgloveSDK.Schemas.Camera
{
    /// <summary>
    /// Unity-free compressed image frame emitted by the core camera publisher.
    /// Optional adapters can translate it to transport-specific message types.
    /// </summary>
    public sealed class SensorCompressedImageFrame
    {
        public SensorCompressedImageFrame(ulong unixNs, string frameId, byte[] data, string format)
        {
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Format = string.IsNullOrWhiteSpace(format) ? "jpeg" : format;
        }

        public ulong UnixNs { get; }
        public string FrameId { get; }
        public byte[] Data { get; }
        public string Format { get; }
    }
}
