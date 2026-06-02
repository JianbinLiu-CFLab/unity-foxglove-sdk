// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes standard ROS2 camera info for SLAM consumers.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using UnityEngine;
using NumericQuaternion = System.Numerics.Quaternion;
using NumericVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes standard ROS2 CameraInfo derived from a Unity Camera.
    /// Optional R2FU adapters can consume the native frame event without the core SDK
    /// referencing ROS2 generated message types.
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

        protected override string SchemaName => FoxgloveSchemaDefinitions.CameraCalibrationSchemaName;
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => false;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.SensorCameraInfo;
        protected override bool IsExpectedEncodingFallback(PublisherEncodingResolution resolution)
            => resolution.Effective == PublisherEffectiveEncoding.Ros2;

        /// <summary>Raised when a standard camera-info frame is ready for optional native ROS2 adapters.</summary>
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
            ApplySensorProfileDefaults();
            if (string.IsNullOrWhiteSpace(_topic))
                _topic = "/unity/sensor/camera/camera_info";
        }

        protected override void OnEnable()
        {
            ApplySensorProfileDefaults();
            base.OnEnable();
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var publishNativeFrame = SensorCameraInfoReady != null;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge && !publishNativeFrame)
                return;

            var unixNs = ResolveCameraInfoUnixNs();
            var frame = BuildSensorCameraInfoFrame(unixNs);
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrSensorCameraInfoBuilder.Serialize(
                    frame.UnixNs,
                    frame.FrameId,
                    frame.Width,
                    frame.Height,
                    frame.DistortionModel,
                    frame.D,
                    frame.K,
                    frame.R,
                    frame.P);
                PublishRos2(ros2Payload, unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrSensorCameraInfoBuilder.Serialize(
                    frame.UnixNs,
                    frame.FrameId,
                    frame.Width,
                    frame.Height,
                    frame.DistortionModel,
                    frame.D,
                    frame.K,
                    frame.R,
                    frame.P);
                PublishRos2Bridge(ros2Payload, unixNs);
            }

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
            var fx = fy;
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
            => _sourceCamera != null ? _sourceCamera : GetComponent<Camera>() ?? Camera.main;

        private FoxgloveCameraPublisher ResolveImagePublisher()
            => _imagePublisher != null ? _imagePublisher : GetComponent<FoxgloveCameraPublisher>();

        private ulong ResolveCameraInfoUnixNs()
            => _useSharedSensorClock && _manager != null
                ? _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)
                : CurrentLogTimeNs;

        private string ResolveFrameId()
            => ResolveSensorProfile() != null
                ? ResolveSensorProfile().CameraFrameId
                : SanitizeFrameId(_frameId, "os_camera");

        private string ResolveSensorCameraInfoTopic()
            => ResolveSensorProfile() != null
                ? ResolveSensorProfile().CameraInfoTopic
                : (string.IsNullOrWhiteSpace(_topic) ? "/unity/sensor/camera/camera_info" : _topic);

        private string ResolveTfParentFrame()
            => ResolveSensorProfile() != null
                ? ResolveSensorProfile().SensorFrameId
                : SanitizeFrameId(_cameraTfParentFrame, "os_sensor");

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

            if (string.IsNullOrWhiteSpace(_topic) || _topic == "/unity/camera/info")
                _topic = profile.CameraInfoTopic;
            if (string.IsNullOrWhiteSpace(_frameId) || _frameId == "os_camera")
                _frameId = profile.CameraFrameId;
            if (string.IsNullOrWhiteSpace(_cameraTfParentFrame) || _cameraTfParentFrame == "os_sensor")
                _cameraTfParentFrame = profile.SensorFrameId;
        }

        private static uint ResolveWidth(Camera cam, FoxgloveCameraPublisher imagePublisher)
        {
            if (imagePublisher != null)
                return checked((uint)imagePublisher.SensorCameraCaptureWidth);
            if (cam != null && cam.pixelWidth > 0) return (uint)cam.pixelWidth;
            return (uint)Mathf.Max(1, Screen.width);
        }

        private static uint ResolveHeight(Camera cam, FoxgloveCameraPublisher imagePublisher)
        {
            if (imagePublisher != null)
                return checked((uint)imagePublisher.SensorCameraCaptureHeight);
            if (cam != null && cam.pixelHeight > 0) return (uint)cam.pixelHeight;
            return (uint)Mathf.Max(1, Screen.height);
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
