#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Fixtures
// Purpose: Small Unity and Manager host surface for production-boundary tests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace UnityEngine
{
    public enum FindObjectsInactive
    {
        Exclude,
        Include
    }

    public enum FindObjectsSortMode
    {
        None
    }

    public enum RuntimePlatform
    {
        WindowsEditor
    }

    public struct Scene
    {
        public int handle;

        public bool IsValid() => handle != 0;
    }

    public static class Application
    {
        public static string dataPath => "Project/Assets";
        public static RuntimePlatform platform => RuntimePlatform.WindowsEditor;
        public static bool isPlaying => true;
    }

    public static class Time
    {
        public static double realtimeSinceStartupAsDouble;
        public static double unscaledTimeAsDouble;
        public static double fixedTimeAsDouble;
    }

    public static class Debug
    {
        public static int WarningCount { get; private set; }
        public static int ErrorCount { get; private set; }
        public static string LastWarning { get; private set; }

        public static void Log(string message) { }

        public static void LogWarning(string message)
        {
            WarningCount++;
            LastWarning = message;
        }

        public static void LogError(string message)
        {
            ErrorCount++;
        }

        public static void Reset()
        {
            WarningCount = 0;
            ErrorCount = 0;
            LastWarning = null;
        }
    }

    public class Object
    {
        private static readonly List<Object> Registry = new List<Object>();

        public static void Register(Object value)
        {
            if (value != null && !Registry.Contains(value))
                Registry.Add(value);
        }

        public static void ResetRegistry() => Registry.Clear();

        public static T[] FindObjectsByType<T>(
            FindObjectsInactive inactive,
            FindObjectsSortMode sortMode)
            where T : Object
            => Registry.OfType<T>().ToArray();

        public static T FindFirstObjectByType<T>()
            where T : Object
            => Registry.OfType<T>().FirstOrDefault();

        public static T FindAnyObjectByType<T>()
            where T : Object
            => Registry.OfType<T>().FirstOrDefault();

        public int GetInstanceID() => RuntimeHelpers.GetHashCode(this);
    }

    public class Component : Object
    {
        public GameObject gameObject { get; set; } = new GameObject();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject(string name = "boundary")
        {
            this.name = name;
        }

        public string name { get; set; }
        public Scene scene = new Scene { handle = 1 };
    }

    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string value) { }
    }

    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string value) { }
    }

    public sealed class SerializeField : Attribute { }
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float minimum, float maximum) { }
    }

    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float value) { }
    }

    public sealed class DisallowMultipleComponentAttribute : Attribute { }
}

