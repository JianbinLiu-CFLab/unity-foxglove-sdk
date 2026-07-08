// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.CameraCalibration messages derived from a Unity Camera.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes camera intrinsics as foxglove.CameraCalibration using either
    /// values derived from a Unity Camera or explicit Inspector overrides.
    /// </summary>
    public class FoxgloveCameraCalibrationPublisher : FoxglovePublisherBase
    {
        [Header("Camera Calibration")]
        [SerializeField] private Camera _sourceCamera;
        [SerializeField] private string _frameId = "camera";
        [SerializeField] private bool _autoFromCamera = true;
        [SerializeField] private uint _widthOverride;
        [SerializeField] private uint _heightOverride;
        [SerializeField] private double _fxOverride;
        [SerializeField] private double _fyOverride;
        [SerializeField] private double _cxOverride;
        [SerializeField] private double _cyOverride;
        [SerializeField] private string _distortionModel = "plumb_bob";

        private const double MainCameraResolveRetrySeconds = 1.0;
        private static readonly double[] NoDistortion = Array.Empty<double>();
        private readonly double[] _k = new double[9];
        private readonly double[] _r = new double[9];
        private readonly double[] _p = new double[12];
        private Camera _cachedMainCamera;
        private double _nextMainCameraResolveTime;

        protected override string SchemaName => FoxgloveSchemaDefinitions.CameraCalibrationSchemaName;
        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.CameraCalibration;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/camera/calibration";
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _cachedMainCamera = _sourceCamera != null ? _sourceCamera : Camera.main;
            _nextMainCameraResolveTime = Time.realtimeSinceStartupAsDouble + MainCameraResolveRetrySeconds;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _cachedMainCamera = null;
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPrepareAnyPublishPayload(
                out var publishWebSocket,
                out var publishBridge,
                out var encodingResolution,
                out var bridgeResolution))
            {
                return;
            }

            var unixNs = CurrentLogTimeNs;
            var calibration = BuildCalibration(unixNs);
            byte[] ros2Payload = null;

            if (publishWebSocket && encodingResolution.Effective == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = CameraCalibrationMessageBuilder.SerializeProtobuf(
                    unixNs,
                    calibration.FrameId,
                    calibration.Width,
                    calibration.Height,
                    calibration.DistortionModel,
                    calibration.D,
                    calibration.K,
                    calibration.R,
                    calibration.P);
                PublishProto(payload, unixNs, encodingResolution);
            }
            else if (publishWebSocket && encodingResolution.Effective == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrCameraCalibrationBuilder.Serialize(
                    unixNs,
                    calibration.FrameId,
                    calibration.Width,
                    calibration.Height,
                    calibration.DistortionModel,
                    calibration.D,
                    calibration.K,
                    calibration.R,
                    calibration.P);
                PublishRos2(ros2Payload, unixNs, encodingResolution);
            }
            else if (publishWebSocket)
            {
                Publish(calibration, unixNs, encodingResolution);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrCameraCalibrationBuilder.Serialize(
                    unixNs,
                    calibration.FrameId,
                    calibration.Width,
                    calibration.Height,
                    calibration.DistortionModel,
                    calibration.D,
                    calibration.K,
                    calibration.R,
                    calibration.P);
                PublishRos2Bridge(ros2Payload, unixNs, bridgeResolution);
            }
        }

        private CameraCalibrationMessage BuildCalibration(ulong unixNs)
        {
            var cam = ResolveSourceCamera();
            var width = _widthOverride != 0 ? _widthOverride : ResolveWidth(cam);
            var height = _heightOverride != 0 ? _heightOverride : ResolveHeight(cam);

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

            WriteMatrices(fx, fy, cx, cy);

            return CameraCalibrationMessageBuilder.CreateJson(
                unixNs,
                SanitizeFrameId(_frameId, "camera"),
                width,
                height,
                _distortionModel,
                NoDistortion,
                _k,
                _r,
                _p);
        }

        private Camera ResolveSourceCamera()
        {
            if (!_autoFromCamera)
                return null;

            if (_sourceCamera != null)
                return _sourceCamera;

            if (_cachedMainCamera != null)
                return _cachedMainCamera;

            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextMainCameraResolveTime)
                return null;

            _cachedMainCamera = Camera.main;
            _nextMainCameraResolveTime = now + MainCameraResolveRetrySeconds;
            return _cachedMainCamera;
        }

        private void WriteMatrices(double fx, double fy, double cx, double cy)
        {
            _k[0] = fx;
            _k[1] = 0;
            _k[2] = cx;
            _k[3] = 0;
            _k[4] = fy;
            _k[5] = cy;
            _k[6] = 0;
            _k[7] = 0;
            _k[8] = 1;

            _r[0] = 1.0;
            _r[1] = 0;
            _r[2] = 0;
            _r[3] = 0;
            _r[4] = 1;
            _r[5] = 0;
            _r[6] = 0;
            _r[7] = 0;
            _r[8] = 1;

            _p[0] = fx;
            _p[1] = 0;
            _p[2] = cx;
            _p[3] = 0;
            _p[4] = 0;
            _p[5] = fy;
            _p[6] = cy;
            _p[7] = 0;
            _p[8] = 0;
            _p[9] = 0;
            _p[10] = 1;
            _p[11] = 0;
        }

        private static uint ResolveWidth(Camera cam)
        {
            if (cam != null && cam.pixelWidth > 0) return (uint)cam.pixelWidth;
            return (uint)Mathf.Max(1, Screen.width);
        }

        private static uint ResolveHeight(Camera cam)
        {
            if (cam != null && cam.pixelHeight > 0) return (uint)cam.pixelHeight;
            return (uint)Mathf.Max(1, Screen.height);
        }
    }
}
