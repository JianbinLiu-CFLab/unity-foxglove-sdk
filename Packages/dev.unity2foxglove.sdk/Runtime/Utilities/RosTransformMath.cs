// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free ROS transform math helpers.

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Math helpers for ROS transform conventions that should not depend on UnityEngine.
    /// </summary>
    public static class RosTransformMath
    {
        /// <summary>
        /// Convert ROS roll-pitch-yaw degrees to a quaternion using the standard
        /// intrinsic ZYX convention.
        /// </summary>
        public static Quaternion RollPitchYawDegreesToQuaternion(double rollDegrees, double pitchDegrees, double yawDegrees)
        {
            ThrowIfNonFinite(rollDegrees, nameof(rollDegrees));
            ThrowIfNonFinite(pitchDegrees, nameof(pitchDegrees));
            ThrowIfNonFinite(yawDegrees, nameof(yawDegrees));

            var roll = rollDegrees * Math.PI / 180.0;
            var pitch = pitchDegrees * Math.PI / 180.0;
            var yaw = yawDegrees * Math.PI / 180.0;

            var cr = Math.Cos(roll * 0.5);
            var sr = Math.Sin(roll * 0.5);
            var cp = Math.Cos(pitch * 0.5);
            var sp = Math.Sin(pitch * 0.5);
            var cy = Math.Cos(yaw * 0.5);
            var sy = Math.Sin(yaw * 0.5);

            var quaternion = new Quaternion(
                (float)(sr * cp * cy - cr * sp * sy),
                (float)(cr * sp * cy + sr * cp * sy),
                (float)(cr * cp * sy - sr * sp * cy),
                (float)(cr * cp * cy + sr * sp * sy));
            return Quaternion.Normalize(quaternion);
        }

        private static void ThrowIfNonFinite(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, "Angle must be finite.");
        }
    }
}
