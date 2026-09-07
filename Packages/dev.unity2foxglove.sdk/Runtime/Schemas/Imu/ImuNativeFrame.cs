// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Imu
// Purpose: Schema-neutral IMU handoff payload for optional transport Providers.

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Schemas.Imu
{
    /// <summary>
    /// Prepared IMU sample handoff that carries ROS-compatible values without ROS or
    /// optional-package dependencies.
    /// </summary>
    public sealed class ImuNativeFrame
    {
        /// <summary>
        /// Create a schema-neutral IMU handoff frame.
        /// </summary>
        public ImuNativeFrame(
            ulong unixNs,
            string frameId,
            Vector3 linearAcceleration,
            Vector3 angularVelocity,
            Quaternion orientation,
            bool hasOrientation)
        {
            ValidateFinite(linearAcceleration, nameof(linearAcceleration));
            ValidateFinite(angularVelocity, nameof(angularVelocity));
            ValidateFinite(orientation, nameof(orientation));
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
            HasOrientation = hasOrientation;
        }

        private static void ValidateFinite(Vector3 value, string parameterName)
        {
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z))
                throw new ArgumentException("IMU vector values must be finite.", parameterName);
        }

        private static void ValidateFinite(Quaternion value, string parameterName)
        {
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z) || !IsFinite(value.W))
                throw new ArgumentException("IMU orientation values must be finite.", parameterName);
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Sample timestamp, in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>Frame id used by the native message header.</summary>
        public string FrameId { get; }

        /// <summary>ROS-compatible linear acceleration vector.</summary>
        public Vector3 LinearAcceleration { get; }

        /// <summary>ROS-compatible angular velocity vector.</summary>
        public Vector3 AngularVelocity { get; }

        /// <summary>ROS-compatible orientation quaternion.</summary>
        public Quaternion Orientation { get; }

        /// <summary>Whether orientation is valid and should be emitted.</summary>
        public bool HasOrientation { get; }
    }
}
