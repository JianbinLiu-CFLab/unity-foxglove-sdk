// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-14 regression coverage for native Draco point-cloud input bounds.

using System;
using System.IO;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_14Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-14: PointCloud LaserScan Draco Path ===");
            _passed = 0;

            NativeDracoInputBudgetMatchesPackedDataBoundary();
            NativeDracoBudgetRejectsOversizedScratchBeforeAllocation();
            NativeDracoTryEncodeChecksBudgetBeforeScratchAllocation();
            PointCloudBuildersAvoidUnnecessaryDualPayloads();
            LaserScanBuildersRejectMissingRangesAndAcceptFlexibleAngles();
            PointCloudProfilesFallbackForUnknownModes();
            LegacyCompressedPointCloudPublisherIsMarkedObsolete();
            DracoSidecarDiagnosticsUseVolatileAndTotalDeadline();
            NativeDracoOutputBufferUsesArrayPool();
            PointCloudPublisherQueuesDracoEncodingOffMainThread();
            LaserScanPublisherExposesProgrammaticPublishPath();

            Console.WriteLine($"Phase 134-14: {_passed} checks passed.");
        }

        private static void NativeDracoInputBudgetMatchesPackedDataBoundary()
        {
            Check(DracoPointCloudNativeEncoder.MaxInputBytes == PointCloudPackedDataBuilder.MaxPackedDataBytes,
                "134-14A-1: native Draco input budget matches packed point-cloud data budget");
            Check(DracoPointCloudNativeEncoder.XyzBytesPerPoint == 12,
                "134-14A-2: native Draco XYZ scratch budget uses 12 bytes per point");
            Check(DracoPointCloudNativeEncoder.MaxInputPoints
                  == PointCloudPackedDataBuilder.MaxPackedDataBytes / DracoPointCloudNativeEncoder.XyzBytesPerPoint,
                "134-14A-3: native Draco max point count derives from byte budget");
        }

        private static void NativeDracoBudgetRejectsOversizedScratchBeforeAllocation()
        {
            Check(DracoPointCloudNativeEncoder.ValidateInputBudget(1, out var smallError)
                  && string.IsNullOrEmpty(smallError),
                "134-14B-1: native Draco accepts small input budgets");

            var oversizedPointCount = DracoPointCloudNativeEncoder.MaxInputPoints + 1;
            Check(!DracoPointCloudNativeEncoder.ValidateInputBudget(oversizedPointCount, out var error)
                  && error.Contains(PointCloudPackedDataBuilder.MaxPackedDataBytes.ToString())
                  && error.Contains(((long)oversizedPointCount * DracoPointCloudNativeEncoder.XyzBytesPerPoint).ToString()),
                "134-14B-2: native Draco rejects oversized input budgets with byte details");
        }

        private static void NativeDracoTryEncodeChecksBudgetBeforeScratchAllocation()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");
            var tryEncodeIndex = source.IndexOf("public static bool TryEncode", StringComparison.Ordinal);
            var validateIndex = Math.Max(
                source.IndexOf("ValidateInputBudget(frame.Points.Count", tryEncodeIndex, StringComparison.Ordinal),
                Math.Max(
                    source.IndexOf("ValidateInputBudget(frame.GetPointCount", tryEncodeIndex, StringComparison.Ordinal),
                    source.IndexOf("ValidateInputBudget(pointCount", tryEncodeIndex, StringComparison.Ordinal)));
            var buildIndex = source.IndexOf("BuildXyzArray(frame)", tryEncodeIndex, StringComparison.Ordinal);

            Check(tryEncodeIndex >= 0 && validateIndex > tryEncodeIndex && buildIndex > validateIndex,
                "134-14C-1: TryEncode validates input budget before allocating XYZ scratch");
            Check(source.Contains("PointCloudPackedDataBuilder.MaxPackedDataBytes"),
                "134-14C-2: native Draco source reuses packed point-cloud byte boundary");
        }

        private static void PointCloudBuildersAvoidUnnecessaryDualPayloads()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/PointCloudMessageBuilder.cs");

            Check(source.Contains("return CreateJson(frame, PointCloudPackedDataBuilder.Build(frame));"),
                "134-14D-1: CreateJson builds only the packed data needed for JSON output");
            Check(source.Contains("return CreateProtobuf(frame, PointCloudPackedDataBuilder.Build(frame));"),
                "134-14D-2: CreateProtobuf builds only the packed data needed for protobuf output");
            Check(!source.Contains("Build(frame).Json"),
                "134-14D-3: CreateJson no longer constructs the full point-cloud result");
            Check(!source.Contains("Build(frame).Protobuf"),
                "134-14D-4: CreateProtobuf no longer constructs the full point-cloud result");

            var packedSource = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            Check(packedSource.Contains("owned by this value")
                && packedSource.Contains("Treat as read-only")
                && packedSource.Contains("clone it first"),
                "134-14D-5: packed point-cloud byte ownership is documented");
            Check(source.Contains("owned by this result")
                && source.Contains("Treat as read-only")
                && source.Contains("invalidate the paired JSON/protobuf payloads"),
                "134-14D-6: point-cloud build result byte ownership is documented");
        }

        private static void LaserScanBuildersRejectMissingRangesAndAcceptFlexibleAngles()
        {
            CheckThrows<ArgumentNullException>(
                () => LaserScanMessageBuilder.CreateJson(1UL, "laser", 0.0, 1.0, null),
                "134-14E-1: protobuf LaserScan JSON builder rejects null ranges");
            CheckThrows<ArgumentNullException>(
                () => LaserScanMessageBuilder.SerializeProtobuf(1UL, "laser", 0.0, 1.0, null),
                "134-14E-2: protobuf LaserScan serializer rejects null ranges");
            CheckThrows<ArgumentNullException>(
                () => Ros2CdrLaserScanBuilder.Serialize(1UL, "laser", 0.0, 1.0, null),
                "134-14E-3: ROS2 CDR LaserScan builder rejects null ranges");

            CheckThrows<ArgumentOutOfRangeException>(
                () => LaserScanMessageBuilder.CreateJson(1UL, "laser", double.NaN, 1.0, new[] { 1.0 }),
                "134-14E-4: protobuf LaserScan builder rejects NaN start angles");
            CheckThrows<ArgumentOutOfRangeException>(
                () => Ros2CdrLaserScanBuilder.Serialize(1UL, "laser", 0.0, double.PositiveInfinity, new[] { 1.0 }),
                "134-14E-5: ROS2 CDR LaserScan builder rejects infinite end angles");

            var scan = LaserScanMessageBuilder.CreateJson(1UL, "laser", 0.0, 1.0, new[] { 1.0 }, null);
            Check(scan.Intensities.Count == 0,
                "134-14E-6: LaserScan null intensities remain a valid empty list");
            Check(LaserScanMessageBuilder.CreateJson(1UL, "laser", 0.0, 0.0, new[] { 1.0 }).Ranges.Count == 1,
                "134-14E-7: protobuf LaserScan builder accepts single-beam equal angles");
            Check(Ros2CdrLaserScanBuilder.Serialize(1UL, "laser", 1.0, -1.0, new[] { 1.0 }).Length > 0,
                "134-14E-8: ROS2 CDR LaserScan builder accepts reverse or wrapped angle ranges");
        }

        private static void PointCloudProfilesFallbackForUnknownModes()
        {
            var profile = PointCloudOutputProfile.ForMode((PointCloudOutputMode)999);
            Check(profile.Mode == PointCloudOutputMode.Raw
                  && profile.SchemaName == PointCloudOutputModeDefaults.RawSchema
                  && profile.DefaultTopic == PointCloudOutputModeDefaults.RawTopic,
                "134-14F-1: unknown serialized point-cloud output modes fall back to Raw for scene compatibility");
        }

        private static void LegacyCompressedPointCloudPublisherIsMarkedObsolete()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedPointCloudPublisher.cs");

            Check(source.Contains("[Obsolete(\"Use FoxglovePointCloudPublisher with PointCloudOutputMode.Draco.\", false)]"),
                "134-14G-1: legacy compressed point-cloud publisher is explicitly obsolete");
            Check(source.Contains("Prefer <see cref=\"FoxglovePointCloudPublisher\"/> with Point Cloud Output Mode set to Draco"),
                "134-14G-2: obsolete message points users to the unified Draco mode");
        }

        private static void DracoSidecarDiagnosticsUseVolatileAndTotalDeadline()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudEncoderSidecar.cs");

            Check(source.Contains("Volatile.Read(ref _lastDiagnosticLine)") && source.Contains("Volatile.Read(ref _lastError)"),
                "134-14H-1: Draco sidecar diagnostics use volatile reads");
            Check(source.Contains("Volatile.Write(ref _lastDiagnosticLine") && source.Contains("Volatile.Write(ref _lastError"),
                "134-14H-2: Draco sidecar diagnostics use volatile writes");
            Check(source.Contains("var deadlineUtc = DateTime.UtcNow.AddMilliseconds(boundedTimeoutMs);"),
                "134-14H-3: Draco sidecar encode uses a total deadline");
            Check(source.Contains("RemainingMilliseconds(deadlineUtc)"),
                "134-14H-4: Draco sidecar IO waits consume the same total deadline");
        }

        private static void NativeDracoOutputBufferUsesArrayPool()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");

            Check(source.Contains("ArrayPool<byte>.Shared.Rent(capacity)"),
                "134-14I-1: native Draco retry buffer rents from ArrayPool");
            Check(source.Contains("ArrayPool<byte>.Shared.Return(output)"),
                "134-14I-2: native Draco retry buffer is returned to ArrayPool");
        }

        private static void PointCloudPublisherQueuesDracoEncodingOffMainThread()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(source.Contains("StartDracoEncodeWorker")
                  && source.Contains("new System.Threading.Thread(RunDracoEncodeWorker)")
                  && source.Contains("Priority = System.Threading.ThreadPriority.BelowNormal"),
                "134-14J-1: Draco point-cloud encoding runs on a below-normal background worker");
            Check(source.Contains("CloneFrameForBackgroundEncode(frame)"),
                "134-14J-2: Draco worker receives a cloned point-cloud frame");
            Check(source.Contains("_pendingDracoEncode = request"),
                "134-14J-3: Draco pending work is last-value-wins");
            Check(source.Contains("DrainCompletedDracoEncode()"),
                "134-14J-4: completed Draco work is drained from Update");
            Check(source.Contains("PublishDracoPayload("),
                "134-14J-5: background encode result is published through a main-thread drain path");
            Check(source.Contains("DracoFailureWarningIntervalFrames"),
                "134-14J-6: repeated Draco failures are throttled");
            Check(source.Contains("Queue<DracoEncodeResult> _completedDracoEncodes")
                  && source.Contains("MaxCompletedDracoEncodeResults"),
                "134-14J-7: completed Draco encode results use a bounded queue instead of a single overwrite slot");
            Check(source.Contains("StopDracoEncodeWorker(clearCompleted: true)")
                  && source.Contains("ManualResetEventSlim"),
                "134-14J-8: Draco worker has an explicit disable/destroy shutdown path");
        }

        private static void LaserScanPublisherExposesProgrammaticPublishPath()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");

            Check(source.Contains("public void PublishFrame("),
                "134-14K-1: LaserScan publisher exposes a programmatic PublishFrame API");
            Check(source.Contains("_warnedIntensityMismatch = false;"),
                "134-14K-2: LaserScan intensity mismatch warning recovers after valid data");
            Check(source.Contains("_syntheticRangesMeters != _syntheticRangeMeters"),
                "134-14K-3: synthetic range cache uses direct equality instead of double.Epsilon");
            Check(source.Contains("ConcurrentQueue<QueuedLaserScanFrame>")
                  && source.Contains("IsUnityMainThread()"),
                "134-14K-4: LaserScan PublishFrame marshals worker-thread calls to Update");
            Check(source.Contains("TryPublishScan(")
                  && source.Contains("LaserScan publish failed; skipping until valid data is provided"),
                "134-14K-5: LaserScan publisher catches recoverable builder failures without per-frame exception spam");
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
