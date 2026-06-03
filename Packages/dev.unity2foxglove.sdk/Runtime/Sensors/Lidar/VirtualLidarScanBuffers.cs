// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using Unity.Collections;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Owns the persistent native buffers and derived layout for one VirtualLidar scan profile.
    /// </summary>
    internal sealed class VirtualLidarScanBuffers : IDisposable
    {
        public bool IsCreated => Commands.IsCreated;

        public int RawRayCount { get; private set; }
        public int RayStride { get; private set; }
        public int EffectiveRayCount { get; private set; }
        public int SpinEffectiveColumns { get; private set; }
        public int ScanColumnCount { get; private set; }
        public int MaxRaysPerColumn { get; private set; } = 1;
        public int[] RayColumns { get; private set; }
        public int[][] ColumnRays { get; private set; }

        public NativeArray<RaycastCommand> Commands { get; private set; }
        public NativeArray<RaycastHit> Results { get; private set; }
        public NativeArray<float> RayTimeOffsets { get; private set; }
        public NativeArray<ushort> RayRings { get; private set; }
        public NativeArray<VirtualLidarPointData> PointData { get; private set; }

        public void Allocate(ILidarScanPattern scanPattern, int maxRaysPerScan)
        {
            Dispose();
            if (scanPattern == null)
                return;

            var layout = VirtualLidarScanLayout.Build(scanPattern, maxRaysPerScan);
            RawRayCount = layout.RawRayCount;
            RayStride = layout.RayStride;
            EffectiveRayCount = layout.EffectiveRayCount;
            SpinEffectiveColumns = layout.SpinEffectiveColumns;
            ScanColumnCount = layout.ScanColumnCount;
            MaxRaysPerColumn = layout.MaxRaysPerColumn;
            RayColumns = layout.RayColumns;
            ColumnRays = layout.ColumnRays;

            Commands = new NativeArray<RaycastCommand>(EffectiveRayCount, Allocator.Persistent);
            Results = new NativeArray<RaycastHit>(EffectiveRayCount, Allocator.Persistent);
            RayTimeOffsets = new NativeArray<float>(EffectiveRayCount, Allocator.Persistent);
            RayRings = new NativeArray<ushort>(EffectiveRayCount, Allocator.Persistent);
            PointData = new NativeArray<VirtualLidarPointData>(EffectiveRayCount, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Commands.IsCreated) Commands.Dispose();
            if (Results.IsCreated) Results.Dispose();
            if (RayTimeOffsets.IsCreated) RayTimeOffsets.Dispose();
            if (RayRings.IsCreated) RayRings.Dispose();
            if (PointData.IsCreated) PointData.Dispose();

            Commands = default;
            Results = default;
            RayTimeOffsets = default;
            RayRings = default;
            PointData = default;
            RawRayCount = 0;
            RayStride = 0;
            EffectiveRayCount = 0;
            SpinEffectiveColumns = 0;
            ScanColumnCount = 0;
            MaxRaysPerColumn = 1;
            RayColumns = null;
            ColumnRays = null;
        }

        public int ComputeProfileHash()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + RawRayCount;
                hash = hash * 31 + EffectiveRayCount;
                hash = hash * 31 + RayStride;
                hash = hash * 31 + ScanColumnCount;
                return hash;
            }
        }

        public int BudgetColumnsPerTick(int maxRaycastCommandsPerFixedUpdate)
        {
            var perColumn = Math.Max(1, MaxRaysPerColumn);
            return Math.Max(1, maxRaycastCommandsPerFixedUpdate / perColumn);
        }
    }
}
