// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors
// Purpose: Burst-safe float3 coordinate conversion helpers.

#if UNITY_5_3_OR_NEWER
using Unity.Mathematics;
using UnityEngine;

namespace Unity.FoxgloveSDK.Sensors
{
    /// <summary>Helper conversions for Burst-friendly point-cloud math paths.</summary>
    public static class CoordinateConverterFloat3
    {
        /// <summary>Convert Unity position to Foxglove (right-hand: X forward, Y left, Z up).</summary>
        public static float3 UnityToFoxglovePosition(float3 pos)
        {
            return new float3(pos.z, -pos.x, pos.y);
        }

        /// <summary>Convert Foxglove position to Unity.</summary>
        public static float3 FoxgloveToUnityPosition(float3 pos)
        {
            return new float3(-pos.y, pos.z, pos.x);
        }

        /// <summary>Convert a Unity matrix to Burst-friendly float4x4.</summary>
        public static float4x4 ToFloat4x4(this Matrix4x4 matrix)
        {
            return new float4x4(
                new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
                new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
                new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
                new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
        }
    }
}
#endif
