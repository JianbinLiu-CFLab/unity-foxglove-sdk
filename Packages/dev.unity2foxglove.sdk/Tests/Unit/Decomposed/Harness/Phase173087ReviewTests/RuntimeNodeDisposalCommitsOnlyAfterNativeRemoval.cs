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



public sealed partial class Phase173087ReviewTests
    {
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
    }
}
