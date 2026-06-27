// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-6 review regression checks for protocol frames, time, and runtime utilities.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_6Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-6: Protocol Frames, Time, and Runtime Utilities Review ===");
            _passed = 0;

            BackgroundEncodePipelineSurvivesEncodeExceptions();
            PlaybackClockPlaySpeedZeroPreservesSpeed();
            DataTimestampNormalizesOverflowIndependentOfJsonOrder();
            PlaybackRequestDecoderRejectsOversizedUintBeforeCast();
            FixedRateSchedulerUsesExactRateChangeDetection();
            TimeUtilNeverFallsBackToZeroAfterInitialization();
            RuntimeReplaySuppressionIsDiagnosed();
            OptionalProtobufSchemaShapeFailuresAreDiagnosed();
            TickCoordinatorSceneSnapshotHasNoUnusedWallClockParameter();
            Ros2ClockDisposeGuardIsConsistentAcrossRuntimePackages();
            CameraPublishOrderDocumentsEqualTimestampDrop();
            PhaseRegistryWiresPhase163_6();

            Console.WriteLine($"Phase 163-6: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void BackgroundEncodePipelineSurvivesEncodeExceptions()
        {
            using var encodeFailed = new ManualResetEventSlim(false);
            using var encodeSucceeded = new ManualResetEventSlim(false);
            var errorCallbackCount = 0;
            var pipeline = new BackgroundEncodePipeline<Phase163_6Request, int>(
                "phase163-6-encode-errors",
                completedCapacity: 2,
                stopWaitMs: 100,
                encode: request =>
                {
                    if (request.Throw)
                        throw new InvalidOperationException("phase163-6 expected encode failure");

                    encodeSucceeded.Set();
                    return request.Value;
                },
                onEncodeError: _ =>
                {
                    Interlocked.Increment(ref errorCallbackCount);
                    encodeFailed.Set();
                });

            Check(pipeline.Enqueue(new Phase163_6Request { Throw = true }, out _, out _)
                  && encodeFailed.Wait(1000),
                "163-6A-1: background encode exceptions are reported through the diagnostic callback");

            var results = new List<int>();
            SpinWait.SpinUntil(() =>
            {
                pipeline.Drain(results, out _, out var errors);
                return errors == 1;
            }, 1000);
            pipeline.Drain(results, out _, out var encodeErrors);
            Check(encodeErrors == 0,
                "163-6A-2: draining resets the accumulated encode-error count");

            Check(pipeline.Enqueue(new Phase163_6Request { Value = 42 }, out _, out _)
                  && encodeSucceeded.Wait(1000),
                "163-6A-3: worker can restart after a failed encode request");
            SpinWait.SpinUntil(() =>
            {
                pipeline.Drain(results, out _, out _);
                return results.Count == 1;
            }, 1000);
            Check(results.Count == 1 && results[0] == 42 && Volatile.Read(ref errorCallbackCount) == 1,
                "163-6A-4: successful work after an encode failure still drains normally");
            pipeline.Stop(clearCompleted: true, out _);
        }

        private static void PlaybackClockPlaySpeedZeroPreservesSpeed()
        {
            var clock = new PlaybackClock();
            clock.EnableRange(0, 10_000_000_000UL);
            clock.Apply(0, 2f, false, 0);
            clock.Apply(0, 0f, false, 0);
            var state = clock.ToState(false, "play-zero");

            Check(state.Status == 0 && Math.Abs(state.Speed - 2f) < 0.0001f,
                "163-6B-1: play speed 0 preserves the previous speed");
            Check(!PlaybackClock.ShouldWarnInvalidSpeed(0, 0f)
                  && !PlaybackClock.ShouldWarnInvalidSpeed(1, 0f)
                  && PlaybackClock.ShouldWarnInvalidSpeed(0, -1f),
                "163-6B-2: playback speed warnings allow client speed-zero play/pause commands only");
        }

        private static void DataTimestampNormalizesOverflowIndependentOfJsonOrder()
        {
            var normalOrder = JsonConvert.DeserializeObject<DataTimestamp>("{\"sec\":5,\"nsec\":1500000000}");
            var reverseOrder = JsonConvert.DeserializeObject<DataTimestamp>("{\"nsec\":1500000000,\"sec\":5}");
            var initializer = new DataTimestamp { Nsec = 1_500_000_000U, Sec = 5UL };

            Check(normalOrder.Sec == 6UL && normalOrder.Nsec == 500_000_000U,
                "163-6C-1: DataTimestamp still normalizes nsec overflow in normal JSON order");
            Check(reverseOrder.Sec == 6UL && reverseOrder.Nsec == 500_000_000U,
                "163-6C-2: DataTimestamp normalizes nsec overflow independent of JSON field order");
            Check(initializer.Sec == 6UL && initializer.Nsec == 500_000_000U,
                "163-6C-3: DataTimestamp object initializers are order independent");
        }

        private static void PlaybackRequestDecoderRejectsOversizedUintBeforeCast()
        {
            var frame = new byte[19];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            BinaryEncoding.WriteU32LE(frame, 15, uint.MaxValue);

            Check(!BinaryEncoding.TryDecodePlaybackControlRequest(frame, out _, out _, out _, out _, out var requestId)
                  && requestId == null,
                "163-6D-1: playback request decoder rejects oversized uint request ids before casting");

            var binaryEncoding = Read("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            Check(!binaryEncoding.Contains("idLen > int.MaxValue", StringComparison.Ordinal)
                  && binaryEncoding.Contains("if (idLen > MaxPlaybackRequestIdBytes) return false;", StringComparison.Ordinal),
                "163-6D-2: playback request id cap is checked before the uint-to-int cast");
        }

        private static void FixedRateSchedulerUsesExactRateChangeDetection()
        {
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/FixedRatePublishScheduler.cs");
            Check(!scheduler.Contains("float.Epsilon", StringComparison.Ordinal)
                  && scheduler.Contains("state.LastRateHz != rateHz", StringComparison.Ordinal),
                "163-6E: fixed-rate scheduler uses exact configured-rate change detection");
        }

        private static void TimeUtilNeverFallsBackToZeroAfterInitialization()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MessageDefinitions/FoxgloveTimeUtil.cs");
            Check(source.Contains("private static long LastUnixNs;", StringComparison.Ordinal)
                  && source.Contains("Interlocked.CompareExchange(ref LastUnixNs", StringComparison.Ordinal)
                  && !source.Contains("return result > 0 ? (ulong)result : 0UL;", StringComparison.Ordinal),
                "163-6F: FoxgloveTimeUtil clamps to the last process timestamp instead of zero");
            Check(FoxgloveTimeUtil.NowUnixTimeNs() > 0UL,
                "163-6F-2: FoxgloveTimeUtil returns a positive initialized timestamp");
        }

        private static void RuntimeReplaySuppressionIsDiagnosed()
        {
            var runtime = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            Check(runtime.Contains("private readonly HashSet<string> _replaySuppressionWarnings", StringComparison.Ordinal)
                  && runtime.Contains("WarnReplaySuppressed(nameof(RegisterChannel)", StringComparison.Ordinal)
                  && runtime.Contains("WarnReplaySuppressed(nameof(PublishJson)", StringComparison.Ordinal)
                  && runtime.Contains("Replay is enabled; ignoring live", StringComparison.Ordinal),
                "163-6G: replay-mode live channel/register suppression emits bounded diagnostics");
        }

        private static void OptionalProtobufSchemaShapeFailuresAreDiagnosed()
        {
            var runtime = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            Check(runtime.Contains("RegisterSchemas was missing", StringComparison.Ordinal)
                  && runtime.Contains("incompatible signature", StringComparison.Ordinal)
                  && runtime.Contains("type == null) return", StringComparison.Ordinal),
                "163-6H: optional protobuf schema assembly shape failures are diagnosed while absence stays quiet");
        }

        private static void TickCoordinatorSceneSnapshotHasNoUnusedWallClockParameter()
        {
            var coordinator = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");
            Check(coordinator.Contains("TryConsumeReplaySceneSnapshot(out var sceneSnapshotTimeNs)", StringComparison.Ordinal)
                  && coordinator.Contains("private bool TryConsumeReplaySceneSnapshot(out ulong timeNs)", StringComparison.Ordinal)
                  && !coordinator.Contains("TryConsumeReplaySceneSnapshot(out ulong timeNs, IFoxgloveClock wallClock)", StringComparison.Ordinal),
                "163-6I: replay scene snapshot consumption no longer carries an unused wall-clock parameter");
        }

        private static void Ros2ClockDisposeGuardIsConsistentAcrossRuntimePackages()
        {
            CheckRos2Clock("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2Clock.cs");
            CheckRos2Clock("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2Clock.cs");
            CheckRos2Clock("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2Clock.cs");
        }

        private static void CameraPublishOrderDocumentsEqualTimestampDrop()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraJpegPublishOrderPolicy.cs");
            Check(!CameraJpegPublishOrderPolicy.ShouldPublish(100UL, 100UL)
                  && source.Contains("Equal timestamps are treated", StringComparison.Ordinal),
                "163-6K: equal camera capture timestamps remain an intentional duplicate/stale drop");
        }

        private static void PhaseRegistryWiresPhase163_6()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase163-6\", \"Phase 163-6\", Phase163_6Validation.Validate", StringComparison.Ordinal),
                "163-6L: PhaseValidationRegistry wires --phase163-6");
        }

        private static void CheckRos2Clock(string relativePath)
        {
            var source = Read(relativePath);
            Check(source.Contains("using System.Threading;", StringComparison.Ordinal)
                  && source.Contains("private int disposed;", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _timeSource)", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Exchange(ref disposed, 1)", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Exchange(ref _timeSource, null)", StringComparison.Ordinal)
                  && source.Contains("throw new ObjectDisposedException(nameof(ROS2Clock));", StringComparison.Ordinal)
                  && !source.Contains("private bool disposed", StringComparison.Ordinal),
                "163-6J: " + relativePath + " uses atomic dispose/read guards");
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[FAIL] " + message);
            }

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class Phase163_6Request : IBackgroundEncodeRequest
        {
            public int Generation { get; set; }
            public int Value { get; set; }
            public bool Throw { get; set; }
        }
    }
}
