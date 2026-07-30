// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes camera calibration data for visualization and Providers.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using UnityEngine;
using NumericQuaternion = System.Numerics.Quaternion;
using NumericVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes camera calibration derived from a Unity Camera. Optional
    /// Providers can consume the owned frame event without a core dependency.
    /// </summary>
    [AddComponentMenu("Foxglove/Publishers/Foxglove Camera Info Publisher")]
    public class FoxgloveCameraInfoPublisher : FoxglovePublisherBase
    {
        [Header("Sensor CameraInfo")]
        [SerializeField] private Camera _sourceCamera;
        [SerializeField] private FoxgloveCameraPublisher _imagePublisher;
        [SerializeField] private MonoBehaviour _sensorUnitProfile;
        [SerializeField] private string _frameId = "os_camera";
        [SerializeField] private bool _useSharedSensorClock = true;
        [SerializeField] private bool _publishCameraTfAnchor = true;
        [SerializeField] private string _cameraTfParentFrame = "os_sensor";
        [SerializeField] private bool _autoFromCamera = true;
        [SerializeField] private uint _widthOverride;
        [SerializeField] private uint _heightOverride;
        [SerializeField] private double _fxOverride;
        [SerializeField] private double _fyOverride;
        [SerializeField] private double _cxOverride;
        [SerializeField] private double _cyOverride;
        [SerializeField] private string _distortionModel = "plumb_bob";
        private Camera _cachedSourceCamera;
        private bool _sourceCameraCacheResolved;
        private FoxgloveCameraPublisher _cachedImagePublisher;
        private bool _imagePublisherCacheResolved;
        private bool _warnedScreenDimensionFallback;

        protected override string SchemaName => FoxgloveSchemaDefinitions.CameraCalibrationSchemaName;
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => false;

        /// <summary>Raised when a camera-info frame is ready for optional Providers.</summary>
        public event Action<SensorCameraInfoFrame> SensorCameraInfoReady;

        /// <summary>Resolved standard CameraInfo topic.</summary>
        public string SensorCameraInfoTopic => ResolveSensorCameraInfoTopic();

        /// <summary>Resolved camera frame ID.</summary>
        public string SensorCameraFrameId => ResolveFrameId();

        /// <summary>Whether the native adapter should publish a TF anchor for this camera.</summary>
        public bool PublishCameraTfAnchor => _publishCameraTfAnchor;

        /// <summary>Resolved TF parent frame for the camera anchor.</summary>
        public string CameraTfParentFrame => ResolveTfParentFrame();

        /// <summary>Resolved TF child frame for the camera anchor.</summary>
        public string CameraTfChildFrame => ResolveFrameId();

        /// <summary>Resolved camera pose translation in the TF parent frame.</summary>
        public NumericVector3 CameraTfTranslation => ResolveCameraPoseInParent().TranslationMeters;

        /// <summary>Resolved camera pose rotation in the TF parent frame.</summary>
        public NumericQuaternion CameraTfRotation => ResolveCameraPoseInParent().Rotation;

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(_topic))
                _topic = "/unity/sensor/camera/camera_info";
        }

        protected override void OnEnable()
        {
            ResetResolvedPublisherCaches();
            ApplySensorProfileDefaults();
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            ResetResolvedPublisherCaches();
            base.OnDisable();
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var publishNativeFrame = SensorCameraInfoReady != null;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishProvider =
                ShouldPrepareOrdinaryTransportPayload();
            if (!publishWebSocket && !publishProvider && !publishNativeFrame)
                return;

            var unixNs = ResolveCameraInfoUnixNs();
            var frame = BuildSensorCameraInfoFrame(unixNs);

            if (publishProvider)
                PublishOrdinaryTransport(frame, SchemaName, unixNs);

            if (publishNativeFrame)
                SensorCameraInfoReady?.Invoke(frame);
        }

        private SensorCameraInfoFrame BuildSensorCameraInfoFrame(ulong unixNs)
        {
            var cam = _autoFromCamera ? ResolveSourceCamera() : null;
            var imagePublisher = ResolveImagePublisher();
            var width = _widthOverride != 0 ? _widthOverride : ResolveWidth(cam, imagePublisher);
            var height = _heightOverride != 0 ? _heightOverride : ResolveHeight(cam, imagePublisher);

            var verticalFov = cam != null ? cam.fieldOfView : 60.0;
            var fovRad = Math.Max(0.001, verticalFov) * Math.PI / 180.0;
            var fy = height / (2.0 * Math.Tan(fovRad / 2.0));
            var fx = fy * ((double)width / Math.Max(1.0, height));
            var cx = width / 2.0;
            var cy = height / 2.0;

            fx = _fxOverride != 0 ? _fxOverride : fx;
            fy = _fyOverride != 0 ? _fyOverride : fy;
            cx = _cxOverride != 0 ? _cxOverride : cx;
            cy = _cyOverride != 0 ? _cyOverride : cy;

            return new SensorCameraInfoFrame(
                unixNs,
                ResolveFrameId(),
                width,
                height,
                _distortionModel,
                Array.Empty<double>(),
                new[] { fx, 0, cx, 0, fy, cy, 0, 0, 1 },
                new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 },
                new[] { fx, 0, cx, 0, 0, fy, cy, 0, 0, 0, 1, 0 });
        }

        private Camera ResolveSourceCamera()
        {
            if (_sourceCamera != null)
                return _sourceCamera;

            if (!_sourceCameraCacheResolved)
            {
                _cachedSourceCamera = GetComponent<Camera>();
                if (_cachedSourceCamera == null)
                    _cachedSourceCamera = Camera.main;
                _sourceCameraCacheResolved = true;
            }

            return _cachedSourceCamera;
        }

        private FoxgloveCameraPublisher ResolveImagePublisher()
        {
            if (_imagePublisher != null)
                return _imagePublisher;

            if (!_imagePublisherCacheResolved)
            {
                _cachedImagePublisher = GetComponent<FoxgloveCameraPublisher>();
                _imagePublisherCacheResolved = true;
            }

            return _cachedImagePublisher;
        }

        private void ResetResolvedPublisherCaches()
        {
            _cachedSourceCamera = null;
            _sourceCameraCacheResolved = false;
            _cachedImagePublisher = null;
            _imagePublisherCacheResolved = false;
        }

        private ulong ResolveCameraInfoUnixNs()
            => _useSharedSensorClock && _manager != null
                ? _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)
                : CurrentLogTimeNs;

        private string ResolveFrameId()
        {
            var profile = ResolveSensorProfile();
            return profile != null
                ? profile.CameraFrameId
                : SanitizeFrameId(_frameId, "os_camera");
        }

        private string ResolveSensorCameraInfoTopic()
        {
            var profile = ResolveSensorProfile();
            return profile != null
                ? profile.CameraInfoTopic
                : (string.IsNullOrWhiteSpace(_topic) ? "/unity/sensor/camera/camera_info" : _topic);
        }

        private string ResolveTfParentFrame()
        {
            var profile = ResolveSensorProfile();
            return profile != null
                ? profile.SensorFrameId
                : SanitizeFrameId(_cameraTfParentFrame, "os_sensor");
        }

        private CameraPose ResolveCameraPoseInParent()
        {
            var profile = ResolveSensorProfile();
            var cameraToParentTranslation = profile != null
                ? profile.CameraToSensorTranslationMeters
                : NumericVector3.Zero;
            var cameraToParentRotation = profile != null
                ? profile.CameraToSensorRotation
                : NumericQuaternion.Identity;

            var inverseRotation = NumericQuaternion.Inverse(cameraToParentRotation);
            var inverseTranslation = NumericVector3.Transform(-cameraToParentTranslation, inverseRotation);
            return new CameraPose(inverseTranslation, inverseRotation);
        }

        private ISensorCameraProfile ResolveSensorProfile()
            => _sensorUnitProfile as ISensorCameraProfile;

        private void ApplySensorProfileDefaults()
        {
            var profile = ResolveSensorProfile();
            if (profile == null)
                return;

            if (string.IsNullOrWhiteSpace(_topic) || _topic == "/unity/sensor/camera/camera_info")
                _topic = profile.CameraInfoTopic;
            if (string.IsNullOrWhiteSpace(_frameId) || _frameId == "os_camera")
                _frameId = profile.CameraFrameId;
            if (string.IsNullOrWhiteSpace(_cameraTfParentFrame) || _cameraTfParentFrame == "os_sensor")
                _cameraTfParentFrame = profile.SensorFrameId;
        }

        private uint ResolveWidth(Camera cam, FoxgloveCameraPublisher imagePublisher)
        {
            if (imagePublisher != null)
                return checked((uint)imagePublisher.SensorCameraCaptureWidth);
            if (cam != null && cam.pixelWidth > 0) return (uint)cam.pixelWidth;
            WarnScreenDimensionFallback();
            return (uint)Mathf.Max(1, Screen.width);
        }

        private uint ResolveHeight(Camera cam, FoxgloveCameraPublisher imagePublisher)
        {
            if (imagePublisher != null)
                return checked((uint)imagePublisher.SensorCameraCaptureHeight);
            if (cam != null && cam.pixelHeight > 0) return (uint)cam.pixelHeight;
            WarnScreenDimensionFallback();
            return (uint)Mathf.Max(1, Screen.height);
        }

        private void WarnScreenDimensionFallback()
        {
            if (_warnedScreenDimensionFallback)
                return;

            _warnedScreenDimensionFallback = true;
            Debug.LogWarning("[Foxglove] CameraInfo publisher has no Source Camera or Image Publisher; falling back to Screen dimensions for calibration.");
        }

        private readonly struct CameraPose
        {
            public CameraPose(NumericVector3 translationMeters, NumericQuaternion rotation)
            {
                TranslationMeters = translationMeters;
                Rotation = rotation;
            }

            public NumericVector3 TranslationMeters { get; }
            public NumericQuaternion Rotation { get; }
        }
    }
}
