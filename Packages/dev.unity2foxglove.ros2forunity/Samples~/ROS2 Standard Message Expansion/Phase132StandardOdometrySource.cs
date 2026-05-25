// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Owns synthetic Odometry sample data for Phase132.

using System;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard Odometry Source")]
public sealed class Phase132StandardOdometrySource : MonoBehaviour
{
    [Header("Odometry")]
    [SerializeField] private string _frameId = "odom";
    [SerializeField] private string _childFrameId = "base_link";
    [SerializeField] private Vector3 _positionMeters = new Vector3(1f, 0f, 0f);
    [SerializeField] private Vector3 _linearVelocityMetersPerSecond = new Vector3(0.2f, 0f, 0f);
    [SerializeField] private Vector3 _angularVelocityRadPerSecond = new Vector3(0f, 0f, 0.05f);

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private string _lastError = string.Empty;

    public string FrameId => Phase132StandardMessagesCommon.CleanFrameId(_frameId, "odom");
    public string ChildFrameId => Phase132StandardMessagesCommon.CleanFrameId(_childFrameId, "base_link");

    public void RecordPublished()
    {
        _publishedCount++;
        _lastError = string.Empty;
        _statusMessage = "Published Odometry=" + _publishedCount + ".";
    }

    public void RecordError(string error)
    {
        _lastError = error;
        _statusMessage = error;
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public nav_msgs.msg.Odometry CreateOdometry(int sec, uint nanosec)
    {
        var poseCovariance = CreateDiagonalCovariance36(0.05d);
        var twistCovariance = CreateDiagonalCovariance36(0.10d);
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(poseCovariance, 36, "Odometry pose covariance double[36]");
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(twistCovariance, 36, "Odometry twist covariance double[36]");

        var pose = new geometry_msgs.msg.PoseWithCovariance
        {
            Pose = Phase132StandardMessagesCommon.CreatePose(_positionMeters)
        };
        Array.Copy(poseCovariance, pose.Covariance, poseCovariance.Length);

        var twist = new geometry_msgs.msg.TwistWithCovariance
        {
            Twist = new geometry_msgs.msg.Twist
            {
                Linear = Phase132StandardMessagesCommon.CreateVector3(_linearVelocityMetersPerSecond),
                Angular = Phase132StandardMessagesCommon.CreateVector3(_angularVelocityRadPerSecond)
            }
        };
        Array.Copy(twistCovariance, twist.Covariance, twistCovariance.Length);

        return new nav_msgs.msg.Odometry
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Child_frame_id = ChildFrameId,
            Pose = pose,
            Twist = twist
        };
    }

    private static double[] CreateDiagonalCovariance36(double diagonalValue)
    {
        var values = new double[36];
        for (var i = 0; i < values.Length; i += 7)
            values[i] = diagonalValue;
        return values;
    }
#endif
}
