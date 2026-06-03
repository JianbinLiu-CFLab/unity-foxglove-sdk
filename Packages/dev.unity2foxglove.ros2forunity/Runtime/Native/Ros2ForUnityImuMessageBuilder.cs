// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Maps SDK IMU DTOs to generated ROS2 sensor_msgs messages.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas.Imu;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityImuMessageBuilder
    {
        public static sensor_msgs.msg.Imu Build(
            ImuNativeFrame frame,
            IReadOnlyList<double> orientationCovariance,
            IReadOnlyList<double> angularVelocityCovariance,
            IReadOnlyList<double> linearAccelerationCovariance)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            ValidateCovariance(orientationCovariance, nameof(orientationCovariance));
            ValidateCovariance(angularVelocityCovariance, nameof(angularVelocityCovariance));
            ValidateCovariance(linearAccelerationCovariance, nameof(linearAccelerationCovariance));

            var message = new sensor_msgs.msg.Imu
            {
                Header = new std_msgs.msg.Header
                {
                    Stamp = new builtin_interfaces.msg.Time
                    {
                        Sec = (int)(frame.UnixNs / 1_000_000_000UL),
                        Nanosec = (uint)(frame.UnixNs % 1_000_000_000UL)
                    },
                    Frame_id = frame.FrameId
                },
                Orientation = BuildOrientation(frame),
                Angular_velocity = new geometry_msgs.msg.Vector3
                {
                    X = frame.AngularVelocity.X,
                    Y = frame.AngularVelocity.Y,
                    Z = frame.AngularVelocity.Z
                },
                Linear_acceleration = new geometry_msgs.msg.Vector3
                {
                    X = frame.LinearAcceleration.X,
                    Y = frame.LinearAcceleration.Y,
                    Z = frame.LinearAcceleration.Z
                }
            };

            CopyInto(orientationCovariance, message.Orientation_covariance);
            if (!frame.HasOrientation)
                message.Orientation_covariance[0] = -1d;
            CopyInto(angularVelocityCovariance, message.Angular_velocity_covariance);
            CopyInto(linearAccelerationCovariance, message.Linear_acceleration_covariance);

            return message;
        }

        private static geometry_msgs.msg.Quaternion BuildOrientation(ImuNativeFrame frame)
        {
            if (!frame.HasOrientation)
            {
                return new geometry_msgs.msg.Quaternion
                {
                    X = 0d,
                    Y = 0d,
                    Z = 0d,
                    W = 1d
                };
            }

            return new geometry_msgs.msg.Quaternion
            {
                X = frame.Orientation.X,
                Y = frame.Orientation.Y,
                Z = frame.Orientation.Z,
                W = frame.Orientation.W
            };
        }

        private static void ValidateCovariance(IReadOnlyList<double> values, string name)
        {
            if (values == null || values.Count != 9)
                throw new ArgumentException(name + " must contain exactly 9 values.");
        }

        private static void CopyInto(IReadOnlyList<double> source, double[] destination)
        {
            if (destination == null)
                return;

            var count = Math.Min(source.Count, destination.Length);
            for (var i = 0; i < count; i++)
                destination[i] = source[i];
        }
    }
}
#endif
