using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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
        public string SessionId { get; }
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
            SessionId = Guid.NewGuid().ToString();

            _transport.OnClientConnected += OnClientConnected;
            _transport.OnClientDisconnected += OnClientDisconnected;
            _transport.OnTextReceived += OnClientText;
            _transport.OnBinaryReceived += OnClientBinary;
        }

        /// <summary>Start listening on the given host and port.</summary>
        public void Start(string host, int port)
        {
            _transport.Start(host, port);
        }

        /// <summary>Stop the server.</summary>
        public void Stop()
        {
            _transport.Stop();
        }

        /// <summary>Clear all session state: channels, subscriptions.</summary>
        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
        }

        // ── Public channel API ──

        /// <summary>
        /// Register a channel and broadcast advertise to all connected clients.
        /// Re-registering the same channelId overwrites the descriptor but preserves
        /// existing subscriptions.
        /// </summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            _channels.Register(channel);

            var adv = new Advertise
            {
                Channels = new List<AdvertiseChannel> { channel }
            };
            var json = JsonConvert.SerializeObject(adv);
            _transport.BroadcastText(json);
        }

        /// <summary>
        /// Unregister a channel, broadcast unadvertise, and clean subscriptions.
        /// No-op if the channel is not registered.
        /// </summary>
        public void UnregisterChannel(uint channelId)
        {
            if (!_channels.Remove(channelId))
                return;

            _subscriptions.RemoveChannel(channelId);

            var msg = new Unadvertise
            {
                ChannelIds = new List<uint> { channelId }
            };
            var json = JsonConvert.SerializeObject(msg);
            _transport.BroadcastText(json);
        }

        // ── Schema-aware channel API ──

        /// <summary>
        /// Register a channel with a known schema from the registry.
        /// Constructs the AdvertiseChannel with encoding="json", schemaEncoding="jsonschema",
        /// and the schema content from the registry.
        /// </summary>
        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName)
        {
            if (!_schemaRegistry.TryGetSchema(schemaName, out var entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'. Ensure core schemas are registered.");

            var ch = new AdvertiseChannel
            {
                Id = channelId,
                Topic = topic,
                Encoding = "json",
                SchemaName = entry.Name,
                SchemaEncoding = entry.Encoding,
                Schema = entry.Content
            };
            RegisterChannel(ch);
        }

        // ── PublishJson ──

        /// <summary>Serialize an object to JSON and publish to a channel.</summary>
        public void PublishJson(uint channelId, object message)
        {
            PublishJson(channelId, message, _clock.NowNs);
        }

        /// <summary>Serialize an object to JSON and publish with an explicit timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            var json = JsonConvert.SerializeObject(message);
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            Publish(channelId, payload, logTimeNs);
        }

        // ── Publish ──

        /// <summary>Publish payload to a channel. Routed to all subscribers.</summary>
        public void Publish(uint channelId, byte[] payload)
        {
            Publish(channelId, payload, _clock.NowNs);
        }

        /// <summary>Publish payload with an explicit log timestamp (nanoseconds).</summary>
        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_channels.Get(channelId) == null)
                return;

            var subscribers = _subscriptions.GetSubscribersForChannel(channelId);
            foreach (var (clientId, subscriptionId) in subscribers)
            {
                var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                _transport.SendBinary(clientId, frame);
            }
        }

        public void Dispose()
        {
            Stop();

            _transport.OnClientConnected -= OnClientConnected;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _transport.OnTextReceived -= OnClientText;
            _transport.OnBinaryReceived -= OnClientBinary;
            // Transport is owned by FoxgloveRuntime — do NOT dispose here.
        }

        // ── Transport event handlers ──

        private void OnClientConnected(uint clientId)
        {
            var info = new ServerInfo
            {
                Name = Name,
                Capabilities = new List<Capability>(),
                SessionId = SessionId
            };

            var json = JsonConvert.SerializeObject(info, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            _transport.SendText(clientId, json);

            // Send advertise snapshot for all current channels — per-client
            var allChannels = _channels.GetAll();
            if (allChannels.Count > 0)
            {
                var adv = new Advertise { Channels = allChannels };
                var advJson = JsonConvert.SerializeObject(adv);
                _transport.SendText(clientId, advJson);
            }
        }

        private void OnClientDisconnected(uint clientId)
        {
            _subscriptions.RemoveClient(clientId);
        }

        private void OnClientText(uint clientId, string json)
        {
            string op;
            try
            {
                var obj = JsonConvert.DeserializeObject<JsonOpOnly>(json);
                op = obj?.Op;
            }
            catch
            {
                Console.Error.WriteLine($"[Foxglove] Malformed JSON from client {clientId}, ignored.");
                return;
            }

            switch (op)
            {
                case "subscribe":
                    HandleSubscribe(clientId, json);
                    break;
                case "unsubscribe":
                    HandleUnsubscribe(clientId, json);
                    break;
                default:
                    Console.Error.WriteLine($"[Foxglove] Unknown op '{op}' from client {clientId}, ignored.");
                    break;
            }
        }

        private void HandleSubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<SubscribeMessage>(json);
                foreach (var sub in msg.Subscriptions)
                {
                    if (_channels.Get(sub.ChannelId) != null)
                        _subscriptions.AddSubscription(clientId, sub.Id, sub.ChannelId);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Foxglove] subscribe parse error: {ex.Message}");
            }
        }

        private void HandleUnsubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<UnsubscribeMessage>(json);
                if (msg.SubscriptionIds != null)
                    _subscriptions.RemoveSubscriptions(clientId, msg.SubscriptionIds);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Foxglove] unsubscribe parse error: {ex.Message}");
            }
        }

        private void OnClientBinary(uint clientId, byte[] data)
        {
            // Client→server binary messages — not implemented in Phase 2
        }

        /// <summary>Minimal DTO used to peek the "op" field.</summary>
        [JsonObject(MemberSerialization.OptIn)]
        private class JsonOpOnly
        {
            [JsonProperty("op")]
            public string Op { get; set; }
        }
    }
}
