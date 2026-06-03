// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Immutable ray subsampling and column-bucket layout for one LiDAR profile.
    /// </summary>
    internal readonly struct VirtualLidarScanLayout
    {
        private VirtualLidarScanLayout(
            int rawRayCount,
            int rayStride,
            int effectiveRayCount,
            int spinEffectiveColumns,
            int scanColumnCount,
            int maxRaysPerColumn,
            int[] rayColumns,
            int[][] columnRays)
        {
            RawRayCount = rawRayCount;
            RayStride = rayStride;
            EffectiveRayCount = effectiveRayCount;
            SpinEffectiveColumns = spinEffectiveColumns;
            ScanColumnCount = scanColumnCount;
            MaxRaysPerColumn = maxRaysPerColumn;
            RayColumns = rayColumns;
            ColumnRays = columnRays;
        }

        public int RawRayCount { get; }
        public int RayStride { get; }
        public int EffectiveRayCount { get; }
        public int SpinEffectiveColumns { get; }
        public int ScanColumnCount { get; }
        public int MaxRaysPerColumn { get; }
        public int[] RayColumns { get; }
        public int[][] ColumnRays { get; }

        public static VirtualLidarScanLayout Build(ILidarScanPattern scanPattern, int maxRaysPerScan)
        {
            if (scanPattern == null)
                return default;

            var rawRayCount = Math.Max(1, scanPattern.RayCount);
            var budget = maxRaysPerScan <= 0 ? rawRayCount : Math.Min(rawRayCount, maxRaysPerScan);
            budget = Math.Max(1, budget);
            var rayStride = Math.Max(1, (rawRayCount + budget - 1) / budget);
            var effectiveRayCount = (rawRayCount + rayStride - 1) / rayStride;

            var spinEffectiveColumns = scanPattern is SpinningScanPattern spin && spin.Rings > 0
                ? spin.RayCount / spin.Rings
                : 0;
            var rawColumns = spinEffectiveColumns > 0 ? spinEffectiveColumns : Math.Max(1, rawRayCount);

            var rayColumns = new int[effectiveRayCount];
            var scanColumnCount = 0;
            for (var k = 0; k < effectiveRayCount; k++)
            {
                var index = k * rayStride;
                if (index >= rawRayCount)
                    index = rawRayCount - 1;

                var column = index % rawColumns;
                if (column < 0 || column >= rawColumns)
                    column = 0;

                rayColumns[k] = column;
                if (column >= scanColumnCount)
                    scanColumnCount = column + 1;
            }

            if (scanColumnCount <= 0)
                scanColumnCount = Math.Max(1, rawColumns);

            var columnCounts = new int[scanColumnCount];
            for (var k = 0; k < effectiveRayCount; k++)
                columnCounts[rayColumns[k]]++;

            var columnRays = new int[scanColumnCount][];
            var maxRaysPerColumn = 1;
            for (var c = 0; c < scanColumnCount; c++)
            {
                columnRays[c] = new int[columnCounts[c]];
                if (columnCounts[c] > maxRaysPerColumn)
                    maxRaysPerColumn = columnCounts[c];
            }

            var columnFill = new int[scanColumnCount];
            for (var k = 0; k < effectiveRayCount; k++)
            {
                var c = rayColumns[k];
                columnRays[c][columnFill[c]++] = k;
            }

            return new VirtualLidarScanLayout(
                rawRayCount,
                rayStride,
                effectiveRayCount,
                spinEffectiveColumns,
                scanColumnCount,
                maxRaysPerColumn,
                rayColumns,
                columnRays);
        }
    }
}
