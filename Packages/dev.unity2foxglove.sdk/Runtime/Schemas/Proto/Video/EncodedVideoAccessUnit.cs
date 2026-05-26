// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Carries encoded video access units with their source capture time.

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// One encoded video access unit and the timestamp captured when its
    /// source camera frame was rendered.
    /// </summary>
    public readonly struct EncodedVideoAccessUnit
    {
        public EncodedVideoAccessUnit(byte[] data, ulong timestampNs)
        {
            Data = data ?? System.Array.Empty<byte>();
            TimestampNs = timestampNs;
        }

        public byte[] Data { get; }
        public ulong TimestampNs { get; }
    }

    internal readonly struct QueuedVideoFrame
    {
        public QueuedVideoFrame(byte[] data, ulong timestampNs)
        {
            Data = data ?? System.Array.Empty<byte>();
            TimestampNs = timestampNs;
        }

        public byte[] Data { get; }
        public ulong TimestampNs { get; }
    }
}
