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
            if (!HasSensorRawImageDemand() || _rawBandwidthWarningIssued)
                return;

            var width = Math.Max(1, _width);
            var height = Math.Max(1, _height);
            var rate = Math.Max(1f, EffectivePublishRateHz);
            var bytesPerFrame = width * height * 3L;
            var bytesPerSecond = (long)(bytesPerFrame * rate);
            _rawBandwidthWarningIssued = true;
            Debug.Log(
                "[Foxglove] Raw image Provider output enabled on topic "
                + ResolveSensorCameraRawImageTopic()
                + $". each frame is {bytesPerFrame} bytes (~{bytesPerSecond} bytes/s at {rate:F0}Hz).");
        }

        private void PublishRawFrame(
            byte[] rgb24Readback,
            ulong unixNs,
            int captureWidth,
            int captureHeight,
            bool takeOwnership = false)
        {
            if (!HasSensorRawImageDemand() || rgb24Readback == null || rgb24Readback.Length == 0)
                return;

            try
            {
                var frame = takeOwnership
                    ? CameraRawImageFrameBuilder.BuildRgb8Owned(
                        unixNs,
                        ResolveFrameId(),
                        captureWidth,
                        captureHeight,
                        rgb24Readback,
                        flipVertical: true)
                    : CameraRawImageFrameBuilder.BuildRgb8(
                        unixNs,
                        ResolveFrameId(),
                        captureWidth,
                        captureHeight,
                        rgb24Readback,
                        flipVertical: true);
                InvokeRawSubscribers(frame);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Foxglove] Failed to build raw camera frame: " + ex.Message);
            }
        }

        private void InvokeRawSubscribers(SensorRawImageFrame frame)
        {
            var handlers = SensorRawImageReady;
            if (handlers == null)
                return;
            foreach (var subscriber in handlers.GetInvocationList())
            {
                try { ((Action<SensorRawImageFrame>)subscriber)(frame); }
                catch (Exception ex) { Debug.LogWarning("[Foxglove] Raw camera subscriber failed: " + ex.Message); }
            }
        }
    }
}
