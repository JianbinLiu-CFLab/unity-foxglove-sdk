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

        [Fact]
        public void RuntimeNodeDisposalCommitsOnlyAfterNativeRemoval()
        {
            foreach (var distro in RuntimeDistros)
            {
                var node = RuntimeSource(distro, "ROS2Node.cs");
                Assert.Contains("public bool TryDispose()", node, StringComparison.Ordinal);
                Assert.Contains("private bool disposing;", node, StringComparison.Ordinal);
                Assert.Contains("disposing = true;", node, StringComparison.Ordinal);
                var remove = node.IndexOf("Ros2cs.RemoveNode", StringComparison.Ordinal);
                var commit = node.IndexOf("disposed = true;", remove, StringComparison.Ordinal);
                var clear = node.IndexOf("node = null;", remove, StringComparison.Ordinal);
                Assert.True(remove >= 0 && commit > remove && clear > remove);
                Assert.Contains("return false;", node.Substring(remove), StringComparison.Ordinal);
                var dispose = TestSources.ExtractMethod(node, "public void Dispose()");
                Assert.Contains("Environment.CurrentManagedThreadId != ownerThreadId", dispose, StringComparison.Ordinal);
                Assert.Contains("throw new InvalidOperationException", dispose, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RuntimeShutdownRetryContractsRemainExplicit()
        {
            foreach (var distro in RuntimeDistros)
            {
                var source = RuntimeSource(distro, "ROS2ForUnity.cs");
                Assert.Contains("shutdown remains pending", source, StringComparison.Ordinal);
                Assert.Contains("ScheduleShutdownRetry();", source, StringComparison.Ordinal);
                Assert.Contains("private static bool nativeShutdownCompleted", source, StringComparison.Ordinal);
                var finish = TestSources.ExtractMethod(source, "private static void FinishShutdownShared()");
                Assert.Contains("bool nativeShutdownSucceeded = nativeShutdownCompleted;", finish, StringComparison.Ordinal);
                Assert.Contains("nativeShutdownCompleted = true;", finish, StringComparison.Ordinal);
                Assert.Contains("if (!isInitialized)", finish, StringComparison.Ordinal);
                Assert.Contains("retryNativeShutdown = true;", finish, StringComparison.Ordinal);
                Assert.DoesNotContain("finally", finish, StringComparison.Ordinal);
                var retry = TestSources.ExtractMethod(source, "internal static void RetryPendingShutdown()");
                Assert.Contains("restoreOnly", retry, StringComparison.Ordinal);
                Assert.Contains("FinishShutdownShared()", retry, StringComparison.Ordinal);
            }

            foreach (var distro in RuntimeDistros)
            {
                var constructor = TestSources.ExtractMethod(
                    RuntimeSource(distro, "ROS2ForUnity.cs"),
                    "internal ROS2ForUnity()");
                if (String.Equals(distro, "jazzy", StringComparison.Ordinal))
                {
                    Assert.Contains("Ros2ForUnityProcessEnvironmentLease.Abort", constructor, StringComparison.Ordinal);
                    var destroy = constructor.LastIndexOf("DestroyROS2ForUnity();", StringComparison.Ordinal);
                    Assert.True(destroy >= 0);
                    Assert.True(
                        constructor.IndexOf(
                            "Ros2ForUnityProcessEnvironmentLease.Abort",
                            destroy,
                            StringComparison.Ordinal) < 0,
                        "The constructor must not abort the environment lease after a deferred native shutdown.");
                    Assert.Contains("bool shutdownPending;", constructor, StringComparison.Ordinal);
                    Assert.Contains(
                        "shutdownPending = isInitialized || shutdownInProgress;",
                        constructor,
                        StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("nativeShutdownSucceeded", constructor, StringComparison.Ordinal);
                    Assert.Contains("nativeShutdownSucceeded = false;", constructor, StringComparison.Ordinal);
                    var pendingCondition = constructor.IndexOf(
                        "if (nativeInitialized && !nativeShutdownSucceeded)",
                        StringComparison.Ordinal);
                    var abort = constructor.IndexOf(
                        "Ros2ForUnityProcessEnvironmentLease.Abort",
                        StringComparison.Ordinal);
                    Assert.True(pendingCondition >= 0 && abort > pendingCondition);
                    Assert.Contains("CompleteShutdownShared();", constructor, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void EditorPathRestoresAddonBeforeRuntimeEnvironmentLease()
        {
            var guard = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            var addonRestore = guard.IndexOf("RestoreEditorProcessPath", StringComparison.Ordinal);
            var runtimeRestore = guard.IndexOf("Ros2ForUnityRuntimeSelection.RestoreProcessEnvironment", StringComparison.Ordinal);
            Assert.True(addonRestore >= 0 && runtimeRestore > addonRestore);
            Assert.Contains("if (!TryRestoreEditorProcessPath())", guard, StringComparison.Ordinal);
            Assert.Contains("private static bool RestoreEditorProcessEnvironment()", guard, StringComparison.Ordinal);
            Assert.Contains("var restored = Ros2ForUnityRuntimeSelection.RestoreProcessEnvironment();", guard, StringComparison.Ordinal);
            Assert.Contains("SessionState.SetBool(EnvironmentRestorePendingKey, !restored);", guard, StringComparison.Ordinal);
            Assert.Contains(
                "if (!RestoreEditorProcessEnvironment())\n                ScheduleEnvironmentRestoreRetry();",
                guard,
                StringComparison.Ordinal);
            Assert.Contains("return restored;", guard, StringComparison.Ordinal);
            Assert.Contains("return result is bool restored ? restored : true;", guard, StringComparison.Ordinal);
            var runtimeSelection = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            Assert.Contains("Apply(pair.Key, current);", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("restorePending = true;", runtimeSelection, StringComparison.Ordinal);
            Assert.Contains("if (!IsNativeRuntimeShutdownReady())", guard, StringComparison.Ordinal);
            Assert.Contains("method.ReturnType == typeof(bool)", guard, StringComparison.Ordinal);
            Assert.Contains("IsShutdownCompleteForEditor", guard, StringComparison.Ordinal);

            var bootstrap = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomTypesupportNativePluginBootstrap.cs");
            Assert.Contains("_wputenv_s(\"PATH\", value ?? string.Empty)", bootstrap, StringComparison.Ordinal);
            Assert.Contains("private static bool processPathRestorePending;", bootstrap, StringComparison.Ordinal);
            Assert.Contains("SetProcessPathNative(pendingRestorePath);", bootstrap, StringComparison.Ordinal);
            Assert.Contains("SetProcessPathNative(currentPath);", bootstrap, StringComparison.Ordinal);
            Assert.Contains("var remainingOwned = new HashSet<string>", bootstrap, StringComparison.Ordinal);
            Assert.Contains("if (remainingOwned.Remove(entry.Trim()))", bootstrap, StringComparison.Ordinal);
            Assert.Contains("restoredPath = kept.Count == 0 ? string.Empty", bootstrap, StringComparison.Ordinal);
            Assert.Contains("if (TryRemoveOwnedPathEntries(currentPath, out targetPath))", bootstrap, StringComparison.Ordinal);

            foreach (var distro in RuntimeDistros)
            {
                var runtime = RuntimeSource(distro, "ROS2ForUnity.cs");
                var removePath = TestSources.ExtractMethod(
                    runtime,
                    "private static bool TryRemovePathEntry(");
                Assert.Contains("var kept = new List<string>(parts.Length);", removePath, StringComparison.Ordinal);
                Assert.Contains("kept.Add(part);", removePath, StringComparison.Ordinal);
                Assert.Contains(
                    "restoredPath = kept.Count == 0 ? String.Empty",
                    removePath,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("previousParts", removePath, StringComparison.Ordinal);
                Assert.DoesNotContain("previousInserted", removePath, StringComparison.Ordinal);
                Assert.DoesNotContain("kept.AddRange", removePath, StringComparison.Ordinal);
                var normalizedRemovePath = removePath.Replace("\r\n", "\n", StringComparison.Ordinal);
                Assert.Contains(
                    "if (!removed\n                && String.Equals(part.Trim(), entry, StringComparison.OrdinalIgnoreCase))",
                    normalizedRemovePath,
                    StringComparison.Ordinal);

                Assert.Contains("internal static bool IsShutdownCompleteForEditor()", runtime, StringComparison.Ordinal);
                Assert.Contains("return !isInitialized && !shutdownInProgress;", runtime, StringComparison.Ordinal);

                var externalPath = new[] { "U2", "A" };
                var retained = externalPath.Where(
                    part => !String.Equals(part, "A", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("U2", String.Join(";", retained));
            }

            var selection = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            Assert.Contains("private static bool restorePending;", selection, StringComparison.Ordinal);
            Assert.Contains("editor environment lease is still pending cleanup", selection, StringComparison.Ordinal);
            Assert.Contains("internal bool HasApplied;", selection, StringComparison.Ordinal);
            Assert.Contains("var hadApplied = entry.HasApplied;", selection, StringComparison.Ordinal);
            Assert.Contains("entry.HasApplied = hadApplied;", selection, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeOwnersRejectExecutorThreadDisposalBeforeMutation()
        {
            foreach (var distro in RuntimeDistros)
            {
                var node = RuntimeSource(distro, "ROS2Node.cs");
                Assert.Contains("private readonly int ownerThreadId;", node, StringComparison.Ordinal);
                Assert.Contains(
                    "if (Environment.CurrentManagedThreadId != ownerThreadId)",
                    node,
                    StringComparison.Ordinal);

                var core = RuntimeSource(distro, "ROS2UnityCore.cs");
                var dispose = TestSources.ExtractMethod(core, "public void Dispose()");
                Assert.Contains("EnsureNotExecutorThread();", dispose, StringComparison.Ordinal);
                Assert.True(
                    dispose.IndexOf("EnsureNotExecutorThread();", StringComparison.Ordinal)
                    < dispose.IndexOf("disposeRequested = true;", StringComparison.Ordinal));
                Assert.Contains("private void EnsureNotExecutorThread()", core, StringComparison.Ordinal);
                Assert.Contains("private readonly int lifecycleOwnerThreadId;", core, StringComparison.Ordinal);
                var coreRemove = TestSources.ExtractMethod(
                    core,
                    "public bool TryRemoveNode(ROS2Node node, bool dispose = true)");
                Assert.Contains("if (dispose)", coreRemove, StringComparison.Ordinal);
                Assert.Contains("EnsureNotExecutorThread();", coreRemove, StringComparison.Ordinal);
                Assert.True(
                    coreRemove.IndexOf("EnsureNotExecutorThread();", StringComparison.Ordinal)
                    < coreRemove.IndexOf("lock (mutex)", StringComparison.Ordinal));

                var component = RuntimeSource(distro, "ROS2UnityComponent.cs");
                var shutdown = TestSources.ExtractMethod(component, "private void Shutdown()");
                Assert.Contains("EnsureNotExecutorThread();", shutdown, StringComparison.Ordinal);
                Assert.True(
                    shutdown.IndexOf("EnsureNotExecutorThread();", StringComparison.Ordinal)
                    < shutdown.IndexOf("Interlocked.CompareExchange", StringComparison.Ordinal));
                Assert.Contains("private void EnsureNotExecutorThread()", component, StringComparison.Ordinal);
                Assert.Contains("private int lifecycleOwnerThreadId;", component, StringComparison.Ordinal);
                Assert.Contains("Interlocked.CompareExchange(", component, StringComparison.Ordinal);
                Assert.Contains("int observedOwnerThreadId", component, StringComparison.Ordinal);
                var awake = TestSources.ExtractMethod(component, "void Awake()");
                Assert.Contains(
                    "Interlocked.CompareExchange(",
                    awake,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "ref lifecycleOwnerThreadId",
                    awake,
                    StringComparison.Ordinal);
                var componentRemove = TestSources.ExtractMethod(
                    component,
                    "public bool TryRemoveNode(ROS2Node node, bool dispose = true)");
                Assert.Contains("if (dispose)", componentRemove, StringComparison.Ordinal);
                Assert.Contains("EnsureNotExecutorThread();", componentRemove, StringComparison.Ordinal);
                Assert.True(
                    componentRemove.IndexOf("EnsureNotExecutorThread();", StringComparison.Ordinal)
                    < componentRemove.IndexOf("lock (mutex)", StringComparison.Ordinal));
                Assert.True(
                    TestSources.Count(componentRemove, "return node.IsDisposed;") >= 2,
                    "TryRemoveNode must treat an already disposed, detached node as idempotently removed.");
            }
        }

        [Fact]
        public void RuntimeBuildersRetainNodeLifecycleOverlay()
        {
            foreach (var distro in RuntimeDistros)
            {
                var builder = TestSources.Text(
                    "Scripts/ros2forunity/windows/" + distro + "/build_r2fu_runtime_package.py");
                Assert.Contains(
                    "Runtime/Ros2ForUnity/Scripts/ROS2Node.cs",
                    builder,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "Runtime/Ros2ForUnity/Scripts/Sensor.cs",
                    builder,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RuntimeAndPublisherDocsDeclareProcessAndMiddlewareContracts()
        {
            foreach (var distro in RuntimeDistros)
            {
                var readme = TestSources.Text(
                    "Packages/dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64/README.md");
                Assert.Contains("process-wide", readme, StringComparison.OrdinalIgnoreCase);
                var normalizedReadme = readme.Replace("\r", " ").Replace("\n", " ");
                Assert.Contains("restart Unity", normalizedReadme, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("RMW_IMPLEMENTATION", readme, StringComparison.Ordinal);
                Assert.Contains("ROS_DISTRO", readme, StringComparison.Ordinal);
            }

            var publisher = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Publisher.cs");
            Assert.Contains("middleware accepted", publisher, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not mean", publisher, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("subscriber", publisher, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NativeBridgeCleanupRetainsFailedHandlesForRetry()
        {
            var camera = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            Assert.Contains("existing.TryDispose()", camera, StringComparison.Ordinal);
            Assert.Contains("bindings[key].TryDispose()", camera, StringComparison.Ordinal);
            Assert.Contains("RemoveCompleted", camera, StringComparison.Ordinal);
            var cameraBase = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraBindingBase.cs");
            var cameraTryDispose = TestSources.ExtractMethod(cameraBase, "internal bool TryDispose()");
            Assert.Contains("catch (Exception ex)", cameraTryDispose, StringComparison.Ordinal);

            foreach (var child in new[]
                     {
                         "Ros2ForUnityCameraInfoBinding.cs",
                         "Ros2ForUnityCameraRawImageBinding.cs",
                         "Ros2ForUnityCameraCompressedImageBinding.cs"
                     })
            {
                var source = TestSources.Text(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + child);
                Assert.Contains("CleanupComplete", source, StringComparison.Ordinal);
                Assert.Contains("CleanupNode();", source, StringComparison.Ordinal);
                AssertCleanupClearsPublisherAfterNativeRemoval(source);
            }

            foreach (var bridge in new[]
                     {
                         "Ros2ForUnityTransformNativeBridge.cs",
                         "Ros2ForUnityImuNativeBridge.cs"
                     })
            {
                var source = TestSources.Text(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + bridge);
                Assert.Contains("TryDispose()", source, StringComparison.Ordinal);
                Assert.Contains("TryRemoveNode", source, StringComparison.Ordinal);
                AssertCleanupClearsPublisherAfterNativeRemoval(source);
            }

            var pointCloud = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            Assert.Contains("TryDispose()", pointCloud, StringComparison.Ordinal);
            Assert.Contains("if (!publishersRemoved)\n                    return;", pointCloud.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("if (!_node.RemovePublisher", pointCloud, StringComparison.Ordinal);
            Assert.Contains("TryRemoveNode", pointCloud, StringComparison.Ordinal);

            foreach (var bridge in new[]
                     {
                         camera,
                         TestSources.Text(
                             "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs"),
                         TestSources.Text(
                             "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs"),
                         pointCloud
                     })
            {
                var beginShutdown = TestSources.ExtractMethod(bridge, "private void BeginShutdown()");
                Assert.DoesNotContain("if (_isStopping)", beginShutdown, StringComparison.Ordinal);
                Assert.Contains("_isStopping = true;", beginShutdown, StringComparison.Ordinal);
                Assert.Contains("ClearBindings();", beginShutdown, StringComparison.Ordinal);
                var onDestroy = TestSources.ExtractMethod(bridge, "private void OnDestroy()");
                Assert.Contains("BeginShutdown();", onDestroy, StringComparison.Ordinal);
            }

            var subscriptionHub = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs");
            var stop = TestSources.ExtractMethod(subscriptionHub, "private void StopBindingsAndNode()");
            var success = stop.IndexOf("if (cleanupComplete && hostReleased)", StringComparison.Ordinal);
            var clearRuntime = stop.IndexOf("_ros2Unity = null", success, StringComparison.Ordinal);
            Assert.True(success >= 0 && clearRuntime > success);
        }

        [Fact]
        public void FoxRunNodeReleaseRetainsOwnershipWhenDriverRejectsRemoval()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/Ros2ForUnityFoxRunInboundBackend.cs");
            Assert.Contains("bool ReleaseNode();", source, StringComparison.Ordinal);
            Assert.Contains("_nodeReleaseInProgress", source, StringComparison.Ordinal);
            Assert.Contains("released = _driver.ReleaseNode()", source, StringComparison.Ordinal);
            Assert.Contains("if (released)", source, StringComparison.Ordinal);
            Assert.Contains("CompareExchange(ref _node, null, node)", source, StringComparison.Ordinal);
            Assert.Contains("TryRemoveNode", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRestoresPreviouslyUnsetVariable()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_UNSET_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRestoresAnExistingValue()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_EXISTING_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal("original-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeasePreservesCallerMutation()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_CALLER_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Environment.SetEnvironmentVariable(name, "caller-value");
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRemovesOnlyItsPathEntry()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_PATH_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var separator = Path.PathSeparator;
                var original = string.Join(separator, new[] { "old-a", "old-b" });
                var runtimeEntry = "runtime-entry";
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, original);
                    InvokeLease(lease, "Begin");
                    InvokeLease(
                        lease,
                        "SetPath",
                        name,
                        original + separator + runtimeEntry,
                        runtimeEntry,
                        separator,
                        false);
                    Environment.SetEnvironmentVariable(
                        name,
                        original + separator + runtimeEntry + separator + "caller-entry");

                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal(
                        original + separator + "caller-entry",
                        Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRejectsASecondBeginUntilCleanupCompletes()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_NESTED_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    var secondBegin = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Begin"));
                    Assert.IsType<InvalidOperationException>(secondBegin.InnerException);
                    Assert.True((bool)InvokeLease(lease, "Restore", false));

                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "second-value", false);
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseBlocksRestartWhileCleanupIsPending()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_PENDING_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    var failedSet = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Set", name, "value", true));
                    Assert.IsType<PlatformNotSupportedException>(failedSet.InnerException);

                    var restart = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Begin"));
                    Assert.IsType<InvalidOperationException>(restart.InnerException);
                    Assert.False((bool)InvokeLease(lease, "Restore", true));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseExecutesWindowsCrtBranch()
        {
            if (!OperatingSystem.IsWindows())
                return;

            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_WINDOWS_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro, windowsSymbols: true);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", true);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", true));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, true);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRollsBackFailedNativeApplyAfterCallerMutation()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_ROLLBACK_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(
                    distro,
                    windowsSymbols: true,
                    replaceWindowsNative: true);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", true);
                    Environment.SetEnvironmentVariable(name, "caller-value");

                    var failedSet = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Set", name, NativeFailureValue, true));
                    Assert.IsType<InvalidOperationException>(failedSet.InnerException);
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", true));

                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "second-value", true);
                    Assert.True((bool)InvokeLease(lease, "Restore", true));
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, true);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        private const string NativeFailureValue = "__U2F_NATIVE_FAILURE__";

        private static Type RuntimeLeaseType(
            string distro,
            bool windowsSymbols = false,
            bool replaceWindowsNative = false)
        {
            return CompileRuntimeLease(distro, windowsSymbols, replaceWindowsNative).GetType(
                "ROS2.Ros2ForUnityProcessEnvironmentLease",
                throwOnError: true);
        }

        private static object InvokeLease(Type lease, string method, params object[] arguments)
        {
            return lease.GetMethod(
                    method,
                    BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, arguments);
        }

        private static void TryRestoreLease(Type lease, bool windows)
        {
            try
            {
                InvokeLease(lease, "Restore", windows);
            }
            catch (TargetInvocationException)
            {
            }
        }

        private static Assembly CompileRuntimeLease(
            string distro,
            bool windowsSymbols,
            bool replaceWindowsNative)
        {
            var symbols = windowsSymbols
                ? new[] { "UNITY_EDITOR_WIN" }
                : Array.Empty<string>();
            var parseOptions = new CSharpParseOptions(preprocessorSymbols: symbols);
            var source = RuntimeSource(distro, "ROS2ForUnity.cs");
            var root = CSharpSyntaxTree.ParseText(source, parseOptions).GetCompilationUnitRoot();
            var declaration = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(node => node.Identifier.Text == "Ros2ForUnityProcessEnvironmentLease")
                .ToFullString();
            if (replaceWindowsNative)
            {
                const string nativeDeclaration =
                    "    [DllImport(\"ucrtbase.dll\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]\n"
                    + "    private static extern int _wputenv_s(string name, string value);";
                var nativeStub =
                    "    private static int _wputenv_s(string name, string value)\n"
                    + "    {\n"
                    + "        return String.Equals(value, \"" + NativeFailureValue
                    + "\", StringComparison.Ordinal) ? 22 : 0;\n"
                    + "    }";
                var replaced = declaration.Replace(nativeDeclaration, nativeStub, StringComparison.Ordinal);
                Assert.NotEqual(declaration, replaced);
                declaration = replaced;
            }
            var probe = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityEngine
{
    internal static class Debug
    {
        internal static void LogException(Exception exception) { }
    }
}

namespace ROS2
{
" + declaration + @"
}
";

            var compilation = CSharpCompilation.Create(
                "Ros2ForUnityProcessEnvironmentLease_" + distro + "_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(probe, parseOptions) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static MetadataReference[] TrustedPlatformReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            Assert.False(string.IsNullOrEmpty(trustedAssemblies));
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static void AssertRos2WarningThrottle(string path)
        {
            var source = TestSources.Text(path);

            Assert.Contains("private int rosUnavailableWarningLogged = 0;", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 1)", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref rosUnavailableWarningLogged)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 0)", source, StringComparison.Ordinal);
        }

        private static void AssertCleanupClearsPublisherAfterNativeRemoval(string source)
        {
            var remove = source.IndexOf("RemovePublisher", StringComparison.Ordinal);
            var clear = source.IndexOf("_publisher = null", remove, StringComparison.Ordinal);
            var guard = source.LastIndexOf("if (!", remove, StringComparison.Ordinal);
            var retry = source.IndexOf("return;", remove, StringComparison.Ordinal);
            Assert.True(remove >= 0 && guard >= 0 && guard < remove && retry > remove && retry < clear);
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
