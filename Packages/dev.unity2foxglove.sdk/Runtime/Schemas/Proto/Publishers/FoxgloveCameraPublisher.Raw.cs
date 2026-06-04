// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Raw image helper methods for FoxgloveCameraPublisher.
using System;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveCameraPublisher
    {

        private void LogRawBandwidthWarningIfNeeded()
        {
            if (!_publishStandardRos2RawImage || _rawBandwidthWarningIssued)
                return;

            var width = Math.Max(1, _width);
            var height = Math.Max(1, _height);
            var rate = Math.Max(1f, EffectivePublishRateHz);
            var bytesPerFrame = width * height * 3L;
            var bytesPerSecond = (long)(bytesPerFrame * rate);
            _rawBandwidthWarningIssued = true;
            Debug.Log(
                "[Foxglove] Standard ROS2 raw image output enabled on topic "
                + ResolveSensorCameraRawImageTopic()
                + $". each frame is {bytesPerFrame} bytes (~{bytesPerSecond} bytes/s at {rate:F0}Hz).");
        }

        private void PublishRawFrame(byte[] rgb24Readback, ulong unixNs, int captureWidth, int captureHeight)
        {
            if (!HasSensorRawImageDemand() || rgb24Readback == null || rgb24Readback.Length == 0)
                return;

            try
            {
                var frame = CameraRawImageFrameBuilder.BuildRgb8(
                    unixNs,
                    ResolveFrameId(),
                    captureWidth,
                    captureHeight,
                    rgb24Readback,
                    flipVertical: true);
                SensorRawImageReady?.Invoke(frame);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Foxglove] Failed to build raw camera frame: " + ex.Message);
            }
        }
    }
}
