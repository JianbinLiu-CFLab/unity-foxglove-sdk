// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{

    /// <summary>
    /// Minimal runtime context used to verify replay subscribe backfill requests.
    /// </summary>
    class Phase13ReplayRuntimeContext : IRuntimeContext
    {
        /// <summary>Number of replay subscriber backfill requests observed by the fake context.</summary>
        public int BackfillRequests { get; private set; }

        /// <summary>Gets whether playback is enabled for the replay subscribe tests.</summary>
        public bool PlaybackEnabled => true;

        /// <summary>Gets the asset registry required by the runtime context contract.</summary>
        public FoxgloveAssetRegistry Assets { get; } = new FoxgloveAssetRegistry();

        /// <summary>Gets the fake playback range start.</summary>
        public ulong GetPlaybackStartNs() => 0;

        /// <summary>Gets the fake playback range end.</summary>
        public ulong GetPlaybackEndNs() => 0;

        /// <summary>Accepts playback commands for the fake runtime context.</summary>
        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs) { }

        /// <summary>Returns the default playback state for the fake runtime context.</summary>
        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => default;

        /// <summary>Applies playback control for the fake runtime context.</summary>
        public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
            byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
            => default;

        /// <summary>Accepts replay seek requests for the fake runtime context.</summary>
        public void ReplaySeek(ulong timeNs) { }

        /// <summary>Accepts replay play requests for the fake runtime context.</summary>
        public void ReplayPlay() { }

        /// <summary>Accepts replay pause requests for the fake runtime context.</summary>
        public void ReplayPause() { }

        /// <summary>Records that a new replay subscriber requested panel-history backfill.</summary>
        public void RequestReplaySubscriberBackfill()
        {
            BackfillRequests++;
        }
    }
}
