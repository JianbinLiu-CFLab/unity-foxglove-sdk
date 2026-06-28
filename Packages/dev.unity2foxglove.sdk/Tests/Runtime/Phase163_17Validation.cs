// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-17 validation for point-cloud, LaserScan, and geometry payload fixes.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_17Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-17: Point Cloud, LaserScan, and Geometry Payloads ===");
            _passed = 0;

            PointCloudQoSReductionPreservesAbsoluteTimeLayout();
            PointCloudSourceDrivenStateUsesThreadVisibleFlag();
            DracoHelperSurfacesTaskFaultsSeparatelyFromTimeouts();
            ScanReferenceMotionCompensationHonorsReferenceTime();
            LaserScanPublisherSanitizesEmptyFrameIds();
            PointCloudDiagnosticsLiveInDiagnosticsPartial();
            PointCloudDisableClearsPendingFrameSlot();
            PointCloud2CdrBuilderDocumentsLittleEndianBoundary();
            LegacyCompressedPointCloudPublisherRemainsMigrationOnly();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-17: {_passed} checks passed.");
        }

        private static void PointCloudQoSReductionPreservesAbsoluteTimeLayout()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = 123UL,
                FrameId = "os_lidar",
                EmitAbsoluteTimeNs = true
            };
            frame.Points.Add(new PointCloudPoint(0f, 0f, 0f) { TimeOffsetSeconds = 0.00f });
            frame.Points.Add(new PointCloudPoint(1f, 0f, 0f) { TimeOffsetSeconds = 0.01f });
            frame.Points.Add(new PointCloudPoint(2f, 0f, 0f) { TimeOffsetSeconds = 0.02f });

            var reducer = new PointCloudQoSReducer();
            var reduced = reducer.PrepareFrameForQoS(
                frame,
                456UL,
                "fallback",
                maxPoints: 2,
                maxPackedBytes: 0,
                PointCloudSamplingMode.FirstPoints,
                voxelSizeMeters: 0f,
                logQosDrops: false,
                out var layout);

            Check(reduced != null && reduced.EmitAbsoluteTimeNs,
                "163-17A-1: QoS-reduced point-cloud frames preserve EmitAbsoluteTimeNs");
            Check(layout != null && layout.HasAbsoluteTime && layout.Fields.Any(field => field.Name == "t"),
                "163-17A-2: QoS-reduced point-cloud packed layout keeps the absolute-time t field");
        }

        private static void PointCloudSourceDrivenStateUsesThreadVisibleFlag()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishState.cs");

            Check(source.Contains("using System.Threading;", StringComparison.Ordinal)
                  && source.Contains("private int _hasSourceDrivenFrames;", StringComparison.Ordinal),
                "163-17B-1: source-driven point-cloud ownership is stored in a thread-visible numeric flag");
            Check(source.Contains("Interlocked.Exchange(ref _hasSourceDrivenFrames, 1)", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Exchange(ref _hasSourceDrivenFrames, 0)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _hasSourceDrivenFrames) != 0", StringComparison.Ordinal),
                "163-17B-2: source-driven point-cloud ownership is written and read with memory barriers");
        }

        private static void DracoHelperSurfacesTaskFaultsSeparatelyFromTimeouts()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudEncoderSidecar.cs");
            var tryEncode = ExtractMethod(source, "public bool TryEncode");
            var wait = ExtractMethod(source, "private static bool WaitForTask(Task task, DateTime deadlineUtc, out string error)");

            Check(tryEncode.Contains("out var writeError", StringComparison.Ordinal)
                  && tryEncode.Contains("Failed writing point-cloud frame to Draco helper", StringComparison.Ordinal)
                  && tryEncode.Contains("Timed out writing point-cloud frame to Draco helper", StringComparison.Ordinal),
                "163-17C-1: Draco helper write faults are reported separately from write timeouts");
            Check(tryEncode.Contains("out var readLengthError", StringComparison.Ordinal)
                  && tryEncode.Contains("Failed reading Draco helper payload length", StringComparison.Ordinal)
                  && tryEncode.Contains("Draco helper stdout ended before payload length", StringComparison.Ordinal),
                "163-17C-2: Draco helper read faults are reported separately from clean EOF");
            Check(wait.Contains("DescribeTaskException", StringComparison.Ordinal)
                  && source.Contains("private static string DescribeTaskException", StringComparison.Ordinal)
                  && !wait.Contains("catch\r\n            {", StringComparison.Ordinal)
                  && !wait.Contains("catch\n            {", StringComparison.Ordinal),
                "163-17C-3: Draco helper task wait preserves fault details instead of swallowing exceptions");
        }

        private static void ScanReferenceMotionCompensationHonorsReferenceTime()
        {
            var points = new[]
            {
                new VirtualLidarPointData
                {
                    X = 1f,
                    Y = 0f,
                    Z = 0f,
                    TimeOffsetSeconds = 0f,
                    IsValid = 1,
                    HasAcquisitionFrame = 1
                },
                new VirtualLidarPointData
                {
                    X = 2f,
                    Y = 0f,
                    Z = 0f,
                    TimeOffsetSeconds = 1f,
                    IsValid = 1,
                    HasAcquisitionFrame = 1
                }
            };
            var request = new PointCloudMotionCompensationRequest(
                "/deskewed",
                PointCloudMotionCompensationReferenceTime.ScanEnd,
                PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame,
                Array.Empty<SensorMotionPoseSample>());

            Check(PointCloudMotionCompensator.TryCompensateVirtualLidar(
                    points,
                    points.Length,
                    1_000_000_000UL,
                    request,
                    out var result,
                    out var error),
                "163-17D-1: scan-reference motion compensation succeeds without pose history: " + error);
            Check(result.ReferenceUnixNs == 2_000_000_000UL,
                "163-17D-2: scan-reference motion compensation uses the requested reference time");
            Check(result.Points[1].X == 2f
                  && result.Points[1].TimeOffsetSeconds == 0f
                  && result.Points[1].HasAcquisitionFrame == 0,
                "163-17D-3: scan-reference motion compensation preserves closed XYZ and clears rolling-time metadata");
        }

        private static void LaserScanPublisherSanitizesEmptyFrameIds()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveLaserScanPublisher.cs");
            var publishFrame = ExtractMethod(source, "private void PublishFrameOnMainThread");
            var update = ExtractMethod(source, "private void Update");
            var sanitizer = ExtractMethod(source, "private static string SanitizeNonEmptyFrameId");

            Check(publishFrame.Contains("SanitizeNonEmptyFrameId(frameId, \"laser\")", StringComparison.Ordinal),
                "163-17E-1: event-driven LaserScan publishes sanitize caller-provided frame ids");
            Check(update.Contains("SanitizeNonEmptyFrameId(_frameId, \"laser\")", StringComparison.Ordinal),
                "163-17E-2: cadence-driven LaserScan publishes sanitize Inspector frame ids");
            Check(sanitizer.Contains("string.IsNullOrWhiteSpace(raw)", StringComparison.Ordinal)
                  && sanitizer.Contains("SanitizeFrameId(value, fallback)", StringComparison.Ordinal),
                "163-17E-3: LaserScan sanitizer falls back to a non-empty ROS-safe frame id");
        }

        private static void PointCloudDiagnosticsLiveInDiagnosticsPartial()
        {
            var core = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var diagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Diagnostics.cs");
            var structureTest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/PointCloudPublisherStructureTests.cs");

            Check(!core.Contains("private void LogPointCloudDiagnosticMessage", StringComparison.Ordinal)
                  && diagnostics.Contains("private void LogPointCloudDiagnosticMessage", StringComparison.Ordinal),
                "163-17F-1: point-cloud diagnostic logging lives in the diagnostics partial");
            Check(structureTest.Contains("FoxglovePointCloudPublisher.Diagnostics.cs", StringComparison.Ordinal)
                  && structureTest.Contains("Assert.DoesNotContain(\"private void LogPointCloudDiagnosticMessage\", core", StringComparison.Ordinal),
                "163-17F-2: unit structure tests enforce the diagnostics partial boundary");
        }

        private static void PointCloudDisableClearsPendingFrameSlot()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var onDisable = ExtractMethod(source, "protected override void OnDisable");

            Check(onDisable.Contains("_pendingFrameSlot.Take();", StringComparison.Ordinal)
                  && CheckOrdered(onDisable, "_pendingFrameSlot.Take();", "_dracoEncodePipeline?.Stop(clearCompleted: true);"),
                "163-17G-1: point-cloud OnDisable clears any pending last-value frame before stopping workers");
        }

        private static void PointCloud2CdrBuilderDocumentsLittleEndianBoundary()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSensorPointCloud2Builder.cs");
            var normalizedSource = source.Replace("\r\n", "\n");
            var serialize = ExtractMethod(normalizedSource, "public static byte[] Serialize(\n            ulong unixNs");
            var guard = ExtractMethod(normalizedSource, "private static void EnsureLittleEndianRuntime");

            Check(source.Contains("Builds little-endian CDR payloads", StringComparison.Ordinal)
                  && source.Contains("is_bigendian flag is serialized as false", StringComparison.Ordinal),
                "163-17H-1: PointCloud2 CDR builder documents its little-endian payload contract");
            Check(serialize.Contains("ValidateLayout(height, width, pointStep, data);", StringComparison.Ordinal)
                  && CheckOrdered(serialize, "ValidateLayout(height, width, pointStep, data);", "EnsureLittleEndianRuntime();"),
                "163-17H-2: PointCloud2 CDR builder validates layout before enforcing the endian boundary");
            Check(guard.Contains("BitConverter.IsLittleEndian", StringComparison.Ordinal)
                  && guard.Contains("PlatformNotSupportedException", StringComparison.Ordinal)
                  && source.Contains("writer.WriteBool(false);", StringComparison.Ordinal),
                "163-17H-3: PointCloud2 CDR builder fails closed on unsupported endian runtimes");
        }

        private static void LegacyCompressedPointCloudPublisherRemainsMigrationOnly()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedPointCloudPublisher.cs");

            Check(source.Contains("[Obsolete(", StringComparison.Ordinal)
                  && source.Contains("[AddComponentMenu(\"\")]", StringComparison.Ordinal),
                "163-17I-1: legacy compressed point-cloud publisher remains hidden and marked obsolete");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_17Validation.cs", StringComparison.Ordinal),
                "163-17J-1: runtime test project compiles Phase163_17Validation");
            Check(registry.Contains("--phase163-17", StringComparison.Ordinal)
                  && registry.Contains("Phase163_17Validation.Validate", StringComparison.Ordinal),
                "163-17J-2: validation registry exposes --phase163-17");
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 163-17 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 163-17 validation helper found opening brace");

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unable to extract source block.");
        }

        private static bool CheckOrdered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
