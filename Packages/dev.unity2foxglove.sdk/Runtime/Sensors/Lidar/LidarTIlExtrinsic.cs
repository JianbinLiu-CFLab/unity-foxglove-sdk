// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// LiDAR-to-IMU extrinsic used by the virtual LiDAR demo TF chain.
    /// </summary>
    public readonly struct LidarTIlExtrinsic
    {
        /// <summary>Identity T_IL, with zero translation and identity rotation.</summary>
        public static readonly LidarTIlExtrinsic Identity =
            new LidarTIlExtrinsic(Vector3.Zero, Quaternion.Identity);

        /// <summary>Translation from LiDAR frame to IMU frame, in meters.</summary>
        public readonly Vector3 TranslationMeters;

        /// <summary>Rotation from LiDAR frame to IMU frame, normalized.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Create a T_IL extrinsic, normalizing invalid rotations to identity.</summary>
        public LidarTIlExtrinsic(Vector3 translationMeters, Quaternion rotation)
        {
            TranslationMeters = IsFinite(translationMeters) ? translationMeters : Vector3.Zero;
            Rotation = NormalizeRotation(rotation);
        }

        /// <summary>
        /// Create a T_IL extrinsic from a row-major 3x3 rotation matrix and standalone
        /// translation.
        /// </summary>
        public static LidarTIlExtrinsic FromRotationMatrix3x3(
            Vector3 translationMeters,
            float m00, float m01, float m02,
            float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            var matrix = new Matrix4x4(
                m00, m01, m02, 0f,
                m10, m11, m12, 0f,
                m20, m21, m22, 0f,
                0f, 0f, 0f, 1f);
            return new LidarTIlExtrinsic(
                translationMeters,
                Quaternion.CreateFromRotationMatrix(matrix));
        }

        /// <summary>Normalize a quaternion, returning identity for zero or non-finite input.</summary>
        public static Quaternion NormalizeRotation(Quaternion rotation)
        {
            var lengthSquared = rotation.LengthSquared();
            if (!IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
                return Quaternion.Identity;
            return Quaternion.Normalize(rotation);
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
