// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Shared QoS reduction for point-cloud payload preparation.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class PointCloudQoSReducer
    {
        private readonly Action<string> _logWarning;
        private bool _warnedPointCloudBudget;

        public PointCloudQoSReducer(Action<string> logWarning = null)
        {
            _logWarning = logWarning ?? (_ => { });
        }

        public void Reset()
        {
            _warnedPointCloudBudget = false;
        }

        public PointCloudFrame PrepareFrameForQoS(
            PointCloudFrame frame,
            ulong unixNs,
            string frameId,
            int maxPoints,
            int maxPackedBytes,
            PointCloudSamplingMode samplingMode,
            float voxelSizeMeters,
            bool logQosDrops)
        {
            if (frame == null)
                return null;

            var pointCount = frame.GetPointCount();
            var stride = PointCloudQoS.ComputePackedStride(frame);
            var pointBudget = PointCloudQoS.ComputeEffectivePointBudget(
                pointCount,
                maxPoints,
                Math.Max(0, maxPackedBytes),
                stride);

            if (pointBudget <= 0)
            {
                WarnPointCloudReduced(pointCount, pointBudget, logQosDrops);
                return null;
            }

            var useVoxelGrid = samplingMode == PointCloudSamplingMode.VoxelGrid && voxelSizeMeters > 0f;
            var forceUniformFallback = samplingMode == PointCloudSamplingMode.VoxelGrid && voxelSizeMeters <= 0f;

            if (!useVoxelGrid && !forceUniformFallback && frame.UnixNs != 0 && !string.IsNullOrEmpty(frame.FrameId) && pointCount <= pointBudget)
            {
                _warnedPointCloudBudget = false;
                return frame;
            }

            var copy = new PointCloudFrame
            {
                UnixNs = frame.UnixNs == 0 ? unixNs : frame.UnixNs,
                FrameId = string.IsNullOrEmpty(frame.FrameId) ? frameId : frame.FrameId
            };

            if (useVoxelGrid)
            {
                var voxelIndices = PointCloudQoS.BuildVoxelSampleIndices(frame, voxelSizeMeters);
                if (voxelIndices.Length <= pointBudget)
                {
                    foreach (var index in voxelIndices)
                        copy.Points.Add(frame.Points[index]);
                }
                else
                {
                    var indices = PointCloudQoS.BuildUniformSampleIndices(voxelIndices.Length, pointBudget);
                    foreach (var index in indices)
                        copy.Points.Add(frame.Points[voxelIndices[index]]);
                }
            }
            else if (pointCount <= pointBudget && !forceUniformFallback)
            {
                for (var i = 0; i < pointCount; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else if (samplingMode == PointCloudSamplingMode.FirstPoints)
            {
                var count = Math.Min(pointCount, pointBudget);
                for (var i = 0; i < count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else
            {
                var indices = PointCloudQoS.BuildUniformSampleIndices(pointCount, pointBudget);
                foreach (var index in indices)
                    copy.Points.Add(frame.Points[index]);
            }

            if (pointCount > pointBudget)
                WarnPointCloudReduced(pointCount, pointBudget, logQosDrops);
            else
            {
                _warnedPointCloudBudget = false;
            }

            copy.ValidCount = copy.Points.Count;

            return copy;
        }

        private void WarnPointCloudReduced(int originalPoints, int outputPoints, bool logQosDrops)
        {
            if (!logQosDrops) return;
            if (_warnedPointCloudBudget) return;

            _logWarning(
                $"[Foxglove] PointCloud frame reduced from {originalPoints} to {Math.Max(0, outputPoints)} points.");
            _warnedPointCloudBudget = true;
        }
    }
}
