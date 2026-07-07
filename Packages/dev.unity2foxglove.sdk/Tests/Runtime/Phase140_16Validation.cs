// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-16 regression coverage for point-cloud, LaserScan, and Draco review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_16Validation.
    /// </summary>
    public static class Phase140_16Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-16: PointCloud, LaserScan, and Draco Paths ===");
            _passed = 0;

            NativeDracoNullFrameContractRemainsGraceful();
            DracoSidecarNullFrameContractIsGraceful();
            PointCloudPublishFrameDocumentsMainThreadContract();
            LaserScanDrainsQueuedBookkeepingBeforeManagerGate();
            LaserScanDisableClearsQueuedFramesAndWarnings();
            DracoSidecarClosesStandardErrorBeforeWaitingForReader();
            Ros2PointCloudMapsPackedFieldTypesThroughARejectingHelper();
            LaserScanJsonBuilderOwnsReturnedLists();
            PointCloudMotionCompensationWarningCounterIsCapped();
            DracoBackwardClockRateResetIsDocumented();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-16: {_passed} checks passed.");
        }

        private static void NativeDracoNullFrameContractRemainsGraceful()
        {
            Check(!DracoPointCloudNativeEncoder.TryEncode(null, out var payload, out var error)
                  && payload == null
                  && !string.IsNullOrWhiteSpace(error),
                "140-16A-1: native Draco TryEncode(null) returns false with an error instead of throwing");
        }

        private static void DracoSidecarNullFrameContractIsGraceful()
        {
            using (var sidecar = new DracoPointCloudEncoderSidecar())
            {
                Check(!sidecar.TryEncode(null, 1000, out var payload)
                      && payload == null
                      && !string.IsNullOrWhiteSpace(sidecar.LastError),
                    "140-16B-1: Draco helper sidecar TryEncode(null) returns false with an error instead of throwing");
            }
        }

        private static void PointCloudPublishFrameDocumentsMainThreadContract()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var method = Slice(source, "public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)", "/// <summary>\r\n        /// Queues a source VirtualLidar");
            Check(method.Contains("Unity main thread", StringComparison.Ordinal)
                  && method.Contains("PublishFrame must run on the Unity main thread", StringComparison.Ordinal),
                "140-16C-1: PointCloud PublishFrame public API states and enforces its Unity main-thread contract");
        }

        private static void LaserScanDrainsQueuedBookkeepingBeforeManagerGate()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var update = Slice(source, "private void Update()", "private bool TryPublishScan");
            var drainIndex = update.IndexOf("DrainQueuedPublishFrames(", StringComparison.Ordinal);
            var managerGateIndex = update.IndexOf("if (_manager == null)", StringComparison.Ordinal);
            Check(drainIndex >= 0 && managerGateIndex >= 0 && drainIndex < managerGateIndex,
                "140-16D-1: LaserScan Update drains queued warnings/drops before the manager null gate");
        }

        private static void LaserScanDisableClearsQueuedFramesAndWarnings()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var onDisable = Slice(source, "protected override void OnDisable()", "private void Update()");
            Check(onDisable.Contains("_warnedOffMainThreadPublishFrame = false", StringComparison.Ordinal)
                  && onDisable.Contains("ClearQueuedPublishFrames()", StringComparison.Ordinal)
                  && onDisable.Contains("base.OnDisable()", StringComparison.Ordinal),
                "140-16E-1: LaserScan OnDisable clears worker queue state and resets off-thread warning");

            var clearMethod = Slice(source, "private void ClearQueuedPublishFrames()", "private void DrainQueuedPublishFrames()");
            Check(clearMethod.Contains("_queuedPublishFrames.TryDequeue", StringComparison.Ordinal)
                  && clearMethod.Contains("Interlocked.Exchange(ref _queuedPublishFrameCount, 0)", StringComparison.Ordinal)
                  && clearMethod.Contains("Interlocked.Exchange(ref _queuedOffMainThreadPublishFrameCount, 0)", StringComparison.Ordinal)
                  && clearMethod.Contains("Interlocked.Exchange(ref _droppedQueuedPublishFrameCount, 0)", StringComparison.Ordinal),
                "140-16E-2: LaserScan queue cleanup discards stale frames and resets all queue counters");
        }

        private static void DracoSidecarClosesStandardErrorBeforeWaitingForReader()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudEncoderSidecar.cs");
            var stop = Slice(source, "public void Stop()", "/// <summary>Stop and dispose");
            var closeIndex = stop.IndexOf("CloseProcessStreams(process)", StringComparison.Ordinal);
            var reapIndex = stop.IndexOf("Task.Run(() => ReapProcess(process, stderrTask, stop))", StringComparison.Ordinal);
            var closeHelper = Slice(source, "private static void CloseProcessStreams", "private static void TryCloseProcessStream");
            Check(closeIndex >= 0
                  && reapIndex >= 0
                  && closeIndex < reapIndex
                  && closeHelper.Contains("process.StandardError.BaseStream.Close()", StringComparison.Ordinal)
                  && closeHelper.Contains("process.StandardOutput.BaseStream.Close()", StringComparison.Ordinal),
                "140-16F-1: Draco helper Stop closes stdout/stderr before background process reaping");
        }

        private static void Ros2PointCloudMapsPackedFieldTypesThroughARejectingHelper()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrPointCloudBuilder.cs");
            Check(source.Contains("private static byte MapDatatype(PointCloudPackedNumericType type)", StringComparison.Ordinal)
                  && source.Contains("throw new NotSupportedException(\"Unsupported PointCloud packed numeric type: \" + type)", StringComparison.Ordinal)
                  && source.Contains("writer.WriteUInt8(MapDatatype(field.Type));", StringComparison.Ordinal),
                "140-16G-1: ROS2 PointCloud builder maps field datatypes through a rejecting helper");
        }

        private static void LaserScanJsonBuilderOwnsReturnedLists()
        {
            var ranges = new List<double> { 1.0, 2.0 };
            var intensities = new List<double> { 0.1, 0.2 };
            var message = LaserScanMessageBuilder.CreateJson(1UL, "laser", 0.0, 1.0, ranges, intensities);

            ranges[0] = 99.0;
            intensities[1] = 88.0;

            Check(Math.Abs(message.Ranges[0] - 1.0) < double.Epsilon
                  && Math.Abs(message.Intensities[1] - 0.2) < double.Epsilon,
                "140-16H-1: LaserScan JSON builder copies caller lists instead of sharing mutable references");
        }

        private static void PointCloudMotionCompensationWarningCounterIsCapped()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.MotionCompensation.cs");
            var method = Slice(source, "private void WarnMotionCompensation", "    }\r\n}");
            Check(method.Contains("if (_motionCompensationWarningCount < int.MaxValue)", StringComparison.Ordinal),
                "140-16I-1: PointCloud motion-compensation warning counter is capped before int overflow");
        }

        private static void DracoBackwardClockRateResetIsDocumented()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs");
            var method = Slice(source, "private bool ShouldQueueVirtualLidarDracoFrame", "private ulong ResolveNativeDracoPublishIntervalNs");
            Check(method.Contains("backward clock", StringComparison.Ordinal)
                  && method.Contains("replay seek", StringComparison.Ordinal),
                "140-16J-1: native Draco rate limiter documents backward-clock reset behavior");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_16Validation.cs", StringComparison.Ordinal),
                "140-16K-1: test project compiles Phase140_16Validation");
            Check(registry.Contains("Ci(\"--phase140-16\",", StringComparison.Ordinal)
                  && registry.Contains("Phase140_16Validation.Validate", StringComparison.Ordinal),
                "140-16K-2: validation registry exposes --phase140-16");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
        }

        private static void CheckThrows<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Check(true, label);
                return;
            }

            throw new Exception("[FAIL] " + label + " (expected " + typeof(TException).Name + ")");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
