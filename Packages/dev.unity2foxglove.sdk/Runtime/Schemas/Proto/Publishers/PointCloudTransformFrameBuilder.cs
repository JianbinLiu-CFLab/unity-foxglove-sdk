// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Builds transform-fallback point-cloud frames for Foxglove publishers.

using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Converts configured Unity transforms into a sparse fallback point-cloud
    /// frame when no source-driven LiDAR frame is available.
    /// </summary>
    internal sealed class TransformPointCloudSource
    {
        public PointCloudFrame CreateFrameFromTransforms(
            ulong unixNs,
            string frameId,
            Transform rootTransform,
            Transform[] pointSources,
            bool useChildrenWhenSourcesEmpty,
            bool includeInactiveChildren,
            bool includeSyntheticIntensity,
            int maxPoints,
            CoordinateMode coordinateMode)
        {
            var frame = new PointCloudFrame
            {
                UnixNs = unixNs,
                FrameId = string.IsNullOrWhiteSpace(frameId) ? "unity_world" : frameId
            };

            var added = 0;
            var boundedMaxPoints = Mathf.Max(1, maxPoints);
            if (pointSources != null && pointSources.Length > 0)
            {
                foreach (var source in pointSources)
                    AddPoint(frame, source, includeSyntheticIntensity, boundedMaxPoints, coordinateMode, ref added);
            }
            else if (useChildrenWhenSourcesEmpty && rootTransform != null)
            {
                for (var i = 0; i < rootTransform.childCount; i++)
                {
                    var child = rootTransform.GetChild(i);
                    if (child == null)
                        continue;

                    if (!includeInactiveChildren && !child.gameObject.activeInHierarchy)
                        continue;

                    AddPoint(frame, child, includeSyntheticIntensity, boundedMaxPoints, coordinateMode, ref added);
                }
            }

            return frame;
        }

        private static void AddPoint(
            PointCloudFrame frame,
            Transform source,
            bool includeSyntheticIntensity,
            int maxPoints,
            CoordinateMode coordinateMode,
            ref int added)
        {
            if (source == null || added >= maxPoints)
                return;

            var pos = coordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(source.position)
                : source.position;

            var point = new PointCloudPoint(pos.x, pos.y, pos.z);
            if (includeSyntheticIntensity)
                point.Intensity = added;

            frame.Points.Add(point);
            added++;
        }
    }
}
