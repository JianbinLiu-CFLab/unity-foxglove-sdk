// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components
// Purpose: Static coordinate conversion helpers between Unity (left-hand) and Foxglove (right-hand) coordinate systems.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Static coordinate conversion between Unity and Foxglove coordinate systems.
    /// </summary>
    public static class CoordinateConverter
    {
        /// <summary>Convert Unity position to Foxglove (right-hand: X forward, Y left, Z up).</summary>
        public static UnityEngine.Vector3 UnityToFoxglovePosition(UnityEngine.Vector3 pos)
        {
            return new UnityEngine.Vector3(pos.z, -pos.x, pos.y);
        }

        /// <summary>Convert Foxglove position to Unity.</summary>
        public static UnityEngine.Vector3 FoxgloveToUnityPosition(UnityEngine.Vector3 pos)
        {
            return new UnityEngine.Vector3(-pos.y, pos.z, pos.x);
        }

        /// <summary>Convert Unity rotation to Foxglove.</summary>
        public static UnityEngine.Quaternion UnityToFoxgloveRotation(UnityEngine.Quaternion q)
        {
            return new UnityEngine.Quaternion(-q.z, q.x, -q.y, q.w);
        }

        /// <summary>Convert Foxglove rotation to Unity.</summary>
        public static UnityEngine.Quaternion FoxgloveToUnityRotation(UnityEngine.Quaternion q)
        {
            return new UnityEngine.Quaternion(q.y, -q.z, -q.x, q.w);
        }
    }
}
