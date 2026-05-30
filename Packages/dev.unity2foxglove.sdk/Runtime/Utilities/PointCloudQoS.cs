// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free raw PointCloud QoS helpers for point/byte budgets
// and deterministic LOD sampling.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Sampling policy used when a raw PointCloud frame exceeds its live
    /// point or packed-byte budget.
    /// </summary>
    public enum PointCloudSamplingMode
    {
        /// <summary>Keep the first N points, preserving the historical clamp behavior.</summary>
        FirstPoints = 0,
        /// <summary>Keep a deterministic spread across the whole frame.</summary>
        UniformStride = 1,
        /// <summary>Keep the first source point encountered in each voxel cell.</summary>
        VoxelGrid = 2
    }

    /// <summary>
    /// Helper methods for raw foxglove.PointCloud QoS decisions.
    /// </summary>
    public static class PointCloudQoS
    {
        /// <summary>XYZ float32 packed field width in bytes.</summary>
        public const int XyzPackedStrideBytes = 12;
        public const int Float32Bytes = 4;
        public const int Uint16Bytes = 2;

        /// <summary>
        /// Computes the frame-wide packed point stride used by PointCloudMessageBuilder.
        /// </summary>
        public static int ComputePackedStride(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var pointCount = frame.GetPointCount();
            var hasIntensity = false;
            var hasReflectivity = false;
            var hasRing = false;
            var hasTimeOffset = false;

            for (var i = 0; i < pointCount; i++)
            {
                var point = frame.Points[i];
                if (point == null)
                    continue;

                hasIntensity |= point.Intensity.HasValue;
                hasReflectivity |= point.Reflectivity.HasValue;
                hasRing |= point.Ring.HasValue;
                hasTimeOffset |= point.TimeOffsetSeconds.HasValue;
            }

            var stride = XyzPackedStrideBytes;
            if (hasIntensity) stride += Float32Bytes;
            if (hasReflectivity) stride += Float32Bytes;
            if (hasRing) stride += Uint16Bytes;
            if (hasTimeOffset) stride += Float32Bytes;
            return stride;
        }

        /// <summary>
        /// Computes the point count allowed by point and packed-data byte budgets.
        /// A non-positive <paramref name="maxPoints"/> keeps the historical "at
        /// least one point" behavior; use <paramref name="maxPackedBytes"/> to
        /// force zero points when the packed byte budget cannot fit one point.
        /// </summary>
        public static int ComputeEffectivePointBudget(
            int pointCount,
            int maxPoints,
            int maxPackedBytes,
            int pointStride)
        {
            if (pointCount <= 0)
                return 0;

            if (pointStride <= 0)
                throw new ArgumentOutOfRangeException(nameof(pointStride), "Point stride must be positive.");

            var budget = Math.Min(pointCount, Math.Max(1, maxPoints));
            if (maxPackedBytes > 0)
            {
                var byteBudgetPoints = maxPackedBytes / pointStride;
                if (byteBudgetPoints <= 0)
                    return 0;

                budget = Math.Min(budget, byteBudgetPoints);
            }

            return budget;
        }

        /// <summary>
        /// Builds deterministic sample indices that preserve the first and last point.
        /// </summary>
        public static int[] BuildUniformSampleIndices(int sourceCount, int targetCount)
        {
            if (sourceCount <= 0 || targetCount <= 0)
                return Array.Empty<int>();

            if (targetCount >= sourceCount)
            {
                var all = new int[sourceCount];
                for (var i = 0; i < all.Length; i++)
                    all[i] = i;
                return all;
            }

            if (targetCount == 1)
                return new[] { 0 };

            var indices = new int[targetCount];
            var step = (double)(sourceCount - 1) / (targetCount - 1);
            var previous = -1;
            for (var i = 0; i < targetCount; i++)
            {
                var index = i == targetCount - 1
                    ? sourceCount - 1
                    : (int)Math.Floor(i * step + 0.5d);

                if (index <= previous)
                    index = previous + 1;
                if (index >= sourceCount)
                    index = sourceCount - 1;

                indices[i] = index;
                previous = index;
            }

            return indices;
        }

        /// <summary>
        /// Builds deterministic sample indices for first-point voxel representatives.
        /// </summary>
        public static int[] BuildVoxelSampleIndices(PointCloudFrame frame, float voxelSizeMeters)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var pointCount = frame.GetPointCount();
            if (pointCount == 0)
                return Array.Empty<int>();

            if (voxelSizeMeters <= 0f)
                return BuildNonNullPointIndices(frame);

            var seen = new HashSet<VoxelKey>();
            var indices = new List<int>();
            for (var i = 0; i < pointCount; i++)
            {
                var point = frame.Points[i];
                if (point == null)
                    continue;

                var key = VoxelKey.From(point, voxelSizeMeters);
                if (seen.Add(key))
                    indices.Add(i);
            }

            return indices.ToArray();
        }

        private static int[] BuildNonNullPointIndices(PointCloudFrame frame)
        {
            var pointCount = frame.GetPointCount();
            var indices = new List<int>();
            for (var i = 0; i < pointCount; i++)
            {
                if (frame.Points[i] != null)
                    indices.Add(i);
            }

            return indices.Count == 0 ? Array.Empty<int>() : indices.ToArray();
        }

        private readonly struct VoxelKey : IEquatable<VoxelKey>
        {
            private readonly long _x;
            private readonly long _y;
            private readonly long _z;

            private VoxelKey(long x, long y, long z)
            {
                _x = x;
                _y = y;
                _z = z;
            }

            public static VoxelKey From(PointCloudPoint point, float voxelSizeMeters)
            {
                return new VoxelKey(
                    (long)Math.Floor(point.X / voxelSizeMeters),
                    (long)Math.Floor(point.Y / voxelSizeMeters),
                    (long)Math.Floor(point.Z / voxelSizeMeters));
            }

            public bool Equals(VoxelKey other)
                => _x == other._x && _y == other._y && _z == other._z;

            public override bool Equals(object obj)
                => obj is VoxelKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + _x.GetHashCode();
                    hash = hash * 31 + _y.GetHashCode();
                    hash = hash * 31 + _z.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
