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
        /// <summary>ROS2 schema name for transform publisher output.</summary>
        public const string FrameTransform = Ros2CdrFrameTransformBuilder.SchemaName;
        /// <summary>ROS2 schema name for scene update publisher output.</summary>
        public const string SceneUpdate = Ros2CdrSceneUpdateBuilder.SchemaName;
        /// <summary>ROS2 schema name for compressed image publisher output.</summary>
        public const string CompressedImage = Ros2CdrCompressedImageBuilder.SchemaName;
        /// <summary>ROS2 schema name for camera calibration output.</summary>
        public const string CameraCalibration = Ros2CdrCameraCalibrationBuilder.SchemaName;
        /// <summary>ROS2 schema name for laser scan publisher output.</summary>
        public const string LaserScan = Ros2CdrLaserScanBuilder.SchemaName;
        /// <summary>ROS2 schema name for foxglove point-cloud publisher output.</summary>
        public const string PointCloud = Ros2CdrPointCloudBuilder.SchemaName;
        /// <summary>ROS2 schema name for standard PointCloud2 output.</summary>
        public const string SensorPointCloud2 = Ros2CdrSensorPointCloud2Builder.SchemaName;
        /// <summary>ROS2 schema name for compressed point-cloud output.</summary>
        public const string CompressedPointCloud = Ros2CdrCompressedPointCloudBuilder.SchemaName;
    }
}
