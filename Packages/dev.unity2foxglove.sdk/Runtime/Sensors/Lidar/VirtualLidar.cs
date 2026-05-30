// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Sensors;
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
            /// <summary>Use the built-in spinning-LiDAR preset (Ouster OS-0/1/2).</summary>
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
        [SerializeField] private FoxgloveManager _manager;

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
                 "Set a value > 0 to cap rays per scan for performance; excess rays are " +
                 "uniformly subsampled. Raycasts run in parallel via RaycastCommand.")]
        [SerializeField, Min(0)] private int _maxRaysPerScan = 0;
        [SerializeField] private LayerMask _layerMask = ~0;
        [SerializeField] private bool _publishEmptyFrames;
        [SerializeField] private bool _drawDebugRays;
        [SerializeField, Min(1)] private int _scanSubSteps = 1;

        [Header("Synthetic Values")]
        [SerializeField, Range(0, 1)] private float _syntheticReflectivity = 1f;
        [SerializeField, Range(0, 1)] private float _syntheticIntensity = 1f;

        /// <summary>The most recently generated PointCloudFrame, or null before the first scan.</summary>
        public PointCloudFrame LastFrame { get; private set; }

        private ILidarScanPattern _scanPattern;
        private int _frameCounter;
        private float _scanPeriod;

        // Uniform sensor clock: single epoch shared across LiDAR scan lifecycle.
        private bool _scanClockInitialized;
        private ulong _scanEpochUnixNs;
        private double _scanEpochPhysSeconds;

        // Stream state.
        private bool _hasPrevPose;
        private Vector3 _prevPosePosition;
        private Quaternion _prevPoseRotation;
        private double _prevFixedTime;
        private double _scanColumnProgress;
        private int _scanColumnCursor;
        private int _scanColumnCount;
        private PointCloudFrame _activeScanFrame;
        private int _activeScanValidPoints;
        private double _activeScanStartPhysSeconds;

        // Batched-raycast buffers (reused each scan; raycasts run on worker threads).
        private NativeArray<RaycastCommand> _commands;
        private NativeArray<RaycastHit> _results;
        private NativeArray<float> _rayTimeOffsets;
        private NativeArray<ushort> _rayRings;
        private NativeArray<VirtualLidarHitData> _rayHits;
        private NativeArray<VirtualLidarPointData> _pointData;
        private VirtualLidarPointData[] _pointDataManaged;
        private int[] _rayColumns;
        private int _rawRayCount;       // pattern.RayCount
        private int _effectiveRayCount; // rays actually cast per scan (after budget)
        private int _rayStride;         // subsampling stride into the pattern
        private int _spinEffectiveColumns; // RayCount/Rings for spinning, else 0

        private void Start()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();

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

            AllocateScanBuffers();
            ResetScanState(Time.fixedTimeAsDouble);
        }

        private void AllocateScanBuffers()
        {
            if (_scanPattern == null)
                return;

            DisposeScanBuffers();

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
            _rayTimeOffsets = new NativeArray<float>(_effectiveRayCount, Allocator.Persistent);
            _rayRings = new NativeArray<ushort>(_effectiveRayCount, Allocator.Persistent);
            _rayHits = new NativeArray<VirtualLidarHitData>(_effectiveRayCount, Allocator.Persistent);
            _pointData = new NativeArray<VirtualLidarPointData>(_effectiveRayCount, Allocator.Persistent);
            _rayColumns = new int[_effectiveRayCount];

            // Exact length so NativeArray.CopyTo(_pointDataManaged) never length-mismatches
            // (CopyTo requires equal lengths; _pointData is reallocated to _effectiveRayCount here too).
            _pointDataManaged = new VirtualLidarPointData[_effectiveRayCount];
            _scanColumnCount = 0;

            var rawColumns = _spinEffectiveColumns > 0 ? _spinEffectiveColumns : Math.Max(1, _rawRayCount);
            for (var k = 0; k < _effectiveRayCount; k++)
            {
                var index = k * _rayStride;
                if (index >= _rawRayCount)
                    index = _rawRayCount - 1;

                var column = index % rawColumns;
                if (column < 0 || column >= rawColumns)
                    column = 0;

                _rayColumns[k] = column;
                if (column >= _scanColumnCount)
                    _scanColumnCount = column + 1;
            }

            if (_scanColumnCount <= 0)
                _scanColumnCount = Math.Max(1, rawColumns);
        }

        private void DisposeScanBuffers()
        {
            if (_commands.IsCreated) _commands.Dispose();
            if (_results.IsCreated) _results.Dispose();
            if (_rayTimeOffsets.IsCreated) _rayTimeOffsets.Dispose();
            if (_rayRings.IsCreated) _rayRings.Dispose();
            if (_rayHits.IsCreated) _rayHits.Dispose();
            if (_pointData.IsCreated) _pointData.Dispose();
            _rayColumns = null;
            _commands = default;
            _results = default;
            _rayTimeOffsets = default;
            _rayRings = default;
            _rayHits = default;
            _pointData = default;
            _scanColumnCount = 0;
        }

        private void OnDestroy()
        {
            DisposeScanBuffers();
        }

        private void OnEnable()
        {
            AllocateScanBuffers();
            ResetScanState(Time.fixedTimeAsDouble);
        }

        private void OnDisable()
        {
            DisposeScanBuffers();
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
                    // reaching here means the registry lookup failed so use the fallback.
                    return null;
            }
        }

        private void FixedUpdate()
        {
            if (_scanPattern == null || !_commands.IsCreated || _effectiveRayCount <= 0)
                return;

            if (_scanPeriod <= 0f || _scanColumnCount <= 0)
                return;

            EnsureScanClock(Time.fixedTimeAsDouble);

            if (_activeScanFrame == null)
                StartNewScan(Time.fixedTimeAsDouble);

            var nowPhys = Time.fixedTimeAsDouble;
            if (!_hasPrevPose)
            {
                _hasPrevPose = true;
                _prevPosePosition = transform.position;
                _prevPoseRotation = transform.rotation;
                _prevFixedTime = nowPhys;
                return;
            }

            var dt = nowPhys - _prevFixedTime;
            if (dt <= 0d)
            {
                _prevPosePosition = transform.position;
                _prevPoseRotation = transform.rotation;
                _prevFixedTime = nowPhys;
                return;
            }

            var subStepsPerColumn = Math.Max(1, _scanSubSteps);
            var subStepsPerScan = Math.Max(1, _scanColumnCount * subStepsPerColumn);
            var subStepsToEmit = dt * subStepsPerScan / Math.Max(1e-12, _scanPeriod);
            if (subStepsToEmit <= 0d)
            {
                _prevPosePosition = transform.position;
                _prevPoseRotation = transform.rotation;
                _prevFixedTime = nowPhys;
                return;
            }

            _scanColumnProgress += subStepsToEmit;

            var endPos = transform.position;
            var endRot = transform.rotation;
            var startPos = _prevPosePosition;
            var startRot = _prevPoseRotation;
            var emittedSubSteps = (int)Math.Floor(_scanColumnProgress);
            if (emittedSubSteps <= 0)
            {
                _prevPosePosition = endPos;
                _prevPoseRotation = endRot;
                _prevFixedTime = nowPhys;
                return;
            }

            for (var i = 0; i < emittedSubSteps; i++)
            {
                _scanColumnProgress -= 1d;
                var scanSubStep = _scanColumnCursor;
                var column = scanSubStep / subStepsPerColumn;
                var subStepIndex = scanSubStep % subStepsPerColumn;
                var scanSubStepOffset = _scanColumnCursor % subStepsPerScan;
                var subStepProgress = subStepsPerScan > 1
                    ? (float)(scanSubStepOffset + 1) / subStepsPerScan
                    : 1f;

                RunStreamingColumn(column, startPos, startRot, endPos, endRot, subStepProgress, subStepIndex, subStepsPerColumn);

                _scanColumnCursor++;
                if (_scanColumnCursor >= subStepsPerScan)
                {
                    PublishActiveScan();
                    StartNewScan(_activeScanStartPhysSeconds + _scanPeriod);
                    _scanColumnCursor = 0;
                }
            }

            _prevPosePosition = endPos;
            _prevPoseRotation = endRot;
            _prevFixedTime = nowPhys;
        }

        private void StartNewScan(double scanStartPhysSeconds)
        {
            EnsureScanClock(Time.fixedTimeAsDouble);

            _activeScanStartPhysSeconds = scanStartPhysSeconds;
            _scanColumnProgress = 0d;
            _scanColumnCursor = 0;
            _activeScanFrame = new PointCloudFrame
            {
                UnixNs = ComputeScanStartUnixNs(scanStartPhysSeconds),
                FrameId = _frameId,
                ValidCount = 0,
                // SLAM front-ends (FAST-LIO/LIVO2) consume the Ouster-style absolute-ns `t`.
                EmitAbsoluteTimeNs = true
            };
            _activeScanValidPoints = 0;
            _activeScanFrame.Points.Clear();
            if (_activeScanFrame.Points.Capacity < _effectiveRayCount)
                _activeScanFrame.Points.Capacity = _effectiveRayCount;
        }

        private void PublishActiveScan()
        {
            if (_activeScanFrame == null)
                return;

            _activeScanFrame.ValidCount = _activeScanValidPoints;
            LastFrame = _activeScanFrame;

            if (_pointCloudPublisher != null && (_activeScanValidPoints > 0 || _publishEmptyFrames))
                _pointCloudPublisher.SetFrame(_activeScanFrame);

            _frameCounter++;
        }

        private ulong ComputeScanStartUnixNs(double scanStartPhysSeconds)
        {
            if (!_scanClockInitialized)
                return FoxgloveTimeUtil.NowUnixTimeNs();

            var deltaSeconds = scanStartPhysSeconds - _scanEpochPhysSeconds;
            if (deltaSeconds < 0d)
                deltaSeconds = 0d;

            return checked(_scanEpochUnixNs + (ulong)Math.Round(deltaSeconds * 1e9));
        }

        private void EnsureScanClock(double physNow)
        {
            if (_scanClockInitialized)
                return;

            _scanClockInitialized = true;
            _scanEpochPhysSeconds = physNow;
            _scanEpochUnixNs = _manager == null
                ? FoxgloveTimeUtil.NowUnixTimeNs()
                : _manager.GetSharedSensorClockUnixTime(physNow);
            _scanColumnProgress = 0d;
        }

        private void ResetScanState(double physNow)
        {
            EnsureScanClock(physNow);
            _hasPrevPose = false;
            _scanColumnCursor = 0;
            _scanColumnProgress = 0d;
            StartNewScan(physNow);
        }

        private void RunStreamingColumn(
            int column,
            Vector3 startPos,
            Quaternion startRot,
            Vector3 endPos,
            Quaternion endRot,
            float poseBlend,
            int subStepIndex,
            int subStepsPerColumn)
        {
            if (_scanPattern == null || !_commands.IsCreated || _effectiveRayCount <= 0)
                return;
            if (_activeScanFrame == null)
                StartNewScan(Time.fixedTimeAsDouble);

            var worldPos = Vector3.Lerp(startPos, endPos, Math.Clamp(poseBlend, 0f, 1f));
            var worldRot = Quaternion.Slerp(startRot, endRot, Math.Clamp(poseBlend, 0f, 1f));
            var worldToLocal = Matrix4x4.TRS(worldPos, worldRot, Vector3.one).inverse.ToFloat4x4();
            var queryParams = new QueryParameters(_layerMask.value);
            var batchCount = 0;

            // Build one sub-scan batch on the main thread (cheap), then cast in parallel.
            for (var k = 0; k < _effectiveRayCount; k++)
            {
                if (_rayColumns[k] != column)
                    continue;

                var index = k * _rayStride;
                if (index >= _rawRayCount) index = _rawRayCount - 1;

                if (!_scanPattern.TryGetRay(index, _frameCounter, out var localDir, out var timeOffset))
                {
                    _commands[batchCount] = new RaycastCommand(worldPos, Vector3.forward, queryParams, 0f);
                    _rayTimeOffsets[batchCount] = 0f;
                    _rayRings[batchCount] = 0;
                    _rayHits[batchCount] = new VirtualLidarHitData
                    {
                        Point = float3.zero,
                        Distance = 0f,
                        ColliderInstanceId = 0
                    };
                }
                else
                {
                    var worldDir = worldRot * new Vector3(localDir.X, localDir.Y, localDir.Z);
                    _commands[batchCount] = new RaycastCommand(worldPos, worldDir, queryParams, _maxRangeMeters);
                    var subStepOffset = subStepsPerColumn <= 1
                        ? 0f
                        : subStepIndex / (float)(_scanColumnCount * subStepsPerColumn);
                    _rayTimeOffsets[batchCount] = Math.Min(1f, timeOffset + subStepOffset);
                    _rayRings[batchCount] = _spinEffectiveColumns > 0 ? (ushort)(index / _spinEffectiveColumns) : (ushort)0;
                }

                batchCount++;
            }

            if (batchCount <= 0)
                return;

            // Parallel raycast across worker threads, then process results on main thread.
            RaycastCommand.ScheduleBatch(_commands.Slice(0, batchCount), _results.Slice(0, batchCount), 64).Complete();

            var minRange = (float)_scanPattern.MinRangeMeters;
            for (var k = 0; k < batchCount; k++)
            {
                var hit = _results[k];
                _rayHits[k] = new VirtualLidarHitData
                {
                    Point = new float3(hit.point.x, hit.point.y, hit.point.z),
                    Distance = hit.distance,
                    ColliderInstanceId = hit.collider == null ? 0u : (uint)hit.colliderInstanceID
                };
            }

            var job = new VirtualLidarBuildPointsJob
            {
                Hits = _rayHits,
                RayTimeOffsets = _rayTimeOffsets,
                RayRings = _rayRings,
                WorldToLocal = worldToLocal,
                MinRange = minRange,
                MaxRange = _maxRangeMeters,
                SyntheticIntensity = _syntheticIntensity,
                SyntheticReflectivity = _syntheticReflectivity,
                Points = _pointData
            };

            job.Schedule(batchCount, 64).Complete();

            _pointData.CopyTo(_pointDataManaged);

            var validPointCount = 0;
            for (var k = 0; k < batchCount; k++)
            {
                var point = _pointDataManaged[k];
                if (point.IsValid == 0)
                    continue;

                _activeScanFrame.Points.Add(new PointCloudPoint(point.X, point.Y, point.Z)
                {
                    Intensity = point.Intensity,
                    Reflectivity = point.Reflectivity,
                    TimeOffsetSeconds = point.TimeOffsetSeconds,
                    Ring = point.Ring
                });

                if (_drawDebugRays)
                    Debug.DrawRay(_commands[k].from, _commands[k].direction * _rayHits[k].Distance, Color.green, _scanPeriod);

                validPointCount++;
            }

            _activeScanValidPoints += validPointCount;
        }

        private void OnValidate()
        {
            _columnStep = Math.Max(1, _columnStep);
            _maxRangeMeters = Math.Max(0f, _maxRangeMeters);
            if (_scanSubSteps < 1)
                _scanSubSteps = 1;
        }
    }
}
