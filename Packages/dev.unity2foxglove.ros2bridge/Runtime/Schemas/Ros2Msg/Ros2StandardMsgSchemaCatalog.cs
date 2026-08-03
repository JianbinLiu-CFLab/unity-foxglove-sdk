// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg
// Purpose: Small standard ROS 2 .msg catalog used by productized non-Foxglove CDR publishers.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;

namespace Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg
{
    /// <summary>
    /// Additional standard ROS 2 message schemas required by SDK publishers.
    /// These entries intentionally do not change the generated Foxglove schema
    /// snapshot count tracked by <see cref="FoxgloveRos2MsgSchemaCatalog"/>.
    /// </summary>
    internal static class Ros2StandardMsgSchemaCatalog
    {
        private const string PointCloud2Content =
@"# sensor_msgs/msg/PointCloud2
std_msgs/Header header
uint32 height
uint32 width
sensor_msgs/PointField[] fields
bool is_bigendian
uint32 point_step
uint32 row_step
uint8[] data
bool is_dense
================================================================================
MSG: std_msgs/Header
builtin_interfaces/Time stamp
string frame_id
================================================================================
MSG: builtin_interfaces/Time
int32 sec
uint32 nanosec
================================================================================
MSG: sensor_msgs/PointField
uint8 INT8=1
uint8 UINT8=2
uint8 INT16=3
uint8 UINT16=4
uint8 INT32=5
uint8 UINT32=6
uint8 FLOAT32=7
uint8 FLOAT64=8
string name
uint32 offset
uint8 datatype
uint32 count
";

        private const string CompressedImageContent =
@"# sensor_msgs/msg/CompressedImage
std_msgs/Header header
string format
uint8[] data
================================================================================
MSG: std_msgs/Header
builtin_interfaces/Time stamp
string frame_id
================================================================================
MSG: builtin_interfaces/Time
int32 sec
uint32 nanosec
";

        private const string CameraInfoContent =
@"# sensor_msgs/msg/CameraInfo
std_msgs/Header header
uint32 height
uint32 width
string distortion_model
float64[] d
float64[9] k
float64[9] r
float64[12] p
uint32 binning_x
uint32 binning_y
sensor_msgs/RegionOfInterest roi
================================================================================
MSG: std_msgs/Header
builtin_interfaces/Time stamp
string frame_id
================================================================================
MSG: builtin_interfaces/Time
int32 sec
uint32 nanosec
================================================================================
MSG: sensor_msgs/RegionOfInterest
uint32 x_offset
uint32 y_offset
uint32 height
uint32 width
bool do_rectify
";

        private static readonly FoxgloveRos2MsgSchemaCatalogEntry[] EntriesArray =
        {
            new FoxgloveRos2MsgSchemaCatalogEntry(
                "sensor_msgs/msg/PointCloud2",
                PointCloud2Content,
                "sensor_msgs/msg/PointCloud2.msg",
                "8084aa09f50d87844883185be4b8cd92e5483d7b20a1959312f3e67477add37d",
                "point cloud",
                hasDedicatedJsonOrProtobufPublisher: false),
            new FoxgloveRos2MsgSchemaCatalogEntry(
                "sensor_msgs/msg/CompressedImage",
                CompressedImageContent,
                "sensor_msgs/msg/CompressedImage.msg",
                "4f54ea047c8a4e6fb5c3a91672ecedfb2f2e1b910fbcf0b22e930fb771ebd92e",
                "camera",
                hasDedicatedJsonOrProtobufPublisher: false),
            new FoxgloveRos2MsgSchemaCatalogEntry(
                "sensor_msgs/msg/CameraInfo",
                CameraInfoContent,
                "sensor_msgs/msg/CameraInfo.msg",
                "5c9ee9c4c843473686361c20b226c8f5f3e844ca1a9ccf32e1c8aa0d637c0892",
                "camera",
                hasDedicatedJsonOrProtobufPublisher: false)
        };

        private static readonly Dictionary<string, FoxgloveRos2MsgSchemaCatalogEntry> BySchemaName = BuildSchemaNameMap();

        /// <summary>Read-only list of bundled standard ROS 2 schemas registered alongside generated Foxglove schemas.</summary>
        public static IReadOnlyList<FoxgloveRos2MsgSchemaCatalogEntry> Entries { get; } = Array.AsReadOnly(EntriesArray);

        /// <summary>Number of bundled standard ROS 2 schemas registered by this catalog.</summary>
        public static int EntryCount => EntriesArray.Length;

        /// <summary>
        /// Resolves one bundled standard ROS 2 schema entry by schema name.
        /// </summary>
        /// <param name="schemaName">ROS 2 schema name to resolve.</param>
        /// <param name="entry">Resolved schema entry, or null when not found.</param>
        /// <returns><see langword="true"/> when the lookup succeeds; otherwise <see langword="false"/>.</returns>
        public static bool TryGet(string schemaName, out FoxgloveRos2MsgSchemaCatalogEntry entry)
        {
            if (schemaName == null)
            {
                entry = null;
                return false;
            }

            return BySchemaName.TryGetValue(schemaName, out entry);
        }

        /// <summary>
        /// Registers all bundled standard ROS 2 schemas with the provided registry.
        /// </summary>
        /// <param name="registry">Target schema registry.</param>
        public static void RegisterSchemas(ISchemaRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            foreach (var entry in EntriesArray)
            {
                registry.Register(new SchemaEntry
                {
                    Name = entry.SchemaName,
                    Encoding = entry.SchemaEncoding,
                    Content = entry.Content,
                    RawContent = null
                });
            }
        }

        private static Dictionary<string, FoxgloveRos2MsgSchemaCatalogEntry> BuildSchemaNameMap()
        {
            var map = new Dictionary<string, FoxgloveRos2MsgSchemaCatalogEntry>(StringComparer.Ordinal);
            foreach (var entry in EntriesArray)
                map.Add(entry.SchemaName, entry);
            return map;
        }
    }
}
