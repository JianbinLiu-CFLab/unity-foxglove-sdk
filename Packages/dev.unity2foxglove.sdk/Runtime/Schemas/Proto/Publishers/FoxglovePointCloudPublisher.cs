// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.PointCloud messages from decoded frames or Unity transforms.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using UVector3 = UnityEngine.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes decoded point frames or child transforms as foxglove.PointCloud.
    /// Programmatic frames are intended for later Ouster/ROS input bridges.
    /// </summary>
    public class FoxglovePointCloudPublisher : FoxglovePublisherBase
    {
        [Header("Point Cloud")]
        [SerializeField] private string _frameId = "unity_world";
        [SerializeField] private Transform[] _pointSources;
        [SerializeField] private bool _includeInactiveChildren;
        [SerializeField] private bool _useChildrenWhenSourcesEmpty = true;
        [SerializeField, Min(1)] private int _maxPoints = 4096;
        [SerializeField] private bool _includeSyntheticIntensity;

        private PointCloudFrame _pendingFrame;
        private bool _warnedPointCloudBudget;

        protected override string SchemaName => FoxgloveSchemaDefinitions.PointCloudSchemaName;
        public override bool SupportsProtobufEncoding => true;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/point_cloud";
        }

        /// <summary>
        /// Queue a decoded frame for the next publish tick.
        /// </summary>
        public void SetFrame(PointCloudFrame frame)
        {
            _pendingFrame = frame;
        }

        /// <summary>
        /// Publish a decoded frame immediately, bypassing the regular Update cadence.
        /// </summary>
        public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)
        {
            ResolveManager();
            if (_manager == null || frame == null) return;

            var prepared = ClampFrameToPointBudget(frame, logTimeNs);
            PublishPreparedFrame(prepared, logTimeNs);
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var unixNs = CurrentLogTimeNs;
            var frame = _pendingFrame != null ? ClampFrameToPointBudget(_pendingFrame, unixNs) : CreateFrameFromTransforms(unixNs);
            _pendingFrame = null;
            if (frame == null || frame.Points.Count == 0) return;

            PublishPreparedFrame(frame, unixNs);
        }

        private void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
            if (EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(PointCloudMessageBuilder.SerializeProtobuf(frame), unixNs);
            }
            else
            {
                Publish(PointCloudMessageBuilder.CreateJson(frame), unixNs);
            }
        }

        private PointCloudFrame ClampFrameToPointBudget(PointCloudFrame frame, ulong unixNs)
        {
            var maxPoints = Math.Max(1, _maxPoints);
            if (frame.UnixNs != 0 && !string.IsNullOrEmpty(frame.FrameId) && frame.Points.Count <= maxPoints)
            {
                _warnedPointCloudBudget = false;
                return frame;
            }

            var copy = new PointCloudFrame
            {
                UnixNs = frame.UnixNs == 0 ? unixNs : frame.UnixNs,
                FrameId = string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId
            };

            var count = Math.Min(frame.Points.Count, maxPoints);
            for (var i = 0; i < count; i++)
                copy.Points.Add(frame.Points[i]);

            if (frame.Points.Count > maxPoints)
            {
                if (!_warnedPointCloudBudget)
                {
                    Debug.LogWarning(
                        $"[Foxglove] PointCloud frame truncated from {frame.Points.Count} to {maxPoints} points.");
                    _warnedPointCloudBudget = true;
                }
            }
            else
            {
                _warnedPointCloudBudget = false;
            }

            return copy;
        }

        private PointCloudFrame CreateFrameFromTransforms(ulong unixNs)
        {
            var frame = new PointCloudFrame
            {
                UnixNs = unixNs,
                FrameId = SanitizeFrameId(_frameId, "unity_world")
            };

            var added = 0;
            if (_pointSources != null && _pointSources.Length > 0)
            {
                foreach (var source in _pointSources)
                    AddPoint(frame, source, ref added);
            }
            else if (_useChildrenWhenSourcesEmpty)
            {
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (!_includeInactiveChildren && !child.gameObject.activeInHierarchy)
                        continue;
                    AddPoint(frame, child, ref added);
                }
            }

            return frame;
        }

        private void AddPoint(PointCloudFrame frame, Transform source, ref int added)
        {
            if (source == null || added >= Math.Max(1, _maxPoints)) return;

            UVector3 pos = Manager != null && Manager.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(source.position)
                : source.position;

            var point = new PointCloudPoint(pos.x, pos.y, pos.z);
            if (_includeSyntheticIntensity)
                point.Intensity = added;
            frame.Points.Add(point);
            added++;
        }
    }
}
