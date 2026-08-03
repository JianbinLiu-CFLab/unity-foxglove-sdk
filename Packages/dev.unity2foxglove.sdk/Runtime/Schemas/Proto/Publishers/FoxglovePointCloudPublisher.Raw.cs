// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Raw foxglove.PointCloud payload publishing path.

using System;
using System.Threading;
using Foxglove.Schemas;
using Google.Protobuf;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxglovePointCloudPublisher
    {

        private void PublishRawFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishProvider))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishProvider = ShouldPrepareOrdinaryTransportPayload();
            }
            Foxglove.PointCloud protobufMessage = null;
            PointCloudBuildResult sharedBuild = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                protobufMessage = packedLayout == null
                    ? PointCloudMessageBuilder.CreateProtobuf(frame)
                    : PointCloudMessageBuilder.CreateProtobuf(frame, packedLayout);
                PublishProto(protobufMessage.ToByteArray(), unixNs);
            }
            else if (publishWebSocket)
            {
                if (publishProvider)
                {
                    sharedBuild = packedLayout == null
                        ? PointCloudMessageBuilder.Build(frame)
                        : PointCloudMessageBuilder.Build(frame, packedLayout);
                    Publish(sharedBuild.Json, unixNs);
                }
                else
                {
                    Publish(packedLayout == null
                        ? PointCloudMessageBuilder.CreateJson(frame)
                        : PointCloudMessageBuilder.CreateJson(frame, packedLayout), unixNs);
                }
            }

            if (publishProvider)
            {
                protobufMessage ??= sharedBuild?.Protobuf
                    ?? (packedLayout == null
                        ? PointCloudMessageBuilder.CreateProtobuf(frame)
                        : PointCloudMessageBuilder.CreateProtobuf(frame, packedLayout));
                PublishOrdinaryTransport(
                    protobufMessage,
                    Foxglove.PointCloud.Descriptor.FullName,
                    unixNs);
            }
        }
    }
}
