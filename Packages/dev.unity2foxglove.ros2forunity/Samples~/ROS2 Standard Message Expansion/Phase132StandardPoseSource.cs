// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Owns synthetic PoseStamped sample data for Phase132.

using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard Pose Source")]
public sealed class Phase132StandardPoseSource : MonoBehaviour
{
    [Header("Pose")]
    [SerializeField] private string _frameId = "map";
    [SerializeField] private Vector3 _positionMeters = new Vector3(1f, 2f, 0.25f);

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private string _lastError = string.Empty;

    public string FrameId => Phase132StandardMessagesCommon.CleanFrameId(_frameId, "map");

    public void RecordPublished()
    {
        _publishedCount++;
        _lastError = string.Empty;
        _statusMessage = "Published PoseStamped=" + _publishedCount + ".";
    }

    public void RecordError(string error)
    {
        _lastError = error;
        _statusMessage = error;
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public geometry_msgs.msg.PoseStamped CreatePoseStamped(int sec, uint nanosec)
    {
        return new geometry_msgs.msg.PoseStamped
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Pose = Phase132StandardMessagesCommon.CreatePose(_positionMeters)
        };
    }
#endif
}
