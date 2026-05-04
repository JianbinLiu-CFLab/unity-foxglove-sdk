using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    public class FoxgloveRuntime : IDisposable, IRuntimeContext
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

        // Phase 13: decomposed controllers
        private readonly RecordingController _recording;
        private readonly ReplayController _replay;
        private Action<string, byte[]> _replayForwarder;

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
            _recording = new RecordingController(_logger);
            _replay = new ReplayController(_logger);
        }

        public FoxgloveSession Session => _session;
        public bool IsRunning => _session?.IsRunning ?? false;
        public ISchemaRegistry Schemas => _schemaRegistry;
        public FoxgloveParameterStore Parameters => _parameters;

        public void RegisterParameter(string name, JToken value, string type, bool writable)
            => _parameters.Register(name, value, type, writable);

        public IReadOnlyCollection<ServiceDescriptor> GetServicesSnapshot() => _services.GetAll();

        public uint RegisterService(ServiceDescriptor descriptor, Func<JToken, JToken> handler = null)
        {
            var id = handler != null
                ? _services.Register(descriptor, handler)
                : _services.Register(descriptor);
            if (_session != null)
            {
                var adv = new AdvertiseServices { Services = new List<ServiceDescriptor> { _services.GetById(id) } };
                _transport.BroadcastText(JsonConvert.SerializeObject(adv));
            }
            return id;
        }

        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            _session = new FoxgloveSession(name, _transport, _playbackClock, _schemaRegistry, _logger, _parameters, _services);
            _session.SetRuntimeContext(this);
            _recording.AttachToSession(_playbackClock, _parameters, _session);
            _session.Start(host, port);
            _replay.RegisterChannels(_session);

            // Wire replay message forwarding (stored to avoid handler accumulation)
            _replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _replay.OnReplayMessage += _replayForwarder;
        }

        public event Action<string, byte[]> OnReplayMessage;

        /// <summary>Test-only hook to fire replay without loading an MCAP file.</summary>
        internal void _TestFireReplay(string topic, byte[] data)
            => _replay._TestFire(topic, data);

        public void Stop()
        {
            if (_replayForwarder != null)
            {
                _replay.OnReplayMessage -= _replayForwarder;
                _replayForwarder = null;
            }
            _recording.DetachFromSession();
            _session?.Dispose();
            _session = null;
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

        // ── Assets ──

        public void RegisterAssetRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
            => _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);

        public FoxgloveAssetRegistry Assets => _assets;

        // ── Recording (delegated) ──

        public bool RecordingEnabled => _recording.IsEnabled;

        public void EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
            => _recording.Enable(filePath, chunkSizeBytes, compression, coordinateMode);

        public void SetRecordingCoordinateMode(string mode) => _recording.SetCoordinateMode(mode);
        public void DisableRecording() => _recording.Disable();

        // ── Playback Control ──

        public void EnablePlaybackControl(ulong startNs, ulong endNs) => _playbackClock.EnableRange(startNs, endNs);
        public bool PlaybackEnabled => _playbackClock.PlaybackEnabled;
        public ulong GetPlaybackStartNs() => _playbackClock.StartNs;
        public ulong GetPlaybackEndNs() => _playbackClock.EndNs;

        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
            => _playbackClock.Apply(cmd, speed, hasSeek, seekNs);

        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => _playbackClock.ToState(didSeek, requestId);

        // ── Replay (delegated) ──

        public bool ReplayEnabled => _replay.IsEnabled;

        public void EnableReplay(string filePath)
            => _replay.Enable(filePath, _playbackClock, _recording.IsEnabled, _recording.CoordinateMode);
        public void DisableReplay() => _replay.Disable();
        public void ReplaySeek(ulong timeNs) => _replay.Seek(timeNs);
        public void ReplayPlay() => _replay.Play();
        public void ReplayPause() => _replay.Pause();

        internal IReadOnlyList<McapChannel> GetReplayChannels() => _replay.GetChannels();

        // ── Tick ──

        public void Tick()
        {
            if (_session == null) return;
            _session.DrainServiceCalls();

            if (_replay.IsEnabled)
                _replay.Tick(_session, _playbackClock.NowNs);
            else
                _session.BroadcastTime();
        }

        public void Dispose()
        {
            Stop();
            _parameters.Clear();
            _services.Clear();
            _recording.Dispose();
            _replay.Dispose();
            _transport.Dispose();
        }
    }
}
