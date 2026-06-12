// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Raw foxglove.PointCloud payload publishing path.

using System;
using System.Threading;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
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
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishBridge))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishBridge = ShouldPrepareRos2BridgePayload();
            }
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(packedLayout == null
                    ? PointCloudMessageBuilder.SerializeProtobuf(frame)
                    : PointCloudMessageBuilder.SerializeProtobuf(frame, packedLayout), unixNs);
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = packedLayout == null
                    ? Ros2CdrPointCloudBuilder.Serialize(frame)
                    : Ros2CdrPointCloudBuilder.Serialize(frame, packedLayout);
                PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                Publish(packedLayout == null
                    ? PointCloudMessageBuilder.CreateJson(frame)
                    : PointCloudMessageBuilder.CreateJson(frame, packedLayout), unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= packedLayout == null
                    ? Ros2CdrPointCloudBuilder.Serialize(frame)
                    : Ros2CdrPointCloudBuilder.Serialize(frame, packedLayout);
                PublishRos2Bridge(ros2Payload, unixNs);
            }
        }
    }
}
