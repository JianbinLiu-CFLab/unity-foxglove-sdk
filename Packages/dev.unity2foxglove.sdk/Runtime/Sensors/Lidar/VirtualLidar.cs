// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// A MonoBehaviour that raycasts Unity scene geometry using a LiDAR profile
    /// and publishes the resulting point cloud.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Foxglove/Sensors/Virtual LiDAR")]
    public class VirtualLidar : MonoBehaviour
    {
        [Header("Output")]
        [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;

        [Header("Profile")]
        [SerializeField] private TextAsset _metadataJson;

        [Header("Scan")]
        [SerializeField] private string _frameId = "os_lidar";
        [SerializeField] private float _scanRateHzOverride;
        [SerializeField] private float _maxRangeMeters = 50f;
        [SerializeField, Min(1)] private int _columnStep = 4;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField] private bool _publishEmptyFrames;
        [SerializeField] private bool _drawDebugRays;

        [Header("Synthetic Values")]
        [SerializeField, Range(0, 1)] private float _syntheticReflectivity = 1f;
        [SerializeField, Range(0, 1)] private float _syntheticIntensity = 1f;

        private Sensors.Lidar.LidarProfile _profile;
        private Sensors.Lidar.LidarRayGenerator _rayGenerator;
        private float _nextScanTime;
        private float _scanPeriod;

        private void Start()
        {
            // Load profile: metadata JSON > built-in Ouster OS-1-32 fallback
            if (_metadataJson != null && !string.IsNullOrEmpty(_metadataJson.text))
            {
                Sensors.Lidar.LidarProfileLoader.TryParseFromJson(
                    _metadataJson.text, _metadataJson.name, out _profile, out _);
            }

            if (_profile == null)
                _profile = Sensors.Lidar.LidarProfileLoader.CreateOs132Default();

            _rayGenerator = new Sensors.Lidar.LidarRayGenerator(_profile, _columnStep);

            // Resolve publisher if unassigned
            if (_pointCloudPublisher == null)
            {
                _pointCloudPublisher = GetComponent<FoxglovePointCloudPublisher>();
                if (_pointCloudPublisher == null)
                    _pointCloudPublisher = GetComponentInChildren<FoxglovePointCloudPublisher>();
            }

            var rateHz = _scanRateHzOverride > 0f ? _scanRateHzOverride : _profile.ScanRateHz;
            _scanPeriod = rateHz > 0f ? (1f / (float)rateHz) : 0.1f;
            _nextScanTime = Time.unscaledTime;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
                return;

            var frame = RunScan();

            if (_pointCloudPublisher != null && (frame.Points.Count > 0 || _publishEmptyFrames))
                _pointCloudPublisher.SetFrame(frame);

            _nextScanTime += _scanPeriod;

            // Guard against large time gaps (e.g. editor pause)
            if (_nextScanTime < Time.unscaledTime)
                _nextScanTime = Time.unscaledTime + _scanPeriod;
        }

        private PointCloudFrame RunScan()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = FoxgloveTimeUtil.NowUnixTimeNs(),
                FrameId = _frameId
            };

            var worldPos = transform.position;
            var columnCount = _profile.ColumnsPerFrame / _columnStep;

            for (var c = 0; c < columnCount; c++)
            {
                var column = c * _columnStep;

                for (var ring = 0; ring < _profile.PixelsPerColumn; ring++)
                {
                    if (!_rayGenerator.TryGetRay(column, ring, out var localDir, out var timeOffset))
                        continue;

                    var worldDir = transform.TransformDirection(localDir);
                    var hit = Physics.Raycast(worldPos, worldDir, out var hitInfo, _maxRangeMeters, _layerMask);

                    if (_drawDebugRays)
                    {
                        var rayLength = hit ? hitInfo.distance : _maxRangeMeters;
                        Debug.DrawRay(worldPos, worldDir * rayLength, hit ? Color.green : Color.red, _scanPeriod);
                    }

                    if (!hit || hitInfo.distance < _profile.MinRangeMeters || hitInfo.distance > _maxRangeMeters)
                        continue;

                    var localHitPoint = transform.InverseTransformPoint(hitInfo.point);
                    var point = new PointCloudPoint(localHitPoint.x, localHitPoint.y, localHitPoint.z)
                    {
                        Intensity = _syntheticIntensity,
                        Reflectivity = _syntheticReflectivity,
                        Ring = (ushort)ring,
                        TimeOffsetSeconds = timeOffset
                    };

                    frame.Points.Add(point);
                }
            }

            return frame;
        }

        private void OnValidate()
        {
            _columnStep = Math.Max(1, _columnStep);
            _maxRangeMeters = Math.Max(0f, _maxRangeMeters);
            _syntheticReflectivity = Mathf.Clamp01(_syntheticReflectivity);
            _syntheticIntensity = Mathf.Clamp01(_syntheticIntensity);
        }
    }
}
