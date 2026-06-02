// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Virtual LiDAR PointCloud2 Digital Twin sample
// Purpose: Maps SDK packed point-cloud frames to sensor_msgs/msg/PointCloud2.
// Colocated from RViz2 PointCloud2 Acceptance sample (Phase 129).
// Modified for Phase 138C: explicit frame_id parameter instead of reading from frame.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

/// <summary>Builds ROS2 PointCloud2 messages from Unity2Foxglove packed layouts.</summary>
public static class Phase138CPointCloud2MessageBuilder
{
    private const byte PointFieldInt8 = 1;
    private const byte PointFieldUint8 = 2;
    private const byte PointFieldInt16 = 3;
    private const byte PointFieldUint16 = 4;
    private const byte PointFieldInt32 = 5;
    private const byte PointFieldUint32 = 6;
    private const byte PointFieldFloat32 = 7;
    private const byte PointFieldFloat64 = 8;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    /// <summary>Build a PointCloud2 message from a prepared native PointCloud2 frame.</summary>
    public static sensor_msgs.msg.PointCloud2 Build(PointCloud2NativeFrame frame, bool copyDataBeforePublish)
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
            Data = copyDataBeforePublish ? (byte[])frame.Data.Clone() : frame.Data,
            Is_dense = frame.IsDense
        };
    }

    /// <summary>Build a PointCloud2 message from a managed frame fallback.</summary>
    public static sensor_msgs.msg.PointCloud2 Build(PointCloudFrame frame, string frameId, int sec, uint nanosec)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        if (frame.Points.Count == 0)
            throw new InvalidOperationException("PointCloud2 smoke frame must contain at least one point.");

        var packed = PointCloudPackedDataBuilder.Build(frame);
        var width = checked((uint)frame.Points.Count);
        var pointStep = packed.PointStride;
        var rowStep = checked(pointStep * width);
        if (packed.Data.Length != rowStep)
            throw new InvalidOperationException("PointCloud2 packed data length does not match row_step for height = 1.");

        return new sensor_msgs.msg.PointCloud2
        {
            Header = CreateHeader(frameId ?? string.Empty, sec, nanosec),
            Height = 1u,
            Width = width,
            Fields = CreateFields(packed.Fields),
            Is_bigendian = false,
            Point_step = pointStep,
            Row_step = rowStep,
            Data = packed.Data,
            Is_dense = true
        };
    }

    private static sensor_msgs.msg.PointField[] CreateFields(System.Collections.Generic.IReadOnlyList<PointCloudPackedField> packedFields)
    {
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

        return fields;
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
#endif
}
