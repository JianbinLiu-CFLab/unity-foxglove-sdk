using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Represents one Foxglove server session. Owns the transport,
    /// channel registry, and subscription state.
    /// </summary>
    public class FoxgloveSession : IDisposable
    {
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _clock;
        private readonly ChannelRegistry _channels = new ChannelRegistry();
        private readonly SubscriptionRegistry _subscriptions = new SubscriptionRegistry();
        private readonly ISchemaRegistry _schemaRegistry;

        public string Name { get; }
        public bool IsRunning => _transport.IsRunning;
        public ISchemaRegistry Schemas => _schemaRegistry;
        public ChannelRegistry Channels => _channels;

        public FoxgloveSession(string name,
            IFoxgloveTransport transport,
            IFoxgloveClock clock = null,
            ISchemaRegistry schemaRegistry = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? new SystemClock();
            _schemaRegistry = schemaRegistry ?? new DefaultSchemaRegistry();

            _transport.OnClientConnected += OnClientConnected;
            _transport.OnClientDisconnected += OnClientDisconnected;
            _transport.OnTextReceived += OnClientText;
            _transport.OnBinaryReceived += OnClientBinary;
        }

        /// <summary>Start listening on the given host and port.</summary>
        public void Start(string host, int port)
        {
            _transport.Start(host, port);

            // Phase 1: send serverInfo to newly connected clients in OnClientConnected
        }

        /// <summary>Stop the server.</summary>
        public void Stop()
        {
            _transport.Stop();
        }

        /// <summary>Clear all session state: channels, subscriptions, connected clients.</summary>
        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
        }

        /// <summary>
        /// Publish data to a channel. Sent to all clients that have an active
        /// subscription for this channel. Phase 2 will implement full routing.
        /// </summary>
        public void Publish(uint channelId, byte[] payload)
        {
            var timestampNs = _clock.NowNs;

            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, timestampNs, payload);
                _transport.SendBinary(clientId, frame);
            }
        }

        public void Dispose()
        {
            Stop();
            (_transport as IDisposable)?.Dispose();
        }

        // ── Transport event handlers (Phase 1+ will expand) ──

        private void OnClientConnected(uint clientId)
        {
            // Phase 1: send serverInfo
        }

        private void OnClientDisconnected(uint clientId)
        {
            _subscriptions.RemoveClient(clientId);
        }

        private void OnClientText(uint clientId, string json)
        {
            // Phase 2: parse subscribe/unsubscribe, dispatch to SubscriptionRegistry
        }

        private void OnClientBinary(uint clientId, byte[] data)
        {
            // Client→server binary messages (MessageData, etc.)
            // Not critical for MVP (Unity → Foxglove only).
        }
    }
}
