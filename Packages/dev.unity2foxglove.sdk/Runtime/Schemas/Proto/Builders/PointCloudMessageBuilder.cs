// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free builders for foxglove.PointCloud JSON and protobuf payloads.

using System;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

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
        public const int MaxPackedDataBytes = PointCloudPackedDataBuilder.MaxPackedDataBytes;

        /// <summary>Create JSON, protobuf, and packed byte forms for a point cloud frame.</summary>
        public static PointCloudBuildResult Build(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var packed = PointCloudPackedDataBuilder.Build(frame);
            var json = CreateJson(frame, packed);
            var proto = CreateProtobuf(frame, packed);
            return new PointCloudBuildResult(json, proto, packed.Data);
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

        private static PointCloudMessage CreateJson(PointCloudFrame frame, PointCloudPackedData packed)
        {
            var message = new PointCloudMessage
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToJsonTime(frame.UnixNs),
                FrameId = frame.FrameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.JsonIdentityPose(),
                PointStride = packed.PointStride,
                Data = Convert.ToBase64String(packed.Data)
            };

            foreach (var field in packed.Fields)
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

        private static Foxglove.PointCloud CreateProtobuf(PointCloudFrame frame, PointCloudPackedData packed)
        {
            var message = new Foxglove.PointCloud
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(frame.UnixNs),
                FrameId = frame.FrameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.ProtoIdentityPose(),
                PointStride = packed.PointStride,
                Data = ByteString.CopyFrom(packed.Data)
            };

            foreach (var field in packed.Fields)
            {
                message.Fields.Add(new Foxglove.PackedElementField
                {
                    Name = field.Name,
                    Offset = field.Offset,
                    Type = (Foxglove.PackedElementField.Types.NumericType)(int)field.Type
                });
            }

            return message;
        }
    }
}
