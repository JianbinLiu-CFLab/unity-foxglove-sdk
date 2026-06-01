// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg
// Purpose: Small standard ROS 2 .msg catalog used by productized non-Foxglove CDR publishers.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
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

        private static readonly FoxgloveRos2MsgSchemaCatalogEntry[] EntriesArray =
        {
            new FoxgloveRos2MsgSchemaCatalogEntry(
                "sensor_msgs/msg/PointCloud2",
                PointCloud2Content,
                "sensor_msgs/msg/PointCloud2.msg",
                "8084aa09f50d87844883185be4b8cd92e5483d7b20a1959312f3e67477add37d",
                "point cloud",
                hasDedicatedJsonOrProtobufPublisher: false)
        };

        private static readonly Dictionary<string, FoxgloveRos2MsgSchemaCatalogEntry> BySchemaName = BuildSchemaNameMap();

        public static bool TryGet(string schemaName, out FoxgloveRos2MsgSchemaCatalogEntry entry)
        {
            if (schemaName == null)
            {
                entry = null;
                return false;
            }

            return BySchemaName.TryGetValue(schemaName, out entry);
        }

        public static void RegisterSchemas(ISchemaRegistry registry)
        {
            if (registry == null)
                return;

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
