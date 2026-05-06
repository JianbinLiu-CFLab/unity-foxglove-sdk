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
        private McapRecorder _recorder;
        private string _recordingPath;
        private string _recordingCompression = "";
        private string _coordinateMode = "";
        private int _recordingChunkSize = McapRecorder.DefaultChunkSizeBytes;
        private bool _recordingEnabled;
        private readonly IFoxgloveLogger _logger;
        private PlaybackClock _playbackClock;
        private FoxgloveParameterStore _parameters;

        public bool IsEnabled => _recordingEnabled;
        public string CoordinateMode => _coordinateMode;

        public RecordingController(IFoxgloveLogger logger)
        {
            _logger = logger;
        }

        public void Enable(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
        {
            _recordingEnabled = true;
            _recordingPath = filePath;
            _recordingCompression = compression ?? "";
            _coordinateMode = coordinateMode ?? "";
            _recordingChunkSize = chunkSizeBytes > 0 ? chunkSizeBytes : McapRecorder.DefaultChunkSizeBytes;
        }

        public void SetCoordinateMode(string mode) { _coordinateMode = mode ?? ""; }
        public void Disable() { _recordingEnabled = false; _recordingPath = null; }

        public void AttachToSession(PlaybackClock clock, FoxgloveParameterStore parameters, FoxgloveSession session)
        {
            _playbackClock = clock;
            _parameters = parameters;
            if (!_recordingEnabled || _recordingPath == null) return;

            try
            {
                var fs = new FileStream(_recordingPath, FileMode.Create, FileAccess.Write);
                _recorder = new McapRecorder(fs, _logger, _recordingChunkSize, _recordingCompression);
                _recorder.CoordinateMode = _coordinateMode;
                session.SetRecorder(_recorder);

                var allParams = parameters.GetAllWireParameters();
                var snapshotTime = clock.NowNs;
                var snapshot = new List<object>();
                foreach (var p in allParams)
                    snapshot.Add(new { name = p.Name, type = p.Type, value = p.Value, timestamp = snapshotTime });
                _recorder.WriteMetadata("foxglove.parameters.snapshot",
                    JsonConvert.SerializeObject(snapshot));

                parameters.OnParameterChanged += OnParameterChanged;
            }
            catch (Exception ex) { _logger.LogError($"Failed to start MCAP recording: {ex.Message}"); }
        }

        public void DetachFromSession()
        {
            if (_recorder != null)
            {
                if (_parameters != null) _parameters.OnParameterChanged -= OnParameterChanged;
                _recorder.Close(); _recorder.Dispose(); _recorder = null;
            }
        }

        private void OnParameterChanged(string name, JToken value, string type)
        {
            if (_recorder != null)
            {
                var timestamp = _playbackClock.NowNs;
                var entry = JsonConvert.SerializeObject(new { name, type, value, timestamp });
                _recorder.WriteMetadata("foxglove.parameters", entry);
            }
        }

        public void Dispose() => DetachFromSession();
    }
}
