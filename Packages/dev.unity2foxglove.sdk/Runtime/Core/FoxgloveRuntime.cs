using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    public class FoxgloveRuntime : IDisposable
    {
        private FoxgloveSession _session;
        private readonly IFoxgloveTransport _transport;
        private readonly PlaybackClock _playbackClock;
        private readonly ISchemaRegistry _schemaRegistry;
        private readonly IFoxgloveLogger _logger;

        // Phase 7: Runtime-owned definitions survive Stop/Start cycles
        private readonly FoxgloveParameterStore _parameters = new();
        private readonly FoxgloveServiceRegistry _services = new();
        private readonly FoxgloveAssetRegistry _assets = new();

        // Phase 10: MCAP recording
        private McapRecorder _recorder;
        private string _recordingPath;
        private int _recordingChunkSize = McapRecorder.DefaultChunkSizeBytes;
        private bool _recordingEnabled;

        // Phase 11: MCAP replay
        private McapReplayEngine _replayEngine;
        private bool _replayEnabled;
        private System.Collections.Generic.Dictionary<ushort, McapSchema> _summarySchemas;
        private System.Collections.Generic.Dictionary<ushort, string> _channelTopicMap;

        /// <summary>Fired on Unity main thread for each replayed message.</summary>
        public event Action<string, byte[]> OnReplayMessage;

        public ulong NowNs => _playbackClock.NowNs;

        public FoxgloveRuntime(IFoxgloveLogger logger = null)
            : this(new ManagedWsBackend(logger), new SystemClock(), new DefaultSchemaRegistry(), logger) { }

        public FoxgloveRuntime(IFoxgloveTransport transport, IFoxgloveClock clock, ISchemaRegistry schemaRegistry, IFoxgloveLogger logger = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _playbackClock = new PlaybackClock(clock ?? new SystemClock());
            _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
            _logger = logger ?? new ConsoleLogger();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(_schemaRegistry);
        }

        public FoxgloveSession Session => _session;
        public bool IsRunning => _session?.IsRunning ?? false;
        public ISchemaRegistry Schemas => _schemaRegistry;

        // Parameters: Runtime-owned, can be registered before Start
        public FoxgloveParameterStore Parameters => _parameters;

        public void RegisterParameter(string name, JToken value, string type, bool writable)
            => _parameters.Register(name, value, type, writable);

        // Services: Runtime-owned, can be registered before Start
        // Public read-only view — mutation must go through RegisterService/UnregisterService
        public IReadOnlyCollection<ServiceDescriptor> GetServicesSnapshot() => _services.GetAll();

        public uint RegisterService(ServiceDescriptor descriptor, Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler = null)
        {
            var id = handler != null
                ? _services.Register(descriptor, handler)
                : _services.Register(descriptor);
            if (_session != null)
            {
                var adv = new AdvertiseServices { Services = new List<ServiceDescriptor> { _services.GetById(id) } };
                _transport.BroadcastText(Newtonsoft.Json.JsonConvert.SerializeObject(adv));
            }
            return id;
        }

        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            _session = new FoxgloveSession(name, _transport, _playbackClock, _schemaRegistry, _logger, _parameters, _services);
            _session.SetRuntime(this);
            if (_recordingEnabled && _recordingPath != null)
            {
                try
                {
                    var fs = new System.IO.FileStream(_recordingPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    _recorder = new McapRecorder(fs, _logger, _recordingChunkSize);
                    _session.SetRecorder(_recorder);
                }
                catch (System.Exception ex) { _logger.LogError($"Failed to start MCAP recording: {ex.Message}"); }
            }

            _session.Start(host, port);

            // Phase 11: register replay channels after session start
            if (_replayEnabled && _replayEngine != null && _replayEngine.IsLoaded)
            {
                var channels = _replayEngine.Channels;
                if (channels != null)
                {
                    foreach (var ch in channels)
                    {
                        var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | ch.Id);
                        var schema = _summarySchemas != null && _summarySchemas.TryGetValue(ch.SchemaId, out var s)
                            ? s : null;
                        _session.RegisterChannel(new AdvertiseChannel
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
            }
        }

        public void Stop()
        {
            if (_recorder != null) { _recorder.Close(); _recorder.Dispose(); _recorder = null; }
            _session?.Dispose();
            _session = null;
            // Phase 11: keep replay loaded across Stop/Start cycles
        }

        // ── Channel API ──

        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.RegisterChannel(channel);
        }

        public void UnregisterChannel(uint channelId)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.UnregisterChannel(channelId);
        }

        public void Publish(uint channelId, byte[] payload)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.Publish(channelId, payload);
        }

        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.Publish(channelId, payload, logTimeNs);
        }

        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.RegisterSchemaChannel(channelId, topic, schemaName);
        }

        public void PublishJson(uint channelId, object message)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.PublishJson(channelId, message);
        }

        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.PublishJson(channelId, message, logTimeNs);
        }

        public void DrainServiceCalls() => _session?.DrainServiceCalls();

        // ── Phase 9: Assets ──

        public void RegisterAssetRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
            => _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);

        internal FoxgloveAssetRegistry Assets => _assets;

        // ── Phase 9: Playback Control ──

        public void EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes)
        {
            _recordingEnabled = true;
            _recordingPath = filePath;
            _recordingChunkSize = chunkSizeBytes > 0 ? chunkSizeBytes : McapRecorder.DefaultChunkSizeBytes;
        }
        public void DisableRecording() { _recordingEnabled = false; _recordingPath = null; }

        public void EnablePlaybackControl(ulong startNs, ulong endNs) => _playbackClock.EnableRange(startNs, endNs);
        public bool PlaybackEnabled => _playbackClock.PlaybackEnabled;
        internal ulong GetPlaybackStartNs() => _playbackClock.StartNs;
        internal ulong GetPlaybackEndNs() => _playbackClock.EndNs;

        // ── Phase 11: MCAP Replay ──

        public bool ReplayEnabled => _replayEnabled;

        public void EnableReplay(string filePath)
        {
            if (_recordingEnabled)
            {
                _logger.LogWarning("Recording and Replay cannot both be enabled. Replay disabled.");
                return;
            }
            try
            {
                _replayEngine = new McapReplayEngine();
                _replayEngine.Load(filePath);

                // Cache summary schemas for channel registration
                using (var fs = System.IO.File.OpenRead(filePath))
                {
                    var reader = new McapReader(fs);
                    var summary = reader.ReadSummary();
                    if (summary?.Schemas != null)
                    {
                        _summarySchemas = new System.Collections.Generic.Dictionary<ushort, McapSchema>();
                        foreach (var s in summary.Schemas)
                            _summarySchemas[s.Id] = s;
                    }
                }

                // Build channel → topic lookup for OnReplayMessage dispatch
                _channelTopicMap = new System.Collections.Generic.Dictionary<ushort, string>();
                var channels = _replayEngine.Channels;
                if (channels != null)
                    foreach (var c in channels)
                        _channelTopicMap[c.Id] = c.Topic;

                _playbackClock.EnableRange(_replayEngine.StartTimeNs, _replayEngine.EndTimeNs);
                _replayEngine.Play();
                _replayEnabled = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError($"Failed to load MCAP replay: {ex.Message}");
                _replayEngine?.Dispose();
                _replayEngine = null;
                _replayEnabled = false;
            }
        }

        public void DisableReplay()
        {
            _replayEngine?.Dispose();
            _replayEngine = null;
            _replayEnabled = false;
        }

        internal void ReplaySeek(ulong timeNs) => _replayEngine?.Seek(timeNs);
        internal void ReplayPlay() => _replayEngine?.Play();
        internal void ReplayPause() => _replayEngine?.Pause();

        internal IReadOnlyList<McapChannel> GetReplayChannels() => _replayEngine?.Channels;

        internal void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
            => _playbackClock.Apply(cmd, speed, hasSeek, seekNs);

        internal PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => _playbackClock.ToState(didSeek, requestId);

        /// <summary>
        /// Per-frame tick: drain service calls → replay due messages → broadcast Time frame.
        /// Called from FoxgloveManager.Update() on the Unity main thread.
        /// </summary>
        public void Tick()
        {
            if (_session == null) return;
            _session.DrainServiceCalls();

            // Phase 11: tick replay engine
            if (_replayEnabled && _replayEngine != null)
            {
                var messages = _replayEngine.Tick();
                ulong latestLogTime = 0;
                if (messages != null)
                {
                    foreach (var msg in messages)
                    {
                        var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                        _session.Publish(replayId, msg.Data, msg.LogTime);
                        if (msg.LogTime > latestLogTime) latestLogTime = msg.LogTime;

                        // Notify adapter
                        if (_channelTopicMap != null && _channelTopicMap.TryGetValue(msg.ChannelId, out var topic2))
                            OnReplayMessage?.Invoke(topic2, msg.Data);
                    }
                }
                // Broadcast Time frame at the latest replayed log_time (matching official pattern)
                if (latestLogTime > 0)
                {
                    var frame = Protocol.BinaryEncoding.EncodeTime(latestLogTime);
                    _session.Transport.BroadcastBinary(frame);
                }
            }
            else
            {
                _session.BroadcastTime();
            }
        }

        public void Dispose()
        {
            Stop();
            _parameters.Clear();
            _services.Clear();
            _transport.Dispose();
        }
    }
}
