// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Point-cloud output mode and profile metadata for point-cloud publishers.

using System;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// User-facing point-cloud output modes supported by <see cref="FoxglovePointCloudPublisher"/>.
    /// </summary>
    public enum PointCloudOutputMode
    {
        /// <summary>Uncompressed foxglove.PointCloud output mode.</summary>
        Raw = 0,
        /// <summary>Compressed foxglove.CompressedPointCloud output mode.</summary>
        Draco = 1,
        /// <summary>Standard sensor_msgs/msg/PointCloud2 output mode for ROS2 consumers.</summary>
        PointCloud2Native = 2
    }

    /// <summary>
    /// Resolved point-cloud output settings for schemas, topics, and encoding support.
    /// </summary>
    public readonly struct PointCloudOutputProfile
    {
        /// <summary>
        /// Creates a resolved point-cloud profile for the selected output mode.
        /// </summary>
        internal PointCloudOutputProfile(
            PointCloudOutputMode mode,
            string displayName,
            string defaultTopic,
            string schemaName,
            string ros2SchemaName,
            bool supportsJson,
            bool supportsProtobuf)
        {
            Mode = mode;
            DisplayName = displayName ?? "";
            DefaultTopic = defaultTopic ?? "";
            SchemaName = schemaName ?? "";
            Ros2SchemaName = ros2SchemaName ?? "";
            SupportsJson = supportsJson;
            SupportsProtobuf = supportsProtobuf;
        }

        /// <summary>Point-cloud output mode represented by this profile.</summary>
        public PointCloudOutputMode Mode { get; }
        /// <summary>Inspector label shown for this profile.</summary>
        public string DisplayName { get; }
        /// <summary>Topic used when the publisher topic is left empty.</summary>
        public string DefaultTopic { get; }
        /// <summary>Schema advertised for the selected profile.</summary>
        public string SchemaName { get; }
        /// <summary>ROS2 schema advertised for the selected profile.</summary>
        public string Ros2SchemaName { get; }
        /// <summary>Whether JSON publishing is supported for the selected profile.</summary>
        public bool SupportsJson { get; }
        /// <summary>Whether protobuf publishing is supported for the selected profile.</summary>
        public bool SupportsProtobuf { get; }

        /// <summary>
        /// Returns the resolved profile definition for a given output mode.
        /// </summary>
        public static PointCloudOutputProfile ForMode(PointCloudOutputMode mode)
        {
            switch (mode)
            {
                case PointCloudOutputMode.Draco:
                    return new PointCloudOutputProfile(
                        mode,
                        "Draco",
                        PointCloudOutputModeDefaults.DracoTopic,
                        PointCloudOutputModeDefaults.DracoSchema,
                        Ros2PublisherSchemaNames.CompressedPointCloud,
                        supportsJson: false,
                        supportsProtobuf: true);

                case PointCloudOutputMode.PointCloud2Native:
                    return new PointCloudOutputProfile(
                        mode,
                        "PointCloud2 Native",
                        PointCloudOutputModeDefaults.PointCloud2NativeTopic,
                        PointCloudOutputModeDefaults.PointCloud2NativeSchema,
                        Ros2PublisherSchemaNames.SensorPointCloud2,
                        supportsJson: false,
                        supportsProtobuf: false);

                case PointCloudOutputMode.Raw:
                    return new PointCloudOutputProfile(
                        PointCloudOutputMode.Raw,
                        "Raw",
                        PointCloudOutputModeDefaults.RawTopic,
                        PointCloudOutputModeDefaults.RawSchema,
                        Ros2PublisherSchemaNames.PointCloud,
                        supportsJson: true,
                        supportsProtobuf: true);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown point-cloud output mode.");
            }
        }
    }

    /// <summary>
    /// Point-cloud output mode constants shared by runtime and Inspector code.
    /// </summary>
    public static class PointCloudOutputModeDefaults
    {
        /// <summary>Default topic for raw point-cloud output.</summary>
        public const string RawTopic = "/unity/point_cloud";
        /// <summary>Default topic for Draco-compressed point-cloud output.</summary>
        public const string DracoTopic = "/unity/point_cloud_draco";
        /// <summary>Default topic for standard ROS2 PointCloud2 output.</summary>
        public const string PointCloud2NativeTopic = "/unity/point_cloud2";
        /// <summary>Schema name for raw foxglove.PointCloud output.</summary>
        public const string RawSchema = "foxglove.PointCloud";
        /// <summary>Schema name for Draco-compressed foxglove.CompressedPointCloud output.</summary>
        public const string DracoSchema = "foxglove.CompressedPointCloud";
        /// <summary>Schema name for standard ROS2 PointCloud2 output.</summary>
        public const string PointCloud2NativeSchema = "sensor_msgs/msg/PointCloud2";
    }
}
