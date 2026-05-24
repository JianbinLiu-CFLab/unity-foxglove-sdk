// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Owns synthetic IMU sample data for Phase132.

using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard IMU Source")]
public sealed class Phase132StandardImuSource : MonoBehaviour
{
    [Header("IMU")]
    [SerializeField] private string _frameId = "base_link";
    [SerializeField] private Vector3 _angularVelocityRadPerSecond = new Vector3(0f, 0f, 0.1f);
    [SerializeField] private Vector3 _linearAccelerationMetersPerSecondSquared = new Vector3(0f, 0f, 9.80665f);

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private string _lastError = string.Empty;

    public string FrameId => Phase132StandardMessagesCommon.CleanFrameId(_frameId, "base_link");

    public void RecordPublished()
    {
        _publishedCount++;
        _lastError = string.Empty;
        _statusMessage = "Published IMU=" + _publishedCount + ".";
    }

    public void RecordError(string error)
    {
        _lastError = error;
        _statusMessage = error;
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public sensor_msgs.msg.Imu CreateImu(int sec, uint nanosec)
    {
        var orientationCovariance = new[] { 0.01d, 0d, 0d, 0d, 0.01d, 0d, 0d, 0d, 0.01d };
        var angularCovariance = new[] { 0.02d, 0d, 0d, 0d, 0.02d, 0d, 0d, 0d, 0.02d };
        var linearCovariance = new[] { 0.04d, 0d, 0d, 0d, 0.04d, 0d, 0d, 0d, 0.04d };

        Phase132StandardMessagesCommon.ValidateFixedArrayLength(orientationCovariance, 9, "IMU orientation covariance double[9]");
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(angularCovariance, 9, "IMU angular_velocity covariance double[9]");
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(linearCovariance, 9, "IMU linear_acceleration covariance double[9]");

        return new sensor_msgs.msg.Imu
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Orientation = Phase132StandardMessagesCommon.IdentityRotation(),
            Orientation_covariance = orientationCovariance,
            Angular_velocity = Phase132StandardMessagesCommon.CreateVector3(_angularVelocityRadPerSecond),
            Angular_velocity_covariance = angularCovariance,
            Linear_acceleration = Phase132StandardMessagesCommon.CreateVector3(_linearAccelerationMetersPerSecondSquared),
            Linear_acceleration_covariance = linearCovariance
        };
    }
#endif
}
