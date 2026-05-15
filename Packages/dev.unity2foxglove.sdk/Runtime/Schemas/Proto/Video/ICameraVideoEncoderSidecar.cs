// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Codec-neutral camera video encoder sidecar boundary.

using System;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Common non-blocking camera video encoder surface used by FFmpeg
    /// and native encoder backends.
    /// </summary>
    public interface ICameraVideoEncoderSidecar : IDisposable
    {
        bool IsRunning { get; }
        string LastDiagnosticLine { get; }
        string LastError { get; }
        bool TrySubmitFrame(byte[] rgb24Frame);
        bool TryDequeueAccessUnit(out byte[] accessUnit);
    }
}
