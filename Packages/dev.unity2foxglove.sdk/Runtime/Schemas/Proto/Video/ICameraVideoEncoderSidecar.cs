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
        bool TrySubmitFrame(byte[] frame);
        bool TryDequeueAccessUnit(out byte[] accessUnit);
    }
}
