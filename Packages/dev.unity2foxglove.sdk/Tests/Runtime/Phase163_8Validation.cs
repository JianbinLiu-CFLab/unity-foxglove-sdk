// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-8 review regression checks for replay controller, cursor, and timeline ownership.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_8Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-8: Replay Controller, Cursor, and Timeline Ownership Review ===");
            _passed = 0;

            BoundedEventQueueClearResetsDropStats();
            CoordinateModeGuardScansPastFirstMatchingChannel();
            ReplayControllerDrainBufferIsClearedBeforeReentry();
            CursorPreflightRunsBeforeBearerAuthorization();
            ReplayControllerCallsClockOutsideReplayEngineLock();
            ReplayControllerAvoidsDeadChannelMapAndLockHeldPreflightIo();
            ReplayControllerSkipsUnknownTopicExternalCursorMessages();
            ReplayControllerFireForTestsAcceptsReplaySessionId();
            ReplayAdapterDoesNotPermanentlyOverrideFallbackParseFailures();
            ReplaySnapshotReusesLatestByChannelDictionary();
            PhaseRegistryWiresPhase163_8();

            Console.WriteLine($"Phase 163-8: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void BoundedEventQueueClearResetsDropStats()
        {
            var queue = new BoundedEventQueue<byte[]>(maxFrames: 1, maxBytes: 3, measureBytes: item => item?.Length ?? 0);
            Check(queue.TryEnqueue(new byte[3], out _), "163-8A-1: bounded queue accepts baseline item");
            Check(!queue.TryEnqueue(new byte[2], out var overflow)
                  && overflow.DroppedCount == 1
                  && overflow.DroppedBytes == 2,
                "163-8A-2: bounded queue reports an overflow before clear");

            queue.Clear();
            Check(queue.Count == 0
                  && queue.QueuedBytes == 0
                  && queue.DroppedCount == 0
                  && queue.DroppedBytes == 0,
                "163-8A-3: bounded queue clear resets queued and dropped counters");
        }

        private static void CoordinateModeGuardScansPastFirstMatchingChannel()
        {
            var channels = new[]
            {
                new McapChannel
                {
                    Id = 1,
                    Topic = "/phase163_8/matching",
                    MessageEncoding = "json",
                    Metadata = new Dictionary<string, string> { ["coordinate_mode"] = "unity" }
                },
                new McapChannel
                {
                    Id = 2,
                    Topic = "/phase163_8/mismatch",
                    MessageEncoding = "json",
                    Metadata = new Dictionary<string, string> { ["coordinate_mode"] = "ros" }
                }
            };

            var warning = ReplayCoordinateModeGuard.FindMismatch(channels, "unity", "phase163_8.mcap");
            Check(warning != null && warning.Contains("ros", StringComparison.Ordinal),
                "163-8B: coordinate-mode guard scans later channels after an initial match");
        }

        private static void ReplayControllerDrainBufferIsClearedBeforeReentry()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var finallyBlock = Slice(source, "finally", "private bool TryQueueReplayCallback");
            var clearIndex = finallyBlock.IndexOf("_drainBuffer.Clear();", StringComparison.Ordinal);
            var flagIndex = finallyBlock.IndexOf("_isDrainingReplayCallbacks = false;", StringComparison.Ordinal);

            Check(finallyBlock.Contains("lock (_replayCallbackDrainGate)", StringComparison.Ordinal)
                  && clearIndex >= 0
                  && flagIndex >= 0
                  && clearIndex < flagIndex,
                "163-8C: replay drain buffer is cleared before a new drain can enter");
        }

        private static void CursorPreflightRunsBeforeBearerAuthorization()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var handle = Slice(source, "private void Handle(HttpListenerContext context)", "private bool IsAuthorized");
            var optionsIndex = handle.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal);
            var authIndex = handle.IndexOf("IsAuthorized(context.Request)", StringComparison.Ordinal);

            Check(optionsIndex >= 0
                  && authIndex >= 0
                  && optionsIndex < authIndex,
                "163-8D: cursor endpoint answers CORS preflight before bearer authorization");
        }

        private static void ReplayControllerCallsClockOutsideReplayEngineLock()
        {
            var source = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var enableCore = Slice(source, "private void EnableCore(", "private static string CreateWarnModeSchemaMismatchMessage");
            var clockIndex = enableCore.IndexOf("_clock?.EnableRange(replayStartTimeNs, replayEndTimeNs);", StringComparison.Ordinal);
            var firstLockIndex = enableCore.IndexOf("lock (_replayEngineLock)", StringComparison.Ordinal);
            var secondLockIndex = enableCore.IndexOf("lock (_replayEngineLock)", firstLockIndex + 1, StringComparison.Ordinal);

            Check(clockIndex > firstLockIndex
                  && secondLockIndex > clockIndex
                  && enableCore.Contains("if (!ReferenceEquals(_replayEngine, loadedEngine))", StringComparison.Ordinal),
                "163-8E: replay enable calls external clock outside the replay-engine lock and revalidates the engine");
        }

        private static void ReplayControllerAvoidsDeadChannelMapAndLockHeldPreflightIo()
        {
            var source = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var enableCore = Slice(source, "private void EnableCore(", "private static string CreateWarnModeSchemaMismatchMessage");
            var validateIndex = enableCore.IndexOf("ValidateReplayFileForLoad(filePath);", StringComparison.Ordinal);
            var firstLockIndex = enableCore.IndexOf("lock (_replayEngineLock)", StringComparison.Ordinal);

            Check(!source.Contains("_channelMap", StringComparison.Ordinal),
                "173-023C: replay controller avoids unused channel map allocations");
            Check(validateIndex >= 0 && firstLockIndex >= 0 && validateIndex < firstLockIndex,
                "173-023D: replay file magic preflight runs before the replay engine lock");
        }

        private static void ReplayControllerSkipsUnknownTopicExternalCursorMessages()
        {
            var source = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var applyTick = Slice(source, "public void ApplyTickToScene(ulong timeNs, bool deferCallbacks)", "/// <summary>");

            Check(source.Contains("private bool TryGetReplayTopic(ushort channelId, out string topic)", StringComparison.Ordinal)
                  && applyTick.Contains("if (TryGetReplayTopic(msg.ChannelId, out _))", StringComparison.Ordinal)
                  && applyTick.Contains("ForwardReplayMessageToScene(msg);", StringComparison.Ordinal),
                "173-023E: external-cursor replay forwarding uses the same known-topic guard as normal Tick");
        }

        private static void ReplayControllerFireForTestsAcceptsReplaySessionId()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            Check(source.Contains("internal void FireForTests(string topic, byte[] data, ulong replaySessionId)", StringComparison.Ordinal)
                  && source.Contains("replaySessionId: replaySessionId", StringComparison.Ordinal),
                "163-8F: replay test hook can target an active replay session");
        }

        private static void ReplayAdapterDoesNotPermanentlyOverrideFallbackParseFailures()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");
            var catchBlock = Slice(source, "catch (Exception ex) when (IsRecoverableReplayException(ex))", "private ReplayChannelBehavior ResolveBehavior");
            Check(!catchBlock.Contains("_channelBehaviorOverrides[context.ChannelId] = ReplayChannelBehavior.NonPose", StringComparison.Ordinal),
                "163-8G: recoverable fallback parse failures do not permanently mark a channel NonPose");
        }

        private static void ReplaySnapshotReusesLatestByChannelDictionary()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            var snapshot = Slice(source, "public List<McapMessage> Snapshot", "/// <summary>");
            Check(source.Contains("private readonly Dictionary<ushort, McapMessage> _snapshotLatestByChannel = new();", StringComparison.Ordinal)
                  && snapshot.Contains("var latestByChannel = _snapshotLatestByChannel;", StringComparison.Ordinal)
                  && snapshot.Contains("latestByChannel.Clear();", StringComparison.Ordinal)
                  && !snapshot.Contains("new Dictionary<ushort, McapMessage>()", StringComparison.Ordinal),
                "163-8H: paused-seek snapshot reuses its latest-by-channel dictionary");
        }

        private static void PhaseRegistryWiresPhase163_8()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase163-8\"", StringComparison.Ordinal)
                  && registry.Contains("Phase163_8Validation.Validate", StringComparison.Ordinal),
                "163-8I: PhaseValidationRegistry wires --phase163-8");
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
