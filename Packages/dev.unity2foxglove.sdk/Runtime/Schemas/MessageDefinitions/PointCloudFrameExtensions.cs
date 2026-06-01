// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MessageDefinitions
// Purpose: Utilities for point-cloud frame traversal and point counts.

using System;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>
    /// Helper methods for consuming <see cref="PointCloudFrame"/> in the shared schema layer.
    /// </summary>
    public static class PointCloudFrameExtensions
    {
        /// <summary>
        /// Returns the number of points that should be treated as valid for build/publish.
        /// </summary>
        public static int GetPointCount(this PointCloudFrame frame)
        {
            if (frame == null)
                return 0;

            var count = frame.Points?.Count ?? 0;
            if (frame.ValidCount < 0)
                return count;

            if (count == 0)
                return Math.Max(0, frame.ValidCount);

            return Math.Max(0, Math.Min(frame.ValidCount, count));
        }
    }
}
