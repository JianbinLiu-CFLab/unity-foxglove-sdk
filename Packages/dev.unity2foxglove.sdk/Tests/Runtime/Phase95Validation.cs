// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 95 validation for ROS2 Bridge productization.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Foxglove;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase95Validation
    {
        private const ulong SampleTimeNs = 1_700_095_000_000_000_000UL;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 95: ROS2 Bridge Productization ===");
            _passed = 0;

            VerifyBridgePolicy();
            VerifyRuntimeQueueAndStats();
            VerifyRuntimeReconnect();
            VerifyRuntimeStopDisposesLateConnectedSink();
            VerifyPublisherWrapperUsesRuntime();
            VerifySourceIntegration();
            VerifyDocumentationAndEvidenceBoundary();

            Console.WriteLine($"Phase 95: {_passed} checks passed.");
        }

        private static void VerifyBridgePolicy()
        {
            Check((int)Ros2BridgeOutputOverride.UseManager == 0, "95A-1: bridge override UseManager explicit value");
            Check((int)Ros2BridgeOutputOverride.Disabled == 1, "95A-2: bridge override Disabled explicit value");
            Check((int)Ros2BridgeOutputOverride.Enabled == 2, "95A-3: bridge override Enabled explicit value");
            Check((int)Ros2BridgeEffectiveOutput.Disabled == 0, "95A-4: bridge effective Disabled explicit value");
            Check((int)Ros2BridgeEffectiveOutput.Enabled == 1, "95A-5: bridge effective Enabled explicit value");
            Check((int)Ros2BridgeEffectiveOutput.Unsupported == 2, "95A-6: bridge effective Unsupported explicit value");

            var masterOff = Ros2BridgeOutputPolicy.Resolve(
                managerEnabled: false,
                managerDefaultEnabled: true,
                allowPublisherOverride: true,
                publisherOverride: Ros2BridgeOutputOverride.Enabled,
                supportsBridge: true);
            Check(masterOff.Effective == Ros2BridgeEffectiveOutput.Disabled && !masterOff.IsEnabled,
                "95A-7: master switch disables bridge output");

            var managerDefault = Ros2BridgeOutputPolicy.Resolve(true, true, true, Ros2BridgeOutputOverride.UseManager, true);
            Check(managerDefault.Effective == Ros2BridgeEffectiveOutput.Enabled && managerDefault.IsEnabled,
                "95A-8: manager default enables bridge output");

            var publisherEnabled = Ros2BridgeOutputPolicy.Resolve(true, false, true, Ros2BridgeOutputOverride.Enabled, true);
            Check(publisherEnabled.Effective == Ros2BridgeEffectiveOutput.Enabled && publisherEnabled.Requested == Ros2BridgeEffectiveOutput.Enabled,
                "95A-9: publisher override can enable bridge when default disabled");

            var publisherDisabled = Ros2BridgeOutputPolicy.Resolve(true, true, true, Ros2BridgeOutputOverride.Disabled, true);
            Check(publisherDisabled.Effective == Ros2BridgeEffectiveOutput.Disabled,
                "95A-10: publisher override can disable bridge when default enabled");

            var overrideBlocked = Ros2BridgeOutputPolicy.Resolve(true, true, false, Ros2BridgeOutputOverride.Disabled, true);
            Check(overrideBlocked.Effective == Ros2BridgeEffectiveOutput.Enabled && overrideBlocked.Requested == Ros2BridgeEffectiveOutput.Enabled,
                "95A-11: manager can block publisher bridge override");

            var unsupported = Ros2BridgeOutputPolicy.Resolve(true, true, true, Ros2BridgeOutputOverride.Enabled, false);
            Check(unsupported.Effective == Ros2BridgeEffectiveOutput.Unsupported && unsupported.FellBack,
                "95A-12: unsupported publishers resolve to Unsupported");
            Check(unsupported.EffectiveLabel == "Unsupported" && managerDefault.EffectiveLabel == "Enabled",
                "95A-13: bridge policy labels are product words");
        }

        private static void VerifyRuntimeQueueAndStats()
        {
            var factory = new FakeSinkFactory();
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                8767,
                queueCapacity: 2,
                reconnectIntervalMs: 10,
                sendTimeoutMs: 100,
                sinkFactory: factory.Create);

            runtime.Start(enabled: true, autoConnect: false);
            Check(!runtime.TryEnqueue(CreateFrame("/unity/a", 1), out var disabledReason)
                  && disabledReason.Contains("auto-connect is disabled"),
                "95B-1: runtime rejects sends while auto-connect disabled");
            Check(!runtime.TryEnqueue(CreateFrame("/unity/b", 2), out _),
                "95B-2: runtime consistently rejects disabled auto-connect sends");

            var stats = runtime.GetStatsSnapshot();
            Check(stats.Enabled && !stats.Connected && stats.QueuedFrames == 0,
                "95B-3: stats expose enabled disconnected idle state");
            Check(stats.DroppedFrames == 0, "95B-4: disabled auto-connect does not count rejected sends as drops");

            runtime.Stop();
            stats = runtime.GetStatsSnapshot();
            Check(!stats.Enabled && stats.QueuedFrames == 0 && stats.DroppedFrames == 0,
                "95B-5: Stop has no idle auto-connect queue to drop");

            Check(Throws<ArgumentException>(() => new Ros2BridgeRuntime("0.0.0.0", 8767, 1, 10, 10, factory.Create)),
                "95B-6: runtime rejects wildcard hosts");
            Check(Throws<ArgumentException>(() => new Ros2BridgeRuntime("192.168.1.10", 8767, 1, 10, 10, factory.Create)),
                "95B-7: runtime rejects LAN hosts");
        }

        private static void VerifyRuntimeReconnect()
        {
            var factory = new FakeSinkFactory { ConnectFailuresRemaining = 1 };
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                8767,
                queueCapacity: 8,
                reconnectIntervalMs: 10,
                sendTimeoutMs: 100,
                sinkFactory: factory.Create);

            runtime.Start(enabled: true, autoConnect: true);
            Check(runtime.TryEnqueue(CreateFrame("/unity/tf", 1), out _), "95C-1: runtime enqueues before reconnect succeeds");
            Check(WaitUntil(() => factory.AllSentFrames.Count == 1, 3000),
                "95C-2: runtime reconnects and sends queued frame");
            var stats = runtime.GetStatsSnapshot();
            Check(stats.Connected && stats.SentFrames == 1 && stats.FailedFrames == 0,
                "95C-3: reconnect failures do not count as frame send failures");

            runtime.Stop();
            Check(factory.DisconnectCalls > 0, "95C-4: runtime disconnects sink on Stop");
        }

        private static void VerifyRuntimeStopDisposesLateConnectedSink()
        {
            var factory = new BlockingConnectSinkFactory();
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                8767,
                queueCapacity: 4,
                reconnectIntervalMs: 10,
                sendTimeoutMs: 100,
                sinkFactory: factory.Create);

            runtime.Start(enabled: true, autoConnect: true);
            Check(factory.ConnectStarted.Wait(3000), "95C2-1: stop race test reaches blocked connect");

            Exception stopException = null;
            var stopThread = new Thread(() =>
            {
                try
                {
                    factory.StopStarted.Set();
                    runtime.Stop();
                }
                catch (Exception ex)
                {
                    stopException = ex;
                }
            });
            stopThread.Start();
            Check(factory.StopStarted.Wait(3000), "95C2-2: stop race test starts Stop while connect is blocked");
            factory.ReleaseConnect.Set();

            Check(stopThread.Join(3000) && stopException == null, "95C2-3: Stop returns after late connect completes");
            Check(factory.DisposeCalls > 0, "95C2-4: Stop disposes sink connected during shutdown race");
        }

        private static void VerifyPublisherWrapperUsesRuntime()
        {
            var factory = new FakeSinkFactory();
            using var runtime = new Ros2BridgeRuntime("127.0.0.1", 8767, 4, 10, 100, factory.Create);
            runtime.Start(enabled: true, autoConnect: true);

            var publisher = new Ros2BridgePublisher(runtime);
            publisher.Publish("/unity/tf", "foxglove_msgs/msg/FrameTransform", CreateFrameTransformSample(), SampleTimeNs);

            Check(WaitUntil(() => factory.AllSentFrames.Count == 1, 3000),
                "95D-1: Ros2BridgePublisher can use background runtime sink");
            var frame = factory.AllSentFrames[0];
            Check(frame.Topic == "/unity/tf" && frame.SchemaName == "foxglove_msgs/msg/FrameTransform",
                "95D-2: runtime-backed publisher preserves topic and schema");
            Check(frame.Payload.Length > 4 && frame.Payload[0] == 0 && frame.Payload[1] == 1,
                "95D-3: runtime-backed publisher sends CDR payload");

            runtime.Stop();
        }

        private static void VerifySourceIntegration()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var ros2BridgeEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Ros2Bridge.cs");
            var cameraEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var pointCloudEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");

            Check(manager.Contains("_ros2BridgeEnabled") && manager.Contains("Ros2BridgeRuntime"),
                "95E-1: Manager owns bridge settings and runtime");
            Check(publishing.Contains("TryPrepareRos2BridgePublish") && publishing.Contains("PublishRos2BridgeCdr"),
                "95E-2: Manager exposes bridge prepare and CDR publish APIs");
            Check(publishing.Contains("SuppressLivePublishersForReplay") && !SourceMethodContains(publishing, "TryPrepareRos2BridgePublish", "IsRunning"),
                "95E-3: bridge prepare respects replay suppression but not WebSocket IsRunning");
            Check(publisherBase.Contains("_ros2BridgeOutput") && publisherBase.Contains("ShouldPrepareAnyPublishPayload"),
                "95E-4: Publisher base has independent bridge output policy");
            Check(publisherBase.Contains("ShouldPrepareRos2BridgePayload") && publisherBase.Contains("PublishRos2Bridge"),
                "95E-5: Publisher base has bridge prepare and publish helpers");

            foreach (var file in ProductPublisherFiles())
            {
                var source = ReadRepoText(file);
                Check(source.Contains("ShouldPrepareAnyPublishPayload") || source.Contains("ShouldPrepareRos2BridgePayload"),
                    "95E-6: publisher uses bridge-aware prepare gate " + Path.GetFileName(file));
                Check(source.Contains("PublishRos2Bridge"),
                    "95E-7: publisher mirrors ROS2 Bridge payload " + Path.GetFileName(file));
            }

            Check(!ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedPointCloudPublisher.cs").Contains("PublishRos2Bridge"),
                "95E-8: legacy compressed point cloud spike stays out of bridge productization");
            Check(managerEditor.Contains("ROS2 Bridge") && ros2BridgeEditor.Contains("Queued Frames") && ros2BridgeEditor.Contains("Last Error"),
                "95E-9: Manager Inspector exposes simple bridge UX");
            Check(cameraEditor.Contains("ROS2 Bridge") && pointCloudEditor.Contains("ROS2 Bridge"),
                "95E-10: custom publisher Inspectors expose bridge UX");
            Check(!managerEditor.Contains("payload-format") && !managerEditor.Contains("ros2msg"),
                "95E-11: Inspector does not expose sidecar internals");
        }

        private static void VerifyDocumentationAndEvidenceBoundary()
        {
            var readme = ReadRepoText("README.md");
            var docs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md");
            Check(readme.Contains("WebSocket") && readme.Contains("ROS2 Bridge") && readme.Contains("disabled by default"),
                "95F-1: README documents independent optional bridge");
            Check(docs.Contains("Phase 95")
                  && (docs.Contains("seven validated publisher") || docs.Contains("7 validated publisher"))
                  && docs.Contains("sidecar"),
                "95F-2: schema coverage docs describe Phase95 product boundary");
            Check(ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md").Contains("Phase 95"),
                "95F-3: sidecar README references productized bridge path");
        }

        private static Ros2BridgeFrame CreateFrame(string topic, ulong sequence)
        {
            return new Ros2BridgeFrame(
                topic,
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs + sequence,
                sequence,
                new byte[] { 0, 1, 0, 0, (byte)sequence });
        }

        private static FrameTransform CreateFrameTransformSample()
        {
            return new FrameTransform
            {
                Timestamp = new Google.Protobuf.WellKnownTypes.Timestamp { Seconds = 1_700_095_000L, Nanos = 123_000_000 },
                ParentFrameId = "world",
                ChildFrameId = "base_link",
                Translation = new Vector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new Quaternion { W = 1 }
            };
        }

        private static IReadOnlyList<string> ProductPublisherFiles()
        {
            return new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraCalibrationPublisher.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs"
            };
        }

        private static bool SourceMethodContains(string source, string methodName, string needle)
        {
            var idx = source.IndexOf(methodName, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            var braceStart = source.IndexOf('{', idx);
            if (braceStart < 0)
                return false;

            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(idx, i - idx + 1).Contains(needle);
                }
            }

            return source.Substring(idx).Contains(needle);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private sealed class FakeSinkFactory
        {
            private readonly object _gate = new object();
            public readonly List<Ros2BridgeFrame> AllSentFrames = new List<Ros2BridgeFrame>();
            public int ConnectFailuresRemaining;
            public int DisconnectCalls;

            public IRos2BridgeSink Create()
            {
                return new FakeSink(this);
            }

            private sealed class FakeSink : IRos2BridgeSink
            {
                private readonly FakeSinkFactory _owner;
                public FakeSink(FakeSinkFactory owner) => _owner = owner;
                public bool IsConnected { get; private set; }

                public void Connect(string host, int port, int timeoutMs)
                {
                    lock (_owner._gate)
                    {
                        if (_owner.ConnectFailuresRemaining > 0)
                        {
                            _owner.ConnectFailuresRemaining--;
                            throw new InvalidOperationException("planned connect failure");
                        }
                    }

                    IsConnected = true;
                }

                public void Send(Ros2BridgeFrame frame, int timeoutMs)
                {
                    if (!IsConnected)
                        throw new InvalidOperationException("not connected");
                    lock (_owner._gate)
                    {
                        _owner.AllSentFrames.Add(frame);
                    }
                }

                public void Disconnect()
                {
                    IsConnected = false;
                    lock (_owner._gate)
                    {
                        _owner.DisconnectCalls++;
                    }
                }

                public void Dispose() => Disconnect();
            }
        }

        private sealed class BlockingConnectSinkFactory
        {
            private readonly object _gate = new object();
            public readonly ManualResetEventSlim ConnectStarted = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseConnect = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim StopStarted = new ManualResetEventSlim(false);
            public int DisposeCalls;

            public IRos2BridgeSink Create()
            {
                return new BlockingSink(this);
            }

            private sealed class BlockingSink : IRos2BridgeSink
            {
                private readonly BlockingConnectSinkFactory _owner;
                public BlockingSink(BlockingConnectSinkFactory owner) => _owner = owner;
                public bool IsConnected { get; private set; }

                public void Connect(string host, int port, int timeoutMs)
                {
                    _owner.ConnectStarted.Set();
                    _owner.ReleaseConnect.Wait();
                    IsConnected = true;
                }

                public void Send(Ros2BridgeFrame frame, int timeoutMs)
                {
                }

                public void Disconnect()
                {
                    IsConnected = false;
                }

                public void Dispose()
                {
                    Disconnect();
                    lock (_owner._gate)
                    {
                        _owner.DisposeCalls++;
                    }
                }
            }
        }
    }
}