namespace UnityEngine.Scripting
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class PreserveAttribute : Attribute { }
}

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager : MonoBehaviour
    {
        private readonly BoundaryRuntime _runtime = new BoundaryRuntime();
        private UnityReplayCursorEndpoint _replayCursorEndpoint;
        private readonly ReplayRuntimeState _replayState = new ReplayRuntimeState();
        private bool _enableReplay;
        private string _replayFilePath;
        private bool _enableRemoteMcapFileServer;
        private string _remoteMcapFileServerHost = "127.0.0.1";
        private int _remoteMcapFileServerPort;
        private string _remoteMcapFileServerSourceId = "manager-boundary";
        private bool _enableReplayCursorBridge;
        private string _replayCursorBridgeHost = "127.0.0.1";
        private int _replayCursorBridgePort;
        private string _replayCursorBridgeToken = string.Empty;
        private bool _replayCursorEndpointLoggedFirstCursor;
        private bool _replayCursorEndpointLoggedUnavailable;
        private readonly ConnectionRuntimeState _connectionState = new ConnectionRuntimeState(1);
        private readonly ClientEventAdmissionState _clientEventAdmission = new ClientEventAdmissionState();
        private readonly List<ClientEvent> _boundaryEnqueuedEvents = new List<ClientEvent>();
        private BoundaryTransport _boundaryTransport;
        private FoxgloveSession _runtimeForwarderSession;
        private Action<string, byte[]> _replayForwarder;
        private Action<ReplayMessageContext> _replayContextForwarder;
        private Action<ReplayBatchContext> _replayBatchForwarder;
        private Action<uint, uint, string, string, byte[]> _clientMessageForwarder;
        private Action<uint> _clientConnectedForwarder;
        private Action<uint> _clientDisconnectedForwarder;

        public bool IsRunning { get; set; } = true;
        public ulong NowNs { get; set; } = 1UL;
        public GlobalEncoding DefaultPublisherEncoding { get; set; } = GlobalEncoding.Json;
        public bool AllowPublisherOverride { get; set; } = true;
        public float DefaultPublishRateHz { get; set; } = 10f;
        public bool HasOrdinaryTransportDemand { get; set; }
        public bool SuppressLivePublishersForReplay { get; set; }

        internal bool TryPrepareMsgPackPublish(string topic, out uint channelId, bool requireDemand)
        {
            channelId = 1U;
            return true;
        }

        internal bool TryPrepareSchemaPublish(
            string topic,
            string schemaName,
            string encoding,
            out uint channelId,
            bool requireDemand)
        {
            channelId = 1U;
            return true;
        }

        public void PublishJson<T>(string topic, string schemaName, T payload, ulong logTimeNs) { }
        public void PublishProto<T>(string topic, string schemaName, T payload, ulong logTimeNs) { }
        public void PublishFoxRunJsonBytes(string topic, string schemaName, byte[] payload, ulong logTimeNs) { }
        public void PublishFoxRunMessagePackBytes(string topic, byte[] payload, ulong logTimeNs) { }
        internal void PublishMsgPack(string topic, byte[] payload, ulong logTimeNs) { }

        internal FoxRunOrdinaryTransportFanoutResult PublishOrdinaryTransports(
            in FoxRunOrdinaryPayloadRequest request)
            => default;

        public bool TryPrepareFoxRunMessagePackRecording(
            string topic,
            out uint channelId,
            out string reason)
        {
            channelId = 0U;
            reason = string.Empty;
            return false;
        }

        public bool TryPublishFoxRunMessagePackRecording(
            string topic,
            byte[] payload,
            ulong logTimeNs,
            out string reason)
        {
            reason = string.Empty;
            return false;
        }

        internal uint RegisterService(
            ServiceDescriptor descriptor,
            Func<JToken, JToken> handler)
            => 1U;

        internal bool UnregisterServiceDuringCleanup(uint serviceId) => true;

        internal ComponentPublisherSessionSnapshot CaptureForTest(ulong generation)
            => CaptureComponentPublisherSession(generation);

        internal ulong AttachComponentSessionForTest(bool keepSessionAlive = false)
        {
            _boundaryTransport = new BoundaryTransport();
            var session = new FoxgloveSession("manager-boundary", _boundaryTransport);
            AttachRuntimeForwarders(session);
            var generation = _connectionState.ChannelSessionGeneration;
            if (!keepSessionAlive)
                session.Dispose();
            return generation;
        }

        internal void DisposeBoundarySessionForTest()
            => _runtimeForwarderSession?.Dispose();

        internal void EmitBoundaryClientEventsForTest()
        {
            _boundaryTransport?.RaiseClientConnected(17U);
            _clientMessageForwarder?.Invoke(
                17U,
                23U,
                "/boundary",
                "json",
                Array.Empty<byte>());
        }

        internal IReadOnlyList<ClientEvent> BoundaryEnqueuedEventsForTest
            => _boundaryEnqueuedEvents;

        internal bool BoundaryAdmissionAcceptsForTest(ulong generation)
            => _clientEventAdmission.IsAccepting(generation);

        internal void SetActiveSessionForTest(ComponentPublisherSessionSnapshot snapshot)
            => SetActiveComponentPublisherSession(snapshot);

        internal void ClearActiveSessionForTest()
            => RunComponentPublisherSessionStopTail(false, null);

        internal void ConfigureRemoteForTest(string path, int port)
        {
            _enableReplay = true;
            _enableRemoteMcapFileServer = true;
            _replayFilePath = path;
            _remoteMcapFileServerPort = port;
        }

        internal void ConfigureCursorForTest(int port)
        {
            _enableReplay = true;
            _enableReplayCursorBridge = true;
            _replayCursorBridgePort = port;
        }

        internal void StartRemoteForTest() => StartRemoteMcapFileServerIfNeeded();
        internal void RefreshRemoteForTest() => RefreshRemoteMcapFileServerIfNeeded();
        internal void StartCursorForTest() => StartReplayCursorEndpointIfNeeded();
        internal void RefreshCursorForTest() => RefreshReplayCursorEndpointIfNeeded();
        internal void StopSidecarsForTest()
        {
            StopRemoteMcapFileServer();
            StopReplayCursorEndpoint();
        }

        internal bool RemoteConfigKnownForTest => _remoteMcapFileServerConfigKnown;
        internal bool CursorConfigKnownForTest => _replayCursorEndpointConfigKnown;
        internal bool RemoteRunningForTest => _remoteMcapFileServer?.IsRunning == true;
        internal bool CursorRunningForTest => _replayCursorEndpoint?.IsRunning == true;
        internal void SetTimeForTest(double value) => Time.realtimeSinceStartupAsDouble = value;

        private string ResolveProjectPath(string path) => path;
        private string ResolveRemoteMcapFileServerToken() => string.Empty;
        private string ResolveReplayCursorBridgeToken() => _replayCursorBridgeToken;

        internal void InvokeReplayMessageForTest(string topic, byte[] payload)
            => InvokeReplayMessageSubscribers(topic, payload);

        internal void InvokeReplayMessageContextForTest(ReplayMessageContext context)
            => InvokeReplayMessageContextSubscribers(context);

        internal void InvokeReplayBatchForTest(ReplayBatchContext context)
            => InvokeReplayBatchSubscribers(context);

        private void AdvanceChannelSessionGeneration()
            => _connectionState.AdvanceChannelSessionGeneration();

        private void EnqueueClientLifecycleEvent(ClientEvent evt)
            => _boundaryEnqueuedEvents.Add(evt);

        private void EnqueueClientMessageEvent(ClientEvent evt)
            => _boundaryEnqueuedEvents.Add(evt);
    }

    internal readonly struct ClientEvent
    {
        private ClientEvent(
            ulong generation,
            uint clientId,
            uint channelId,
            string topic,
            string encoding,
            byte[] payload,
            bool isConnect,
            bool isMessage)
        {
            Generation = generation;
            ClientId = clientId;
            ChannelId = channelId;
            Topic = topic;
            Encoding = encoding;
            Payload = payload;
            IsConnect = isConnect;
            IsMessage = isMessage;
        }

        public static ClientEvent Connect(uint clientId)
            => Connect(0UL, clientId);

        public static ClientEvent Connect(ulong generation, uint clientId)
            => new ClientEvent(generation, clientId, 0U, null, null, null, true, false);

        public static ClientEvent Disconnect(uint clientId)
            => Disconnect(0UL, clientId);

        public static ClientEvent Disconnect(ulong generation, uint clientId)
            => new ClientEvent(generation, clientId, 0U, null, null, null, false, false);

        public static ClientEvent Message(
            uint clientId,
            uint channelId,
            string topic,
            string encoding,
            byte[] payload)
            => Message(0UL, clientId, channelId, topic, encoding, payload);

        public static ClientEvent Message(
            ulong generation,
            uint clientId,
            uint channelId,
            string topic,
            string encoding,
            byte[] payload)
            => new ClientEvent(generation, clientId, channelId, topic, encoding, payload, false, true);

        public readonly ulong Generation;
        public readonly uint ClientId;
        public readonly uint ChannelId;
        public readonly string Topic;
        public readonly string Encoding;
        public readonly byte[] Payload;
        public readonly bool IsConnect;
        public readonly bool IsMessage;
    }

    internal sealed class BoundaryRuntime
    {
        internal event Action<string, byte[]> OnReplayMessage;
        internal event Action<ReplayMessageContext> OnReplayMessageContext;
        internal event Action<ReplayBatchContext> OnReplayBatchCompleted;
        internal bool UnregisterServiceDuringCleanup(uint serviceId) => true;
        internal void SetExternalReplayCursorEnabled(bool enabled) { }

        internal ExternalReplayCursorEnqueueResult TryEnqueueExternalReplayCursor(
            ReplayCursorRequest request,
            out string message)
        {
            message = "accepted";
            return ExternalReplayCursorEnqueueResult.Accepted;
        }

        internal ReplayCursorState GetExternalReplayCursorState()
            => default;
    }

    internal sealed class BoundaryTransport : IFoxgloveTransport
    {
        public bool IsRunning => false;
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;
        public void Start(string host, int port) { }
        public void Stop() { }
        public void BroadcastText(string json) { }
        public void BroadcastBinary(byte[] data) { }
        public void SendText(uint clientId, string json) { }
        public void SendBinary(uint clientId, byte[] data) { }
        public void Dispose() { }

        internal void RaiseClientConnected(uint clientId)
            => OnClientConnected?.Invoke(clientId);
    }
}
#endif
