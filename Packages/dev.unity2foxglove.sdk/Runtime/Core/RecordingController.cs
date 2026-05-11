// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Manages MCAP recording lifecycle — creates the McapRecorder,
// attaches it to a session via dual-write hooks, captures parameter
// snapshots, and tracks parameter changes for metadata.

using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Manages the MCAP recording lifecycle. Created by FoxgloveRuntime
    /// and attached to a FoxgloveSession on Start. Captures parameter
    /// snapshots and change events as MCAP metadata.
    /// </summary>
    public class RecordingController : IDisposable
    {
        /// <summary>Active MCAP recorder, or null when not recording.</summary>
        private McapRecorder _recorder;
        /// <summary>Target file path for the recording.</summary>
        private string _recordingPath;
        /// <summary>Compression scheme for the MCAP file (e.g. "zstd").</summary>
        private string _recordingCompression = "";
        /// <summary>Coordinate mode for spatial transforms (e.g. "ros" or "unity").</summary>
        private string _coordinateMode = "";
        /// <summary>Chunk size in bytes for MCAP chunk boundaries.</summary>
        private int _recordingChunkSize = McapRecorder.DefaultChunkSizeBytes;
        /// <summary>Whether recording has been enabled (attached on next session start).</summary>
        private bool _recordingEnabled;
        private readonly IFoxgloveLogger _logger;
        private PlaybackClock _playbackClock;
        private FoxgloveParameterStore _parameters;

        /// <summary>Whether recording is enabled via <c>Enable()</c>.</summary>
        public bool IsEnabled => _recordingEnabled;
        /// <summary>Coordinate mode set for this recording.</summary>
        public string CoordinateMode => _coordinateMode;

        public RecordingController(IFoxgloveLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Enable recording for the next session start.
        /// <para>Pass <c>chunkSizeBytes</c>, <c>compression</c> (e.g. "zstd"), and
        /// <c>coordinateMode</c> to configure the MCAP file.</para>
        /// </summary>
        public void Enable(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
        {
            _recordingEnabled = true;
            _recordingPath = filePath;
            _recordingCompression = compression ?? "";
            _coordinateMode = coordinateMode ?? "";
            _recordingChunkSize = chunkSizeBytes > 0 ? chunkSizeBytes : McapRecorder.DefaultChunkSizeBytes;
        }

        /// <summary>Set the coordinate mode after recording was enabled.</summary>
        public void SetCoordinateMode(string mode) { _coordinateMode = mode ?? ""; }
        /// <summary>Disable recording without destroying any in-flight state.</summary>
        public void Disable() { _recordingEnabled = false; _recordingPath = null; }

        /// <summary>
        /// Attach the recorder to a session on start.
        /// <para>Creates an MCAP file, writes a parameter snapshot as metadata,
        /// subscribes to parameter change events, then hands the recorder to the session.</para>
        /// </summary>
        public void AttachToSession(PlaybackClock clock, FoxgloveParameterStore parameters, FoxgloveSession session)
        {
            if (_recorder != null)
                DetachFromSession();

            _playbackClock = clock;
            _parameters = parameters;
            if (!_recordingEnabled || _recordingPath == null) return;

            FileStream fileStream = null;
            McapRecorder recorder = null;
            try
            {
                fileStream = new FileStream(_recordingPath, FileMode.Create, FileAccess.Write);
                recorder = new McapRecorder(fileStream, _logger, _recordingChunkSize, _recordingCompression);
                recorder.CoordinateMode = _coordinateMode;

                // Defer session attachment until snapshot and event wiring succeed.
                // If the snapshot or event subscription throws, the recorder and
                // stream remain owned locally and are cleaned up in catch.
                var allParams = parameters.GetAllWireParameters();
                var snapshotTime = clock.NowNs;
                var snapshot = new List<object>();
                foreach (var p in allParams)
                    snapshot.Add(new { name = p.Name, type = p.Type, value = p.Value, timestamp = snapshotTime });
                recorder.WriteMetadata("foxglove.parameters.snapshot",
                    JsonConvert.SerializeObject(snapshot));
                parameters.OnParameterChanged -= OnParameterChanged;
                parameters.OnParameterChanged += OnParameterChanged;

                // All setup succeeded — transfer ownership to session
                session.SetRecorder(recorder);
                fileStream = null;
                _recorder = recorder;
                recorder = null;
            }
            catch (Exception ex)
            {
                recorder?.Dispose();
                fileStream?.Dispose();
                _recorder = null;
                _logger.LogError($"Failed to start MCAP recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Detach the recorder from the session.
        /// <para>Unsubscribes parameter change events, closes and disposes the recorder.</para>
        /// </summary>
        public void DetachFromSession()
        {
            if (_recorder != null)
            {
                if (_parameters != null) _parameters.OnParameterChanged -= OnParameterChanged;
                _recorder.Close(); _recorder.Dispose(); _recorder = null;
            }
        }

        /// <summary>Callback invoked when a registered parameter changes; writes a metadata entry.</summary>
        private void OnParameterChanged(string name, JToken value, string type)
        {
            if (_recorder != null)
            {
                var timestamp = _playbackClock.NowNs;
                var entry = JsonConvert.SerializeObject(new { name, type, value, timestamp });
                _recorder.WriteMetadata("foxglove.parameters", entry);
            }
        }

        /// <summary>Detach and dispose all resources.</summary>
        public void Dispose() => DetachFromSession();
    }
}
