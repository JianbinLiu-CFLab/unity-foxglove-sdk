// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/PointCloud
// Purpose: Schema-neutral native PointCloud2 payload handoff for optional DDS publishers.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>
    /// Prepared PointCloud2-compatible payload metadata and packed point bytes.
    /// This type intentionally has no ROS2 For Unity dependency.
    /// </summary>
    public sealed class PointCloud2NativeFrame
    {
        /// <summary>Create a prepared PointCloud2 frame handoff.</summary>
        public PointCloud2NativeFrame(
            ulong unixNs,
            string frameId,
            uint height,
            uint width,
            IReadOnlyList<PointCloudPackedField> fields,
            uint pointStep,
            byte[] data,
            bool isDense,
            string topic = null,
            bool isMotionCompensatedVisualization = false)
        {
            if (height == 0U)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (pointStep == 0U)
                throw new ArgumentOutOfRangeException(nameof(pointStep));

            data ??= Array.Empty<byte>();
            var rowStep = checked(pointStep * width);
            var expectedBytes = checked((ulong)rowStep * height);
            if ((ulong)data.Length != expectedBytes)
            {
                throw new ArgumentException(
                    "PointCloud2 data length must equal height * width * point_step.",
                    nameof(data));
            }

            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Height = height;
            Width = width;
            Fields = fields ?? Array.Empty<PointCloudPackedField>();
            PointStep = pointStep;
            RowStep = rowStep;
            Data = data;
            IsDense = isDense;
            ValidCount = checked((int)((ulong)height * width));
            Topic = topic ?? string.Empty;
            IsMotionCompensatedVisualization = isMotionCompensatedVisualization;
        }

        /// <summary>Frame timestamp, in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>Frame id written into the PointCloud2 header.</summary>
        public string FrameId { get; }

        /// <summary>PointCloud2 height. Phase 138L emits unorganized clouds with height 1.</summary>
        public uint Height { get; }

        /// <summary>PointCloud2 width, equal to compacted valid point count for unorganized clouds.</summary>
        public uint Width { get; }

        /// <summary>Point field descriptors and byte offsets.</summary>
        public IReadOnlyList<PointCloudPackedField> Fields { get; }

        /// <summary>Bytes per point.</summary>
        public uint PointStep { get; }

        /// <summary>Bytes per row.</summary>
        public uint RowStep { get; }

        /// <summary>
        /// Packed point bytes owned by this handoff. Treat as read-only; consumers
        /// that retain data after their publish call should clone it.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>True when invalid rays were compacted out of the payload.</summary>
        public bool IsDense { get; }

        /// <summary>Number of compacted valid points in the payload.</summary>
        public int ValidCount { get; }

        /// <summary>Optional per-frame output topic override for native DDS adapters.</summary>
        public string Topic { get; }

        /// <summary>True when this frame is a deskewed visualization stream, not raw sensor truth.</summary>
        public bool IsMotionCompensatedVisualization { get; }
    }
}
