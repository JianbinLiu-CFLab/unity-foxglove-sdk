// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 73 validation for subscription-aware heavy topic demand gating.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the demand-aware publish preflight surface and source-level
    /// guard placement used to keep heavy topic work subscription-aware.
    /// </summary>
    public static class Phase73Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 73 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 73: Subscription-Aware Heavy Topic QoS ===");
            _passed = 0;

            VerifySubscriptionDemand();
            VerifySessionRuntimeAndManagerDemandSurface();
            VerifyPublisherPreflightSurface();
            VerifyOrdinaryPublisherGuards();
            VerifyHeavyPublisherGuards();

            Console.WriteLine($"Phase 73: {_passed} checks passed.");
        }

        private static void VerifySubscriptionDemand()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            Check(source.Contains("public bool HasSubscribersForChannel"),
                "73A-1: SubscriptionRegistry exposes HasSubscribersForChannel");
            Check(source.Contains("_byChannel") && source.Contains("CopySubscribersForChannel"),
                "73A-2: SubscriptionRegistry maintains a reverse channel subscriber index");

            var method = typeof(SubscriptionRegistry).GetMethod(
                "HasSubscribersForChannel",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(uint) },
                modifiers: null);
            Check(method != null && method.ReturnType == typeof(bool),
                "73A-3: HasSubscribersForChannel signature is stable");

            var registry = new SubscriptionRegistry();
            Check(!InvokeHasSubscribers(method, registry, 42),
                "73A-4: new subscription registry has no demand");

            registry.AddSubscription(clientId: 10, subscriptionId: 20, channelId: 42);
            Check(InvokeHasSubscribers(method, registry, 42),
                "73A-5: adding a subscription creates channel demand");

            registry.RemoveSubscriptions(10, new[] { 20u });
            Check(!InvokeHasSubscribers(method, registry, 42),
                "73A-6: removing a subscription clears channel demand");

            registry.AddSubscription(clientId: 10, subscriptionId: 20, channelId: 42);
            registry.AddSubscription(clientId: 11, subscriptionId: 21, channelId: 43);
            registry.RemoveClient(10);
            Check(!InvokeHasSubscribers(method, registry, 42),
                "73A-7: removing a client clears that client's channel demand");
            Check(InvokeHasSubscribers(method, registry, 43),
                "73A-8: removing one client preserves other client demand");

            registry.AddSubscription(clientId: 11, subscriptionId: 21, channelId: 44);
            Check(!InvokeHasSubscribers(method, registry, 43) && InvokeHasSubscribers(method, registry, 44),
                "73A-9: replacing a subscription id moves demand between channels");

            var copyMethod = typeof(SubscriptionRegistry).GetMethod(
                "CopySubscribersForChannel",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(uint), typeof(List<(uint clientId, uint subscriptionId)>) },
                modifiers: null);
            Check(copyMethod != null,
                "73A-10: CopySubscribersForChannel is available for caller-owned snapshots");

            var copied = new List<(uint clientId, uint subscriptionId)>();
            copyMethod?.Invoke(registry, new object[] { 44u, copied });
            Check(copied.Count == 1 && copied[0].clientId == 11 && copied[0].subscriptionId == 21,
                "73A-11: CopySubscribersForChannel writes the channel snapshot into a caller-owned list");

            var removed = registry.RemoveChannel(44);
            Check(removed.Count == 1 && !InvokeHasSubscribers(method, registry, 44),
                "73A-12: removing a channel clears reverse-index demand");

            var hasDemandBody = ExtractMethodBody(source, "HasSubscribersForChannel");
            Check(!hasDemandBody.Contains("_clients.Values") && hasDemandBody.Contains("_byChannel.TryGetValue"),
                "73A-13: HasSubscribersForChannel uses the reverse index instead of a full client scan");
        }

        private static void VerifySessionRuntimeAndManagerDemandSurface()
        {
            var sessionSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var pointCloudSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(sessionSource.Contains("public bool HasChannelDemand"),
                "73B-1: FoxgloveSession exposes HasChannelDemand");
            Check(sessionSource.Contains("_channels.Get(channelId) == null"),
                "73B-2: session demand defensively rejects missing channels");
            Check(sessionSource.Contains("_subscriptions.HasSubscribersForChannel(channelId)"),
                "73B-3: session demand considers live subscribers");
            Check(sessionSource.Contains("Volatile.Read(ref _recorder) != null"),
                "73B-4: session demand considers MCAP recorder presence");
            Check(runtimeSource.Contains("public bool HasChannelDemand"),
                "73B-5: FoxgloveRuntime exposes HasChannelDemand passthrough");
            Check(managerSource.Contains("public bool TryPrepareSchemaPublish"),
                "73B-6: FoxgloveManager exposes schema publish preflight");
            Check(managerSource.Contains("requireDemand = true"),
                "73B-7: manager preflight defaults to demand-required mode");
            Check(managerSource.Contains("HasChannelDemand(channelId)"),
                "73B-8: manager preflight asks runtime for channel demand");

            var preflight = Slice(managerSource, "public bool TryPrepareSchemaPublish", "/// <summary>");
            Check(IndexOf(preflight, "GetOrRegisterSchemaChannel") < IndexOf(preflight, "HasChannelDemand(channelId)")
                    || IndexOf(preflight, "GetOrRegisterChannel") < IndexOf(preflight, "HasChannelDemand(channelId)"),
                "73B-9: manager registers/adverts the channel before checking demand");

            Check(IndexOf(pointCloudSource, "ShouldPreparePublishPayload()") < IndexOf(pointCloudSource, "_pendingFrame = null"),
                "73B-10: point cloud pending frame is not cleared before demand guard");

            Check(sessionSource.Contains("CopySubscribersForChannel")
                  && sessionSource.Contains("_subscriberScratchLock")
                  && !sessionSource.Contains("_subscriptions.GetSubscribersForChannel(channelId)"),
                "73B-11: session publish paths reuse caller-owned subscriber snapshots");

            VerifyCompiledSessionDemand();
            VerifyCompiledRuntimeDemandSurface();
        }

        private static void VerifyCompiledSessionDemand()
        {
            var method = typeof(FoxgloveSession).GetMethod(
                "HasChannelDemand",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(uint) },
                modifiers: null);
            Check(method != null && method.ReturnType == typeof(bool),
                "73C-1: compiled FoxgloveSession HasChannelDemand signature is stable");

            var transport = new Phase73FakeTransport();
            using var session = new FoxgloveSession("phase73", transport, schemaRegistry: new DefaultSchemaRegistry());

            Check(!InvokeChannelDemand(method, session, 77),
                "73C-2: session demand is false for missing channels");

            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 73,
                Topic = "/phase73",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            });
            Check(!InvokeChannelDemand(method, session, 73),
                "73C-3: advertised channel without subscribers or recorder has no demand");

            transport.SimulateText(1, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 9, ChannelId = 73 }
                }
            }));
            Check(InvokeChannelDemand(method, session, 73),
                "73C-4: subscriber creates session channel demand");

            transport.SimulateText(1, JsonConvert.SerializeObject(new UnsubscribeMessage
            {
                SubscriptionIds = new List<uint> { 9 }
            }));
            Check(!InvokeChannelDemand(method, session, 73),
                "73C-5: unsubscribe clears session channel demand");

            using var mcap = new MemoryStream();
            using var recorder = new McapRecorder(mcap);
            session.SetRecorder(recorder);
            Check(InvokeChannelDemand(method, session, 73),
                "73C-6: attached MCAP recorder counts as demand");
            session.SetRecorder(null);
        }

        private static void VerifyCompiledRuntimeDemandSurface()
        {
            var method = typeof(FoxgloveRuntime).GetMethod(
                "HasChannelDemand",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(uint) },
                modifiers: null);
            Check(method != null && method.ReturnType == typeof(bool),
                "73D-1: compiled FoxgloveRuntime HasChannelDemand signature is stable");

            using var runtime = new FoxgloveRuntime(
                new Phase73FakeTransport(),
                new SystemClock(),
                new DefaultSchemaRegistry());
            Check(!InvokeRuntimeDemand(method, runtime, 1),
                "73D-2: runtime demand is false before a session is running");
        }

        private static void VerifyPublisherPreflightSurface()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var helper = Slice(source, "protected bool ShouldPreparePublishPayload()", "private PublisherEncodingResolution ResolvePublisherEncoding");

            Check(source.Contains("protected bool ShouldPreparePublishPayload()"),
                "73E-1: publisher base exposes default preflight helper");
            Check(source.Contains("protected bool ShouldPreparePublishPayload(PublisherEffectiveEncoding effectiveEncoding)"),
                "73E-2: publisher base exposes encoding-specific preflight helper");
            Check(helper.Contains("ResolvePublisherEncoding()"),
                "73E-3: preflight resolves effective encoding once");
            Check(helper.Contains("WarnIfEncodingFallback"),
                "73E-4: preflight preserves fallback warnings");
            Check(helper.Contains("WarnEncodingMismatch"),
                "73E-5: preflight preserves mismatch warnings");
            Check(helper.Contains("PublisherEncodingPolicy.ToProtocolEncoding(attemptedEncoding)")
                  && helper.Contains("PublisherEffectiveEncoding.Ros2")
                  && helper.Contains("TryPrepareRos2Publish"),
                "73E-6: preflight maps publisher encoding to wire encoding strings");
            Check(helper.Contains("TryPrepareSchemaPublish"),
                "73E-7: preflight delegates registration and demand to manager");
        }

        private static void VerifyOrdinaryPublisherGuards()
        {
            var genericSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisher.cs");
            var genericUpdate = Slice(genericSource, "protected virtual void Update()", "    }\r\n}");
            CheckOrdered(genericUpdate, "ShouldPublishNow()", "TryPreparePublishPayload", "73F-1: generic publisher preflights after cadence");
            CheckOrdered(genericUpdate, "TryPreparePublishPayload", "CreateMessage()", "73F-2: generic publisher preflights before message creation");

            var protoSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/ProtobufPublisher.cs");
            var protoUpdate = Slice(protoSource, "protected virtual void Update()", "    }\r\n}");
            CheckOrdered(protoUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73F-3: protobuf publisher preflights after cadence");
            CheckOrdered(protoUpdate, "ShouldPreparePublishPayload()", "CreateMessage()", "73F-4: protobuf publisher preflights before message creation");
            CheckOrdered(protoUpdate, "ShouldPreparePublishPayload()", "ToByteArray()", "73F-5: protobuf publisher preflights before serialization");

            var transformSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            var transformUpdate = Slice(transformSource, "protected override void Update()", "protected override FrameTransformMessage CreateMessage()");
            CheckOrdered(transformUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73F-6: transform publisher preflights after cadence");
            CheckOrdered(transformUpdate, "ShouldPreparePublishPayload()", "PublishProtobufTransform", "73F-7: transform publisher preflights before protobuf transform creation");
            CheckOrdered(transformUpdate, "ShouldPreparePublishPayload()", "CreateMessage(unixNs)", "73F-8: transform publisher preflights before JSON transform creation");

            var calibrationSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraCalibrationPublisher.cs");
            var calibrationUpdate = Slice(calibrationSource, "private void Update()", "private CameraCalibrationMessage BuildCalibration");
            CheckOrdered(calibrationUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73F-9: camera calibration preflights after cadence");
            CheckOrdered(calibrationUpdate, "ShouldPreparePublishPayload()", "BuildCalibration", "73F-10: camera calibration preflights before object construction");

            var laserSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var laserUpdate = Slice(laserSource, "private void Update()", "private double[] ResolveRanges()");
            CheckOrdered(laserUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73F-11: laser scan preflights after cadence");
            CheckOrdered(laserUpdate, "ShouldPreparePublishPayload()", "ResolveRanges()", "73F-12: laser scan preflights before range construction");
        }

        private static void VerifyHeavyPublisherGuards()
        {
            var cameraSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraLateUpdate = Slice(cameraSource, "private void LateUpdate()", "private void OnReadbackComplete");
            Check(cameraSource.Contains("_maxPendingReadbacks = 1"),
                "73G-1: new camera publisher default max pending readbacks is one");
            CheckOrdered(cameraLateUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73G-2: camera preflights after cadence");
            CheckOrdered(cameraLateUpdate, "ShouldPreparePublishPayload()", "_captureCam.Render()", "73G-3: camera preflights before render");
            CheckOrdered(cameraLateUpdate, "ShouldPreparePublishPayload()", "AsyncGPUReadback.Request", "73G-4: camera preflights before GPU readback");

            var sceneSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            var sceneUpdate = Slice(sceneSource, "protected override void Update()", "protected override SceneUpdateMessage CreateMessage()");
            CheckOrdered(sceneUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73G-5: scene cube preflights after cadence");
            CheckOrdered(sceneUpdate, "ShouldPreparePublishPayload()", "PublishProtobufSceneUpdate", "73G-6: scene cube preflights before protobuf scene construction");
            CheckOrdered(sceneUpdate, "ShouldPreparePublishPayload()", "CreateMessage(unixNs)", "73G-7: scene cube preflights before JSON scene construction");

            var pointSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var pointUpdate = Slice(pointSource, "protected virtual void Update()", "protected virtual void PublishPreparedFrame");
            CheckOrdered(pointUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "73G-8: point cloud preflights after cadence");
            CheckOrdered(pointUpdate, "ShouldPreparePublishPayload()", "PrepareFrameForQoS", "73G-9: point cloud preflights before frame QoS copy");
            CheckOrdered(pointUpdate, "ShouldPreparePublishPayload()", "CreateFrameFromTransforms", "73G-10: point cloud preflights before child transform scan");
            CheckOrdered(pointUpdate, "ShouldPreparePublishPayload()", "_pendingFrame = null", "73G-11: point cloud preflights before pending frame consumption");
        }

        private static bool InvokeHasSubscribers(MethodInfo method, SubscriptionRegistry registry, uint channelId)
            => (bool)method.Invoke(registry, new object[] { channelId });

        private static bool InvokeChannelDemand(MethodInfo method, FoxgloveSession session, uint channelId)
            => (bool)method.Invoke(session, new object[] { channelId });

        private static bool InvokeRuntimeDemand(MethodInfo method, FoxgloveRuntime runtime, uint channelId)
            => (bool)method.Invoke(runtime, new object[] { channelId });

        private static void CheckOrdered(string text, string before, string after, string name)
        {
            Check(IndexOf(text, before) >= 0 && IndexOf(text, after) >= 0 && IndexOf(text, before) < IndexOf(text, after), name);
        }

        private static int IndexOf(string text, string pattern)
            => text.IndexOf(pattern, StringComparison.Ordinal);

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? text.Substring(startIndex)
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var index = source.IndexOf(methodName, StringComparison.Ordinal);
            if (index < 0) return "";
            var brace = source.IndexOf('{', index);
            if (brace < 0) return "";
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(brace, i - brace + 1);
                }
            }
            return source.Substring(brace);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        private sealed class Phase73FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }

            public void SimulateText(uint clientId, string json)
                => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
