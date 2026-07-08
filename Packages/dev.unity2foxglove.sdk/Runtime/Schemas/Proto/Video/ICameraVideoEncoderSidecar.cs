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
    /// Implementations must be thread-safe: frames are submitted from the
    /// Unity main thread while encoder-owned background work may concurrently
    /// enqueue completed access units for main-thread dequeue.
    /// </summary>
    public interface ICameraVideoEncoderSidecar : IDisposable
    {
        bool IsRunning { get; }
        int OutputQueueDepth { get; }
        int MaxOutputQueue { get; }
        string LastDiagnosticLine { get; }
        string LastError { get; }
        /// <summary>
        /// Submit one raw camera frame. The input pixel format is implementation-specific
        /// and documented by each sidecar options type, such as RGB24 for FFmpeg and
        /// Media Foundation or I420 for the OpenH264 helper.
        /// This method must be non-blocking and safe to call while background
        /// encoder threads are producing output.
        /// </summary>
        bool TrySubmitFrame(byte[] frame);
        /// <summary>
        /// Attempts to dequeue one completed access unit. This method is called
        /// on the Unity main thread and must be safe while encoder-owned
        /// background threads append output.
        /// </summary>
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
        /// This method follows the same thread-safety and non-blocking contract
        /// as <see cref="ICameraVideoEncoderSidecar.TrySubmitFrame(byte[])"/>.
        /// </summary>
        bool TrySubmitFrame(byte[] frame, ulong timestampNs);
        /// <summary>
        /// Attempts to dequeue one timestamped access unit under the same
        /// thread-safety contract as
        /// <see cref="ICameraVideoEncoderSidecar.TryDequeueAccessUnit(out byte[])"/>.
        /// </summary>
        bool TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit);
    }
}
