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
            bool isMotionCompensatedVisualization = false,
            bool ownsPooledData = false,
            int validCount = -1,
            bool preferPooledDataRetention = false)
        {
            if (height == 0U)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (pointStep == 0U)
                throw new ArgumentOutOfRangeException(nameof(pointStep));

            data ??= Array.Empty<byte>();
            if (width != 0U && pointStep > uint.MaxValue / width)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "PointCloud2 row step must fit in a uint.");
            }

            var rowStep = checked(pointStep * width);
            var expectedBytes = checked((ulong)rowStep * height);
            if ((ulong)data.Length != expectedBytes)
            {
                throw new ArgumentException(
                    "PointCloud2 data length must equal height * width * point_step.",
                    nameof(data));
            }

            var publishedPointCount = checked((int)((ulong)height * width));
            if (validCount < -1 || validCount > publishedPointCount)
                throw new ArgumentOutOfRangeException(nameof(validCount));

            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            Height = height;
            Width = width;
            Fields = fields ?? Array.Empty<PointCloudPackedField>();
            PointStep = pointStep;
            RowStep = rowStep;
            Data = data;
            IsDense = isDense;
            ValidCount = validCount < 0 ? publishedPointCount : validCount;
            Topic = topic ?? string.Empty;
            IsMotionCompensatedVisualization = isMotionCompensatedVisualization;
            _ownsPooledData = ownsPooledData && Data.Length != 0;
            _preferPooledDataRetention = _ownsPooledData && preferPooledDataRetention;
        }

        private readonly bool _ownsPooledData;
        private readonly bool _preferPooledDataRetention;
        private bool _dataRecycled;

        /// <summary>Frame timestamp, in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>Frame id written into the PointCloud2 header.</summary>
        public string FrameId { get; }

        /// <summary>PointCloud2 height. Phase 138L emits unorganized clouds with height 1.</summary>
        public uint Height { get; }

        /// <summary>PointCloud2 width, equal to the published point-slot count for unorganized clouds.</summary>
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

        /// <summary>True when every published point slot contains a valid point.</summary>
        public bool IsDense { get; }

        /// <summary>Number of valid points represented by the published point slots.</summary>
        public int ValidCount { get; }

        /// <summary>Optional per-frame output topic override for native DDS adapters.</summary>
        public string Topic { get; }

        /// <summary>True when this frame is a deskewed visualization stream, not raw sensor truth.</summary>
        public bool IsMotionCompensatedVisualization { get; }

        internal void RecycleData()
        {
            if (!_ownsPooledData || _dataRecycled)
                return;

            _dataRecycled = true;
            PointCloudPackedByteBufferPool.Return(Data, _preferPooledDataRetention);
        }
    }

    internal static class PointCloudPublishRateGate
    {
        internal static bool ShouldPublish(ref ulong lastPublishUnixNs, ulong timestampNs, ulong intervalNs)
        {
            if (intervalNs == 0UL)
                throw new ArgumentOutOfRangeException(nameof(intervalNs));

            if (lastPublishUnixNs != 0UL
                && timestampNs >= lastPublishUnixNs
                && timestampNs - lastPublishUnixNs < intervalNs)
            {
                return false;
            }

            lastPublishUnixNs = timestampNs;
            return true;
        }
    }
}
