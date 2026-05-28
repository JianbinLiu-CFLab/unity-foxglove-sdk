// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/Clock
// Purpose: Playback-aware clock that supports publishing a known playback range.
// Extends IFoxgloveClock so consumers needing only NowNs can depend on the
// narrower interface.

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Playback-aware clock that supports publishing a known playback range.
    /// Extends <see cref="IFoxgloveClock"/> so consumers needing only NowNs can
    /// keep depending on the narrower interface.
    /// </summary>
    public interface IRangePlaybackClock : IFoxgloveClock
    {
        void EnableRange(ulong startNs, ulong endNs);
    }
}
