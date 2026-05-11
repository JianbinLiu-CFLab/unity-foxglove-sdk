// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Publishes foxglove.CameraCalibration messages derived from a Unity Camera.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

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

        protected override string SchemaName => FoxgloveSchemaDefinitions.CameraCalibrationSchemaName;
        public override bool SupportsProtobufEncoding => true;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/camera/calibration";
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var unixNs = CurrentLogTimeNs;
            var calibration = BuildCalibration(unixNs);

            if (EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
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
                PublishProto(payload, unixNs);
            }
            else
            {
                Publish(calibration, unixNs);
            }
        }

        private CameraCalibrationMessage BuildCalibration(ulong unixNs)
        {
            var cam = _autoFromCamera ? (_sourceCamera != null ? _sourceCamera : Camera.main) : null;
            var width = _widthOverride != 0 ? _widthOverride : ResolveWidth(cam);
            var height = _heightOverride != 0 ? _heightOverride : ResolveHeight(cam);

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

            return CameraCalibrationMessageBuilder.CreateJson(
                unixNs,
                SanitizeFrameId(_frameId, "camera"),
                width,
                height,
                _distortionModel,
                Array.Empty<double>(),
                new[] { fx, 0, cx, 0, fy, cy, 0, 0, 1 },
                new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 },
                new[] { fx, 0, cx, 0, 0, fy, cy, 0, 0, 0, 1, 0 });
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
