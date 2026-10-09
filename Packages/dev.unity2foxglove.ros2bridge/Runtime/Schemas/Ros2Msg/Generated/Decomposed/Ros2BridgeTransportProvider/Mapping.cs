// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Manager-local FoxRun transport Provider for the ROS 2 sidecar.

using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity2Foxglove.Ros2Bridge
{

#if UNITY_5_3_OR_NEWER
    public sealed partial class Ros2BridgeTransportProvider
    {
        private static bool TryMapOrdinary(
            in FoxRunOrdinaryPayloadRequest request,
            out FoxRunOrdinaryPayloadContribution contribution,
            out string reason)
        {
            string schemaName;
            byte[] payload;
            if (request.Value is SensorCompressedImageFrame compressedImage)
            {
                if (!MatchesLogicalType<SensorCompressedImageFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(SensorCompressedImageFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorCompressedImage;
                payload = Ros2CdrSensorCompressedImageBuilder.Serialize(
                    compressedImage.UnixNs,
                    compressedImage.FrameId,
                    compressedImage.Data,
                    compressedImage.Format);
            }
            else if (request.Value is SensorCameraInfoFrame cameraInfo)
            {
                if (!MatchesLogicalType<SensorCameraInfoFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(SensorCameraInfoFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorCameraInfo;
                payload = Ros2CdrSensorCameraInfoBuilder.Serialize(
                    cameraInfo.UnixNs,
                    cameraInfo.FrameId,
                    cameraInfo.Width,
                    cameraInfo.Height,
                    cameraInfo.DistortionModel,
                    cameraInfo.D,
                    cameraInfo.K,
                    cameraInfo.R,
                    cameraInfo.P);
            }
            else if (request.Value is PackedPointCloudFrame pointCloud2)
            {
                if (!MatchesLogicalType<PackedPointCloudFrame>(
                        request.LogicalSchemaName))
                {
                    return RejectLogicalType(
                        request.LogicalSchemaName,
                        typeof(PackedPointCloudFrame),
                        out contribution,
                        out reason);
                }

                schemaName = Ros2PublisherSchemaNames.SensorPointCloud2;
                payload = Ros2CdrSensorPointCloud2Builder.Serialize(
                    pointCloud2.UnixNs,
                    pointCloud2.FrameId,
                    pointCloud2.Height,
                    pointCloud2.Width,
                    pointCloud2.Fields,
                    pointCloud2.PointStep,
                    pointCloud2.Data,
                    pointCloud2.IsDense);
            }
            else if (request.Value is IMessage message)
            {
                var descriptorName = message.Descriptor?.FullName ?? string.Empty;
                if (!Ros2CdrSerializerRegistry.TryGetByClrType(
                        message.GetType(),
                        out var serializer))
                {
                    contribution = default;
                    reason = "ROS2 Bridge has no ordinary mapping for protobuf schema '"
                             + descriptorName
                             + "'.";
                    return false;
                }

                if (!Ros2BridgeOrdinaryLogicalSchema.Matches(
                        request.LogicalSchemaName,
                        message))
                {
                    contribution = default;
                    reason = "Logical schema '"
                             + request.LogicalSchemaName
                             + "' does not match protobuf CLR type '"
                             + serializer.ClrType.FullName
                             + "' or descriptor '"
                             + descriptorName
                             + "'.";
                    return false;
                }

                schemaName = serializer.SchemaName;
                payload = serializer.Serialize(message);
            }
            else
            {
                contribution = default;
                reason =
                    "ROS2 Bridge ordinary mapping accepts only supported neutral sensor DTOs or Foxglove protobuf values.";
                return false;
            }

            Ros2CdrPayloadValidator.Validate(payload);
            contribution = new FoxRunOrdinaryPayloadContribution(
                schemaName,
                payload,
                Ros2BridgeMcapCodecs.MessageEncoding,
                Ros2BridgeMcapCodecs.SchemaEncoding);
            reason = string.Empty;
            return true;
        }

        private static bool MatchesLogicalType<T>(string logicalSchemaName)
            => string.Equals(
                logicalSchemaName,
                typeof(T).FullName,
                StringComparison.Ordinal);

        private static bool RejectLogicalType(
            string logicalSchemaName,
            Type expectedType,
            out FoxRunOrdinaryPayloadContribution contribution,
            out string reason)
        {
            contribution = default;
            reason = "Logical schema '"
                     + logicalSchemaName
                     + "' does not match value type '"
                     + expectedType.FullName
                     + "'.";
            return false;
        }

    }
#endif
}
