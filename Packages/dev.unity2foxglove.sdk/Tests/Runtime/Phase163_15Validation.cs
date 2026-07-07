// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-15 validation for camera publisher and editor review fixes.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_15Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-15: Camera Publisher and Camera Editor ===");
            _passed = 0;

            LegacyCompressedVideoLifecycleIsThreadVisible();
            PrimaryCameraDestroyOrdersCleanupBeforeWorkerStop();
            CameraInfoAndCalibrationIntrinsicsUseAspectRatio();
            CameraInfoProfileAndFallbacksAreStable();
            CameraBackpressureDoesNotSuppressRawOnlyCapture();
            CameraDiagnosticsAndEditorContractsAreExplicit();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-15: {_passed} checks passed.");
        }

        private static void LegacyCompressedVideoLifecycleIsThreadVisible()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");
            var onReadback = ExtractMethod(source, "private void OnReadbackComplete");
            var onDestroy = ExtractMethod(source, "private void OnDestroy");

            Check(source.Contains("using System.Threading;", StringComparison.Ordinal)
                  && CountOccurrences(source, "Interlocked.Increment(ref _captureGeneration)") >= 3,
                "163-15A-1: legacy compressed-video generation increments use Interlocked");
            Check(onReadback.Contains("Volatile.Read(ref _captureGeneration)", StringComparison.Ordinal),
                "163-15A-2: legacy compressed-video stale readback check uses Volatile.Read");
            Check(onReadback.Contains("try", StringComparison.Ordinal)
                  && onReadback.Contains("finally", StringComparison.Ordinal)
                  && CheckOrdered(onReadback, "finally", "CompletePendingReadback();"),
                "163-15A-3: legacy compressed-video decrements pending readbacks in finally");
            Check(CheckOrdered(onDestroy, "_cleanupWhenReadbacksDrain = _pendingRequests > 0;", "StopSidecar();"),
                "163-15A-4: legacy compressed-video destroy marks drain cleanup before sidecar stop");
        }

        private static void PrimaryCameraDestroyOrdersCleanupBeforeWorkerStop()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var lateUpdate = ExtractMethod(source, "private void LateUpdate");
            var onDestroy = ExtractMethod(source, "private void OnDestroy");

            Check(CheckOrdered(onDestroy, "_cleanupWhenReadbacksDrain = _pendingRequests > 0;", "StopVideoSidecar();")
                  && CheckOrdered(onDestroy, "_cleanupWhenReadbacksDrain = _pendingRequests > 0;", "StopJpegWorker(clearQueues: true);"),
                "163-15B-1: primary camera destroy marks drain cleanup before stopping workers");
            Check(lateUpdate.Contains("var publishJpegOutput = !profile.IsVideo && (publishWebSocket || publishBridge || publishNativeFrame);", StringComparison.Ordinal)
                  && CheckOrdered(lateUpdate, "var publishRawFrame = HasSensorRawImageDemand();", "if (publishJpegOutput && !AllowJpegCaptureByBackpressure()) return;"),
                "163-15B-2: raw-only camera output is not suppressed by JPEG backpressure");
            Check(source.Contains("optional R2FU/native ROS2 adapter subscribes to the raw image event", StringComparison.Ordinal),
                "163-15B-3: raw image tooltip documents native adapter subscriber requirement");
        }

        private static void CameraInfoAndCalibrationIntrinsicsUseAspectRatio()
        {
            var info = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            var calibration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraCalibrationPublisher.cs");

            Check(info.Contains("var fx = fy * ((double)width / Math.Max(1.0, height));", StringComparison.Ordinal),
                "163-15C-1: CameraInfo derives fx from vertical FOV and aspect ratio");
            Check(calibration.Contains("var fx = fy * ((double)width / Math.Max(1.0, height));", StringComparison.Ordinal),
                "163-15C-2: CameraCalibration derives fx from vertical FOV and aspect ratio");
        }

        private static void CameraInfoProfileAndFallbacksAreStable()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            var resolveFrame = ExtractMethod(source, "private string ResolveFrameId");
            var resolveTopic = ExtractMethod(source, "private string ResolveSensorCameraInfoTopic");
            var resolveParent = ExtractMethod(source, "private string ResolveTfParentFrame");

            Check(CountOccurrences(resolveFrame, "ResolveSensorProfile()") == 1
                  && CountOccurrences(resolveTopic, "ResolveSensorProfile()") == 1
                  && CountOccurrences(resolveParent, "ResolveSensorProfile()") == 1,
                "163-15D-1: CameraInfo profile-dependent helpers cache the profile per call");
            Check(source.Contains("_topic == \"/unity/sensor/camera/camera_info\"", StringComparison.Ordinal),
                "163-15D-2: CameraInfo profile default sentinel matches actual default topic");
            Check(source.Contains("WarnScreenDimensionFallback", StringComparison.Ordinal)
                  && source.Contains("falling back to Screen dimensions for calibration", StringComparison.Ordinal),
                "163-15D-3: CameraInfo warns when misconfiguration falls back to Screen dimensions");
        }

        private static void CameraBackpressureDoesNotSuppressRawOnlyCapture()
        {
            var gate = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraBackpressureGate.cs");
            var unsupportedStatsBlock = ExtractBlock(gate, gate.IndexOf("if (!statsSupported)", StringComparison.Ordinal));

            Check(gate.Contains("transport does not support drop stats; adaptation is inactive", StringComparison.Ordinal)
                  && gate.Contains("_warnedStatsUnsupported", StringComparison.Ordinal),
                "163-15E-1: camera backpressure warns once when transport stats are unavailable");
            Check(unsupportedStatsBlock.Contains("warning = StatsUnsupportedWarning;", StringComparison.Ordinal)
                  && unsupportedStatsBlock.Contains("return true;", StringComparison.Ordinal),
                "163-15E-2: unsupported stats leaves capture active instead of silently evaluating fake drop counters");
        }

        private static void CameraDiagnosticsAndEditorContractsAreExplicit()
        {
            var jpeg = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");
            var outputMode = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs");
            var infoEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Check(jpeg.Contains("var copyStart = Stopwatch.GetTimestamp();", StringComparison.Ordinal)
                  && jpeg.Contains("var copyMs = ElapsedMs(copyStart);", StringComparison.Ordinal)
                  && jpeg.Contains("_diagnostics.RecordReadbackCopy(", StringComparison.Ordinal)
                  && jpeg.Contains("copyMs,", StringComparison.Ordinal),
                "163-15F-1: JPEG readback copy diagnostics use elapsed copy timing despite pipeline placeholder argument");
            Check(outputMode.Contains("mode switches change schema/encoding without forcing topic churn", StringComparison.Ordinal),
                "163-15F-2: camera output mode defaults document intentional shared topic behavior");
            Check(infoEditor.Contains("could not resolve ObjectField type", StringComparison.Ordinal)
                  && infoEditor.Contains("UnityEngine.Object fallback", StringComparison.Ordinal),
                "163-15F-3: CameraInfo editor warns when an unknown object field type falls back");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_15Validation.cs", StringComparison.Ordinal),
                "163-15G-1: runtime test project compiles Phase163_15Validation");
            Check(registry.Contains("--phase163-15", StringComparison.Ordinal)
                  && registry.Contains("Phase163_15Validation.Validate", StringComparison.Ordinal),
                "163-15G-2: validation registry exposes --phase163-15");
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 163-15 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 163-15 validation helper found opening brace");

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

        private static int CountOccurrences(string source, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
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
