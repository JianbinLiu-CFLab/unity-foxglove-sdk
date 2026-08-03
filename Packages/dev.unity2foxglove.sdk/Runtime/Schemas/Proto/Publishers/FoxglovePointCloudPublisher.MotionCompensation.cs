// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PackedPointCloud motion compensation request and source-frame suppression helpers.

using System;
using System.Threading;
using Foxglove.Schemas;
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

        private PointCloudMotionCompensationSettings ResolveMotionCompensationSettings()
        {
            return new PointCloudMotionCompensationSettings(
                _enableMotionCompensation,
                _motionCompensationOutputPolicy,
                _deskewedPackedPointCloudTopic,
                _motionCompensationReferenceTime,
                _motionCompensationSource);
        }

        private PointCloudMotionCompensationRequest TryCreateMotionCompensationRequest(
            PointCloudMotionCompensationSettings settings,
            bool publishNativeFrame)
        {
            if (!settings.EmitDeskewedOutput || !publishNativeFrame)
                return null;

            if (settings.IsLikelySlamReplacementTopic(PackedPointCloudTopic))
            {
                WarnMotionCompensation(
                    "ReplaceOutput is publishing deskewed visualization data on a likely SLAM topic; FAST-LIO2/LIVO2 should subscribe to raw output instead.");
            }

            return new PointCloudMotionCompensationRequest(
                settings.ResolveDeskewedTopic(PackedPointCloudTopic),
                settings.ReferenceTime,
                PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame,
                _motionPoseHistory.Snapshot());
        }

        private bool ShouldQueueDeskewedPackedPointCloudFrame(ulong unixNs)
        {
            var rateHz = _deskewedPackedPointCloudMaxPublishRateHz;
            if (rateHz <= 0f)
                return true;

            var intervalNs = ResolveDeskewedPackedPointCloudPublishIntervalNs(rateHz);
            var timestampNs = unixNs == 0UL ? FoxgloveTimeUtil.NowUnixTimeNs() : unixNs;

            if (_lastDeskewedPackedPointCloudPublishUnixNs != 0UL
                && timestampNs >= _lastDeskewedPackedPointCloudPublishUnixNs
                && timestampNs - _lastDeskewedPackedPointCloudPublishUnixNs < intervalNs)
            {
                _diagnostics.RecordDeskewRateSkip(_logPerformanceDiagnostics);
                return false;
            }

            // A backward clock jump, usually from replay seek or sensor clock reset,
            // intentionally resets the deskewed visualization cadence baseline.
            _lastDeskewedPackedPointCloudPublishUnixNs = timestampNs;
            return true;
        }

        private ulong ResolveDeskewedPackedPointCloudPublishIntervalNs(float rateHz)
        {
            if (!rateHz.Equals(_cachedDeskewedPackedPointCloudMaxPublishRateHz))
            {
                _cachedDeskewedPackedPointCloudMaxPublishRateHz = rateHz;
                _cachedDeskewedPackedPointCloudPublishIntervalNs = (ulong)Math.Max(1d, Math.Round(1_000_000_000d / rateHz));
            }

            return _cachedDeskewedPackedPointCloudPublishIntervalNs;
        }

        private void WarnMotionCompensation(string reason)
        {
            if (_motionCompensationWarningCount < int.MaxValue)
                _motionCompensationWarningCount++;
            if (_motionCompensationWarningCount != 1 && _motionCompensationWarningCount % PackedPointCloudFailureWarningIntervalFrames != 0)
                return;

            Debug.LogWarning("[Foxglove] PackedPointCloud motion compensation " + reason);
        }

    }
}
