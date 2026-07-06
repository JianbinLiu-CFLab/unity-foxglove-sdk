// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
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
        [Tooltip("Physics layers included in LiDAR raycasts. Exclude the sensor/vehicle's own layer to avoid self-collision returns; Min Range is not a replacement for self-layer exclusion.")]
        [SerializeField] private LayerMask _layerMask = Physics.DefaultRaycastLayers;
        [SerializeField] private bool _publishEmptyFrames;
        [SerializeField] private bool _drawDebugRays;
        [SerializeField] private bool _logPerformanceDiagnostics;
        [Tooltip("Maximum RaycastCommands scheduled per FixedUpdate. This is the real main-thread protection: it caps how much PhysX raycast work one physics tick can block on (the Complete() call). The scan rate falls out of it automatically (rate ~= budget * physicsHz / raysPerScan), so a full-fidelity scan simply publishes slower instead of stalling TF/camera/IMU. Lower it if the main loop still drops; raise it for a faster point cloud.")]
        [SerializeField, Min(256)] private int _maxRaycastCommandsPerFixedUpdate = 6144;

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
            => LidarUnityNumericsConversions.ToUnityVector3(value);

        /// <summary>Convert a numerics quaternion to a normalized Unity quaternion.</summary>
        public static Quaternion ToUnityQuaternion(System.Numerics.Quaternion value)
            => LidarUnityNumericsConversions.ToUnityQuaternion(value);

        /// <summary>Convert a Unity vector to a numerics vector.</summary>
        public static System.Numerics.Vector3 ToNumericsVector3(Vector3 value)
            => LidarUnityNumericsConversions.ToNumericsVector3(value);

        /// <summary>Convert a Unity quaternion to a normalized numerics quaternion.</summary>
        public static System.Numerics.Quaternion ToNumericsQuaternion(Quaternion value)
            => LidarUnityNumericsConversions.ToNumericsQuaternion(value);

        private ILidarScanPattern _scanPattern;
        private int _frameCounter;
        private float _scanPeriod;

        private readonly VirtualLidarScanClock _scanClock = new VirtualLidarScanClock();
        private readonly VirtualLidarScanBuffers _scanBuffers = new VirtualLidarScanBuffers();
        private readonly VirtualLidarScanFramePublisher _scanFramePublisher = new VirtualLidarScanFramePublisher();
        private LidarScanBoundaryHandler _onScanBoundary;
        private VirtualLidarScanScheduler _scanScheduler;

        private static readonly ProfilerMarker FixedUpdateMarker = new ProfilerMarker("VirtualLidar.FixedUpdate");

        // Stream state.
        private bool _hasPrevPose;
        private double _prevFixedTime;
        private double _scanColumnProgress;
        private int _scanColumnCursor;
        private PointCloudFrame _activeScanFrame;
        private VirtualLidarPointData[] _activeScanPointSnapshot;
        private int _activeScanPointSnapshotCount;
        private int _activeScanValidPoints;
        private float4x4 _activeScanWorldToLocal;

        private VirtualLidarScanScheduler ScanScheduler => _scanScheduler ??= new VirtualLidarScanScheduler(this);

        private LidarScanBoundaryHandler OnScanBoundaryAction => _onScanBoundary ??= OnScanBoundary;

        private void OnScanBoundary(ref LidarScanBoundaryTimings timings)
        {
            var publishStart = timings.Start();
            PublishActiveScan(ref timings);
            timings.PublishActiveScanMs += timings.ElapsedMs(publishStart);
            var startNewScanStart = timings.Start();
            StartNewScan(Time.fixedTimeAsDouble);
            timings.StartNewScanMs += timings.ElapsedMs(startNewScanStart);
        }

        private void Start()
        {
            ResolveSensorUnitProfile();

            if (_manager == null)
                _manager = _sensorUnitProfile != null && _sensorUnitProfile.Manager != null
                    ? _sensorUnitProfile.Manager
                    : FindFirstObjectByType<FoxgloveManager>();

            WarnIfOwnLayerIncludedInRaycastMask();

            if (_sensorUnitProfile != null)
            {
                _scanPattern = _sensorUnitProfile.CreateScanPattern(_columnStep);
            }
            else if (_profileSource == ProfileSource.BuiltInPreset)
            {
                if (Sensors.Lidar.LidarModelRegistry.TryGet(_vendor, _model, out var spec))
                    _scanPattern = Sensors.Lidar.LidarScanPatternFactory.Create(spec, _mode, _columnStep);
                else
                    Debug.LogWarning($"[VirtualLidar] Unknown built-in LiDAR model '{_model}', using OS-1-32 fallback.");
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
            _pointCloudPublisher?.MarkSourceDrivenPointCloud();

            var rateHz = _scanRateSource == ScanRateSource.Override && _scanRateHzOverride > 0f
                ? _scanRateHzOverride
                : _scanPattern.ScanRateHz;
            _scanPeriod = rateHz > 0f ? (1f / (float)rateHz) : 0.1f;

            AllocateScanBuffers();
            _scanClock.Reset();
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
            _scanBuffers.Allocate(_scanPattern, _maxRaysPerScan);
            _activeScanPointSnapshotCount = 0;
        }

        private void DisposeScanBuffers()
        {
            ScanScheduler.DrainPendingScan();
            _scanBuffers.Dispose();
            ReleaseActiveScanSnapshot();
        }

        private void OnDestroy()
        {
            DisposeScanBuffers();
        }

        private void OnEnable()
        {
            AllocateScanBuffers();
            if (_scanPattern != null)
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
            using (FixedUpdateMarker.Auto())
            {
                if (_scanPattern == null || !_scanBuffers.IsCreated || _scanBuffers.EffectiveRayCount <= 0)
                    return;

                if (_scanPeriod <= 0f || _scanBuffers.ScanColumnCount <= 0)
                    return;

                EnsureScanClock(Time.fixedTimeAsDouble);

                ScanScheduler.ConsumePendingScan(
                    _logPerformanceDiagnostics,
                    Time.fixedDeltaTime,
                    UseNativePointCloudSnapshotPath(),
                    _scanBuffers,
                    ref _activeScanFrame,
                    ref _activeScanPointSnapshot,
                    ref _activeScanPointSnapshotCount,
                    ref _activeScanValidPoints,
                    OnScanBoundaryAction);

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

                // Columns this scan rate wants to advance this tick; carry the remainder.
                _scanColumnProgress += dt * _scanBuffers.ScanColumnCount / Math.Max(1e-12, (double)_scanPeriod);

                // Hard cap on per-tick raycast work: the real fix. PhysX must finish the batch
                // within one fixed step or RaycastCommand.Complete() blocks the physics loop and
                // starves TF/camera/render. When the budget can not keep up with the nominal scan
                // rate, the scan just spans more ticks (lower effective Hz), never a stall.
                var budgetColumns = BudgetColumnsPerTick();

                // Never let the backlog grow past one revolution, or a slow start would burst a
                // giant batch and reintroduce the very stall we are preventing.
                var maxProgress = _scanBuffers.ScanColumnCount + budgetColumns;
                if (_scanColumnProgress > maxProgress)
                    _scanColumnProgress = maxProgress;

                // Keep one scheduled batch inside the current revolution. A completed scan has
                // one reference pose; crossing into the next revolution inside the same build job
                // would mix two scan frames through one world-to-local matrix.
                Debug.Assert(_scanColumnCursor >= 0 && _scanColumnCursor < _scanBuffers.ScanColumnCount);
                var remainingColumns = _scanBuffers.ScanColumnCount - _scanColumnCursor;
                if (remainingColumns <= 0)
                    remainingColumns = _scanBuffers.ScanColumnCount;

                var columnsToEmit = Math.Min((int)Math.Floor(_scanColumnProgress),
                    Math.Min(budgetColumns, remainingColumns));
                if (columnsToEmit <= 0)
                    return;
                _scanColumnProgress -= columnsToEmit;

                var scheduleStart = BeginLidarFixedUpdateTiming();
                ScanScheduler.SchedulePendingScan(
                    columnsToEmit,
                    _logPerformanceDiagnostics,
                    Time.fixedDeltaTime,
                    _frameCounter,
                    ref _scanColumnCursor,
                    transform.position,
                    transform.rotation,
                    _layerMask,
                    _maxRangeMeters,
                    _syntheticIntensity,
                    _syntheticReflectivity,
                    _scanPattern,
                    _activeScanWorldToLocal,
                    RequiresNativeAcquisitionFrame(),
                    _scanBuffers);
                LogLidarFixedUpdateTiming(
                    _logPerformanceDiagnostics,
                    this,
                    columnsToEmit,
                    budgetColumns,
                    _scanColumnCursor,
                    _scanColumnProgress,
                    _scanBuffers.EffectiveRayCount,
                    Time.fixedDeltaTime,
                    ElapsedLidarFixedUpdateTiming(scheduleStart));
            }
        }

        private long BeginLidarFixedUpdateTiming()
            => _logPerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0L;

        private static double ElapsedLidarFixedUpdateTiming(long startTicks)
            => startTicks == 0L ? 0d : (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        private static void LogLidarFixedUpdateTiming(
            bool logPerformanceDiagnostics,
            UnityEngine.Object context,
            int columnsToEmit,
            int budgetColumns,
            int scanColumnCursor,
            double scanColumnProgress,
            int effectiveRayCount,
            float fixedDeltaTimeSeconds,
            double scheduleMs)
        {
            if (!logPerformanceDiagnostics)
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                context,
                "[LidarDiag] fixed-update timing: columnsToEmit={0} budgetColumns={1} cursor={2} progress={3:F2} effectiveRays={4} fixedDeltaMs={5:F2} scheduleMs={6:F2}",
                columnsToEmit,
                budgetColumns,
                scanColumnCursor,
                scanColumnProgress,
                effectiveRayCount,
                fixedDeltaTimeSeconds * 1000f,
                scheduleMs);
        }

        private void StartNewScan(double scanStartPhysSeconds)
        {
            EnsureScanClock(Time.fixedTimeAsDouble);

            // Note: scan phase (_scanColumnProgress/_scanColumnCursor) is owned by FixedUpdate
            // and ResetScanState; StartNewScan must not clear it or a cross-revolution restart
            // would drop the in-tick remainder.
            _activeScanWorldToLocal = CoordinateConverterFloat3.RigidWorldToLocal(transform.position, transform.rotation);
            _activeScanFrame = new PointCloudFrame
            {
                UnixNs = _scanClock.GetScanStartUnixNs(scanStartPhysSeconds),
                FrameId = _frameId,
                ValidCount = 0,
                // SLAM front-ends (FAST-LIO/LIVO2) consume the Ouster-style absolute-ns `t`.
                EmitAbsoluteTimeNs = true
            };
            _activeScanValidPoints = 0;
            _activeScanPointSnapshotCount = 0;
            if (UseNativePointCloudSnapshotPath())
            {
                EnsureActiveScanSnapshotCapacity();
            }
            else
            {
                ReleaseActiveScanSnapshot();
                _activeScanFrame.Points.Clear();
                if (_activeScanFrame.Points.Capacity < _scanBuffers.EffectiveRayCount)
                    _activeScanFrame.Points.Capacity = _scanBuffers.EffectiveRayCount;
            }
        }

        private void EnsureActiveScanSnapshotCapacity()
        {
            if (_activeScanPointSnapshot != null && _activeScanPointSnapshot.Length >= _scanBuffers.EffectiveRayCount)
                return;

            var nextSnapshot = VirtualLidarPointSnapshotPool.Rent(_scanBuffers.EffectiveRayCount);
            if (_activeScanPointSnapshot != null && _activeScanPointSnapshotCount > 0)
                Array.Copy(_activeScanPointSnapshot, nextSnapshot, Math.Min(_activeScanPointSnapshotCount, nextSnapshot.Length));

            VirtualLidarPointSnapshotPool.Return(_activeScanPointSnapshot);
            _activeScanPointSnapshot = nextSnapshot;
        }

        private void ReleaseActiveScanSnapshot()
        {
            VirtualLidarPointSnapshotPool.Return(_activeScanPointSnapshot);
            _activeScanPointSnapshot = null;
            _activeScanPointSnapshotCount = 0;
        }

        private bool UseNativePointCloudSnapshotPath()
            => _pointCloudPublisher != null && _pointCloudPublisher.CanQueueVirtualLidarNativeFrame;

        private bool RequiresNativeAcquisitionFrame()
            => _pointCloudPublisher != null && _pointCloudPublisher.RequiresVirtualLidarAcquisitionFrame;

        private void PublishActiveScan(ref LidarScanBoundaryTimings timings)
        {
            if (_activeScanFrame == null)
                return;

            _scanFramePublisher.TryPublishActiveScan(
                _pointCloudPublisher,
                _publishEmptyFrames,
                _activeScanFrame,
                _activeScanValidPoints,
                ref _activeScanPointSnapshot,
                ref _activeScanPointSnapshotCount,
                ref timings);

            LastFrame = _activeScanFrame;
            _frameCounter++;
        }

        // Largest number of whole columns whose rays fit inside one FixedUpdate's raycast
        // budget. With OS-2-128 (128 rays/column) and a 6144 budget that is 48 columns/tick,
        // i.e. ~1.2 Hz full-fidelity at 50 Hz physics: slow but rock-steady, with TF/camera
        // and the main loop fully protected.
        private int BudgetColumnsPerTick()
            => _scanBuffers.BudgetColumnsPerTick(_maxRaycastCommandsPerFixedUpdate);

        private void EnsureScanClock(double physNow)
        {
            if (_scanClock.IsInitialized)
                return;

            Func<double, ulong> resolveUnixNs = _manager == null
                ? null
                : _manager.GetSharedSensorClockUnixTime;
            if (_scanClock.EnsureInitialized(physNow, resolveUnixNs))
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
            if (_maxRaycastCommandsPerFixedUpdate < 256)
                _maxRaycastCommandsPerFixedUpdate = 256;
        }

        private void WarnIfOwnLayerIncludedInRaycastMask()
        {
            var ownLayerMask = 1 << gameObject.layer;
            if ((_layerMask.value & ownLayerMask) == 0)
                return;

            Debug.LogWarning(
                "[VirtualLidar] LiDAR raycast Layer Mask includes this GameObject's layer. " +
                "Move the sensor/vehicle to an excluded layer or remove that layer from the mask to avoid self-collision point-cloud returns.",
                this);
        }
    }
}
