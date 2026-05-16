// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free raw PointCloud QoS helpers for point/byte budgets
// and deterministic LOD sampling.

using System;
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
        UniformStride = 1
    }

    /// <summary>
    /// Helper methods for raw foxglove.PointCloud QoS decisions.
    /// </summary>
    public static class PointCloudQoS
    {
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

            var hasIntensity = false;
            var hasReflectivity = false;
            var hasRing = false;
            var hasTimeOffset = false;

            foreach (var point in frame.Points)
            {
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
    }
}
