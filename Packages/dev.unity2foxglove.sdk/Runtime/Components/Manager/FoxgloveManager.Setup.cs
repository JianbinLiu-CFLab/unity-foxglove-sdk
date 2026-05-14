// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Configures runtime services that are derived from FoxgloveManager inspector state.

using System.IO;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Converts seconds to milliseconds for playback-control windows.
        /// </summary>
        private const long PlaybackMillisecondsPerSecond = 1000L;

        /// <summary>
        /// Converts milliseconds to nanoseconds for playback-control timestamps.
        /// </summary>
        private const long NanosecondsPerMillisecond = 1_000_000L;

        /// <summary>
        /// Converts seconds to nanoseconds for playback-control durations.
        /// </summary>
        private const long NanosecondsPerSecond = 1_000_000_000L;

        /// <summary>
        /// Converts MCAP recording chunk sizes from kilobytes to bytes.
        /// </summary>
        private const int RecordingBytesPerKilobyte = 1024;

        /// <summary>
        /// Project-relative folder used when the recording directory is left empty.
        /// </summary>
        private const string DefaultRecordingDirectoryName = "Recordings";

        /// <summary>
        /// Timestamp format appended to generated recording file names.
        /// </summary>
        private const string RecordingTimestampFormat = "yyyyMMdd_HHmmss";

        /// <summary>
        /// MCAP compression string for LZ4 output.
        /// </summary>
        private const string Lz4CompressionName = "lz4";

        /// <summary>
        /// MCAP compression string for Zstandard output.
        /// </summary>
        private const string ZstdCompressionName = "zstd";

        /// <summary>
        /// MCAP compression string for uncompressed output.
        /// </summary>
        private const string NoCompressionName = "";

        /// <summary>
        /// Runtime coordinate mode label for converted right-handed data.
        /// </summary>
        private const string RightHandCoordinateModeName = "RightHand";

        /// <summary>
        /// Runtime coordinate mode label for Unity-native left-handed data.
        /// </summary>
        private const string LeftHandCoordinateModeName = "LeftHand";

        /// <summary>
        /// Unix epoch used for playback-control wall-clock conversion.
        /// </summary>
        private static readonly System.DateTime UnixEpochUtc = new System.DateTime(1970, 1, 1);

        /// <summary>
        /// Registers asset roots from the Inspector list with the runtime.
        /// </summary>
        private void RegisterAssetRoots()
        {
            if (_assetRoots == null)
            {
                return;
            }

            foreach (var ar in _assetRoots)
            {
                if (ar.uriPrefix == null || ar.localRoot == null
                    || string.IsNullOrEmpty(ar.uriPrefix) || string.IsNullOrEmpty(ar.localRoot))
                {
                    continue;
                }

                var absRoot = Path.IsPathRooted(ar.localRoot)
                    ? ar.localRoot
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", ar.localRoot));
                _runtime.RegisterAssetRoot(ar.uriPrefix, absRoot, (long)ar.MaxBytesOrDefault);
            }
        }

        /// <summary>
        /// Configures playback-control bounds when playback control is enabled.
        /// </summary>
        private void SetupPlaybackControl()
        {
            if (!_enablePlaybackControl)
            {
                return;
            }

            var nowMs = (long)(System.DateTime.UtcNow - UnixEpochUtc).TotalMilliseconds;
            var startNs = (ulong)((nowMs + (long)(_playbackStartOffsetSeconds * PlaybackMillisecondsPerSecond)) * NanosecondsPerMillisecond);
            var endNs = startNs + (ulong)(_playbackDurationSeconds * NanosecondsPerSecond);
            _runtime.EnablePlaybackControl(startNs, endNs);
        }

        /// <summary>
        /// Configures MCAP recording, including output directory, file name, compression, and coordinate mode.
        /// </summary>
        private void SetupRecording()
        {
            if (!_enableRecording)
            {
                return;
            }

            var dir = string.IsNullOrEmpty(_recordingDirectory)
                ? Path.Combine(ProjectRoot, DefaultRecordingDirectoryName)
                : ResolveProjectPath(_recordingDirectory);
            Directory.CreateDirectory(dir);
            var timestamp = System.DateTime.Now.ToString(RecordingTimestampFormat);
            var path = Path.Combine(dir, $"{_recordingPrefix}_{timestamp}.mcap");
            var comp = _recordingCompression switch
            {
                McapCompressionMode.Lz4 => Lz4CompressionName,
                McapCompressionMode.Zstd => ZstdCompressionName,
                _ => NoCompressionName
            };
            var coord = _coordinateMode == CoordinateMode.RightHand ? RightHandCoordinateModeName : LeftHandCoordinateModeName;
            _runtime.EnableRecording(path, _recordingChunkSizeKB * RecordingBytesPerKilobyte, comp, coord);
        }

        /// <summary>
        /// Configures MCAP replay and disables live publishers when replay owns published data.
        /// </summary>
        private void SetupReplay()
        {
            if (!_enableReplay || string.IsNullOrEmpty(_replayFilePath))
            {
                return;
            }

            if (_disableLivePublishers && !_livePublishersDisabled)
            {
                DisableLivePublishers();
            }

            var coord = _coordinateMode == CoordinateMode.RightHand ? RightHandCoordinateModeName : LeftHandCoordinateModeName;
            _runtime.SetRecordingCoordinateMode(coord);
            _runtime.EnableReplay(ResolveProjectPath(_replayFilePath));
            if (!_runtime.ReplayEnabled)
            {
                RestoreLivePublishers();
                return;
            }

            if (_replayAutoPlay)
            {
                _runtime.ReplayPlay();
            }
            else
            {
                _runtime.ReplayPause();
            }
        }

        /// <summary>
        /// Syncs Inspector-configured browser origin allowlist to the runtime before starting.
        /// </summary>
        private void SetupAllowedOrigins()
        {
            _runtime.ClearAllowedOrigins();
            if (_allowHostedFoxgloveWeb)
            {
                _runtime.AddAllowedOrigin(FoxgloveAppUrl.HostedWebBaseUrl);
            }

            if (_allowedBrowserOrigins != null)
            {
                foreach (var origin in _allowedBrowserOrigins)
                {
                    _runtime.AddAllowedOrigin(origin);
                }
            }
        }

        /// <summary>
        /// Disables live publishers so replay-driven data and live publisher data do not collide.
        /// </summary>
        private void DisableLivePublishers()
        {
            if (_livePublishersDisabled)
            {
                return;
            }

            var pubs = FindObjectsByType<FoxglovePublisherBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _disabledPublishers.Clear();
            foreach (var pub in pubs)
            {
                if (pub.enabled)
                {
                    pub.enabled = false;
                    _disabledPublishers.Add(pub);
                }
            }

            _livePublishersDisabled = true;
            Debug.Log($"[Foxglove] Disabled {_disabledPublishers.Count} live publisher(s)");
        }

        /// <summary>
        /// Restores publishers that were disabled while replay owned the data stream.
        /// </summary>
        private void RestoreLivePublishers()
        {
            if (!_livePublishersDisabled)
            {
                return;
            }

            foreach (var pub in _disabledPublishers)
            {
                if (pub != null)
                {
                    pub.enabled = true;
                }
            }

            _disabledPublishers.Clear();
            _livePublishersDisabled = false;
            Debug.Log("[Foxglove] Restored live publishers");
        }
    }
}
