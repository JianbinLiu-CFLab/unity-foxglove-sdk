// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free builder for foxglove.CompressedPointCloud Draco payloads.

using System;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Builds <c>foxglove.CompressedPointCloud</c> protobuf payloads from a
    /// point-cloud frame and externally encoded Draco bytes.
    /// </summary>
    public static class CompressedPointCloudMessageBuilder
    {
        /// <summary>Foxglove-supported compressed point-cloud format value.</summary>
        public const string DracoFormat = "draco";

        /// <summary>Create an official protobuf CompressedPointCloud message.</summary>
        public static Foxglove.CompressedPointCloud CreateProtobuf(PointCloudFrame frame, byte[] dracoPayload)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (dracoPayload == null || dracoPayload.Length == 0)
                throw new ArgumentException("Draco payload must be non-empty.", nameof(dracoPayload));

            return new Foxglove.CompressedPointCloud
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(frame.UnixNs),
                FrameId = frame.FrameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.ProtoIdentityPose(),
                Data = ByteString.CopyFrom(dracoPayload),
                Format = DracoFormat
            };
        }

        /// <summary>Create and serialize an official protobuf CompressedPointCloud payload.</summary>
        public static byte[] SerializeProtobuf(PointCloudFrame frame, byte[] dracoPayload)
        {
            return CreateProtobuf(frame, dracoPayload).ToByteArray();
        }
    }
}
