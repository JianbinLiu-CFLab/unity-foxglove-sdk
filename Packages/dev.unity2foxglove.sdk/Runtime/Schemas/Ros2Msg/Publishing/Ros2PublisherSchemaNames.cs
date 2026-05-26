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
        /// <summary>Official foxglove_msgs schema name for transform publisher output.</summary>
        public const string FrameTransform = Ros2CdrFrameTransformBuilder.SchemaName;
        public const string SceneUpdate = Ros2CdrSceneUpdateBuilder.SchemaName;
        public const string CompressedImage = Ros2CdrCompressedImageBuilder.SchemaName;
        public const string CameraCalibration = Ros2CdrCameraCalibrationBuilder.SchemaName;
        public const string LaserScan = Ros2CdrLaserScanBuilder.SchemaName;
        public const string PointCloud = Ros2CdrPointCloudBuilder.SchemaName;
        public const string CompressedPointCloud = Ros2CdrCompressedPointCloudBuilder.SchemaName;
    }
}
