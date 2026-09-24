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

    public sealed class DisallowMultipleComponent : Attribute { }
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
        private ulong _boundaryGeneration;

        public bool IsRunning { get; set; } = true;
        public ulong NowNs { get; set; } = 1UL;
        public GlobalEncoding DefaultPublisherEncoding { get; set; } = GlobalEncoding.Json;
        public bool AllowPublisherOverride { get; set; } = true;
        public float DefaultPublishRateHz { get; set; } = 10f;
        public bool HasOrdinaryTransportDemand { get; set; }

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

        internal void PublishJson<T>(string topic, string schemaName, T payload, ulong logTimeNs) { }
        internal void PublishProto(string topic, string schemaName, byte[] payload, ulong logTimeNs) { }
        internal void PublishMsgPack(string topic, byte[] payload, ulong logTimeNs) { }

        internal FoxRunOrdinaryTransportFanoutResult PublishOrdinaryTransports(
            in FoxRunOrdinaryPayloadRequest request)
            => default;

        internal uint RegisterService(
            ServiceDescriptor descriptor,
            Func<JToken, JToken> handler)
            => 1U;

        internal bool UnregisterServiceDuringCleanup(uint serviceId) => true;

        internal ComponentPublisherSessionSnapshot CaptureForTest(ulong generation)
            => CaptureComponentPublisherSession(generation);

        internal ulong AttachComponentSessionForTest()
            => _componentPublisherSessionState.AdvanceActivateAndCapture(
                () => ++_boundaryGeneration,
                _ => { },
                CaptureComponentPublisherSession);

        internal void SetActiveSessionForTest(ComponentPublisherSessionSnapshot snapshot)
            => SetActiveComponentPublisherSession(snapshot);

        internal void ClearActiveSessionForTest()
            => ClearActiveComponentPublisherSession();

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

        private readonly ReplaySubscriberFanoutState<Action<string, byte[]>> _replayMessageSubscribers =
            new ReplaySubscriberFanoutState<Action<string, byte[]>>();
        private readonly ReplaySubscriberFanoutState<Action<ReplayMessageContext>> _replayMessageContextSubscribers =
            new ReplaySubscriberFanoutState<Action<ReplayMessageContext>>();
        private readonly ReplaySubscriberFanoutState<Action<ReplayBatchContext>> _replayBatchSubscribers =
            new ReplaySubscriberFanoutState<Action<ReplayBatchContext>>();

        public event Action<string, byte[]> OnReplayMessage
        {
            add => _replayMessageSubscribers.Add(value);
            remove => _replayMessageSubscribers.Remove(value);
        }

        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add => _replayMessageContextSubscribers.Add(value);
            remove => _replayMessageContextSubscribers.Remove(value);
        }

        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add => _replayBatchSubscribers.Add(value);
            remove => _replayBatchSubscribers.Remove(value);
        }

        private void InvokeReplayMessageSubscribers(string topic, byte[] payload)
            => _replayMessageSubscribers.Invoke(
                handler => handler(topic, payload),
                exception => Debug.LogWarning("[Foxglove] Replay message listener failed: " + exception.Message));

        private void InvokeReplayMessageContextSubscribers(ReplayMessageContext context)
            => _replayMessageContextSubscribers.Invoke(
                handler => handler(context),
                exception => Debug.LogWarning("[Foxglove] Replay message context listener failed: " + exception.Message));

        private void InvokeReplayBatchSubscribers(ReplayBatchContext context)
            => _replayBatchSubscribers.Invoke(
                handler => handler(context),
                exception => Debug.LogWarning("[Foxglove] Replay batch listener failed: " + exception.Message));

        internal void InvokeReplayMessageForTest(string topic, byte[] payload)
            => InvokeReplayMessageSubscribers(topic, payload);

        internal void InvokeReplayMessageContextForTest(ReplayMessageContext context)
            => InvokeReplayMessageContextSubscribers(context);

        internal void InvokeReplayBatchForTest(ReplayBatchContext context)
            => InvokeReplayBatchSubscribers(context);
    }

    internal sealed class BoundaryRuntime
    {
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
}
#endif
