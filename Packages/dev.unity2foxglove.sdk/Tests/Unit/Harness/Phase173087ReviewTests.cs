// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-087 Unity review regression checks.

using System;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-087")]
    [Trait("Domain", "UnityReview")]
    public sealed class Phase173087ReviewTests
    {
        [Fact]
        public void ManagedWebSocketRedactUrlUsesCachedRegex()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWebSocketOptions.cs");

            Assert.Equal(
                "wss://127.0.0.1:8765?token=REDACTED&foo=bar",
                ManagedWebSocketOptions.RedactUrl("wss://127.0.0.1:8765?token=secret&foo=bar"));
            Assert.Contains("private static readonly Regex TokenRedactRegex", source, StringComparison.Ordinal);
            Assert.Contains("return TokenRedactRegex.Replace(url, \"$1REDACTED\");", source, StringComparison.Ordinal);
            Assert.DoesNotContain("return Regex.Replace(", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2TimeSourcesThrottleUnavailableWarningsAcrossRuntimePackages()
        {
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
        }

        [Fact]
        public void SamplesDocumentKeepsBridgeOutOfCoreAndSequentialHeadings()
        {
            var doc = TestSources.Text("Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md");

            Assert.DoesNotContain("## 5. ROS2 Bridge Sample", doc, StringComparison.Ordinal);
            Assert.Contains("## 5. Repository Demo Project", doc, StringComparison.Ordinal);
            Assert.Contains("## 6. Sample Promotion Rule", doc, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayPendingQueueExposesTestOnlyDebugHeadIndexWithoutReflection()
        {
            var queueSource = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayPendingQueue.cs");
            var testSource = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Replay/McapReplayHelperExtractionTests.cs");

            Assert.Contains("internal int DebugHeadIndex => _headIndex;", queueSource, StringComparison.Ordinal);
            Assert.DoesNotContain("BindingFlags", testSource, StringComparison.Ordinal);
            Assert.DoesNotContain("GetField(", testSource, StringComparison.Ordinal);
            Assert.Contains("queue.DebugHeadIndex", testSource, StringComparison.Ordinal);
        }

        [Fact]
        public void McapDirectMessageTestsDisposeReaderAndUseValueAssertions()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Mcap/McapDirectMessageRecordsTests.cs");

            Assert.DoesNotContain("\n            var reader = new McapReader(ms);", source, StringComparison.Ordinal);
            Assert.Contains("using var reader = new McapReader(ms);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Assert.True(summary.Statistics.MessageCount ==", source, StringComparison.Ordinal);
            Assert.Contains("Assert.Equal(20UL, summary.Statistics.MessageCount);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorCompileSymbolEntryPointUsesSafeWrapper()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var method = TestSources.ExtractMethod(source, "public static void ReconcileCompileSymbolForEditor()");

            Assert.Contains("ReconcileCompileSymbolSafely();", method, StringComparison.Ordinal);
            Assert.DoesNotContain("ReconcileCompileSymbol();", method, StringComparison.Ordinal);
        }

        [Fact]
        public void TopicMetadataEmitterDocumentsProcessLifetimeSha256()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");

            Assert.Contains("Process-lifetime generator helper", source, StringComparison.Ordinal);
            Assert.Contains("SHA256.HashData", source, StringComparison.Ordinal);
        }

        [Fact]
        public void HumbleStandaloneDistroComesFromPackagedMetadata()
        {
            var source = RuntimeSource("humble", "ROS2ForUnity.cs");
            var constructor = TestSources.ExtractMethod(source, "internal ROS2ForUnity()");

            Assert.Contains("bool standaloneBuild = IsStandalone();", constructor, StringComparison.Ordinal);
            Assert.Contains(
                "standaloneBuild\n                ? GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\")\n                : GetROSVersion();",
                constructor.Replace("\r\n", "\n", StringComparison.Ordinal),
                StringComparison.Ordinal);
            Assert.True(
                constructor.IndexOf("GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\")", StringComparison.Ordinal)
                < constructor.IndexOf("WarnIfStandaloneRosDistroOverride", StringComparison.Ordinal));
        }

        [Fact]
        public void RuntimeOwnersPruneDirectlyDisposedNodeFacadesByStableIdentity()
        {
            foreach (var distro in RuntimeDistros)
            {
                var node = RuntimeSource(distro, "ROS2Node.cs");
                Assert.Contains("internal INode NativeNode { get; }", node, StringComparison.Ordinal);
                Assert.Contains("NativeNode = node;", node, StringComparison.Ordinal);

                foreach (var ownerFile in new[] { "ROS2UnityCore.cs", "ROS2UnityComponent.cs" })
                {
                    var owner = RuntimeSource(distro, ownerFile);
                    var prune = TestSources.ExtractMethod(owner, "private void PruneDisposedNodesLocked()");

                    Assert.Contains("ros2csNodes.Add(node.NativeNode);", owner, StringComparison.Ordinal);
                    Assert.Contains("ros2csNodes.Remove(node.NativeNode)", owner, StringComparison.Ordinal);
                    Assert.Contains("PruneDisposedNodesLocked();", owner, StringComparison.Ordinal);
                    Assert.Contains("ROS2Node candidate = nodes[index];", prune, StringComparison.Ordinal);
                    Assert.Contains("candidate.IsDisposed", prune, StringComparison.Ordinal);
                    Assert.Contains("ros2csNodes.RemoveAt(index);", prune, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void RuntimeSensorsAcquireOnMainThreadAndSerializePublisherTeardown()
        {
            foreach (var distro in RuntimeDistros)
            {
                var sensor = RuntimeSource(distro, "Sensor.cs");
                var executor = TestSources.ExtractMethod(sensor, "internal void ExecutorThreadSensorPublishAction()");

                Assert.Contains("(agentName ?? String.Empty).Replace(\" \", \"_\")", sensor, StringComparison.Ordinal);
                Assert.Contains("private readonly object readingsMutex = new object();", sensor, StringComparison.Ordinal);
                Assert.Contains("UpdateReadingOnMainThread();", sensor, StringComparison.Ordinal);
                Assert.DoesNotContain("HasNewData()", executor, StringComparison.Ordinal);
                Assert.DoesNotContain("AcquireValue()", executor, StringComparison.Ordinal);
            }

            var jazzy = RuntimeSource("jazzy", "Sensor.cs");
            var dispose = TestSources.ExtractMethod(jazzy, "private void DisposeRosParticipants()");
            Assert.True(
                dispose.IndexOf("UnregisterExecutable(ExecutorThreadSensorPublishAction)", StringComparison.Ordinal)
                < dispose.IndexOf("ownershipToRetire = publisherOwnership;", StringComparison.Ordinal));
            Assert.True(
                dispose.IndexOf("ownershipToRetire = publisherOwnership;", StringComparison.Ordinal)
                < dispose.IndexOf("RemovePublisher", StringComparison.Ordinal));
        }

        [Fact]
        public void RuntimeTimeoutsRetainNativeOwnersUntilExecutorStops()
        {
            foreach (var distro in RuntimeDistros)
            {
                var core = RuntimeSource(distro, "ROS2UnityCore.cs");
                AssertTimeoutRetainsOwner(core, "public void Dispose()");

                var component = RuntimeSource(distro, "ROS2UnityComponent.cs");
                Assert.Contains("private bool StopExecutor()", component, StringComparison.Ordinal);
                AssertTimeoutRetainsOwner(component, "private void Shutdown()");

                var shutdown = TestSources.ExtractMethod(
                    component,
                    "private void Shutdown()");
                Assert.Contains(
                    "Interlocked.CompareExchange(ref shutdownInProgress, 1, 0)",
                    shutdown,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "Volatile.Write(ref shutdownInProgress, 0);",
                    shutdown,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "disposed || shutdownRequested",
                    shutdown,
                    StringComparison.Ordinal);

                var stopAll = TestSources.ExtractMethod(
                    component,
                    "public static void StopAllExecutorsForRosShutdown()");
                var stop = stopAll.IndexOf("StopExecutor()", StringComparison.Ordinal);
                var pending = stopAll.IndexOf(
                    "MarkRuntimeShutdownPendingExecutor()",
                    stop,
                    StringComparison.Ordinal);
                var skip = stopAll.IndexOf("continue;", pending, StringComparison.Ordinal);
                var mark = stopAll.IndexOf("MarkRuntimeShutdown()", StringComparison.Ordinal);
                Assert.True(stop >= 0);
                Assert.True(pending > stop);
                Assert.True(skip > pending);
                Assert.True(mark > skip);

                var pendingMethod = TestSources.ExtractMethod(
                    component,
                    "private void MarkRuntimeShutdownPendingExecutor()");
                Assert.Contains(
                    "shutdownRequested = true;",
                    pendingMethod,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "runtimeShutdownRequested = true;",
                    pendingMethod,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "ros2forUnity = null",
                    pendingMethod,
                    StringComparison.Ordinal);
            }
        }

        private static void AssertRos2WarningThrottle(string path)
        {
            var source = TestSources.Text(path);

            Assert.Contains("private int rosUnavailableWarningLogged = 0;", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 1)", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref rosUnavailableWarningLogged)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 0)", source, StringComparison.Ordinal);
        }

        private static readonly string[] RuntimeDistros = { "humble", "jazzy", "lyrical" };

        private static string RuntimeSource(string distro, string file)
            => TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity.runtime." + distro +
                ".win64/Runtime/Ros2ForUnity/Scripts/" + file);

        private static void AssertTimeoutRetainsOwner(string source, string shutdownSignature)
        {
            var shutdown = TestSources.ExtractMethod(source, shutdownSignature);
            var failure = shutdown.IndexOf("if (!executorStopped)", StringComparison.Ordinal);
            Assert.True(failure >= 0);

            var retained = shutdown.IndexOf("native ownership remains active", StringComparison.Ordinal);
            var earlyReturn = shutdown.IndexOf("return;", failure, StringComparison.Ordinal);
            var detach = shutdown.IndexOf("TryDetachRuntimeState", StringComparison.Ordinal);
            var quarantine = TestSources.ExtractMethod(source, "private void QuarantineNodesAfterExecutorTimeout()");

            Assert.True(retained > failure);
            Assert.True(earlyReturn > retained);
            Assert.True(detach < 0 || earlyReturn < detach);
            Assert.DoesNotContain("nodes.Clear();", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("ros2csNodes.Clear();", quarantine, StringComparison.Ordinal);
        }
    }
}
