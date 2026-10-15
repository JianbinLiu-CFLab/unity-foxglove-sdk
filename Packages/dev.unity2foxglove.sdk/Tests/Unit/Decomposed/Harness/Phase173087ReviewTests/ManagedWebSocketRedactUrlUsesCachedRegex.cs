// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-087 Unity review regression checks.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "173-087")]
    [Trait("Domain", "UnityReview")]
    public sealed partial class Phase173087ReviewTests
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
            var metadataIndex = constructor.IndexOf("GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\")", StringComparison.Ordinal);
            var warningIndex = constructor.IndexOf("WarnIfStandaloneRosDistroOverride", StringComparison.Ordinal);
            Assert.True(metadataIndex >= 0 && warningIndex >= 0 && metadataIndex < warningIndex);
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
                var update = TestSources.ExtractMethod(sensor, "void Update()");
                Assert.Contains(
                    distro == "jazzy" ? "RetryPendingPublisherRemoval()" : "RetryPendingPublisherCleanup()",
                    update,
                    StringComparison.Ordinal);
                Assert.Contains("void OnEnable()", sensor, StringComparison.Ordinal);
                Assert.Contains("publisherCleanupPending", sensor, StringComparison.Ordinal);
                Assert.Contains("PublisherOwnership", sensor, StringComparison.Ordinal);
                Assert.Contains("ActiveCalls", sensor, StringComparison.Ordinal);
                Assert.Contains("Retired = true", sensor, StringComparison.Ordinal);
                Assert.Contains("TryCompletePublisherRemoval", sensor, StringComparison.Ordinal);
                Assert.Contains("publisherCleanupPending", sensor, StringComparison.Ordinal);
                var teardown = distro == "jazzy"
                    ? TestSources.ExtractMethod(sensor, "private void DisposeRosParticipants()")
                    : TestSources.ExtractMethod(sensor, "private void UnregisterExecutable()");
                Assert.True(
                    teardown.IndexOf("UnregisterExecutable(ExecutorThreadSensorPublishAction)", StringComparison.Ordinal) < 0
                    || teardown.IndexOf("UnregisterExecutable(ExecutorThreadSensorPublishAction)", StringComparison.Ordinal)
                       < teardown.IndexOf("TryCompletePublisherRemoval", StringComparison.Ordinal));
            }

            var jazzy = RuntimeSource("jazzy", "Sensor.cs");
            var dispose = TestSources.ExtractMethod(jazzy, "private void DisposeRosParticipants()");
            Assert.True(
                dispose.IndexOf("UnregisterExecutable(ExecutorThreadSensorPublishAction)", StringComparison.Ordinal)
                < dispose.IndexOf("var ownershipToRetire = publisherOwnership;", StringComparison.Ordinal));
            Assert.True(
                dispose.IndexOf("var ownershipToRetire = publisherOwnership;", StringComparison.Ordinal)
                < dispose.IndexOf("TryCompletePublisherRemoval", StringComparison.Ordinal));
            Assert.Contains("rosParticipantsDisposed && publisherOwnership == null", dispose, StringComparison.Ordinal);
            Assert.Contains("!ownershipToRetire.RemovalClaimed", dispose, StringComparison.Ordinal);
            var complete = TestSources.ExtractMethod(jazzy, "private void TryCompletePublisherRemoval(PublisherOwnership ownership)");
            Assert.Contains("if (removed)", complete, StringComparison.Ordinal);
            Assert.Contains("publisherOwnership = null", complete, StringComparison.Ordinal);
            Assert.Contains("ownership.RemovalClaimed = false", complete, StringComparison.Ordinal);
            Assert.Contains("publisherCleanupPending = true", complete, StringComparison.Ordinal);
            Assert.Contains(
                "publisherCleanupPending = retiredPublisherOwnerships.Count > 0;",
                complete,
                StringComparison.Ordinal);
            var remove = TestSources.ExtractMethod(jazzy, "private static bool TryRemovePublisher(");
            Assert.Contains("return nodeToUse.RemovePublisher<T>(publisherToRemove);", remove, StringComparison.Ordinal);
            Assert.DoesNotContain("nodeToUse.RemovePublisher<T>(publisherToRemove);\n            return true;", remove, StringComparison.Ordinal);
            foreach (var distro in new[] { "humble", "lyrical" })
            {
                var source = RuntimeSource(distro, "Sensor.cs");
                var sensorRemove = TestSources.ExtractMethod(source, "private static bool TryRemovePublisher(");
                Assert.Contains("return nodeToUse.RemovePublisher(publisherToRemove);", sensorRemove, StringComparison.Ordinal);
                Assert.DoesNotContain("nodeToUse.RemovePublisher(publisherToRemove);\n            return true;", sensorRemove, StringComparison.Ordinal);
            }
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

                var executorFailure = shutdown.IndexOf(
                    "if (!executorStopped)",
                    StringComparison.Ordinal);
                var disposeFailure = shutdown.IndexOf(
                    "if (!DisposeNodes())",
                    executorFailure,
                    StringComparison.Ordinal);
                var executorRetry = shutdown.IndexOf(
                    "MarkRuntimeShutdownPendingExecutor();",
                    executorFailure,
                    StringComparison.Ordinal);
                var disposeRetry = shutdown.IndexOf(
                    "MarkRuntimeShutdownPendingExecutor();",
                    disposeFailure,
                    StringComparison.Ordinal);
                Assert.True(executorFailure >= 0);
                Assert.True(disposeFailure > executorFailure);
                Assert.True(executorRetry > executorFailure && executorRetry < disposeFailure);
                Assert.True(disposeRetry > disposeFailure);

                var stopAll = TestSources.ExtractMethod(
                    component,
                    "public static bool StopAllExecutorsForRosShutdown()");
                Assert.Contains("StopForRosShutdown()", stopAll, StringComparison.Ordinal);
                Assert.Contains("return allStopped;", stopAll, StringComparison.Ordinal);

                var stopForRosShutdown = TestSources.ExtractMethod(
                    component,
                    "private bool StopForRosShutdown()");
                var stop = stopForRosShutdown.IndexOf("StopExecutor()", StringComparison.Ordinal);
                var pending = stopForRosShutdown.IndexOf(
                    "MarkRuntimeShutdownPendingExecutor()",
                    stop,
                    StringComparison.Ordinal);
                var dispose = stopForRosShutdown.IndexOf("DisposeNodes()", StringComparison.Ordinal);
                var detach = stopForRosShutdown.IndexOf("TryDetachRuntimeState", StringComparison.Ordinal);
                var destroy = stopForRosShutdown.IndexOf("DestroyROS2ForUnity()", StringComparison.Ordinal);
                Assert.True(stop >= 0);
                Assert.True(pending > stop);
                Assert.True(dispose > stop);
                Assert.True(detach > dispose);
                Assert.True(destroy > detach);

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

        [Fact]
        public void RuntimeStartExecutorRestoresStateWhenThreadStartFails()
        {
            foreach (var distro in RuntimeDistros)
            {
                var source = RuntimeSource(distro, "ROS2UnityComponent.cs");
                var start = TestSources.ExtractMethod(source, "private void StartExecutor()");
                Assert.Contains("threadToStart.Start();", start, StringComparison.Ordinal);
                Assert.Contains("catch", start, StringComparison.Ordinal);
                Assert.Contains("executorThread = null;", start, StringComparison.Ordinal);
                Assert.Contains("initialized = previousInitialized;", start, StringComparison.Ordinal);
                Assert.Contains("executorStarted = previousExecutorStarted;", start, StringComparison.Ordinal);
                Assert.Contains("quitting = previousQuitting;", start, StringComparison.Ordinal);
                Assert.Contains("cachedOk = previousCachedOk;", start, StringComparison.Ordinal);
                Assert.DoesNotContain("DestroyROS2ForUnity", start, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RuntimeCoreRegistryParticipatesInSharedShutdownOnOwnerThread()
        {
            foreach (var distro in RuntimeDistros)
            {
                var core = RuntimeSource(distro, "ROS2UnityCore.cs");
                Assert.Contains(
                    "private static readonly HashSet<ROS2UnityCore> instances",
                    core,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "lifecycleSynchronizationContext = SynchronizationContext.Current;",
                    core,
                    StringComparison.Ordinal);
                Assert.Contains("private int shutdownRetryPending;", core, StringComparison.Ordinal);
                Assert.Contains(
                    "internal static bool RetryPendingShutdownsOnCurrentThread()",
                    core,
                    StringComparison.Ordinal);
                Assert.Contains("HasPendingShutdownRetry", core, StringComparison.Ordinal);
                Assert.Contains("ROS2ForUnity failedInstance;", core, StringComparison.Ordinal);
                Assert.Contains("failedInstance = ros2forUnity;", core, StringComparison.Ordinal);
                Assert.Contains("failedInstance?.DestroyROS2ForUnity();", core, StringComparison.Ordinal);
                var failedCleanup = core.IndexOf("ROS2ForUnity failedInstance;", StringComparison.Ordinal);
                Assert.True(
                    core.IndexOf("instances.Remove(this)", failedCleanup, StringComparison.Ordinal)
                    > failedCleanup);
                var registry = TestSources.ExtractMethod(
                    core,
                    "internal static bool StopAllExecutorsForRosShutdown()");
                Assert.Contains("RequestStopForRosShutdown()", registry, StringComparison.Ordinal);
                var request = TestSources.ExtractMethod(
                    core,
                    "private bool RequestStopForRosShutdown()");
                Assert.Contains("context.Post", request, StringComparison.Ordinal);
                Assert.Contains("if (context == null)", request, StringComparison.Ordinal);
                Assert.Contains(
                    "Volatile.Write(ref shutdownRetryPending, 1);",
                    request,
                    StringComparison.Ordinal);
                Assert.Contains("RetryPendingShutdown()", request, StringComparison.Ordinal);
                var dispatchedStop = request.IndexOf("RetryPendingShutdown()", StringComparison.Ordinal);
                var dispatchedRetry = request.IndexOf("ROS2ForUnity.RetryPendingShutdown();", dispatchedStop, StringComparison.Ordinal);
                Assert.True(dispatchedStop >= 0 && dispatchedRetry > dispatchedStop);
                var retry = TestSources.ExtractMethod(
                    core,
                    "internal bool RetryPendingShutdown()");
                Assert.Contains("IsLifecycleOwnerThread()", retry, StringComparison.Ordinal);
                Assert.Contains(
                    "Volatile.Write(ref shutdownRetryPending, completed ? 0 : 1);",
                    retry,
                    StringComparison.Ordinal);
                Assert.Contains("bool completed = StopForRosShutdown();", retry, StringComparison.Ordinal);
                var schedule = TestSources.ExtractMethod(
                    core,
                    "private void ScheduleShutdownRetry()");
                Assert.Contains("context.Post", schedule, StringComparison.Ordinal);
                Assert.Contains("disposeRequested", schedule, StringComparison.Ordinal);
                Assert.Contains(
                    "Volatile.Write(ref shutdownRetryPending, 1);",
                    schedule,
                    StringComparison.Ordinal);
                Assert.Contains("RetryPendingShutdown();", schedule, StringComparison.Ordinal);
                Assert.DoesNotContain("ThreadPool", request + schedule, StringComparison.Ordinal);
                var disposeCore = TestSources.ExtractMethod(core, "public void Dispose()");
                Assert.Contains("ScheduleShutdownRetry();", disposeCore, StringComparison.Ordinal);
                var stop = TestSources.ExtractMethod(
                    core,
                    "private bool StopForRosShutdown()");
                var owner = stop.IndexOf("IsLifecycleOwnerThread()", StringComparison.Ordinal);
                var executor = stop.IndexOf("StopExecutor()", owner, StringComparison.Ordinal);
                var dispose = stop.IndexOf("DisposeNodes()", executor, StringComparison.Ordinal);
                var detach = stop.IndexOf("TryDetachRuntimeState", dispose, StringComparison.Ordinal);
                Assert.True(owner >= 0 && executor > owner && dispose > executor && detach > dispose);
                Assert.Contains("instances.Remove(this)", core, StringComparison.Ordinal);

                var component = RuntimeSource(distro, "ROS2UnityComponent.cs");
                Assert.Contains(
                    "private SynchronizationContext lifecycleSynchronizationContext;",
                    component,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "private int shutdownDispatchScheduled;",
                    component,
                    StringComparison.Ordinal);
                var pending = TestSources.ExtractMethod(
                    component,
                    "private void MarkRuntimeShutdownPendingExecutor()");
                Assert.Contains("ScheduleShutdownRetry();", pending, StringComparison.Ordinal);
                var dispatch = TestSources.ExtractMethod(
                    component,
                    "private void ScheduleShutdownRetry()");
                Assert.Contains("context.Post", dispatch, StringComparison.Ordinal);
                Assert.Contains("if (runtimeShutdownRequested)", dispatch, StringComparison.Ordinal);
                var dispatchedComponentRetry = dispatch.IndexOf(
                    "StopForRosShutdown();",
                    StringComparison.Ordinal);
                var dispatchedGlobalRetry = dispatch.IndexOf(
                    "ROS2ForUnity.RetryPendingShutdown();",
                    dispatchedComponentRetry,
                    StringComparison.Ordinal);
                Assert.True(dispatchedComponentRetry >= 0 && dispatchedGlobalRetry > dispatchedComponentRetry);
                Assert.Contains("ROS2ForUnity.RetryPendingShutdown()", dispatch, StringComparison.Ordinal);
                var fixedUpdate = TestSources.ExtractMethod(component, "void FixedUpdate()");
                Assert.Contains(
                    "ROS2UnityCore.RetryPendingShutdownsOnCurrentThread();",
                    fixedUpdate,
                    StringComparison.Ordinal);
                var fixedComponentRetry = fixedUpdate.IndexOf(
                    "StopForRosShutdown();",
                    StringComparison.Ordinal);
                var fixedGlobalRetry = fixedUpdate.IndexOf(
                    "ROS2ForUnity.RetryPendingShutdown();",
                    fixedComponentRetry,
                    StringComparison.Ordinal);
                Assert.True(fixedComponentRetry >= 0 && fixedGlobalRetry > fixedComponentRetry);
                var componentStop = TestSources.ExtractMethod(
                    component,
                    "private bool StopForRosShutdown()");
                Assert.Contains("if (disposed)", componentStop, StringComparison.Ordinal);
                var lazyConstruct = TestSources.ExtractMethod(component, "private void LazyConstruct()");
                Assert.Contains("Interlocked.CompareExchange(", lazyConstruct, StringComparison.Ordinal);
                Assert.Contains("ref lifecycleOwnerThreadId", lazyConstruct, StringComparison.Ordinal);
                Assert.Contains(
                    "lifecycleSynchronizationContext = SynchronizationContext.Current;",
                    lazyConstruct,
                    StringComparison.Ordinal);
                var ensure = TestSources.ExtractMethod(component, "private void EnsureNotExecutorThread()");
                Assert.DoesNotContain("ownerThread == 0\n", ensure, StringComparison.Ordinal);
                var stopAll = TestSources.ExtractMethod(
                    component,
                    "public static bool StopAllExecutorsForRosShutdown()");
                Assert.Contains(
                    "ROS2UnityCore.StopAllExecutorsForRosShutdown()",
                    stopAll,
                    StringComparison.Ordinal);
            }
        }
    }
}
