// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.PointCloud messages from decoded frames or Unity transforms.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
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
        [SerializeField, Min(0)] private int _maxPackedBytes;
        [SerializeField] private PointCloudSamplingMode _samplingMode = PointCloudSamplingMode.FirstPoints;
        [SerializeField, Min(0f)] private float _voxelSizeMeters = 0.1f;
        [SerializeField] private bool _logQosDrops;
        [SerializeField] private bool _includeSyntheticIntensity;

        private PointCloudFrame _pendingFrame;
        private bool _warnedPointCloudBudget;
        private bool _warnedPendingDrop;

        protected override string SchemaName => FoxgloveSchemaDefinitions.PointCloudSchemaName;
        public override bool SupportsProtobufEncoding => true;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/point_cloud";
        }

        protected override void Reset()
        {
            base.Reset();
            _samplingMode = PointCloudSamplingMode.UniformStride;
        }

        /// <summary>
        /// Queue a decoded frame for the next publish tick. This is a
        /// last-value-wins buffer: a new frame replaces stale pending data.
        /// </summary>
        public void SetFrame(PointCloudFrame frame)
        {
            if (_pendingFrame != null && frame != null && _logQosDrops && !_warnedPendingDrop)
            {
                Debug.LogWarning("[Foxglove] PointCloud pending frame replaced; stale pending frame dropped.");
                _warnedPendingDrop = true;
            }

            _pendingFrame = frame;
        }

        /// <summary>
        /// Publish a decoded frame immediately, bypassing the regular Update cadence.
        /// </summary>
        public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)
        {
            ResolveManager();
            if (_manager == null || frame == null) return;
            if (!ShouldPreparePublishPayload()) return;

            var prepared = PrepareFrameForQoS(frame, logTimeNs);
            if (prepared == null || prepared.Points.Count == 0) return;
            PublishPreparedFrame(prepared, logTimeNs);
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPreparePublishPayload()) return;

            var unixNs = CurrentLogTimeNs;
            var frame = _pendingFrame != null ? PrepareFrameForQoS(_pendingFrame, unixNs) : PrepareFrameForQoS(CreateFrameFromTransforms(unixNs), unixNs);
            _pendingFrame = null;
            _warnedPendingDrop = false;
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

        private PointCloudFrame PrepareFrameForQoS(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null)
                return null;

            var stride = PointCloudQoS.ComputePackedStride(frame);
            var pointBudget = PointCloudQoS.ComputeEffectivePointBudget(
                frame.Points.Count,
                _maxPoints,
                Math.Max(0, _maxPackedBytes),
                stride);

            if (pointBudget <= 0)
            {
                WarnPointCloudReduced(frame.Points.Count, pointBudget);
                return null;
            }

            var useVoxelGrid = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters > 0f;
            var forceUniformFallback = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters <= 0f;

            if (!useVoxelGrid && !forceUniformFallback && frame.UnixNs != 0 && !string.IsNullOrEmpty(frame.FrameId) && frame.Points.Count <= pointBudget)
            {
                _warnedPointCloudBudget = false;
                return frame;
            }

            var copy = new PointCloudFrame
            {
                UnixNs = frame.UnixNs == 0 ? unixNs : frame.UnixNs,
                FrameId = string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId
            };

            if (useVoxelGrid)
            {
                var voxelIndices = PointCloudQoS.BuildVoxelSampleIndices(frame, _voxelSizeMeters);
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
            else if (frame.Points.Count <= pointBudget && !forceUniformFallback)
            {
                for (var i = 0; i < frame.Points.Count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else if (_samplingMode == PointCloudSamplingMode.FirstPoints)
            {
                var count = Math.Min(frame.Points.Count, pointBudget);
                for (var i = 0; i < count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else
            {
                var indices = PointCloudQoS.BuildUniformSampleIndices(frame.Points.Count, pointBudget);
                foreach (var index in indices)
                    copy.Points.Add(frame.Points[index]);
            }

            if (frame.Points.Count > pointBudget)
                WarnPointCloudReduced(frame.Points.Count, pointBudget);
            else
            {
                _warnedPointCloudBudget = false;
            }

            return copy;
        }

        private void WarnPointCloudReduced(int originalPoints, int outputPoints)
        {
            if (!_logQosDrops) return;
            if (_warnedPointCloudBudget) return;

            Debug.LogWarning(
                $"[Foxglove] PointCloud frame reduced from {originalPoints} to {Math.Max(0, outputPoints)} points.");
            _warnedPointCloudBudget = true;
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
