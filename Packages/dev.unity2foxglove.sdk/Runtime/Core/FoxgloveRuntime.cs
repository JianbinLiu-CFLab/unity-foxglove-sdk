using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
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
            _session.Start(host, port);
        }

        public void Stop()
        {
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

        // ── Phase 9: Assets ──

        public void RegisterAssetRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
            => _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);

        internal FoxgloveAssetRegistry Assets => _assets;

        // ── Phase 9: Playback Control ──

        public void EnablePlaybackControl(ulong startNs, ulong endNs) => _playbackClock.EnableRange(startNs, endNs);
        public bool PlaybackEnabled => _playbackClock.PlaybackEnabled;
        internal ulong GetPlaybackStartNs() => _playbackClock.StartNs;
        internal ulong GetPlaybackEndNs() => _playbackClock.EndNs;

        internal void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
            => _playbackClock.Apply(cmd, speed, hasSeek, seekNs);

        internal PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => _playbackClock.ToState(didSeek, requestId);

        /// <summary>
        /// Per-frame tick: drain service calls and broadcast Time frame.
        /// Called from FoxgloveManager.Update() on the Unity main thread.
        /// </summary>
        public void Tick()
        {
            if (_session == null) return;
            _session.DrainServiceCalls();
            _session.BroadcastTime();
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
