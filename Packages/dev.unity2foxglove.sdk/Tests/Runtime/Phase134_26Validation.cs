// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-26 regression coverage for R2FU adapter sample queue bounds.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_26Validation
    {
        private const string SmokePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs";
        private const string ContextPath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityContext.cs";
        private const string Phase128SmokePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 Standard Visualization Acceptance/Phase128Rviz2TfLaserScanSmoke.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-26: R2FU Adapter Samples I ===");
            _passed = 0;

            VerifyDirectModeQueueBound();
            VerifyDirectModeRetryAndCleanup();
            VerifyContextStatusReads();
            VerifyPhase128SetupGuard();
            VerifyCleanupAndDiagnostics();

            Console.WriteLine($"Phase 134-26: {_passed} checks passed.");
        }

        private static void VerifyDirectModeQueueBound()
        {
            var source = ReadRepoText(SmokePath);
            Check(source.Contains("private const int MaxDirectReceived = 32;", StringComparison.Ordinal),
                "134-26A-1: Phase110 direct diagnostic receive queue declares a fixed bound");
            Check(source.Contains("while (_directReceived.Count >= MaxDirectReceived)", StringComparison.Ordinal)
                  && source.Contains("_directReceived.Dequeue();", StringComparison.Ordinal)
                  && source.Contains("_directReceived.Enqueue(message.Data)", StringComparison.Ordinal),
                "134-26A-2: Phase110 direct diagnostic receive queue drops oldest messages before enqueue");
            Check(source.IndexOf("while (_directReceived.Count >= MaxDirectReceived)", StringComparison.Ordinal)
                  < source.IndexOf("_directReceived.Enqueue(message.Data)", StringComparison.Ordinal),
                "134-26A-3: Phase110 direct diagnostic receive queue bounds before enqueueing new data");
        }

        private static void VerifyDirectModeRetryAndCleanup()
        {
            var source = ReadRepoText(SmokePath);
            Check(source.Contains("_directInitializationFailed = false;", StringComparison.Ordinal)
                  && source.IndexOf("private void OnEnable()", StringComparison.Ordinal)
                     < source.IndexOf("_directInitializationFailed = false;", StringComparison.Ordinal),
                "134-26B-1: Phase110 direct mode resets transient initialization failures on enable");
            Check(source.Contains("private bool _ownsDirectRos2Unity;", StringComparison.Ordinal)
                  && source.Contains("_ownsDirectRos2Unity = true;", StringComparison.Ordinal)
                  && source.Contains("Destroy(_directRos2Unity)", StringComparison.Ordinal)
                  && source.Contains("_directRos2Unity = null;", StringComparison.Ordinal),
                "134-26B-2: Phase110 direct mode tracks and destroys only auto-created ROS2UnityComponent instances");
            Check(source.Contains("private void OnDestroy()", StringComparison.Ordinal)
                  && source.Contains("DisposeDirectEndpoints();", StringComparison.Ordinal),
                "134-26B-3: Phase110 direct mode cleanup also runs from OnDestroy");
            Check(source.Contains("TryEnsureDirectEndpoints", StringComparison.Ordinal)
                  && source.Contains("Direct ROS2 For Unity endpoint setup failed", StringComparison.Ordinal),
                "134-26B-4: Phase110 direct endpoint setup failures are blocked instead of repeating every frame");
            Check(source.Contains("NormalizeName(_nodeName, NodeName)", StringComparison.Ordinal)
                  && source.Contains("private static string NormalizeName", StringComparison.Ordinal),
                "134-26B-5: Phase110 direct and facade node names no longer use the topic-normalization helper");
        }

        private static void VerifyContextStatusReads()
        {
            var context = ReadRepoText(ContextPath);
            var isAvailableIndex = context.IndexOf("public bool IsAvailable", StringComparison.Ordinal);
            var tryEnsureIndex = context.IndexOf("public bool TryEnsureReady()", StringComparison.Ordinal);
            var isAvailableBlock = context.Substring(isAvailableIndex, tryEnsureIndex - isAvailableIndex);
            Check(isAvailableBlock.Contains("_ros2Unity != null", StringComparison.Ordinal)
                  && isAvailableBlock.Contains("_ros2Unity.Ok()", StringComparison.Ordinal)
                  && !isAvailableBlock.Contains("TryEnsureReady()", StringComparison.Ordinal)
                  && !isAvailableBlock.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal),
                "134-26C-1: Phase110 context IsAvailable observes cached runtime state without initializing");
            Check(context.Contains("if (_initializationFailed)", StringComparison.Ordinal)
                  && context.Contains("return Unity2FoxgloveRos2Status.Error;", StringComparison.Ordinal),
                "134-26C-2: Phase110 context Status reports Error after initialization failure");
            Check(context.Contains("_nodes.RemoveAll(node => node.IsDisposed)", StringComparison.Ordinal),
                "134-26C-3: Phase110 context prunes disposed nodes before live-node reuse");
            Check(context.Contains("var normalizedTopic = NormalizeTopic(topic);", StringComparison.Ordinal)
                  && context.Contains("new StringPublisher(_ros2Node, normalizedTopic, publisher)", StringComparison.Ordinal),
                "134-26C-4: Phase110 context caches normalized topics when creating string publishers");
            Check(context.Contains("_warnedDroppedCallbacks", StringComparison.Ordinal)
                  && context.Contains("dropped \" + dropped + \" queued string callback", StringComparison.Ordinal),
                "134-26C-5: Phase110 queued callback drops are surfaced with a one-shot warning");
        }

        private static void VerifyPhase128SetupGuard()
        {
            var source = ReadRepoText(Phase128SmokePath);
            Check(source.Contains("TryEnsurePostReadySetup", StringComparison.Ordinal)
                  && source.Contains("EnsureExecutorStarted();", StringComparison.Ordinal)
                  && source.Contains("EnsureEndpoints();", StringComparison.Ordinal)
                  && source.Contains("ROS2 For Unity endpoint setup failed", StringComparison.Ordinal)
                  && source.Contains("_initializationBlocked = true;", StringComparison.Ordinal),
                "134-26D-1: Phase128 wraps executor and endpoint setup in one-shot failure blocking");
            Check(source.Contains("FixedUpdate may still start it", StringComparison.Ordinal)
                  && source.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal),
                "134-26D-2: Phase128 documents the optional private StartExecutor reflection dependency");
            Check(source.Contains("dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal)
                  && source.Contains("/runtime/ros2forunity", StringComparison.Ordinal)
                  && !source.Contains("runtime.jazzy.win64/runtime/ros2forunity", StringComparison.Ordinal),
                "134-26D-3: Phase128 package-runtime diagnostic classification is distro/platform agnostic");
        }

        private static void VerifyCleanupAndDiagnostics()
        {
            var smoke = ReadRepoText(SmokePath);
            var phase128 = ReadRepoText(Phase128SmokePath);
            Check(smoke.Contains("Acceptance samples keep Unity active", StringComparison.Ordinal)
                  && phase128.Contains("Acceptance samples keep Unity active", StringComparison.Ordinal),
                "134-26E-1: sample-wide runInBackground changes are documented at the call sites");
            Check(smoke.Contains("_droppedDirectReceivedCount", StringComparison.Ordinal)
                  && smoke.Contains("dropped \" + dropped + \" queued direct string message", StringComparison.Ordinal),
                "134-26E-2: Phase110 direct-mode callback drops are visible in Inspector/log evidence");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
