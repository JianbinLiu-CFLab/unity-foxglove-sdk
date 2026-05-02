using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// MonoBehaviour entry point. Manages FoxgloveRuntime lifecycle
    /// within Unity's game loop.
    /// </summary>
    public class FoxgloveManager : MonoBehaviour
    {
        [SerializeField] private string _serverName = "Unity Foxglove SDK";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 8765;
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private bool _runInBackground = true;

        private Core.FoxgloveRuntime _runtime;
        private int _nextChannelId = 1;
        private bool _warnedNotRunning;
        private readonly System.Collections.Concurrent.ConcurrentQueue<ClientEvent> _clientEvents = new();
        public event System.Action<uint> OnClientConnected;
        public event System.Action<uint> OnClientDisconnected;
        public event System.Action<uint, uint, string, byte[]> OnClientMessage;
        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string), uint>();

        public Core.FoxgloveRuntime Runtime => _runtime;
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        private void Awake()
        {
            if (_runInBackground)
                Application.runInBackground = true;

            _runtime = new Core.FoxgloveRuntime(new UnityLogger());
        }

        private void EnqueueConnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = true });

        private void EnqueueDisconnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = false });

        private void OnEnable()
        {
            if (_startOnEnable)
                StartServer();
        }

        private void Update()
        {
            _runtime?.Tick();
            while (_clientEvents.TryDequeue(out var evt))
            {
                if (evt.IsMessage)
                    OnClientMessage?.Invoke(evt.ClientId, evt.ChannelId, evt.Topic, evt.Payload);
                else if (evt.IsConnect)
                    OnClientConnected?.Invoke(evt.ClientId);
                else
                    OnClientDisconnected?.Invoke(evt.ClientId);
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
            _runtime?.Dispose();
            _runtime = null;
        }

        public void StartServer()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Foxglove] Server already running.");
                return;
            }

            _runtime.Start(_serverName, _host, _port);
            _warnedNotRunning = false;

            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected += EnqueueConnect;
                transport.OnClientDisconnected += EnqueueDisconnect;
                _runtime.Session.OnClientMessage += (cid, chId, topic, payload) =>
                    _clientEvents.Enqueue(new ClientEvent { ClientId = cid, ChannelId = chId, Topic = topic, Payload = payload, IsConnect = false, IsMessage = true });
            }

            Debug.Log($"[Foxglove] Server started on ws://{_host}:{_port}");
        }

        public void StopServer()
        {
            if (!IsRunning) return;
            _runtime.Stop();
            _channelCache.Clear();
            _nextChannelId = 1;
        }

        /// <summary>
        /// Get or register a schema channel. Idempotent: same (topic, schemaName) returns same id.
        /// </summary>
        public uint GetOrRegisterSchemaChannel(string topic, string schemaName)
        {
            var key = (topic, schemaName);
            if (_channelCache.TryGetValue(key, out var id))
                return id;

            id = (uint)_nextChannelId++;
            _channelCache[key] = id;
            _runtime.RegisterSchemaChannel(id, topic, schemaName);
            return id;
        }

        /// <summary>
        /// Serialize a message to JSON and publish. Safe no-op if runtime is not started.
        /// </summary>
        public void RegisterParameter(string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            _runtime?.RegisterParameter(name, value, type, writable);
        }

        public uint RegisterService(Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor)
        {
            return _runtime?.RegisterService(descriptor) ?? 0;
        }

        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs)
        {
            if (!IsRunning)
            {
                if (!_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishJson called but server is not running.");
                    _warnedNotRunning = true;
                }
                return;
            }

            var channelId = GetOrRegisterSchemaChannel(topic, schemaName);
            _runtime.PublishJson(channelId, message, logTimeNs);
        }
    }

    internal struct ClientEvent
    {
        public uint ClientId;
        public uint ChannelId;
        public string Topic;
        public byte[] Payload;
        public bool IsConnect;
        public bool IsMessage;
    }
}
