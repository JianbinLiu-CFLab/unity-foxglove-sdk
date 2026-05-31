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

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>Unmanaged output record consumed by VirtualLidar.</summary>
    internal struct VirtualLidarPointData
    {
        public float X;
        public float Y;
        public float Z;
        public float Intensity;
        public float Reflectivity;
        public float TimeOffsetSeconds;
        public ushort Ring;
        public byte IsValid;
    }

    /// <summary>Unmanaged cache of raycast outputs for Burst preprocessing.</summary>
    internal struct VirtualLidarHitData
    {
        public float3 Point;
        public float Distance;
        public uint ColliderInstanceId;
    }

    /// <summary>Builds point payload records from raycast results.</summary>
    [BurstCompile]
    internal struct VirtualLidarBuildPointsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VirtualLidarHitData> Hits;
        [ReadOnly] public NativeArray<float> RayTimeOffsets;
        [ReadOnly] public NativeArray<ushort> RayRings;
        [ReadOnly] public float4x4 WorldToLocal;
        [ReadOnly] public float MinRange;
        [ReadOnly] public float MaxRange;
        [ReadOnly] public float SyntheticIntensity;
        [ReadOnly] public float SyntheticReflectivity;

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
            if (hit.ColliderInstanceId != 0u && hit.Distance > 0f && hit.Distance >= MinRange && hit.Distance <= MaxRange)
            {
                var local = math.mul(WorldToLocal, new float4(hit.Point, 1f)).xyz;
                var converted = CoordinateConverterFloat3.UnityToFoxglovePosition(local);
                output.X = converted.x;
                output.Y = converted.y;
                output.Z = converted.z;
                output.Intensity = SyntheticIntensity;
                output.Reflectivity = SyntheticReflectivity;
                output.TimeOffsetSeconds = index < RayTimeOffsets.Length ? RayTimeOffsets[index] : 0f;
                output.Ring = index < RayRings.Length ? RayRings[index] : (ushort)0;
                output.IsValid = 1;
            }

            Points[index] = output;
        }
    }
}
#endif
