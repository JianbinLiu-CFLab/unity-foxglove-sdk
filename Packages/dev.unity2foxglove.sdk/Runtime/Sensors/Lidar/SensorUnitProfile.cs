// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using Unity.FoxgloveSDK.Sensors.Lidar;
using UnityEngine;
using NumericQuaternion = System.Numerics.Quaternion;
using NumericVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Shared profile and factory extrinsic settings for a LiDAR/IMU sensor unit.
    /// Attach this to the physical unit frame GameObject (for Ouster, os_sensor).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Foxglove/Sensors/Sensor Unit Profile")]
    public class SensorUnitProfile : MonoBehaviour
    {
        /// <summary>Where the LiDAR scan geometry comes from.</summary>
        public enum ProfileSource
        {
            /// <summary>Parse an Ouster-format metadata JSON TextAsset.</summary>
            MetadataJson,
            /// <summary>Use the built-in spinning-LiDAR preset registry.</summary>
            BuiltInPreset,
            /// <summary>Use the manually edited Custom Profile fields below.</summary>
            Custom
        }

        /// <summary>Inspector input mode for editing extrinsic rotations.</summary>
        public enum RotationInputFormat
        {
            /// <summary>Edit the rotation as quaternion x/y/z/w.</summary>
            Quaternion,
            /// <summary>Edit the rotation as a row-major 3x3 matrix.</summary>
            Matrix3x3
        }

        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;

        [SerializeField] private ProfileSource _profileSource = ProfileSource.BuiltInPreset;
        [Tooltip("Used when Profile Source = MetadataJson.")]
        [SerializeField] private TextAsset _metadataJson;
        [Tooltip("Mode string for metadata parsing.")]
        [SerializeField] private string _metadataMode = "1024x10";
        [Tooltip("Used when Profile Source = BuiltInPreset.")]
        [SerializeField] private LidarVendor _vendor = LidarVendor.Ouster;
        [Tooltip("Model identifier within the vendor (e.g. OS-1-32).")]
        [SerializeField] private string _model = "OS-1-32";
        [Tooltip("Optional scan mode for models that support multiple modes.")]
        [SerializeField] private string _mode = "1024x10";

        [SerializeField] private string _sensorFrameId = "os_sensor";
        [SerializeField] private string _lidarFrameId = "os_lidar";
        [SerializeField] private string _imuFrameId = "os_imu";

        [SerializeField] private bool _useLidarToSensorExtrinsic = true;
        [SerializeField] private bool _useImuToSensorExtrinsic = true;
        [SerializeField] private bool _useLidarToImuExtrinsic;

        [SerializeField] private bool _overrideLidarToSensor;
        [SerializeField] private RotationInputFormat _lidarToSensorRotationInputFormat = RotationInputFormat.Quaternion;
        [SerializeField] private Vector3 _lidarToSensorTranslationMeters = new Vector3(0f, 0f, 0.038195f);
        [SerializeField] private Quaternion _lidarToSensorRotation = new Quaternion(0f, 0f, 1f, 0f);

        [SerializeField] private bool _overrideImuToSensor;
        [SerializeField] private RotationInputFormat _imuToSensorRotationInputFormat = RotationInputFormat.Quaternion;
        [SerializeField] private Vector3 _imuToSensorTranslationMeters = new Vector3(-0.002441f, -0.009725f, 0.007533f);
        [SerializeField] private Quaternion _imuToSensorRotation = Quaternion.identity;

        [SerializeField] private bool _overrideLidarToImu;
        [SerializeField] private RotationInputFormat _lidarToImuRotationInputFormat = RotationInputFormat.Quaternion;
        [SerializeField] private Vector3 _lidarToImuTranslationMeters = new Vector3(0.002441f, 0.009725f, 0.030662f);
        [SerializeField] private Quaternion _lidarToImuRotation = new Quaternion(0f, 0f, 1f, 0f);

        [SerializeField, Min(1)] private int _customPixelsPerColumn = 32;
        [SerializeField] private float _customFovTopDeg = 16.6f;
        [SerializeField] private float _customFovBottomDeg = -16.6f;
        [SerializeField, Min(16)] private int _customColumnsPerFrame = 1024;
        [SerializeField, Min(1f)] private float _customScanRateHz = 10f;
        [SerializeField, Min(0f)] private float _customMinRangeMeters = 0.5f;

        /// <summary>Manager used by publishers in this sensor unit.</summary>
        public FoxgloveManager Manager => _manager;

        /// <summary>Point-cloud publisher owned by this sensor unit.</summary>
        public FoxglovePointCloudPublisher PointCloudPublisher => _pointCloudPublisher;

        /// <summary>Common sensor body frame, e.g. os_sensor.</summary>
        public string SensorFrameId => string.IsNullOrWhiteSpace(_sensorFrameId) ? "os_sensor" : _sensorFrameId;

        /// <summary>LiDAR measurement frame, e.g. os_lidar.</summary>
        public string LidarFrameId => string.IsNullOrWhiteSpace(_lidarFrameId) ? "os_lidar" : _lidarFrameId;

        /// <summary>IMU measurement frame, e.g. os_imu.</summary>
        public string ImuFrameId => string.IsNullOrWhiteSpace(_imuFrameId) ? "os_imu" : _imuFrameId;

        /// <summary>Whether LiDAR-to-sensor is one of the two authored extrinsics.</summary>
        public bool UseLidarToSensorExtrinsic => _useLidarToSensorExtrinsic;

        /// <summary>Whether IMU-to-sensor is one of the two authored extrinsics.</summary>
        public bool UseImuToSensorExtrinsic => _useImuToSensorExtrinsic;

        /// <summary>Whether LiDAR-to-IMU is one of the two authored extrinsics.</summary>
        public bool UseLidarToImuExtrinsic => _useLidarToImuExtrinsic;

        /// <summary>Inspector rotation input mode for the LiDAR-to-sensor override.</summary>
        public RotationInputFormat LidarToSensorRotationFormat => _lidarToSensorRotationInputFormat;

        /// <summary>Inspector rotation input mode for the IMU-to-sensor override.</summary>
        public RotationInputFormat ImuToSensorRotationFormat => _imuToSensorRotationInputFormat;

        /// <summary>Inspector rotation input mode for the LiDAR-to-IMU override.</summary>
        public RotationInputFormat LidarToImuRotationFormat => _lidarToImuRotationInputFormat;

        /// <summary>The selected model's default LiDAR-to-sensor extrinsic.</summary>
        public LidarTIlExtrinsic ModelLidarToSensor
        {
            get
            {
                if (TryGetBuiltinSpec(out var spec))
                    return new LidarTIlExtrinsic(spec.LidarToSensorTranslationMeters, spec.LidarToSensorRotation);
                return LidarTIlExtrinsic.Identity;
            }
        }

        /// <summary>The selected model's default IMU-to-sensor extrinsic.</summary>
        public LidarTIlExtrinsic ModelImuToSensor
        {
            get
            {
                if (TryGetBuiltinSpec(out var spec))
                    return new LidarTIlExtrinsic(spec.ImuToSensorTranslationMeters, spec.ImuToSensorRotation);
                return LidarTIlExtrinsic.Identity;
            }
        }

        /// <summary>The selected model's default LiDAR-to-IMU extrinsic derived from child-to-sensor metadata.</summary>
        public LidarTIlExtrinsic ModelLidarToImu
            => Compose(ModelLidarToSensor, Invert(ModelImuToSensor));

        /// <summary>Effective LiDAR-to-sensor extrinsic after optional per-unit override.</summary>
        public LidarTIlExtrinsic EffectiveLidarToSensor
        {
            get
            {
                NormalizeExtrinsicSelection();
                return _useLidarToSensorExtrinsic
                    ? AuthoredLidarToSensor
                    : Compose(AuthoredLidarToImu, AuthoredImuToSensor);
            }
        }

        /// <summary>Effective IMU-to-sensor extrinsic after optional per-unit override.</summary>
        public LidarTIlExtrinsic EffectiveImuToSensor
        {
            get
            {
                NormalizeExtrinsicSelection();
                return _useImuToSensorExtrinsic
                    ? AuthoredImuToSensor
                    : Compose(Invert(AuthoredLidarToImu), AuthoredLidarToSensor);
            }
        }

        /// <summary>Derived LiDAR-to-IMU extrinsic, suitable for FAST-LIO2-style configs.</summary>
        public LidarTIlExtrinsic EffectiveLidarToImu
        {
            get
            {
                NormalizeExtrinsicSelection();
                return _useLidarToImuExtrinsic
                    ? AuthoredLidarToImu
                    : Compose(AuthoredLidarToSensor, Invert(AuthoredImuToSensor));
            }
        }

        /// <summary>Resolve the selected built-in model, if the source is BuiltInPreset.</summary>
        public bool TryGetBuiltinSpec(out LidarModelSpec spec)
        {
            if (_profileSource == ProfileSource.BuiltInPreset &&
                LidarModelRegistry.TryGet(_vendor, _model, out spec))
                return true;

            spec = null;
            return false;
        }

        /// <summary>Create a scan pattern from the shared unit profile.</summary>
        public ILidarScanPattern CreateScanPattern(int columnStep)
        {
            if (TryGetBuiltinSpec(out var spec))
                return LidarScanPatternFactory.Create(spec, _mode, columnStep);

            var profile = CreateProfile();
            if (profile != null)
                return LidarScanPatternFactory.FromProfile(profile, columnStep);

            Debug.LogWarning("[SensorUnitProfile] No valid LiDAR profile resolved; using OS-1-32 fallback.");
            return LidarScanPatternFactory.FromProfile(LidarProfileLoader.CreateOs132Default(), columnStep);
        }

        /// <summary>Create a metadata/custom profile for runtime or preview use.</summary>
        public LidarProfile CreateProfile()
        {
            switch (_profileSource)
            {
                case ProfileSource.MetadataJson:
                    if (_metadataJson == null || string.IsNullOrEmpty(_metadataJson.text))
                    {
                        Debug.LogWarning("[SensorUnitProfile] Profile Source is MetadataJson but no JSON is assigned.");
                        return null;
                    }

                    if (LidarProfileLoader.TryParseFromJson(
                            _metadataJson.text, _metadataMode, out var parsed, out var error))
                        return parsed;

                    Debug.LogWarning($"[SensorUnitProfile] Metadata parse failed ({error}).");
                    return null;

                case ProfileSource.Custom:
                    return LidarProfileLoader.CreateUniform(
                        "Custom", _customPixelsPerColumn, _customColumnsPerFrame,
                        _customScanRateHz, _customFovTopDeg, _customFovBottomDeg, _customMinRangeMeters);

                case ProfileSource.BuiltInPreset:
                default:
                    return null;
            }
        }

        /// <summary>Keep exactly two authored extrinsics selected.</summary>
        public void NormalizeExtrinsicSelection()
        {
            var count =
                (_useLidarToSensorExtrinsic ? 1 : 0) +
                (_useImuToSensorExtrinsic ? 1 : 0) +
                (_useLidarToImuExtrinsic ? 1 : 0);

            if (count == 2)
                return;

            _useLidarToSensorExtrinsic = true;
            _useImuToSensorExtrinsic = true;
            _useLidarToImuExtrinsic = false;
        }

        /// <summary>Copy current model default into the editable LiDAR-to-sensor override fields.</summary>
        public void CopyModelLidarToSensorToOverride()
            => CopyToUnityFields(ModelLidarToSensor, out _lidarToSensorTranslationMeters, out _lidarToSensorRotation);

        /// <summary>Copy current model default into the editable IMU-to-sensor override fields.</summary>
        public void CopyModelImuToSensorToOverride()
            => CopyToUnityFields(ModelImuToSensor, out _imuToSensorTranslationMeters, out _imuToSensorRotation);

        /// <summary>Copy current model-derived default into the editable LiDAR-to-IMU override fields.</summary>
        public void CopyModelLidarToImuToOverride()
            => CopyToUnityFields(ModelLidarToImu, out _lidarToImuTranslationMeters, out _lidarToImuRotation);

        /// <summary>Convert a numerics vector to a Unity vector.</summary>
        public static Vector3 ToUnityVector3(NumericVector3 value)
            => new Vector3(value.X, value.Y, value.Z);

        /// <summary>Convert a Unity vector to a numerics vector.</summary>
        public static NumericVector3 ToNumericsVector3(Vector3 value)
            => new NumericVector3(value.x, value.y, value.z);

        /// <summary>Convert a numerics quaternion to a normalized Unity quaternion.</summary>
        public static Quaternion ToUnityQuaternion(NumericQuaternion value)
        {
            var normalized = LidarTIlExtrinsic.NormalizeRotation(value);
            return new Quaternion(
                CleanNearZero(normalized.X),
                CleanNearZero(normalized.Y),
                CleanNearZero(normalized.Z),
                CleanNearZero(normalized.W));
        }

        /// <summary>Convert a Unity quaternion to a normalized numerics quaternion.</summary>
        public static NumericQuaternion ToNumericsQuaternion(Quaternion value)
            => LidarTIlExtrinsic.NormalizeRotation(new NumericQuaternion(value.x, value.y, value.z, value.w));

        private static void CopyToUnityFields(
            LidarTIlExtrinsic extrinsic,
            out Vector3 translation,
            out Quaternion rotation)
        {
            translation = ToUnityVector3(extrinsic.TranslationMeters);
            rotation = ToUnityQuaternion(extrinsic.Rotation);
        }

        private LidarTIlExtrinsic AuthoredLidarToSensor
            => _overrideLidarToSensor
                ? new LidarTIlExtrinsic(
                    ToNumericsVector3(_lidarToSensorTranslationMeters),
                    ToNumericsQuaternion(_lidarToSensorRotation))
                : ModelLidarToSensor;

        private LidarTIlExtrinsic AuthoredImuToSensor
            => _overrideImuToSensor
                ? new LidarTIlExtrinsic(
                    ToNumericsVector3(_imuToSensorTranslationMeters),
                    ToNumericsQuaternion(_imuToSensorRotation))
                : ModelImuToSensor;

        private LidarTIlExtrinsic AuthoredLidarToImu
            => _overrideLidarToImu
                ? new LidarTIlExtrinsic(
                    ToNumericsVector3(_lidarToImuTranslationMeters),
                    ToNumericsQuaternion(_lidarToImuRotation))
                : ModelLidarToImu;

        private static float CleanNearZero(float value)
            => Mathf.Abs(value) < 1e-6f ? 0f : value;

        private static LidarTIlExtrinsic Invert(LidarTIlExtrinsic extrinsic)
        {
            var inverseRotation = NumericQuaternion.Inverse(extrinsic.Rotation);
            var inverseTranslation = NumericVector3.Transform(-extrinsic.TranslationMeters, inverseRotation);
            return new LidarTIlExtrinsic(inverseTranslation, inverseRotation);
        }

        private static LidarTIlExtrinsic Compose(LidarTIlExtrinsic sourceToMid, LidarTIlExtrinsic midToTarget)
        {
            var rotation = NumericQuaternion.Concatenate(sourceToMid.Rotation, midToTarget.Rotation);
            var translation =
                NumericVector3.Transform(sourceToMid.TranslationMeters, midToTarget.Rotation) +
                midToTarget.TranslationMeters;
            return new LidarTIlExtrinsic(translation, rotation);
        }

        private void OnValidate()
        {
            NormalizeExtrinsicSelection();
        }
    }
}
