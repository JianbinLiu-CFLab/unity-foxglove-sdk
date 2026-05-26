// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Codec-neutral camera video encoder sidecar contract.

using System;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Common non-blocking camera video encoder process surface.
    /// </summary>
    public interface ICameraVideoEncoderSidecar : IDisposable
    {
        bool IsRunning { get; }
        string LastDiagnosticLine { get; }
        string LastError { get; }
        /// <summary>
        /// Submit one raw camera frame. The input pixel format is implementation-specific
        /// and documented by each sidecar options type, such as RGB24 for FFmpeg and
        /// Media Foundation or I420 for the OpenH264 helper.
        /// </summary>
        bool TrySubmitFrame(byte[] frame);
        bool TryDequeueAccessUnit(out byte[] accessUnit);
    }

    /// <summary>
    /// Optional timestamp-preserving video encoder surface. Implementations
    /// should pair each encoded access unit with the render timestamp of its
    /// source frame.
    /// </summary>
    public interface ITimestampedCameraVideoEncoderSidecar : ICameraVideoEncoderSidecar
    {
        /// <summary>
        /// Submit one raw camera frame with its render timestamp. The input pixel
        /// format follows the same implementation-specific contract as TrySubmitFrame.
        /// </summary>
        bool TrySubmitFrame(byte[] frame, ulong timestampNs);
        bool TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit);
    }
}
