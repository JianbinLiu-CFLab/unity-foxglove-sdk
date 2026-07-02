// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PointCloud2 motion compensation request and source-frame suppression helpers.

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

        private PointCloudMotionCompensationSettings ResolveMotionCompensationSettings()
        {
            return new PointCloudMotionCompensationSettings(
                _enableMotionCompensation,
                _motionCompensationOutputPolicy,
                _deskewedPointCloud2NativeTopic,
                _motionCompensationReferenceTime,
                _motionCompensationSource);
        }

        private PointCloudMotionCompensationRequest TryCreateMotionCompensationRequest(
            PointCloudMotionCompensationSettings settings,
            bool publishNativeFrame)
        {
            if (!settings.EmitDeskewedOutput || !publishNativeFrame)
                return null;

            if (settings.IsLikelySlamReplacementTopic(PointCloud2NativeTopic))
            {
                WarnMotionCompensation(
                    "ReplaceOutput is publishing deskewed visualization data on a likely SLAM topic; FAST-LIO2/LIVO2 should subscribe to raw output instead.");
            }

            return new PointCloudMotionCompensationRequest(
                settings.ResolveDeskewedTopic(PointCloud2NativeTopic),
                settings.ReferenceTime,
                PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame,
                _motionPoseHistory.Snapshot());
        }

        private void WarnMotionCompensation(string reason)
        {
            if (_motionCompensationWarningCount < int.MaxValue)
                _motionCompensationWarningCount++;
            if (_motionCompensationWarningCount != 1 && _motionCompensationWarningCount % PointCloud2NativeFailureWarningIntervalFrames != 0)
                return;

            Debug.LogWarning("[Foxglove] PointCloud2 motion compensation " + reason);
        }

    }
}
