// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Unity-free builders for foxglove.PointCloud JSON and protobuf payloads.

using System;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>Built PointCloud payloads sharing the same packed bytes.</summary>
    public sealed class PointCloudBuildResult
    {
        /// <summary>
        /// Creates paired JSON/protobuf payloads that share the same packed point
        /// byte buffer.
        /// </summary>
        public PointCloudBuildResult(PointCloudMessage json, Foxglove.PointCloud protobuf, byte[] data)
        {
            Json = json;
            Protobuf = protobuf;
            Data = data;
        }

        /// <summary>JSON DTO with base64 data.</summary>
        public PointCloudMessage Json { get; }
        /// <summary>Official protobuf message.</summary>
        public Foxglove.PointCloud Protobuf { get; }
        /// <summary>Packed point bytes.</summary>
        public byte[] Data { get; }
    }

    /// <summary>Builds <c>foxglove.PointCloud</c> JSON/protobuf payloads from typed points.</summary>
    public static class PointCloudMessageBuilder
    {
        /// <summary>Maximum packed point-cloud byte buffer built in one call.</summary>
        public const int MaxPackedDataBytes = 64 * 1024 * 1024;

        /// <summary>Create JSON, protobuf, and packed byte forms for a point cloud frame.</summary>
        public static PointCloudBuildResult Build(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var layout = PointCloudLayout.From(frame);
            var data = Pack(frame, layout);
            var json = CreateJson(frame, layout, data);
            var proto = CreateProtobuf(frame, layout, data);
            return new PointCloudBuildResult(json, proto, data);
        }

        /// <summary>Create a JSON PointCloud DTO.</summary>
        public static PointCloudMessage CreateJson(PointCloudFrame frame)
        {
            return Build(frame).Json;
        }

        /// <summary>Create an official protobuf PointCloud message.</summary>
        public static Foxglove.PointCloud CreateProtobuf(PointCloudFrame frame)
        {
            return Build(frame).Protobuf;
        }

        /// <summary>Create and serialize an official protobuf PointCloud payload.</summary>
        public static byte[] SerializeProtobuf(PointCloudFrame frame)
        {
            return CreateProtobuf(frame).ToByteArray();
        }

        private static PointCloudMessage CreateJson(PointCloudFrame frame, PointCloudLayout layout, byte[] data)
        {
            var message = new PointCloudMessage
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToJsonTime(frame.UnixNs),
                FrameId = frame.FrameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.JsonIdentityPose(),
                PointStride = layout.Stride,
                Data = Convert.ToBase64String(data)
            };

            foreach (var field in layout.Fields)
            {
                message.Fields.Add(new PackedElementFieldMessage
                {
                    Name = field.Name,
                    Offset = field.Offset,
                    Type = (int)field.Type
                });
            }

            return message;
        }

        private static Foxglove.PointCloud CreateProtobuf(PointCloudFrame frame, PointCloudLayout layout, byte[] data)
        {
            var message = new Foxglove.PointCloud
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(frame.UnixNs),
                FrameId = frame.FrameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.ProtoIdentityPose(),
                PointStride = layout.Stride,
                Data = ByteString.CopyFrom(data)
            };

            foreach (var field in layout.Fields)
            {
                message.Fields.Add(new Foxglove.PackedElementField
                {
                    Name = field.Name,
                    Offset = field.Offset,
                    Type = field.Type
                });
            }

            return message;
        }

        private static byte[] Pack(PointCloudFrame frame, PointCloudLayout layout)
        {
            var capacity = ValidatePackedDataBudget(frame, layout);
            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var point in frame.Points)
                {
                    writer.Write(point.X);
                    writer.Write(point.Y);
                    writer.Write(point.Z);

                    if (layout.HasIntensity) writer.Write(point.Intensity ?? 0f);
                    if (layout.HasReflectivity) writer.Write(point.Reflectivity ?? 0f);
                    if (layout.HasRing) writer.Write(point.Ring ?? (ushort)0);
                    if (layout.HasTimeOffset) writer.Write(point.TimeOffsetSeconds ?? 0f);
                }

                return stream.ToArray();
            }
        }

        private static int ValidatePackedDataBudget(PointCloudFrame frame, PointCloudLayout layout)
        {
            var packedBytes = checked((long)frame.Points.Count * layout.Stride);
            if (packedBytes > MaxPackedDataBytes)
            {
                throw new InvalidOperationException(
                    $"PointCloud packed data exceeds {MaxPackedDataBytes} bytes ({packedBytes} requested).");
            }

            return (int)packedBytes;
        }

        private sealed class PointCloudLayout
        {
            public bool HasIntensity { get; private set; }
            public bool HasReflectivity { get; private set; }
            public bool HasRing { get; private set; }
            public bool HasTimeOffset { get; private set; }
            public uint Stride { get; private set; }
            public PackedField[] Fields { get; private set; }

            public static PointCloudLayout From(PointCloudFrame frame)
            {
                var layout = new PointCloudLayout();
                foreach (var point in frame.Points)
                {
                    layout.HasIntensity |= point.Intensity.HasValue;
                    layout.HasReflectivity |= point.Reflectivity.HasValue;
                    layout.HasRing |= point.Ring.HasValue;
                    layout.HasTimeOffset |= point.TimeOffsetSeconds.HasValue;
                }

                var fields = new System.Collections.Generic.List<PackedField>
                {
                    Field("x", 0, Foxglove.PackedElementField.Types.NumericType.Float32),
                    Field("y", 4, Foxglove.PackedElementField.Types.NumericType.Float32),
                    Field("z", 8, Foxglove.PackedElementField.Types.NumericType.Float32)
                };

                uint offset = 12;
                if (layout.HasIntensity) AddField(fields, "intensity", Foxglove.PackedElementField.Types.NumericType.Float32, ref offset, 4);
                if (layout.HasReflectivity) AddField(fields, "reflectivity", Foxglove.PackedElementField.Types.NumericType.Float32, ref offset, 4);
                if (layout.HasRing) AddField(fields, "ring", Foxglove.PackedElementField.Types.NumericType.Uint16, ref offset, 2);
                if (layout.HasTimeOffset) AddField(fields, "time_offset", Foxglove.PackedElementField.Types.NumericType.Float32, ref offset, 4);

                layout.Stride = offset;
                layout.Fields = fields.ToArray();
                return layout;
            }

            private static void AddField(System.Collections.Generic.List<PackedField> fields, string name, Foxglove.PackedElementField.Types.NumericType type, ref uint offset, uint width)
            {
                fields.Add(Field(name, offset, type));
                offset += width;
            }

            private static PackedField Field(string name, uint offset, Foxglove.PackedElementField.Types.NumericType type)
            {
                return new PackedField(name, offset, type);
            }
        }

        private sealed class PackedField
        {
            public PackedField(string name, uint offset, Foxglove.PackedElementField.Types.NumericType type)
            {
                Name = name;
                Offset = offset;
                Type = type;
            }

            public string Name { get; }
            public uint Offset { get; }
            public Foxglove.PackedElementField.Types.NumericType Type { get; }
        }
    }
}
