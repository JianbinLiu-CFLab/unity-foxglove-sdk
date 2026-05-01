using System;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Top-level entry point for the Foxglove SDK.
    /// Owns the session and provides a simple API for creating and starting a server.
    /// </summary>
    public class FoxgloveRuntime : IDisposable
    {
        private FoxgloveSession _session;
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _clock;
        private readonly ISchemaRegistry _schemaRegistry;

        public FoxgloveRuntime()
            : this(new ManagedWsBackend(), new SystemClock(), new DefaultSchemaRegistry()) { }

        public FoxgloveRuntime(IFoxgloveTransport transport, IFoxgloveClock clock, ISchemaRegistry schemaRegistry)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(_schemaRegistry);
        }

        public FoxgloveSession Session => _session;
        public bool IsRunning => _session?.IsRunning ?? false;
        public ISchemaRegistry Schemas => _schemaRegistry;

        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            _session = new FoxgloveSession(name, _transport, _clock, _schemaRegistry);
            _session.Start(host, port);
        }

        public void Stop()
        {
            _session?.Dispose();
            _session = null;
        }

        // ── Channel API proxies ──

        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.RegisterChannel(channel);
        }

        public void UnregisterChannel(uint channelId)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.UnregisterChannel(channelId);
        }

        public void Publish(uint channelId, byte[] payload)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.Publish(channelId, payload);
        }

        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.Publish(channelId, payload, logTimeNs);
        }

        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.RegisterSchemaChannel(channelId, topic, schemaName);
        }

        public void PublishJson(uint channelId, object message)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.PublishJson(channelId, message);
        }

        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (_session == null)
                throw new InvalidOperationException("Session not started. Call Start() first.");
            _session.PublishJson(channelId, message, logTimeNs);
        }

        public void Dispose()
        {
            Stop();
            (_transport as IDisposable)?.Dispose();
        }
    }
}
