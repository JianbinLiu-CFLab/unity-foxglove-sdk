// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Unity-free rigid transform helpers for LiDAR, IMU, and camera extrinsics.
    /// </summary>
    public static class LidarExtrinsicMath
    {
        /// <summary>Return the inverse of a source-to-target extrinsic.</summary>
        public static LidarTIlExtrinsic Invert(LidarTIlExtrinsic extrinsic)
        {
            var inverseRotation = Quaternion.Inverse(extrinsic.Rotation);
            var inverseTranslation = Vector3.Transform(-extrinsic.TranslationMeters, inverseRotation);
            return new LidarTIlExtrinsic(inverseTranslation, inverseRotation);
        }

        /// <summary>Compose source-to-mid and mid-to-target into source-to-target.</summary>
        public static LidarTIlExtrinsic Compose(LidarTIlExtrinsic sourceToMid, LidarTIlExtrinsic midToTarget)
        {
            var rotation = Quaternion.Concatenate(sourceToMid.Rotation, midToTarget.Rotation);
            var translation = Vector3.Transform(sourceToMid.TranslationMeters, midToTarget.Rotation)
                + midToTarget.TranslationMeters;
            return new LidarTIlExtrinsic(translation, rotation);
        }

        /// <summary>Transform a point through the given extrinsic.</summary>
        public static Vector3 TransformPoint(LidarTIlExtrinsic extrinsic, Vector3 point)
            => Vector3.Transform(point, extrinsic.Rotation) + extrinsic.TranslationMeters;
    }
}
