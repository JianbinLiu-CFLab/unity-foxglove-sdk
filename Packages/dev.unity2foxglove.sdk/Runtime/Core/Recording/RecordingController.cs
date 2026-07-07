// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Recording
// Purpose: Manages MCAP recording lifecycle — creates the McapRecorder,
// attaches it to a session via dual-write hooks, captures parameter
// snapshots, and tracks parameter changes for metadata.

using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Manages the MCAP recording lifecycle. Created by FoxgloveRuntime
    /// and attached to a FoxgloveSession on Start. Captures parameter
    /// snapshots and change events as MCAP metadata.
    /// </summary>
    public class RecordingController : IDisposable, IRecordingStateReader
    {
        /// <summary>Active MCAP recorder, or null when not recording.</summary>
        private McapRecorder _recorder;
        /// <summary>Atomic configuration snapshot for the next recorder.</summary>
        private RecordingConfiguration _recordingConfiguration;
        private readonly IFoxgloveLogger _logger;
        private readonly IFoxgloveClock _clock;
        private FoxgloveParameterStore _parameters;
        private FoxgloveSession _session;

        /// <summary>
        /// Indicates whether recording is configured or active. Concurrent enable/disable
        /// calls can observe a transitional snapshot and must re-check before acting.
        /// </summary>
        public bool IsEnabled => Volatile.Read(ref _recordingConfiguration) != null || Volatile.Read(ref _recorder) != null;
        /// <inheritdoc cref="IRecordingStateReader.CoordinateMode"/>
        public string CoordinateMode => Volatile.Read(ref _recordingConfiguration)?.CoordinateMode ?? "";

        /// <summary>
        /// Creates a recording controller with the provided logger.
        /// Uses a default <see cref="Transport.SystemClock"/> for timestamp generation.
        /// </summary>
        public RecordingController(IFoxgloveLogger logger) : this(logger, new Transport.SystemClock()) { }

        /// <summary>
        /// Creates a recording controller with the provided logger and clock.
        /// </summary>
        public RecordingController(IFoxgloveLogger logger, IFoxgloveClock clock)
        {
            _logger = logger;
            _clock = clock;
        }

        /// <summary>
        /// Enable recording for the next session start.
        /// <para>Pass <c>chunkSizeBytes</c>, <c>compression</c> (e.g. "zstd"), and
        /// <c>coordinateMode</c> to configure the MCAP file.</para>
        /// </summary>
        public void Enable(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
            => Enable(filePath, new McapWriterOptions { ChunkSizeBytes = chunkSizeBytes, Compression = compression }, coordinateMode);

        /// <summary>
        /// Enable recording with advanced MCAP writer options for the next session start.
        /// </summary>
        public void Enable(string filePath, McapWriterOptions options, string coordinateMode = "")
        {
            var normalized = McapWriterOptions.Normalize(options);
            Volatile.Write(
                ref _recordingConfiguration,
                new RecordingConfiguration(filePath, normalized, coordinateMode ?? ""));
        }

        /// <summary>Set the coordinate mode after recording was enabled.</summary>
        public void SetCoordinateMode(string mode)
        {
            var current = Volatile.Read(ref _recordingConfiguration);
            if (current == null)
                return;

            Volatile.Write(
                ref _recordingConfiguration,
                new RecordingConfiguration(current.FilePath, current.WriterOptions, mode ?? ""));
        }

        /// <summary>Disable recording without destroying any in-flight state.</summary>
        public void Disable()
        {
            Volatile.Write(ref _recordingConfiguration, null);
            DetachFromSession();
        }

        /// <summary>
        /// Attach the recorder to a session on start.
        /// Uses the clock supplied at construction time.
        /// </summary>
        public void AttachToSession(FoxgloveParameterStore parameters, FoxgloveSession session)
        {
            AttachToSessionCore(parameters, session);
        }

        /// <summary>
        /// Attach the recorder to a session on start with an externally provided clock.
        /// </summary>
        [Obsolete("Use AttachToSession(FoxgloveParameterStore, FoxgloveSession) — the clock is now supplied through the constructor.")]
        public void AttachToSession(PlaybackClock clock, FoxgloveParameterStore parameters, FoxgloveSession session)
        {
            AttachToSessionCore(parameters, session);
        }

        private void AttachToSessionCore(FoxgloveParameterStore parameters, FoxgloveSession session)
        {
            if (Volatile.Read(ref _recorder) != null)
                DetachFromSession();

            var configuration = Volatile.Read(ref _recordingConfiguration);
            if (configuration == null || configuration.FilePath == null) return;
            Volatile.Write(ref _parameters, parameters);

            FileStream fileStream = null;
            McapRecorder recorder = null;
            try
            {
                fileStream = new FileStream(configuration.FilePath, FileMode.Create, FileAccess.Write);
                recorder = new McapRecorder(fileStream, _logger, configuration.WriterOptions, leaveOpen: false);
                recorder.CoordinateMode = configuration.CoordinateMode;

                // Defer session attachment until snapshot and event wiring succeed.
                // If the snapshot or event subscription throws, the recorder and
                // stream remain owned locally and are cleaned up in catch.
                var allParams = parameters.GetAllWireParameters();
                var snapshotTime = _clock.NowNs;
                var snapshot = new List<object>();
                foreach (var p in allParams)
                    snapshot.Add(new { name = p.Name, type = p.Type, value = p.Value, timestamp = snapshotTime });
                recorder.WriteMetadata("foxglove.parameters.snapshot",
                    JsonConvert.SerializeObject(snapshot));
                TryWriteFoxRunSchemaMetadata(recorder);
                parameters.OnParameterChanged -= OnParameterChanged;
                parameters.OnParameterChanged += OnParameterChanged;

                // All setup succeeded — transfer ownership to session
                session.SetRecorder(recorder);
                fileStream = null;
                Volatile.Write(ref _session, session);
                Volatile.Write(ref _recorder, recorder);
                recorder = null;
            }
            catch (Exception ex)
            {
                parameters.OnParameterChanged -= OnParameterChanged;
                session.SetRecorder(null);
                recorder?.Dispose();
                fileStream?.Dispose();
                Volatile.Write(ref _recorder, null);
                Volatile.Write(ref _session, null);
                Volatile.Write(ref _parameters, null);
                _logger.LogError($"Failed to start MCAP recording: {ex.Message}");
            }
        }

        private void TryWriteFoxRunSchemaMetadata(McapRecorder recorder)
        {
            if (recorder == null || !FoxRunSchemaInfoRegistry.HasGeneratedSchemaInfo)
                return;

            try
            {
                if (FoxRunSchemaMcapMetadata.TryCreateJson(FoxRunSchemaInfoRegistry.Current, out var json))
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Skipping FoxRun schema metadata for MCAP recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Detach the recorder from the session.
        /// <para>Unsubscribes parameter change events, closes and disposes the recorder.</para>
        /// </summary>
        public void DetachFromSession()
        {
            var session = Interlocked.Exchange(ref _session, null);
            session?.SetRecorder(null);

            var recorder = Interlocked.Exchange(ref _recorder, null);

            var parameters = Interlocked.Exchange(ref _parameters, null);
            if (parameters != null) parameters.OnParameterChanged -= OnParameterChanged;

            if (recorder != null)
                DisposeRecorderBestEffort(recorder);
        }

        private void DisposeRecorderBestEffort(McapRecorder recorder)
        {
            try
            {
                recorder.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"MCAP recorder dispose failed during shutdown; continuing: {ex.Message}");
            }
        }

        /// <summary>Callback invoked when a registered parameter changes; writes a metadata entry.</summary>
        private void OnParameterChanged(string name, JToken value, string type)
        {
            var recorder = Volatile.Read(ref _recorder);
            if (recorder == null)
                return;

            try
            {
                var timestamp = _clock.NowNs;
                var entry = JsonConvert.SerializeObject(new ParameterMetadataEntry
                {
                    Name = name,
                    Type = type,
                    Value = value,
                    Timestamp = timestamp
                });
                recorder.WriteMetadata("foxglove.parameters", entry);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"MCAP parameter metadata write failed; continuing: {ex.Message}");
            }
        }

        /// <summary>Detach and dispose all resources.</summary>
        public void Dispose() => DetachFromSession();

        private sealed class RecordingConfiguration
        {
            public RecordingConfiguration(string filePath, McapWriterOptions writerOptions, string coordinateMode)
            {
                FilePath = filePath;
                WriterOptions = writerOptions;
                CoordinateMode = coordinateMode ?? "";
            }

            public string FilePath { get; }
            public McapWriterOptions WriterOptions { get; }
            public string CoordinateMode { get; }
        }

        private sealed class ParameterMetadataEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("value")]
            public JToken Value { get; set; }

            [JsonProperty("timestamp")]
            public ulong Timestamp { get; set; }
        }
    }
}
