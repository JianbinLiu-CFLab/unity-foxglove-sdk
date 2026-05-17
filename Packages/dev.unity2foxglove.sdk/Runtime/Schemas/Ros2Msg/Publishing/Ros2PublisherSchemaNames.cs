// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Publishing
// Purpose: User-facing publisher mapping to official Foxglove ROS 2 .msg schema names.

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>
    /// ROS 2 .msg schema names used by productized Unity publishers.
    /// </summary>
    public static class Ros2PublisherSchemaNames
    {
        public const string FrameTransform = "foxglove_msgs/msg/FrameTransform";
        public const string SceneUpdate = "foxglove_msgs/msg/SceneUpdate";
        public const string CompressedImage = "foxglove_msgs/msg/CompressedImage";
        public const string CameraCalibration = "foxglove_msgs/msg/CameraCalibration";
        public const string LaserScan = "foxglove_msgs/msg/LaserScan";
        public const string PointCloud = "foxglove_msgs/msg/PointCloud";
        public const string CompressedPointCloud = "foxglove_msgs/msg/CompressedPointCloud";
    }
}
