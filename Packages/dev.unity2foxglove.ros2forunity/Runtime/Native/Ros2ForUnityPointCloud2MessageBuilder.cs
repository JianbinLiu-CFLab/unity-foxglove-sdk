// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Maps SDK-prepared PointCloud2 native frames to generated ROS2 messages.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityPointCloud2MessageBuilder
    {
        private const byte PointFieldInt8 = 1;
        private const byte PointFieldUint8 = 2;
        private const byte PointFieldInt16 = 3;
        private const byte PointFieldUint16 = 4;
        private const byte PointFieldInt32 = 5;
        private const byte PointFieldUint32 = 6;
        private const byte PointFieldFloat32 = 7;
        private const byte PointFieldFloat64 = 8;

        private static sensor_msgs.msg.PointField[] s_cachedFields;

        public static sensor_msgs.msg.PointCloud2 Build(PointCloud2NativeFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            return new sensor_msgs.msg.PointCloud2
            {
                Header = CreateHeader(
                    frame.FrameId,
                    (int)(frame.UnixNs / 1_000_000_000UL),
                    (uint)(frame.UnixNs % 1_000_000_000UL)),
                Height = frame.Height,
                Width = frame.Width,
                Fields = CreateFields(frame.Fields),
                Is_bigendian = false,
                Point_step = frame.PointStep,
                Row_step = frame.RowStep,
                Data = frame.Data,
                Is_dense = frame.IsDense
            };
        }

        private static sensor_msgs.msg.PointField[] CreateFields(
            System.Collections.Generic.IReadOnlyList<PointCloudPackedField> packedFields)
        {
            if (FieldsMatch(s_cachedFields, packedFields))
                return s_cachedFields;

            var fields = new sensor_msgs.msg.PointField[packedFields.Count];
            for (var i = 0; i < packedFields.Count; i++)
            {
                var field = packedFields[i];
                fields[i] = new sensor_msgs.msg.PointField
                {
                    Name = field.Name,
                    Offset = field.Offset,
                    Datatype = MapDatatype(field.Type),
                    Count = 1u
                };
            }

            s_cachedFields = fields;
            return fields;
        }

        private static bool FieldsMatch(
            sensor_msgs.msg.PointField[] cachedFields,
            System.Collections.Generic.IReadOnlyList<PointCloudPackedField> packedFields)
        {
            if (cachedFields == null || cachedFields.Length != packedFields.Count)
                return false;

            for (var i = 0; i < cachedFields.Length; i++)
            {
                var cached = cachedFields[i];
                var field = packedFields[i];
                if (!string.Equals(cached.Name, field.Name, StringComparison.Ordinal)
                    || cached.Offset != field.Offset
                    || cached.Datatype != MapDatatype(field.Type)
                    || cached.Count != 1u)
                {
                    return false;
                }
            }

            return true;
        }

        private static byte MapDatatype(PointCloudPackedNumericType type)
        {
            switch (type)
            {
                case PointCloudPackedNumericType.Int8:
                    return PointFieldInt8;
                case PointCloudPackedNumericType.Uint8:
                    return PointFieldUint8;
                case PointCloudPackedNumericType.Int16:
                    return PointFieldInt16;
                case PointCloudPackedNumericType.Uint16:
                    return PointFieldUint16;
                case PointCloudPackedNumericType.Int32:
                    return PointFieldInt32;
                case PointCloudPackedNumericType.Uint32:
                    return PointFieldUint32;
                case PointCloudPackedNumericType.Float32:
                    return PointFieldFloat32;
                case PointCloudPackedNumericType.Float64:
                    return PointFieldFloat64;
                default:
                    throw new NotSupportedException("Unsupported PointCloud packed numeric type: " + type);
            }
        }

        private static std_msgs.msg.Header CreateHeader(string frameId, int sec, uint nanosec)
        {
            return new std_msgs.msg.Header
            {
                Stamp = new builtin_interfaces.msg.Time
                {
                    Sec = sec,
                    Nanosec = nanosec
                },
                Frame_id = frameId
            };
        }
    }
}
#endif
