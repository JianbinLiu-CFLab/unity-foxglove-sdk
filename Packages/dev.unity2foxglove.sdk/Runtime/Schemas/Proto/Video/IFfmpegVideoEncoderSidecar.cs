// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Shared surface for FFmpeg-backed camera video encoder sidecars.

using System;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Common non-blocking video encoder sidecar surface used by camera video modes.
    /// </summary>
    public interface IFfmpegVideoEncoderSidecar : ICameraVideoEncoderSidecar
    {
        string LastStderrLine { get; }
    }
}
