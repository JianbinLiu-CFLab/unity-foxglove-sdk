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
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Sensors;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Stopwatch = System.Diagnostics.Stopwatch;

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

        /// <summary>Inspector input mode for editing T_IL rotation overrides.</summary>
        public enum TIlRotationInputFormat
        {
            /// <summary>Edit the rotation as quaternion x/y/z/w.</summary>
            Quaternion,
            /// <summary>Edit the rotation as a row-major 3x3 matrix.</summary>
            Matrix3x3
        }

        [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;
        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private SensorUnitProfile _sensorUnitProfile;

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

        [SerializeField] private bool _overrideTIl;
        [SerializeField] private TIlRotationInputFormat _tIlRotationInputFormat = TIlRotationInputFormat.Quaternion;
        [SerializeField] private Vector3 _tIlTranslationMeters = new Vector3(0.006253f, -0.011775f, 0.007645f);
        [SerializeField] private Quaternion _tIlRotation = Quaternion.identity;

        [SerializeField, Min(1)] private int _customPixelsPerColumn = 32;
        [SerializeField] private float _customFovTopDeg = 16.6f;
        [SerializeField] private float _customFovBottomDeg = -16.6f;
        [SerializeField, Min(16)] private int _customColumnsPerFrame = 1024;
        [SerializeField, Min(1f)] private float _customScanRateHz = 10f;
        [SerializeField, Min(0f)] private float _customMinRangeMeters = 0.5f;

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
        [SerializeField] private bool _logPerformanceDiagnostics;
        [Tooltip("When enabled, cap LiDAR scan cadence at Protected Scan Rate Hz so point-cloud work cannot dominate the main loop.")]
        [SerializeField] private bool _protectMainThreadFrameRate = true;
        [Tooltip("Stable LiDAR scan-rate cap used when main-thread protection is enabled. This is intentionally fixed, not frame-time feedback, to avoid point-cloud cadence jitter.")]
        [SerializeField, Min(0.1f)] private float _protectedScanRateHz = 2f;

        [SerializeField, Range(0, 1)] private float _syntheticReflectivity = 1f;
        [SerializeField, Range(0, 1)] private float _syntheticIntensity = 1f;

        /// <summary>The most recently generated PointCloudFrame, or null before the first scan.</summary>
        public PointCloudFrame LastFrame { get; private set; }

        /// <summary>Current Inspector rotation input mode for the T_IL override.</summary>
        public TIlRotationInputFormat TIlRotationFormat => _tIlRotationInputFormat;

        /// <summary>The selected model's default LiDAR-to-sensor extrinsic, or identity when no model default applies.</summary>
        public LidarTIlExtrinsic ModelLidarToSensor
        {
            get
            {
                if (ResolveSensorUnitProfile() != null)
                    return _sensorUnitProfile.ModelLidarToSensor;

                if (_profileSource == ProfileSource.BuiltInPreset &&
                    Sensors.Lidar.LidarModelRegistry.TryGet(_vendor, _model, out var spec))
                    return new LidarTIlExtrinsic(spec.LidarToSensorTranslationMeters, spec.LidarToSensorRotation);

                return LidarTIlExtrinsic.Identity;
            }
        }

        /// <summary>The selected model's default IMU-to-sensor extrinsic, or identity when no model default applies.</summary>
        public LidarTIlExtrinsic ModelImuToSensor
        {
            get
            {
                if (ResolveSensorUnitProfile() != null)
                    return _sensorUnitProfile.ModelImuToSensor;

                if (_profileSource == ProfileSource.BuiltInPreset &&
                    Sensors.Lidar.LidarModelRegistry.TryGet(_vendor, _model, out var spec))
                    return new LidarTIlExtrinsic(spec.ImuToSensorTranslationMeters, spec.ImuToSensorRotation);

                return LidarTIlExtrinsic.Identity;
            }
        }

        /// <summary>Legacy alias for the selected model's default IMU-to-sensor extrinsic.</summary>
        public LidarTIlExtrinsic ModelTIl => ModelImuToSensor;

        /// <summary>The effective IMU-to-sensor extrinsic after applying the optional component override.</summary>
        public LidarTIlExtrinsic EffectiveImuToSensor
            => ResolveSensorUnitProfile() != null
                ? _sensorUnitProfile.EffectiveImuToSensor
                : _overrideTIl
                ? new LidarTIlExtrinsic(
                    ToNumericsVector3(_tIlTranslationMeters),
                    ToNumericsQuaternion(_tIlRotation))
                : ModelImuToSensor;

        /// <summary>Legacy alias for the effective IMU-to-sensor extrinsic.</summary>
        public LidarTIlExtrinsic EffectiveTIl => EffectiveImuToSensor;

        /// <summary>Copy the currently selected model default into the editable override fields.</summary>
        public void CopyModelTIlToOverride()
        {
            if (ResolveSensorUnitProfile() != null)
            {
                _sensorUnitProfile.CopyModelImuToSensorToOverride();
                return;
            }

            var modelTIl = ModelImuToSensor;
            _tIlTranslationMeters = ToUnityVector3(modelTIl.TranslationMeters);
            _tIlRotation = ToUnityQuaternion(modelTIl.Rotation);
        }

        /// <summary>Convert a numerics vector to a Unity vector.</summary>
        public static Vector3 ToUnityVector3(System.Numerics.Vector3 value)
            => new Vector3(value.X, value.Y, value.Z);

        /// <summary>Convert a numerics quaternion to a normalized Unity quaternion.</summary>
        public static Quaternion ToUnityQuaternion(System.Numerics.Quaternion value)
        {
            var normalized = LidarTIlExtrinsic.NormalizeRotation(value);
            return new Quaternion(normalized.X, normalized.Y, normalized.Z, normalized.W);
        }

        /// <summary>Convert a Unity vector to a numerics vector.</summary>
        public static System.Numerics.Vector3 ToNumericsVector3(Vector3 value)
            => new System.Numerics.Vector3(value.x, value.y, value.z);

        /// <summary>Convert a Unity quaternion to a normalized numerics quaternion.</summary>
        public static System.Numerics.Quaternion ToNumericsQuaternion(Quaternion value)
            => LidarTIlExtrinsic.NormalizeRotation(
                new System.Numerics.Quaternion(value.x, value.y, value.z, value.w));

        private ILidarScanPattern _scanPattern;
        private int _frameCounter;
        private float _scanPeriod;

        // Uniform sensor clock: single epoch shared across LiDAR scan lifecycle.
        private bool _scanClockInitialized;
        private ulong _scanEpochUnixNs;
        private double _scanEpochPhysSeconds;

        // Stream state.
        private bool _hasPrevPose;
        private double _prevFixedTime;
        private double _scanColumnProgress;
        private int _scanColumnCursor;
        private int _scanColumnCount;
        private PointCloudFrame _activeScanFrame;
        private VirtualLidarPointData[] _activeScanPointSnapshot;
        private int _activeScanPointSnapshotCount;
        private int _activeScanValidPoints;
        private double _activeScanStartPhysSeconds;
        private enum PendingScanState
        {
            Idle,
            Scheduled,
            Consumed,
            Published
        }

        private PendingScanState _pendingScanState;
        private JobHandle _pendingScanHandle;
        private int _pendingBatchCount;
        private int[] _pendingScanCrossings = Array.Empty<int>();
        private int _pendingScanCrossingCount;
        private int _pendingProfileHash;
        private int _nextPendingScanId;
        private int _pendingScanId;

        private const int DiagnosticLogIntervalTicks = 60;
        private int _diagnosticTicks;
        private int _diagnosticScans;
        private long _diagnosticRays;
        private long _diagnosticValidPoints;
        private int _diagnosticOverruns;
        private double _diagnosticCompleteMsTotal;
        private double _diagnosticCompleteMsMax;
        private double _diagnosticBuildMsTotal;
        private double _diagnosticAppendMsTotal;
        // Rays grouped by column (built once) so a tick batch gathers a column's rays in
        // O(rays-in-column) instead of scanning all rays per column (the O(N^2) hot path).
        private int[][] _columnRays;
        // Ray-index positions inside the current tick batch where a revolution completes.
        private readonly System.Collections.Generic.List<int> _scanCrossings =
            new System.Collections.Generic.List<int>();

        // Batched-raycast buffers (reused each scan; raycasts run on worker threads).
        private NativeArray<RaycastCommand> _commands;
        private NativeArray<RaycastHit> _results;
        private NativeArray<float> _rayTimeOffsets;
        private NativeArray<ushort> _rayRings;
        private NativeArray<VirtualLidarPointData> _pointData;
        private int[] _rayColumns;
        private int _rawRayCount;       // pattern.RayCount
        private int _effectiveRayCount; // rays actually cast per scan (after budget)
        private int _rayStride;         // subsampling stride into the pattern
        private int _spinEffectiveColumns; // RayCount/Rings for spinning, else 0

        private void Start()
        {
            ResolveSensorUnitProfile();

            if (_manager == null)
                _manager = _sensorUnitProfile != null && _sensorUnitProfile.Manager != null
                    ? _sensorUnitProfile.Manager
                    : FindFirstObjectByType<FoxgloveManager>();

            if (_sensorUnitProfile != null)
            {
                _scanPattern = _sensorUnitProfile.CreateScanPattern(_columnStep);
            }
            else if (_profileSource == ProfileSource.BuiltInPreset)
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
                if (_sensorUnitProfile != null)
                    _pointCloudPublisher = _sensorUnitProfile.PointCloudPublisher;

                if (_pointCloudPublisher == null)
                    _pointCloudPublisher = GetComponentInParent<FoxglovePointCloudPublisher>();

                if (_pointCloudPublisher == null)
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

        private SensorUnitProfile ResolveSensorUnitProfile()
        {
            if (_sensorUnitProfile == null)
                _sensorUnitProfile = GetComponentInParent<SensorUnitProfile>();
            return _sensorUnitProfile;
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
            _pointData = new NativeArray<VirtualLidarPointData>(_effectiveRayCount, Allocator.Persistent);
            _activeScanPointSnapshot = new VirtualLidarPointData[_effectiveRayCount];
            _activeScanPointSnapshotCount = 0;
            _rayColumns = new int[_effectiveRayCount];
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

            // Bucket ray indices by column once (CSR-style jagged array) for O(1) column gather.
            var columnCounts = new int[_scanColumnCount];
            for (var k = 0; k < _effectiveRayCount; k++)
                columnCounts[_rayColumns[k]]++;
            _columnRays = new int[_scanColumnCount][];
            for (var c = 0; c < _scanColumnCount; c++)
                _columnRays[c] = new int[columnCounts[c]];
            var columnFill = new int[_scanColumnCount];
            for (var k = 0; k < _effectiveRayCount; k++)
            {
                var c = _rayColumns[k];
                _columnRays[c][columnFill[c]++] = k;
            }

            _pendingScanCrossings = new int[Math.Max(1, _scanColumnCount)];
        }

        private void DisposeScanBuffers()
        {
            DrainPendingScan();
            if (_commands.IsCreated) _commands.Dispose();
            if (_results.IsCreated) _results.Dispose();
            if (_rayTimeOffsets.IsCreated) _rayTimeOffsets.Dispose();
            if (_rayRings.IsCreated) _rayRings.Dispose();
            if (_pointData.IsCreated) _pointData.Dispose();
            _rayColumns = null;
            _columnRays = null;
            _activeScanPointSnapshot = null;
            _activeScanPointSnapshotCount = 0;
            _commands = default;
            _results = default;
            _rayTimeOffsets = default;
            _rayRings = default;
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

            ConsumePendingScan();

            if (_activeScanFrame == null)
                StartNewScan(Time.fixedTimeAsDouble);

            var nowPhys = Time.fixedTimeAsDouble;
            if (!_hasPrevPose)
            {
                _hasPrevPose = true;
                _prevFixedTime = nowPhys;
                return;
            }

            var dt = nowPhys - _prevFixedTime;
            _prevFixedTime = nowPhys;
            if (dt <= 0d)
                return;

            // Columns to advance this tick; carry the fractional remainder to avoid drift.
            _scanColumnProgress += dt * _scanColumnCount / Math.Max(1e-12, ComputeProtectedScanPeriodSeconds());
            var columnsToEmit = (int)Math.Floor(_scanColumnProgress);
            if (columnsToEmit <= 0)
                return;
            _scanColumnProgress -= columnsToEmit;

            SchedulePendingScan(columnsToEmit);
        }

        private void SchedulePendingScan(int columnsToEmit)
        {
            if (_pendingScanState == PendingScanState.Scheduled)
            {
                RecordLidarDiagnostics(0, 0, 0d, 0d, 0d, asyncOverrun: true);
                return;
            }

            // One tick-end pose for the whole batch: a single worldToLocal keeps the build
            // job unchanged (no per-ray matrix). One revolution => _scanColumnCount poses,
            // enough for IMU de-skew. Per-column pose interpolation is a future option.
            var worldPos = transform.position;
            var worldRot = transform.rotation;
            var worldToLocal = Matrix4x4.TRS(worldPos, worldRot, Vector3.one).inverse.ToFloat4x4();
            var queryParams = new QueryParameters(_layerMask.value);

            // Build one batch for all columns this tick (cap at one revolution).
            _scanCrossings.Clear();
            var batchCount = 0;
            for (var c = 0; c < columnsToEmit && batchCount < _effectiveRayCount; c++)
            {
                var rays = _columnRays[_scanColumnCursor];
                for (var r = 0; r < rays.Length && batchCount < _effectiveRayCount; r++)
                {
                    var k = rays[r];
                    var index = k * _rayStride;
                    if (index >= _rawRayCount) index = _rawRayCount - 1;

                    if (!_scanPattern.TryGetRay(index, _frameCounter, out var localDir, out var timeOffset))
                    {
                        _commands[batchCount] = new RaycastCommand(worldPos, Vector3.forward, queryParams, 0f);
                        _rayTimeOffsets[batchCount] = 0f;
                        _rayRings[batchCount] = 0;
                    }
                    else
                    {
                        var worldDir = worldRot * new Vector3(localDir.X, localDir.Y, localDir.Z);
                        _commands[batchCount] = new RaycastCommand(worldPos, worldDir, queryParams, _maxRangeMeters);
                        _rayTimeOffsets[batchCount] = timeOffset;
                        _rayRings[batchCount] = _spinEffectiveColumns > 0 ? (ushort)(index / _spinEffectiveColumns) : (ushort)0;
                    }
                    batchCount++;
                }

                _scanColumnCursor++;
                if (_scanColumnCursor >= _scanColumnCount)
                {
                    _scanCrossings.Add(batchCount);
                    _scanColumnCursor = 0;
                }
            }

            if (batchCount <= 0)
                return;

            _pendingScanCrossingCount = Math.Min(_scanCrossings.Count, _pendingScanCrossings.Length);
            for (var i = 0; i < _pendingScanCrossingCount; i++)
                _pendingScanCrossings[i] = _scanCrossings[i];

            _pendingBatchCount = batchCount;
            _pendingProfileHash = ComputeScanProfileHash();
            _pendingScanId = ++_nextPendingScanId;
            var raycastHandle = RaycastCommand.ScheduleBatch(
                _commands.GetSubArray(0, batchCount),
                _results.GetSubArray(0, batchCount),
                64);
            var minRange = (float)_scanPattern.MinRangeMeters;
            var buildJob = new VirtualLidarBuildPointsJob
            {
                Hits = _results,
                RayTimeOffsets = _rayTimeOffsets,
                RayRings = _rayRings,
                WorldToLocal = worldToLocal,
                MinRange = minRange,
                MaxRange = _maxRangeMeters,
                SyntheticIntensity = _syntheticIntensity,
                SyntheticReflectivity = _syntheticReflectivity,
                Points = _pointData
            };
            _pendingScanHandle = buildJob.Schedule(batchCount, 64, raycastHandle);
            _pendingScanState = PendingScanState.Scheduled;
        }

        private void ConsumePendingScan()
        {
            if (_pendingScanState != PendingScanState.Scheduled || _pendingBatchCount <= 0)
                return;

            var completeStart = DiagnosticStart();
            _pendingScanHandle.Complete();
            var completeMs = DiagnosticElapsedMs(completeStart);
            _pendingScanState = PendingScanState.Consumed;

            if (_pendingProfileHash != ComputeScanProfileHash())
            {
                RecordLidarDiagnostics(_pendingBatchCount, 0, completeMs, 0d, 0d, asyncOverrun: true);
                ClearPendingScan();
                return;
            }

            // BuildPointsJob is now chained behind RaycastCommand; any remaining wait is
            // included in completeMs, and there is no separate main-thread build phase here.
            var buildMs = 0d;

            // Distribute points to the active frame, publishing each completed revolution.
            var appendStart = DiagnosticStart();
            var validPoints = 0;
            var ci = 0;
            var segmentStart = 0;
            var useNativeDraco = UseNativeDracoPointCloudPath();
            for (var k = 0; k < _pendingBatchCount; k++)
            {
                while (ci < _pendingScanCrossingCount && k == _pendingScanCrossings[ci])
                {
                    AppendOrCopyPendingPointDataSegment(segmentStart, k - segmentStart, useNativeDraco, ref validPoints);
                    PublishActiveScan();
                    _pendingScanState = PendingScanState.Published;
                    StartNewScan(_activeScanStartPhysSeconds + ComputeProtectedScanPeriodSeconds());
                    segmentStart = k;
                    ci++;
                }
            }

            AppendOrCopyPendingPointDataSegment(segmentStart, _pendingBatchCount - segmentStart, useNativeDraco, ref validPoints);
            while (ci < _pendingScanCrossingCount && _pendingBatchCount == _pendingScanCrossings[ci])
            {
                PublishActiveScan();
                _pendingScanState = PendingScanState.Published;
                StartNewScan(_activeScanStartPhysSeconds + ComputeProtectedScanPeriodSeconds());
                ci++;
            }

            var appendMs = DiagnosticElapsedMs(appendStart);
            RecordLidarDiagnostics(_pendingBatchCount, validPoints, completeMs, buildMs, appendMs, asyncOverrun: false);
            ClearPendingScan();
        }

        private bool UseNativeDracoPointCloudPath()
            => _pointCloudPublisher != null && _pointCloudPublisher.CanQueueVirtualLidarDracoFrame;

        private void AppendOrCopyPendingPointDataSegment(int sourceStart, int length, bool useNativeDraco, ref int validPoints)
        {
            if (length <= 0)
                return;

            if (useNativeDraco)
            {
                CopyPendingPointDataSegment(sourceStart, length);
                return;
            }

            AppendPendingPointDataSegment(sourceStart, length, ref validPoints);
        }

        private void CopyPendingPointDataSegment(int sourceStart, int length)
        {
            if (length <= 0)
                return;

            if (_activeScanPointSnapshot == null || _activeScanPointSnapshot.Length < _effectiveRayCount)
                _activeScanPointSnapshot = new VirtualLidarPointData[_effectiveRayCount];

            var writableLength = Math.Min(length, _activeScanPointSnapshot.Length - _activeScanPointSnapshotCount);
            if (writableLength <= 0)
                return;

            NativeArray<VirtualLidarPointData>.Copy(
                _pointData,
                sourceStart,
                _activeScanPointSnapshot,
                _activeScanPointSnapshotCount,
                writableLength);
            _activeScanPointSnapshotCount += writableLength;
        }

        private void AppendPendingPointDataSegment(int sourceStart, int length, ref int validPoints)
        {
            var end = Math.Min(_pendingBatchCount, sourceStart + length);
            for (var k = sourceStart; k < end; k++)
            {
                var point = _pointData[k];
                if (point.IsValid == 0)
                    continue;

                _activeScanFrame.Points.Add(new PointCloudPoint(point.X, point.Y, point.Z)
                {
                    Intensity = point.Intensity,
                    Reflectivity = point.Reflectivity,
                    TimeOffsetSeconds = point.TimeOffsetSeconds,
                    Ring = point.Ring
                });
                _activeScanValidPoints++;
                validPoints++;
            }
        }

        private void DrainPendingScan()
        {
            if (_pendingScanState == PendingScanState.Scheduled)
                _pendingScanHandle.Complete();

            ClearPendingScan();
        }

        private void ClearPendingScan()
        {
            _pendingScanHandle = default;
            _pendingScanState = PendingScanState.Idle;
            _pendingBatchCount = 0;
            _pendingScanCrossingCount = 0;
            _pendingProfileHash = 0;
            _pendingScanId = 0;
        }

        private int ComputeScanProfileHash()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _rawRayCount;
                hash = hash * 31 + _effectiveRayCount;
                hash = hash * 31 + _rayStride;
                hash = hash * 31 + _scanColumnCount;
                return hash;
            }
        }

        private long DiagnosticStart()
            => _logPerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0L;

        private double DiagnosticElapsedMs(long startTicks)
            => startTicks == 0L
                ? 0d
                : (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        private void RecordLidarDiagnostics(
            int rayCount,
            int validPointCount,
            double completeMs,
            double buildMs,
            double appendMs,
            bool asyncOverrun)
        {
            if (!_logPerformanceDiagnostics)
                return;

            _diagnosticTicks++;
            _diagnosticScans++;
            _diagnosticRays += Math.Max(0, rayCount);
            _diagnosticValidPoints += Math.Max(0, validPointCount);
            _diagnosticCompleteMsTotal += completeMs;
            _diagnosticCompleteMsMax = Math.Max(_diagnosticCompleteMsMax, completeMs);
            _diagnosticBuildMsTotal += buildMs;
            _diagnosticAppendMsTotal += appendMs;
            if (asyncOverrun || completeMs > Time.fixedDeltaTime * 1000d)
                _diagnosticOverruns++;

            if (_diagnosticTicks < DiagnosticLogIntervalTicks)
                return;

            var divisor = Math.Max(1, _diagnosticScans);
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                "[LidarDiag] scanId={0} scans={1} rays={2} valid={3} completeMs avg={4:F2} max={5:F2} buildMs avg={6:F2} appendMs avg={7:F2} overrun={8}",
                _pendingScanId,
                _diagnosticScans,
                _diagnosticRays,
                _diagnosticValidPoints,
                _diagnosticCompleteMsTotal / divisor,
                _diagnosticCompleteMsMax,
                _diagnosticBuildMsTotal / divisor,
                _diagnosticAppendMsTotal / divisor,
                _diagnosticOverruns);

            _diagnosticTicks = 0;
            _diagnosticScans = 0;
            _diagnosticRays = 0;
            _diagnosticValidPoints = 0;
            _diagnosticOverruns = 0;
            _diagnosticCompleteMsTotal = 0d;
            _diagnosticCompleteMsMax = 0d;
            _diagnosticBuildMsTotal = 0d;
            _diagnosticAppendMsTotal = 0d;
        }

        private void StartNewScan(double scanStartPhysSeconds)
        {
            EnsureScanClock(Time.fixedTimeAsDouble);

            // Note: scan phase (_scanColumnProgress/_scanColumnCursor) is owned by FixedUpdate
            // and ResetScanState; StartNewScan must not clear it or a cross-revolution restart
            // would drop the in-tick remainder.
            _activeScanStartPhysSeconds = scanStartPhysSeconds;
            _activeScanFrame = new PointCloudFrame
            {
                UnixNs = ComputeScanStartUnixNs(scanStartPhysSeconds),
                FrameId = _frameId,
                ValidCount = 0,
                // SLAM front-ends (FAST-LIO/LIVO2) consume the Ouster-style absolute-ns `t`.
                EmitAbsoluteTimeNs = true
            };
            _activeScanValidPoints = 0;
            _activeScanPointSnapshotCount = 0;
            if (UseNativeDracoPointCloudPath())
            {
                if (_activeScanPointSnapshot == null || _activeScanPointSnapshot.Length < _effectiveRayCount)
                    _activeScanPointSnapshot = new VirtualLidarPointData[_effectiveRayCount];
            }
            else
            {
                _activeScanFrame.Points.Clear();
                if (_activeScanFrame.Points.Capacity < _effectiveRayCount)
                    _activeScanFrame.Points.Capacity = _effectiveRayCount;
            }
        }

        private void PublishActiveScan()
        {
            if (_activeScanFrame == null)
                return;

            _activeScanFrame.ValidCount = _activeScanValidPoints > 0
                ? _activeScanValidPoints
                : _activeScanPointSnapshotCount;
            LastFrame = _activeScanFrame;

            var hasNativeSnapshot = _activeScanPointSnapshotCount > 0;
            if (_pointCloudPublisher != null && (_activeScanValidPoints > 0 || hasNativeSnapshot || _publishEmptyFrames))
            {
                if (!TryPublishActiveNativeDracoScan())
                    _pointCloudPublisher.SetFrame(_activeScanFrame);
            }

            _frameCounter++;
        }

        private bool TryPublishActiveNativeDracoScan()
        {
            if (!UseNativeDracoPointCloudPath()
                || _activeScanPointSnapshot == null
                || _activeScanPointSnapshotCount <= 0)
                return false;

            var snapshot = _activeScanPointSnapshot;
            var snapshotCount = _activeScanPointSnapshotCount;
            if (!_pointCloudPublisher.TryQueueVirtualLidarDracoFrame(
                    snapshot,
                    snapshotCount,
                    _activeScanFrame.UnixNs,
                    _activeScanFrame.FrameId,
                    _activeScanFrame.EmitAbsoluteTimeNs))
                return false;

            _activeScanPointSnapshot = null;
            _activeScanPointSnapshotCount = 0;
            return true;
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

        private double ComputeProtectedScanPeriodSeconds()
        {
            var basePeriod = Math.Max(1e-12, _scanPeriod);
            if (!_protectMainThreadFrameRate)
                return basePeriod;

            var protectedRateHz = Math.Max(0.1f, _protectedScanRateHz);
            return Math.Max(basePeriod, 1d / protectedRateHz);
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

        private void OnValidate()
        {
            _columnStep = Math.Max(1, _columnStep);
            _maxRangeMeters = Math.Max(0f, _maxRangeMeters);
            if (_scanSubSteps < 1)
                _scanSubSteps = 1;
            _protectedScanRateHz = Math.Max(0.1f, _protectedScanRateHz);
        }
    }
}
