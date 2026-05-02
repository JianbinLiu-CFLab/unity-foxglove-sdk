using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    public class FoxgloveSession : IDisposable
    {
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _clock;
        private readonly ChannelRegistry _channels = new();
        private readonly SubscriptionRegistry _subscriptions = new();
        private readonly ISchemaRegistry _schemaRegistry;
        private readonly IFoxgloveLogger _logger;

        // Phase 6-7: Runtime-owned definitions, Session holds references
        private readonly FoxgloveParameterStore _parameters;
        private readonly ParameterSubscriptionRegistry _paramSubs = new();
        private readonly FoxgloveServiceRegistry _services;

        public string Name { get; }
        public string SessionId { get; }
        public bool IsRunning => _transport.IsRunning;
        public ISchemaRegistry Schemas => _schemaRegistry;
        public ChannelRegistry Channels => _channels;
        public FoxgloveParameterStore Parameters => _parameters;
        /// <summary>Internal access to registry for advanced use (GetPendingCalls etc).</summary>
        internal FoxgloveServiceRegistry Services => _services;

        public FoxgloveSession(string name,
            IFoxgloveTransport transport,
            IFoxgloveClock clock = null,
            ISchemaRegistry schemaRegistry = null,
            IFoxgloveLogger logger = null,
            FoxgloveParameterStore paramStore = null,
            FoxgloveServiceRegistry serviceRegistry = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? new SystemClock();
            _schemaRegistry = schemaRegistry ?? new DefaultSchemaRegistry();
            _logger = logger ?? new ConsoleLogger();
            _parameters = paramStore ?? new FoxgloveParameterStore();
            _services = serviceRegistry ?? new FoxgloveServiceRegistry();
            SessionId = Guid.NewGuid().ToString();

            _transport.OnClientConnected += OnClientConnected;
            _transport.OnClientDisconnected += OnClientDisconnected;
            _transport.OnTextReceived += OnClientText;
            _transport.OnBinaryReceived += OnClientBinary;
        }

        public void Start(string host, int port) => _transport.Start(host, port);
        public void Stop() => _transport.Stop();

        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
            _paramSubs.Clear();
            // Do NOT clear _parameters or _services — they are Runtime-owned
        }

        // ── Channel API ──

        public void RegisterChannel(AdvertiseChannel channel)
        {
            _channels.Register(channel);
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Advertise { Channels = new List<AdvertiseChannel> { channel } }));
        }

        public void UnregisterChannel(uint channelId)
        {
            if (!_channels.Remove(channelId)) return;
            _subscriptions.RemoveChannel(channelId);
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Unadvertise { ChannelIds = new List<uint> { channelId } }));
        }

        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName)
        {
            if (!_schemaRegistry.TryGetSchema(schemaName, out var entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'.");
            RegisterChannel(new AdvertiseChannel
            {
                Id = channelId, Topic = topic,
                Encoding = "json", SchemaName = entry.Name,
                SchemaEncoding = entry.Encoding, Schema = entry.Content
            });
        }

        // ── Publish ──

        public void Publish(uint channelId, byte[] payload) => Publish(channelId, payload, _clock.NowNs);

        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_channels.Get(channelId) == null) return;
            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                _transport.SendBinary(clientId,
                    BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload));
            }
        }

        public void PublishJson(uint channelId, object message) => PublishJson(channelId, message, _clock.NowNs);

        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            Publish(channelId, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)), logTimeNs);
        }

        /// <summary>Register a service and broadcast incremental advertiseServices.</summary>
        public uint RegisterService(Protocol.ServiceDescriptor descriptor)
        {
            var id = _services.Register(descriptor);
            var adv = new Protocol.AdvertiseServices { Services = new List<ServiceDescriptor> { _services.GetById(id) } };
            _transport.BroadcastText(JsonConvert.SerializeObject(adv));
            return id;
        }

        /// <summary>Broadcast a Time frame (opcode=2) at controlled rate.</summary>
        public void BroadcastTime(float rateHz = 10f)
        {
            var now = System.DateTime.UtcNow.Ticks;
            var effectiveRate = rateHz > 0 ? rateHz : 10f;
            // Avoid float precision loss: compute interval with long division
            var interval = System.TimeSpan.TicksPerSecond / (long)effectiveRate;
            if (effectiveRate > System.TimeSpan.TicksPerSecond)
                interval = 1L; // sub-tick rate → every call
            if (now - _lastTimeBroadcastTicks < interval)
                return;
            _lastTimeBroadcastTicks = now;

            var frame = BinaryEncoding.EncodeTime(_clock.NowNs);
            _transport.BroadcastBinary(frame);
        }
        private long _lastTimeBroadcastTicks;

        // ── Service call lifecycle (Phase 6) ──

        /// <summary>Drain completed service calls and send responses/failures.</summary>
        public void DrainServiceCalls()
        {
            _services.SweepTimeouts(FoxgloveServiceRegistry.DefaultTimeout);

            // Execute pending calls via registered handlers (Phase 7 delegate model)
            foreach (var call in _services.GetPendingCalls())
            {
                var handler = _services.GetHandler(call.ServiceId);
                if (handler == null) continue;
                try
                {
                    var payloadStr = System.Text.Encoding.UTF8.GetString(call.Payload);
                    var input = Newtonsoft.Json.Linq.JToken.Parse(payloadStr);
                    var result = handler(input);
                    var responseBytes = System.Text.Encoding.UTF8.GetBytes(result.ToString(Newtonsoft.Json.Formatting.None));
                    _services.CompleteResponse(call.ClientId, call.CallId, "json", responseBytes);
                }
                catch (Exception ex)
                {
                    _services.Fail(call.ClientId, call.CallId, $"Handler exception: {ex.Message}");
                }
            }

            foreach (var call in _services.DrainCompleted())
            {
                if (call.FailureMessage != null)
                {
                    var fail = new ServiceCallFailure
                    { ServiceId = call.ServiceId, CallId = call.CallId, Message = call.FailureMessage };
                    _transport.SendText(call.ClientId, JsonConvert.SerializeObject(fail));
                }
                else
                {
                    var frame = BinaryEncoding.EncodeServerServiceCallResponse(
                        call.ServiceId, call.CallId, call.ResponseEncoding ?? "json", call.ResponsePayload);
                    _transport.SendBinary(call.ClientId, frame);
                }
            }
        }

        /// <summary>Test-only: trigger a logger call to verify injection.</summary>
        internal void ForceLoggerTest() => _logger.LogWarning("logger test");

        // ── Dispose ──

        public void Dispose()
        {
            Stop();
            _transport.OnClientConnected -= OnClientConnected;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _transport.OnTextReceived -= OnClientText;
            _transport.OnBinaryReceived -= OnClientBinary;
        }

        // ── Transport event handlers ──

        private void OnClientConnected(uint clientId)
        {
            var info = new ServerInfo
            {
                Name = Name,
                Capabilities = new List<Capability>
                {
                    Capability.Parameters,
                    Capability.ParametersSubscribe,
                    Capability.Services,
                    Capability.Time
                },
                SupportedEncodings = new List<string> { "json" },
                SessionId = SessionId
            };
            _transport.SendText(clientId, JsonConvert.SerializeObject(info,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

            // Channel advertise snapshot
            var chs = _channels.GetAll();
            if (chs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new Advertise { Channels = chs }));

            // Service advertise snapshot
            var svcs = _services.GetAll();
            if (svcs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new AdvertiseServices { Services = svcs }));
        }

        private void OnClientDisconnected(uint clientId)
        {
            _subscriptions.RemoveClient(clientId);
            _paramSubs.RemoveClient(clientId);
            _services.RemoveClientCalls(clientId);
        }

        private void OnClientText(uint clientId, string json)
        {
            string op;
            try { op = JObject.Parse(json)["op"]?.ToString(); }
            catch { _logger.LogWarning($"Malformed JSON from client {clientId}"); return; }

            switch (op)
            {
                case "subscribe": HandleSubscribe(clientId, json); break;
                case "unsubscribe": HandleUnsubscribe(clientId, json); break;
                case "getParameters": HandleGetParameters(clientId, json); break;
                case "setParameters": HandleSetParameters(clientId, json); break;
                case "subscribeParameterUpdates": HandleSubscribeParameterUpdates(clientId, json); break;
                case "unsubscribeParameterUpdates": HandleUnsubscribeParameterUpdates(clientId, json); break;
                default: _logger.LogWarning($"Unknown op '{op}' from client {clientId}"); break;
            }
        }

        private void OnClientBinary(uint clientId, byte[] data)
        {
            if (!BinaryEncoding.TryDecodeClientServiceCallRequest(data,
                    out var serviceId, out var callId, out var encoding, out var payload))
                return;

            if (encoding != "json")
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = serviceId, CallId = callId, Message = $"Unsupported encoding: {encoding}" }));
                return;
            }

            if (!_services.TryGet(serviceId, out _))
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = serviceId, CallId = callId, Message = $"Unknown service: {serviceId}" }));
                return;
            }

            if (payload.Length > FoxgloveServiceRegistry.MaxPayloadBytes)
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = serviceId, CallId = callId, Message = $"Payload exceeds 1 MiB limit" }));
                return;
            }

            // Validate payload is legal UTF-8 JSON
            try { JToken.Parse(Encoding.UTF8.GetString(payload)); }
            catch
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = serviceId, CallId = callId, Message = "Malformed JSON payload" }));
                return;
            }

            _services.Enqueue(serviceId, callId, clientId, encoding, payload);
        }

        // ── Parameter handlers ──

        private void HandleGetParameters(uint clientId, string json)
        {
            GetParameters msg;
            try { msg = JsonConvert.DeserializeObject<GetParameters>(json); }
            catch { _logger.LogWarning($"getParameters parse error from client {clientId}"); return; }

            var list = _parameters.GetWireParameters(msg.ParameterNames?.Count > 0 ? msg.ParameterNames : null);
            var resp = new ParameterValues { Parameters = list, Id = msg.Id };
            _transport.SendText(clientId, JsonConvert.SerializeObject(resp));
        }

        private void HandleSetParameters(uint clientId, string json)
        {
            SetParameters msg;
            try { msg = JsonConvert.DeserializeObject<SetParameters>(json); }
            catch { _logger.LogWarning($"setParameters parse error from client {clientId}"); return; }

            var changedNames = new List<string>();
            foreach (var p in msg.Parameters ?? new List<Parameter>())
            {
                if (p != null && p.Name != null && _parameters.TrySetFromClient(p.Name, p.Value))
                    changedNames.Add(p.Name);
            }

            // Echo back current values to requestor
            var names = msg.Parameters?.Select(p => p?.Name).Where(n => n != null);
            var current = _parameters.GetWireParameters(names);
            var resp = new ParameterValues { Parameters = current, Id = msg.Id };
            _transport.SendText(clientId, JsonConvert.SerializeObject(resp));

            // Broadcast to other subscribed clients (Phase 7 push)
            if (changedNames.Count > 0)
            {
                var broadcast = new ParameterValues { Parameters = _parameters.GetWireParameters(changedNames) };
                var broadcastJson = JsonConvert.SerializeObject(broadcast);
                foreach (var cid in GetParamSubscribersForChanged(changedNames, clientId))
                    _transport.SendText(cid, broadcastJson);
            }
        }

        private IEnumerable<uint> GetParamSubscribersForChanged(List<string> names, uint excludeClient)
        {
            foreach (var cid in _paramSubs.GetSubscribedClientIds())
            {
                if (cid == excludeClient) continue;
                foreach (var n in names)
                {
                    if (_paramSubs.IsSubscribed(cid, n))
                    { yield return cid; break; }
                }
            }
        }

        private void HandleSubscribeParameterUpdates(uint clientId, string json)
        {
            SubscribeParameterUpdates msg;
            try { msg = JsonConvert.DeserializeObject<SubscribeParameterUpdates>(json); }
            catch { _logger.LogWarning($"subscribeParameterUpdates parse error from client {clientId}"); return; }

            _paramSubs.Subscribe(clientId, msg.ParameterNames);
        }

        private void HandleUnsubscribeParameterUpdates(uint clientId, string json)
        {
            UnsubscribeParameterUpdates msg;
            try { msg = JsonConvert.DeserializeObject<UnsubscribeParameterUpdates>(json); }
            catch { _logger.LogWarning($"unsubscribeParameterUpdates parse error from client {clientId}"); return; }

            _paramSubs.Unsubscribe(clientId, msg.ParameterNames);
        }

        // ── Subscribe/unsubscribe (Phase 2) ──

        private void HandleSubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<SubscribeMessage>(json);
                foreach (var sub in msg.Subscriptions)
                    if (_channels.Get(sub.ChannelId) != null)
                        _subscriptions.AddSubscription(clientId, sub.Id, sub.ChannelId);
            }
            catch (Exception ex) { _logger.LogWarning($"subscribe parse error: {ex.Message}"); }
        }

        private void HandleUnsubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<UnsubscribeMessage>(json);
                if (msg.SubscriptionIds != null)
                    _subscriptions.RemoveSubscriptions(clientId, msg.SubscriptionIds);
            }
            catch (Exception ex) { _logger.LogWarning($"unsubscribe parse error: {ex.Message}"); }
        }

        /// <summary>Minimal DTO used to peek the "op" field. Used in Phase 2, kept for consistency.</summary>
        [JsonObject(MemberSerialization.OptIn)]
        private class JsonOpOnly
        {
            [JsonProperty("op")] public string Op { get; set; }
        }
    }
}
