// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Validates captured RGB frames before submitting them to video sidecars.

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Validates readback geometry against RGB24 layout and fixed-size sidecar encoders.
    /// </summary>
    internal static class CameraVideoFrameValidator
    {
        public static bool TryValidateCapturedFrame(
            int captureWidth,
            int captureHeight,
            int rgb24ByteCount,
            int sidecarWidth,
            int sidecarHeight,
            out string error)
        {
            if (!CameraVideoFrameGeometry.TryGetRgb24FrameByteCount(captureWidth, captureHeight, out var expectedRgbBytes))
            {
                error = $"dimensionMismatch=capturedUnsupported captured={captureWidth}x{captureHeight} bytes={rgb24ByteCount}";
                return false;
            }

            if (rgb24ByteCount != expectedRgbBytes)
            {
                error = $"dimensionMismatch=byteCount captured={captureWidth}x{captureHeight} bytes={rgb24ByteCount} expectedBytes={expectedRgbBytes}";
                return false;
            }

            if (sidecarWidth != captureWidth || sidecarHeight != captureHeight)
            {
                error = $"dimensionMismatch=sidecar captured={captureWidth}x{captureHeight} sidecar={sidecarWidth}x{sidecarHeight}";
                return false;
            }

            error = "";
            return true;
        }
    }
}
