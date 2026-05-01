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
        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string), uint>();

        public Core.FoxgloveRuntime Runtime => _runtime;
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        private void Awake()
        {
            if (_runInBackground)
                Application.runInBackground = true;

            _runtime = new Core.FoxgloveRuntime();
        }

        private void OnEnable()
        {
            if (_startOnEnable)
                StartServer();
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
        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs)
        {
            if (!IsRunning)
            {
                Debug.LogWarning("[Foxglove] PublishJson called but server is not running.");
                return;
            }

            var channelId = GetOrRegisterSchemaChannel(topic, schemaName);
            _runtime.PublishJson(channelId, message, logTimeNs);
        }
    }
}
