// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Manages MCAP replay lifecycle — loads an .mcap file, registers
// replay channels on the session, and ticks the replay engine each frame
// to emit messages in log-time order. Forwards replay data to listeners.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Manages the MCAP replay lifecycle. Loads an .mcap file, registers
    /// channels on the session (with replay ID prefix), and ticks the
    /// replay engine each frame. Tracks schema/channel topic maps for
    /// metadata forwarding and coordinate-mode warn-on-mismatch.
    /// </summary>
    public class ReplayController : IDisposable
    {
        private McapReplayEngine _replayEngine;
        private bool _replayEnabled;
        private Dictionary<ushort, McapSchema> _summarySchemas;
        private Dictionary<ushort, string> _channelTopicMap;
        private readonly IFoxgloveLogger _logger;

        public bool IsEnabled => _replayEnabled;
        public McapReplayEngine Engine => _replayEngine;

        public event Action<string, byte[]> OnReplayMessage;

        internal void _TestFire(string topic, byte[] data) => OnReplayMessage?.Invoke(topic, data);

        public ReplayController(IFoxgloveLogger logger)
        {
            _logger = logger;
        }

        public void Enable(string filePath, PlaybackClock playbackClock, bool recordingEnabled, string currentCoordinateMode = "")
        {
            if (recordingEnabled)
            {
                _logger.LogWarning("Recording and Replay cannot both be enabled. Replay disabled.");
                return;
            }
            try
            {
                _replayEngine = new McapReplayEngine();
                _replayEngine.Load(filePath);
                var summary = _replayEngine.Summary;

                if (summary?.Schemas != null)
                {
                    _summarySchemas = new Dictionary<ushort, McapSchema>();
                    foreach (var s in summary.Schemas)
                        _summarySchemas[s.Id] = s;
                }

                if (summary?.Channels != null)
                {
                    foreach (var ch in summary.Channels)
                    {
                        if (ch.Metadata != null && ch.Metadata.TryGetValue("coordinate_mode", out var mcapMode)
                            && !string.IsNullOrEmpty(mcapMode))
                        {
                            if (mcapMode != currentCoordinateMode)
                                _logger.LogWarning($"MCAP '{Path.GetFileName(filePath)}' was recorded with coordinate_mode '{mcapMode}', " +
                                    $"but current mode is '{currentCoordinateMode}'. Mismatch may cause incorrect object transforms.");
                            break;
                        }
                    }
                }

                _channelTopicMap = new Dictionary<ushort, string>();
                var channels = _replayEngine.Channels;
                if (channels != null)
                    foreach (var c in channels)
                        _channelTopicMap[c.Id] = c.Topic;

                playbackClock?.EnableRange(_replayEngine.StartTimeNs, _replayEngine.EndTimeNs);
                _replayEngine.Play();
                _replayEnabled = true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load MCAP replay: {ex.Message}");
                _replayEngine?.Dispose();
                _replayEngine = null;
                _replayEnabled = false;
            }
        }

        public void Disable()
        {
            _replayEngine?.Dispose();
            _replayEngine = null;
            _replayEnabled = false;
        }

        public void RegisterChannels(FoxgloveSession session)
        {
            if (!_replayEnabled || _replayEngine == null || !_replayEngine.IsLoaded) return;
            var channels = _replayEngine.Channels;
            if (channels == null) return;
            foreach (var ch in channels)
            {
                var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | ch.Id);
                var schema = _summarySchemas != null && _summarySchemas.TryGetValue(ch.SchemaId, out var s) ? s : null;
                session.RegisterChannel(new AdvertiseChannel
                {
                    Id = replayId,
                    Topic = ch.Topic,
                    Encoding = ch.MessageEncoding,
                    SchemaName = schema?.Name ?? "",
                    SchemaEncoding = schema?.Encoding ?? "",
                    Schema = schema?.Data != null ? System.Text.Encoding.UTF8.GetString(schema.Data) : ""
                });
            }
        }

        public void Tick(FoxgloveSession session, ulong nowNs)
        {
            if (!_replayEnabled || _replayEngine == null) return;
            var messages = _replayEngine.Tick(nowNs);
            ulong latestLogTime = 0;
            if (messages != null)
            {
                foreach (var msg in messages)
                {
                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                    session.Publish(replayId, msg.Data, msg.LogTime);
                    if (msg.LogTime > latestLogTime) latestLogTime = msg.LogTime;

                    if (_channelTopicMap != null && _channelTopicMap.TryGetValue(msg.ChannelId, out var topic))
                        OnReplayMessage?.Invoke(topic, msg.Data);
                }
            }
            if (latestLogTime > 0)
            {
                var frame = BinaryEncoding.EncodeTime(latestLogTime);
                session.Transport.BroadcastBinary(frame);
            }
        }

        public void Seek(ulong timeNs) => _replayEngine?.Seek(timeNs);
        public void Play() => _replayEngine?.Play();
        public void Pause() => _replayEngine?.Pause();
        public IReadOnlyList<McapChannel> GetChannels() => _replayEngine?.Channels;

        public void Dispose() => Disable();
    }
}
