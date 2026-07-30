// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Imu
// Purpose: Schema-neutral IMU handoff payload for optional transport Providers.

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
            UnixNs = unixNs;
            FrameId = frameId ?? string.Empty;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
            HasOrientation = hasOrientation;
        }

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
