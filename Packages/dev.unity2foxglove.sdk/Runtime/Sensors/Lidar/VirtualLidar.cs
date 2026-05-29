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
        /// <summary>Where the LiDAR scan geometry comes from.</summary>
        public enum ProfileSource
        {
            /// <summary>Parse an Ouster-format metadata JSON TextAsset.</summary>
            MetadataJson,
            /// <summary>Use a built-in spinning-LiDAR preset (Ouster OS-0/1/2 × 32/64/128).</summary>
            BuiltInPreset,
            /// <summary>Use the manually edited Custom Profile fields below.</summary>
            Custom
        }

        [Header("Output")]
        [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;

        [Header("Profile")]
        [Tooltip("Where the scan geometry comes from. Default BuiltInPreset reproduces the previous OS-1-32 behaviour. To load a sensor's metadata.json, switch to MetadataJson.")]
        [SerializeField] private ProfileSource _profileSource = ProfileSource.BuiltInPreset;
        [Tooltip("Used when Profile Source = MetadataJson.")]
        [SerializeField] private TextAsset _metadataJson;
        [Tooltip("Mode string for metadata parsing: \"<columns>x<rateHz>\", e.g. 1024x10 or 2048x10.")]
        [SerializeField] private string _metadataMode = "1024x10";
        [Tooltip("Used when Profile Source = BuiltInPreset.")]
        [SerializeField] private Sensors.Lidar.LidarPreset _preset = Sensors.Lidar.LidarPreset.Os1_32;

        [Header("Custom Profile (Profile Source = Custom)")]
        [SerializeField, Min(1)] private int _customPixelsPerColumn = 32;
        [SerializeField] private float _customFovTopDeg = 16.6f;
        [SerializeField] private float _customFovBottomDeg = -16.6f;
        [SerializeField, Min(16)] private int _customColumnsPerFrame = 1024;
        [SerializeField, Min(1f)] private float _customScanRateHz = 10f;
        [SerializeField, Min(0f)] private float _customMinRangeMeters = 0.5f;

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

        /// <summary>The most recently generated PointCloudFrame, or null before the first scan.</summary>
        public PointCloudFrame LastFrame { get; private set; }

        private Sensors.Lidar.LidarProfile _profile;
        private Sensors.Lidar.LidarRayGenerator _rayGenerator;
        private float _nextScanTime;
        private float _scanPeriod;

        private void Start()
        {
            _profile = LoadProfile();
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

        private Sensors.Lidar.LidarProfile LoadProfile()
        {
            switch (_profileSource)
            {
                case ProfileSource.MetadataJson:
                {
                    if (_metadataJson == null || string.IsNullOrEmpty(_metadataJson.text))
                    {
                        Debug.LogWarning("[VirtualLidar] Profile Source is MetadataJson but no JSON is assigned; using OS-1-32 fallback.");
                        return null;
                    }
                    if (Sensors.Lidar.LidarProfileLoader.TryParseFromJson(
                            _metadataJson.text, _metadataMode, out var parsed, out var error))
                        return parsed;
                    Debug.LogWarning($"[VirtualLidar] Metadata parse failed ({error}); using OS-1-32 fallback.");
                    return null;
                }

                case ProfileSource.Custom:
                    return Sensors.Lidar.LidarProfileLoader.CreateUniform(
                        "Custom", _customPixelsPerColumn, _customColumnsPerFrame,
                        _customScanRateHz, _customFovTopDeg, _customFovBottomDeg, _customMinRangeMeters);

                case ProfileSource.BuiltInPreset:
                default:
                    return Sensors.Lidar.LidarProfileLoader.CreatePreset(_preset);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
                return;

            var frame = RunScan();
            LastFrame = frame;

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

                    // Convert System.Numerics.Vector3 (Unity-free generator output) to UnityEngine.Vector3.
                    var unityLocalDir = new Vector3(localDir.X, localDir.Y, localDir.Z);
                    var worldDir = transform.TransformDirection(unityLocalDir);
                    var hit = Physics.Raycast(worldPos, worldDir, out var hitInfo, _maxRangeMeters, _layerMask);

                    if (_drawDebugRays)
                    {
                        var rayLength = hit ? hitInfo.distance : _maxRangeMeters;
                        Debug.DrawRay(worldPos, worldDir * rayLength, hit ? Color.green : Color.red, _scanPeriod);
                    }

                    if (!hit || hitInfo.distance < _profile.MinRangeMeters || hitInfo.distance > _maxRangeMeters)
                        continue;

                    var localHitPoint = transform.InverseTransformPoint(hitInfo.point);
                    // Convert Unity left-handed (X right, Y up, Z forward) to
                    // Foxglove/ROS right-handed (X forward, Y left, Z up) so
                    // downstream Foxglove panels render the cloud correctly in
                    // its frame_id without per-axis TF gymnastics.
                    var rosPoint = CoordinateConverter.UnityToFoxglovePosition(localHitPoint);
                    var point = new PointCloudPoint(rosPoint.x, rosPoint.y, rosPoint.z)
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
