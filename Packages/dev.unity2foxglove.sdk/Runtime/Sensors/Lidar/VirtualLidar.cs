// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Sensors.Lidar;

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

        /// <summary>How the scan (frame-generation) rate is chosen.</summary>
        public enum ScanRateSource
        {
            /// <summary>Use the selected sensor's nominal scan rate (from the model/profile).</summary>
            UseSensorRate,
            /// <summary>Use the Scan Rate Hz field below.</summary>
            Override
        }

        [Header("Output")]
        [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;

        [Header("Profile")]
        [Tooltip("Where the scan geometry comes from.")]
        [SerializeField] private ProfileSource _profileSource = ProfileSource.BuiltInPreset;
        [Tooltip("Used when Profile Source = MetadataJson.")]
        [SerializeField] private TextAsset _metadataJson;
        [Tooltip("Mode string for metadata parsing.")]
        [SerializeField] private string _metadataMode = "1024x10";
        [Tooltip("Used when Profile Source = BuiltInPreset.")]
        [SerializeField] private Sensors.Lidar.LidarVendor _vendor = Sensors.Lidar.LidarVendor.Ouster;
        [Tooltip("Model identifier within the vendor (e.g. OS-1-32, VLP-16, Mid-360).")]
        [SerializeField] private string _model = "OS-1-32";
        [Tooltip("Optional scan mode for models that support multiple modes (e.g. 1024x10, 2048x10).")]
        [SerializeField] private string _mode = "1024x10";

        [Header("Custom Profile (Profile Source = Custom)")]
        [SerializeField, Min(1)] private int _customPixelsPerColumn = 32;
        [SerializeField] private float _customFovTopDeg = 16.6f;
        [SerializeField] private float _customFovBottomDeg = -16.6f;
        [SerializeField, Min(16)] private int _customColumnsPerFrame = 1024;
        [SerializeField, Min(1f)] private float _customScanRateHz = 10f;
        [SerializeField, Min(0f)] private float _customMinRangeMeters = 0.5f;

        [Header("Scan")]
        [SerializeField] private string _frameId = "os_lidar";
        [Tooltip("Use Sensor Rate = the model's nominal Hz; Override = use Scan Rate Hz below. " +
                 "This is the LiDAR's frame-generation rate; the point cloud's publish rate to " +
                 "Foxglove is set separately on FoxglovePointCloudPublisher (Publish Rate).")]
        [SerializeField] private ScanRateSource _scanRateSource = ScanRateSource.UseSensorRate;
        [Tooltip("Scan rate in Hz, used when Scan Rate Source = Override.")]
        [SerializeField, Min(0f)] private float _scanRateHzOverride = 10f;
        [SerializeField] private float _maxRangeMeters = 50f;
        [SerializeField, Min(1)] private int _columnStep = 4;
        [Tooltip("0 (default) = no clipping: cast every ray the selected sensor defines " +
                 "(full resolution, scales automatically with the model). " +
                 "Set a value > 0 to cap rays per scan for performance — excess rays are " +
                 "uniformly subsampled. Raycasts run in parallel via RaycastCommand.")]
        [SerializeField, Min(0)] private int _maxRaysPerScan = 0;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField] private bool _publishEmptyFrames;
        [SerializeField] private bool _drawDebugRays;

        [Header("Synthetic Values")]
        [SerializeField, Range(0, 1)] private float _syntheticReflectivity = 1f;
        [SerializeField, Range(0, 1)] private float _syntheticIntensity = 1f;

        /// <summary>The most recently generated PointCloudFrame, or null before the first scan.</summary>
        public PointCloudFrame LastFrame { get; private set; }

        private ILidarScanPattern _scanPattern;
        private int _frameCounter;
        private float _nextScanTime;
        private float _scanPeriod;

        // Batched-raycast buffers (reused each scan; raycasts run on worker threads).
        private NativeArray<RaycastCommand> _commands;
        private NativeArray<RaycastHit> _results;
        private float[] _rayTimeOffsets;
        private ushort[] _rayRings;
        private int _rawRayCount;       // pattern.RayCount
        private int _effectiveRayCount; // rays actually cast per scan (after budget)
        private int _rayStride;         // subsampling stride into the pattern
        private int _spinEffectiveColumns; // RayCount/Rings for spinning, else 0

        private void Start()
        {
            if (_profileSource == ProfileSource.BuiltInPreset)
            {
                if (Sensors.Lidar.LidarModelRegistry.TryGet(_vendor, _model, out var spec))
                    _scanPattern = Sensors.Lidar.LidarScanPatternFactory.Create(spec, _mode, _columnStep);
            }

            if (_scanPattern == null)
            {
                // Fallback: metadata JSON or custom params via old profile path
                var profile = LoadProfile();
                if (profile == null)
                    profile = Sensors.Lidar.LidarProfileLoader.CreateOs132Default();
                _scanPattern = Sensors.Lidar.LidarScanPatternFactory.FromProfile(profile, _columnStep);
            }

            // Resolve publisher if unassigned
            if (_pointCloudPublisher == null)
            {
                _pointCloudPublisher = GetComponent<FoxglovePointCloudPublisher>();
                if (_pointCloudPublisher == null)
                    _pointCloudPublisher = GetComponentInChildren<FoxglovePointCloudPublisher>();
            }

            var rateHz = _scanRateSource == ScanRateSource.Override && _scanRateHzOverride > 0f
                ? _scanRateHzOverride
                : _scanPattern.ScanRateHz;
            _scanPeriod = rateHz > 0f ? (1f / (float)rateHz) : 0.1f;
            _nextScanTime = Time.unscaledTime;

            AllocateScanBuffers();
        }

        private void AllocateScanBuffers()
        {
            _rawRayCount = Math.Max(1, _scanPattern.RayCount);
            var budget = _maxRaysPerScan <= 0 ? _rawRayCount : Math.Min(_rawRayCount, _maxRaysPerScan);
            budget = Math.Max(1, budget);
            _rayStride = Math.Max(1, (_rawRayCount + budget - 1) / budget);          // ceil
            _effectiveRayCount = (_rawRayCount + _rayStride - 1) / _rayStride;        // ceil

            _spinEffectiveColumns = _scanPattern is Sensors.Lidar.SpinningScanPattern spin && spin.Rings > 0
                ? spin.RayCount / spin.Rings
                : 0;

            _commands = new NativeArray<RaycastCommand>(_effectiveRayCount, Allocator.Persistent);
            _results = new NativeArray<RaycastHit>(_effectiveRayCount, Allocator.Persistent);
            _rayTimeOffsets = new float[_effectiveRayCount];
            _rayRings = new ushort[_effectiveRayCount];
        }

        private void OnDestroy()
        {
            if (_commands.IsCreated) _commands.Dispose();
            if (_results.IsCreated) _results.Dispose();
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
                    // BuiltInPreset is resolved via LidarModelRegistry in Start();
                    // reaching here means the registry lookup failed → use the fallback.
                    return null;
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

            if (!_commands.IsCreated || _effectiveRayCount <= 0)
            {
                _frameCounter++;
                return frame;
            }

            var worldPos = transform.position;
            var queryParams = new QueryParameters(_layerMask.value);

            // Build the ray batch on the main thread (cheap), then cast in parallel.
            for (var k = 0; k < _effectiveRayCount; k++)
            {
                var index = k * _rayStride;
                if (index >= _rawRayCount) index = _rawRayCount - 1;

                if (!_scanPattern.TryGetRay(index, _frameCounter, out var localDir, out var timeOffset))
                {
                    _commands[k] = new RaycastCommand(worldPos, Vector3.forward, queryParams, 0f);
                    _rayTimeOffsets[k] = 0f;
                    _rayRings[k] = 0;
                    continue;
                }

                var worldDir = transform.TransformDirection(new Vector3(localDir.X, localDir.Y, localDir.Z));
                _commands[k] = new RaycastCommand(worldPos, worldDir, queryParams, _maxRangeMeters);
                _rayTimeOffsets[k] = timeOffset;
                _rayRings[k] = _spinEffectiveColumns > 0 ? (ushort)(index / _spinEffectiveColumns) : (ushort)0;
            }

            // Parallel raycast across worker threads, then read results on the main thread.
            RaycastCommand.ScheduleBatch(_commands, _results, 64).Complete();

            var minRange = (float)_scanPattern.MinRangeMeters;
            for (var k = 0; k < _effectiveRayCount; k++)
            {
                var hit = _results[k];
                if (hit.collider == null)
                    continue;

                var d = hit.distance;
                if (d < minRange || d > _maxRangeMeters)
                    continue;

                if (_drawDebugRays)
                    Debug.DrawRay(_commands[k].from, _commands[k].direction * d, Color.green, _scanPeriod);

                var localHitPoint = transform.InverseTransformPoint(hit.point);
                var rosHit = CoordinateConverter.UnityToFoxglovePosition(localHitPoint);
                frame.Points.Add(new PointCloudPoint(rosHit.x, rosHit.y, rosHit.z)
                {
                    Intensity = _syntheticIntensity,
                    Reflectivity = _syntheticReflectivity,
                    TimeOffsetSeconds = _rayTimeOffsets[k],
                    Ring = _rayRings[k]
                });
            }

            _frameCounter++;
            return frame;
        }

        private void OnValidate()
        {
            _columnStep = Math.Max(1, _columnStep);
            _maxRangeMeters = Math.Max(0f, _maxRangeMeters);
        }
    }
}
