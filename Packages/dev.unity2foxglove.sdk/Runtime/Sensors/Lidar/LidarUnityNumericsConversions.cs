// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Shared conversion helpers between Unity and System.Numerics LiDAR extrinsics.

using System;
using UnityEngine;
using NumericQuaternion = System.Numerics.Quaternion;
using NumericVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Converts LiDAR extrinsic values between Unity authoring fields and System.Numerics math.
    /// </summary>
    internal static class LidarUnityNumericsConversions
    {
        public static Vector3 ToUnityVector3(NumericVector3 value)
            => new Vector3(value.X, value.Y, value.Z);

        public static NumericVector3 ToNumericsVector3(Vector3 value)
            => new NumericVector3(value.x, value.y, value.z);

        public static Quaternion ToUnityQuaternion(NumericQuaternion value)
        {
            var normalized = LidarTIlExtrinsic.NormalizeRotation(value);
            return new Quaternion(normalized.X, normalized.Y, normalized.Z, normalized.W);
        }

        public static Quaternion ToCleanUnityQuaternion(NumericQuaternion value)
        {
            var normalized = LidarTIlExtrinsic.NormalizeRotation(value);
            return new Quaternion(
                CleanNearZero(normalized.X),
                CleanNearZero(normalized.Y),
                CleanNearZero(normalized.Z),
                CleanNearZero(normalized.W));
        }

        public static NumericQuaternion ToNumericsQuaternion(Quaternion value)
            => LidarTIlExtrinsic.NormalizeRotation(new NumericQuaternion(value.x, value.y, value.z, value.w));

        private static float CleanNearZero(float value)
            => Math.Abs(value) < 1e-6f ? 0f : value;
    }
}
