// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Burst job that maps raycast results into managed point payload format.

#if UNITY_5_3_OR_NEWER
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.FoxgloveSDK.Sensors;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;
#endif

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
#if UNITY_5_3_OR_NEWER
    /// <summary>Builds point payload records from raycast results.</summary>
    [BurstCompile]
    internal struct VirtualLidarBuildPointsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<RaycastHit> Hits;
        [ReadOnly] public NativeArray<float> RayTimeOffsets;
        [ReadOnly] public NativeArray<ushort> RayRings;

        // WorldToLocal is fixed for the scan reference. AcquisitionWorldToLocal
        // is the current batch pose for raw rolling PointCloud2 Native output.
        [ReadOnly] public float4x4 WorldToLocal;
        [ReadOnly] public float4x4 AcquisitionWorldToLocal;
        [ReadOnly] public float MinRange;
        [ReadOnly] public float MaxRange;
        [ReadOnly] public float SyntheticIntensity;
        [ReadOnly] public float SyntheticReflectivity;

        // The output keeps ray-slot order and marks misses invalid so the Draco worker
        // can compact valid XYZ points off the main thread.
        [WriteOnly] public NativeArray<VirtualLidarPointData> Points;

        public void Execute(int index)
        {
            var output = new VirtualLidarPointData { IsValid = 0 };
            if (index >= Hits.Length)
            {
                if (index < Points.Length)
                    Points[index] = output;
                return;
            }

            var hit = Hits[index];
            if (hit.distance > 0f && hit.distance >= MinRange && hit.distance <= MaxRange)
            {
                var world = new float4(new float3(hit.point.x, hit.point.y, hit.point.z), 1f);
                var local = math.mul(WorldToLocal, world).xyz;
                var acquisitionLocal = math.mul(AcquisitionWorldToLocal, world).xyz;
                var converted = CoordinateConverterFloat3.UnityToFoxglovePosition(local);
                var acquisitionConverted = CoordinateConverterFloat3.UnityToFoxglovePosition(acquisitionLocal);
                output.X = converted.x;
                output.Y = converted.y;
                output.Z = converted.z;
                output.AcquisitionX = acquisitionConverted.x;
                output.AcquisitionY = acquisitionConverted.y;
                output.AcquisitionZ = acquisitionConverted.z;
                output.Intensity = SyntheticIntensity;
                output.Reflectivity = SyntheticReflectivity;
                output.TimeOffsetSeconds = index < RayTimeOffsets.Length ? RayTimeOffsets[index] : 0f;
                output.Ring = index < RayRings.Length ? RayRings[index] : (ushort)0;
                output.IsValid = 1;
                output.HasAcquisitionFrame = 1;
            }

            Points[index] = output;
        }
    }
#endif
}
