// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Interface that FoxgloveSession uses to read runtime-level state
// (playback control, replay, assets) without a direct dependency on
// FoxgloveRuntime. Implemented by FoxgloveRuntime.

using System;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Context interface that decouples FoxgloveSession from FoxgloveRuntime.
    /// Exposes playback control, replay, and asset root state needed by
    /// session-level protocol handlers.
    /// </summary>
    public interface IRuntimeContext
    {
        /// <summary>Whether PlaybackControl is currently enabled.</summary>
        bool PlaybackEnabled { get; }

        /// <summary>Playback window start time in nanoseconds.</summary>
        ulong GetPlaybackStartNs();

        /// <summary>Playback window end time in nanoseconds.</summary>
        ulong GetPlaybackEndNs();

        /// <summary>Apply a playback command byte from a client request.</summary>
        void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs);

        /// <summary>Get a snapshot of the current playback state for wire encoding.</summary>
        PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId);

        /// <summary>Seek replay to a given log-time in nanoseconds.</summary>
        void ReplaySeek(ulong timeNs);

        /// <summary>Start or resume replay playback.</summary>
        void ReplayPlay();

        /// <summary>Pause replay playback.</summary>
        void ReplayPause();

        /// <summary>The asset registry for fetchAsset requests.</summary>
        FoxgloveAssetRegistry Assets { get; }
    }
}
