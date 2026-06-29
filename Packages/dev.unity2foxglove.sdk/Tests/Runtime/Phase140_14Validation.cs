// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-14 camera publisher and async pipeline review fixes.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for camera image publisher lifecycle, ordering,
    /// and async pipeline hardening found in Phase 140-14.
    /// </summary>
    public static class Phase140_14Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-14 camera publisher review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-14: camera image publishers and async pipelines review fixes ===");
            _passed = 0;

            SyncJpegFallbackUpdatesPublishTimeForEverySuccessfulPath();
            CaptureGenerationUsesMemoryBarriers();
            JpegWorkerQueuesUseVolatileReferences();
            JpegWorkerStopTimeoutTracksOrphanedThread();
            CaptureCameraIsHiddenAndNotSaved();
            OutputModeWarningIsNotLoggedFromPropertyGetter();
            VideoFrameFactoryNullCheckRunsBeforeOtherValidation();
            VideoFrameSubmitAvoidsPerFrameClosure();
            CameraDemandChecksReuseResolvedProfile();
            RawImageBuilderDocumentsPackedRgb24Assumption();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-14: {_passed} checks passed.");
        }

        private static void SyncJpegFallbackUpdatesPublishTimeForEverySuccessfulPath()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");
            var method = ExtractMethod(source, "private void PublishJpegFrame");

            Check(CountOccurrences(method, "_lastPublishedCaptureUnixNs = unixNs") >= 4,
                "140-14A-1: synchronous JPEG fallback updates publish order after every successful output path");
            Check(method.Contains("if (!CameraJpegPublishOrderPolicy.ShouldPublish(unixNs, _lastPublishedCaptureUnixNs))", StringComparison.Ordinal),
                "140-14A-2: synchronous JPEG fallback rejects late or duplicate timestamps before publishing");
        }

        private static void CaptureGenerationUsesMemoryBarriers()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var jpeg = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");

            Check(publisher.Contains("using System.Threading;", StringComparison.Ordinal)
                  && publisher.Contains("Volatile.Read(ref _captureGeneration)", StringComparison.Ordinal)
                  && publisher.Contains("Interlocked.Increment(ref _captureGeneration)", StringComparison.Ordinal)
                  && jpeg.Contains("() => Volatile.Read(ref _captureGeneration)", StringComparison.Ordinal),
                "140-14B-1: capture generation cross-thread reads and writes use memory barriers");
        }

        private static void JpegWorkerQueuesUseVolatileReferences()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");

            Check(source.Contains("Volatile.Read(ref _encodeQueue)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _completedQueue)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Write(ref _encodeQueue", StringComparison.Ordinal)
                  && source.Contains("Volatile.Write(ref _completedQueue", StringComparison.Ordinal),
                "140-14C-1: JPEG worker queue reference replacement uses volatile access");
        }

        private static void JpegWorkerStopTimeoutTracksOrphanedThread()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPipeline.cs");

            Check(source.Contains("_orphanedWorker", StringComparison.Ordinal)
                  && source.Contains("TryJoinOrphanedWorker", StringComparison.Ordinal)
                  && source.Contains("_orphanedWorker = worker", StringComparison.Ordinal),
                "140-14D-1: JPEG worker stop timeout tracks orphaned workers for the next start");
        }

        private static void CaptureCameraIsHiddenAndNotSaved()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");

            Check(source.Contains("go.hideFlags = HideFlags.HideAndDontSave", StringComparison.Ordinal),
                "140-14E-1: hidden capture camera is not shown or saved in scenes");
        }

        private static void OutputModeWarningIsNotLoggedFromPropertyGetter()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var property = ExtractBlock(source, "private CameraOutputMode ResolvedOutputMode");

            Check(!property.Contains("Debug.LogWarning", StringComparison.Ordinal)
                  && source.Contains("WarnIfRuntimeOutputModeSwitchIgnored", StringComparison.Ordinal),
                "140-14F-1: output mode warning is emitted from an explicit method, not a property getter");
        }

        private static void VideoFrameFactoryNullCheckRunsBeforeOtherValidation()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var method = ExtractMethod(source, "public CameraVideoSubmitResult SubmitVideoFrame");

            Check(!method.Contains("Func<byte[]>", StringComparison.Ordinal)
                  && method.Contains("ICameraVideoFrameBytesSource", StringComparison.Ordinal)
                  && method.Contains("frameBytes.CopyTo(ownedFrameBytes)", StringComparison.Ordinal),
                "140-14G-1: video submit accepts a frame byte source and defers scratch copy until validation passes");
        }

        private static void VideoFrameSubmitAvoidsPerFrameClosure()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs");
            var method = ExtractMethod(source, "private void SubmitVideoFrame");

            Check(!method.Contains("() => readbackData.ToArray()", StringComparison.Ordinal)
                  && method.Contains("new CameraVideoReadbackFrameBytesSource(req)", StringComparison.Ordinal)
                  && method.Contains("readbackData,", StringComparison.Ordinal),
                "140-14G-2: camera video readback submit does not allocate a per-frame bytes factory closure");
        }

        private static void CameraDemandChecksReuseResolvedProfile()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var lateUpdate = ExtractMethod(source, "private void LateUpdate");
            var readback = ExtractMethod(source, "private void OnReadbackComplete");

            Check(lateUpdate.Contains("HasSensorCompressedImageDemand(profile)", StringComparison.Ordinal)
                  && readback.Contains("HasSensorCompressedImageDemand(profile)", StringComparison.Ordinal)
                  && source.Contains("private bool HasSensorCompressedImageDemand(CameraVideoOutputProfile profile)", StringComparison.Ordinal),
                "140-14G-3: camera compressed-image demand reuses the per-frame resolved profile");
        }

        private static void RawImageBuilderDocumentsPackedRgb24Assumption()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraRawImageFrameBuilder.cs");

            Check(source.Contains("AsyncGPUReadback RGB24 buffers are expected to be tightly packed", StringComparison.Ordinal),
                "140-14H-1: raw RGB image builder documents the packed readback assumption");
        }

        private static void PhaseWiringIsPresent()
        {
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(csproj.Contains("Phase140_14Validation.cs", StringComparison.Ordinal),
                "140-14I-1: test project compiles Phase140_14Validation");
            Check(registry.Contains("\"--phase140-14\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_14Validation.Validate", StringComparison.Ordinal),
                "140-14I-2: validation registry exposes --phase140-14");
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 140-14 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 140-14 validation helper found block: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 140-14 validation helper found opening brace");

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
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException("Unable to locate repository root.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("FAIL: " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
