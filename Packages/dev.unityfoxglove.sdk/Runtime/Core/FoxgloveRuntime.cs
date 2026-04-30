using System;
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
        }

        /// <summary>Current session. Created when Start is called.</summary>
        public FoxgloveSession Session => _session;

        /// <summary>Whether the server is running.</summary>
        public bool IsRunning => _session?.IsRunning ?? false;

        /// <summary>Schema registry for this runtime.</summary>
        public ISchemaRegistry Schemas => _schemaRegistry;

        /// <summary>Start a new Foxglove server session.</summary>
        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            _session = new FoxgloveSession(name, _transport, _clock, _schemaRegistry);
            _session.Start(host, port);
        }

        /// <summary>Stop the current session.</summary>
        public void Stop()
        {
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            Stop();
            (_transport as IDisposable)?.Dispose();
        }
    }
}
