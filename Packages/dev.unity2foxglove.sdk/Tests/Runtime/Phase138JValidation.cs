// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138J validation for async JPEG camera budget and payload rules.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Phase 138J checks for async JPEG camera behavior and payload integrity.
    /// </summary>
    public static class Phase138JValidation
    {
        private static int _passed;

        /// <summary>
        /// Runs all validation checks for phase 138J.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138J: Camera Async JPEG Pipeline ===");
            _passed = 0;

            BudgetAllowsCaptureWhenQueuesAreBelowCaps();
            BudgetSkipsReadbackQueueAtCap();
            BudgetSkipsEncodeQueueAtCap();
            BudgetSkipsCompletedQueueAtCap();
            BudgetSkipsPixelBudgetWithoutRateInput();
            DropOldestQueueKeepsNewestFrames();
            MonotonicPolicyDropsLateResults();
            ManagedJpegEncoderProducesJpegBytes();
            ManagedJpegEncoderHandlesVgaFramesWithoutWorkerStarvation();
            CameraPublisherWiresAsyncJpegWithoutUnityWorkerApis();
            CameraPublisherFlipsUnityReadbackRowsForAsyncJpeg();
            CameraPublisherCarriesCaptureDimensionsAcrossReadback();

            Console.WriteLine($"Phase 138J: {_passed} checks passed.");
        }

        /// <summary>
        /// Confirms capture is allowed when queue counters are below caps.
        /// </summary>
        private static void BudgetAllowsCaptureWhenQueuesAreBelowCaps()
        {
            var result = CameraFrameBudgetPolicy.Evaluate(new CameraFrameBudgetInput
            {
                PendingReadbacks = 0,
                MaxPendingReadbacks = 1,
                EncodeQueueDepth = 0,
                MaxEncodeQueueDepth = 2,
                CompletedQueueDepth = 0,
                MaxCompletedQueueDepth = 2,
                Width = 640,
                Height = 480,
                MaxPixelsPerFrame = 0
            });

            Check(result.AllowCapture, "138J-1A: policy allows capture below every cap");
            Check(result.SkipReason == CameraFrameBudgetSkipReason.None, "138J-1A2: no skip reason when allowed");
        }

        /// <summary>
        /// Confirms readback-queue limit blocks capture at cap.
        /// </summary>
        private static void BudgetSkipsReadbackQueueAtCap()
        {
            var result = CameraFrameBudgetPolicy.Evaluate(DefaultBudgetInput(pendingReadbacks: 1));
            Check(!result.AllowCapture, "138J-1B: readback queue at cap skips capture");
            Check(result.SkipReason == CameraFrameBudgetSkipReason.ReadbackQueueFull, "138J-1B2: readback skip is classified");
        }

        /// <summary>
        /// Confirms encode-queue limit blocks capture at cap.
        /// </summary>
        private static void BudgetSkipsEncodeQueueAtCap()
        {
            var result = CameraFrameBudgetPolicy.Evaluate(DefaultBudgetInput(encodeQueueDepth: 2));
            Check(!result.AllowCapture, "138J-1C: encode queue at cap skips capture");
            Check(result.SkipReason == CameraFrameBudgetSkipReason.EncodeQueueFull, "138J-1C2: encode skip is classified");
        }

        /// <summary>
        /// Confirms completed-queue limit blocks capture at cap.
        /// </summary>
        private static void BudgetSkipsCompletedQueueAtCap()
        {
            var result = CameraFrameBudgetPolicy.Evaluate(DefaultBudgetInput(completedQueueDepth: 2));
            Check(!result.AllowCapture, "138J-1D: completed queue at cap skips capture");
            Check(result.SkipReason == CameraFrameBudgetSkipReason.CompletedQueueFull, "138J-1D2: completed skip is classified");
        }

        /// <summary>
        /// Confirms pixel budget rejects oversized frames.
        /// </summary>
        private static void BudgetSkipsPixelBudgetWithoutRateInput()
        {
            var result = CameraFrameBudgetPolicy.Evaluate(DefaultBudgetInput(maxPixelsPerFrame: 10));
            Check(!result.AllowCapture, "138J-1E: pixel budget skips oversized capture");
            Check(result.SkipReason == CameraFrameBudgetSkipReason.PixelBudgetExceeded, "138J-1E2: pixel skip is classified");
        }

        /// <summary>
        /// Verifies bounded queue drops oldest item when overflowing.
        /// </summary>
        private static void DropOldestQueueKeepsNewestFrames()
        {
            var queue = new DropOldestBoundedQueue<int>(capacity: 2);

            Check(queue.Enqueue(1) == false, "138J-2A: first enqueue does not drop");
            Check(queue.Enqueue(2) == false, "138J-2B: second enqueue fills queue without drop");
            Check(queue.Enqueue(3), "138J-2C: third enqueue drops oldest");
            Check(queue.Count == 2, "138J-2D: bounded queue stays at cap");
            Check(queue.TryDequeue(out var first) && first == 2, "138J-2E: oldest item was dropped");
            Check(queue.TryDequeue(out var second) && second == 3, "138J-2F: newest item is retained");
        }

        /// <summary>
        /// Verifies monotonicity policy for async JPEG publish order.
        /// </summary>
        private static void MonotonicPolicyDropsLateResults()
        {
            ulong lastPublished = 100;

            Check(!CameraJpegPublishOrderPolicy.ShouldPublish(100, lastPublished), "138J-3A: equal timestamp is dropped");
            Check(!CameraJpegPublishOrderPolicy.ShouldPublish(99, lastPublished), "138J-3B: older timestamp is dropped");
            Check(CameraJpegPublishOrderPolicy.ShouldPublish(101, lastPublished), "138J-3C: newer timestamp publishes");
        }

        /// <summary>
        /// Encodes a tiny RGB24 sample and verifies JPEG framing markers.
        /// </summary>
        private static void ManagedJpegEncoderProducesJpegBytes()
        {
            var rgb = new byte[]
            {
                255, 0, 0,
                0, 255, 0,
                0, 0, 255,
                255, 255, 255
            };

            var encoded = ManagedJpegEncoder.EncodeRgb24(rgb, width: 2, height: 2, quality: 70, flipVertical: false);
            Check(encoded.Length > 32, "138J-4A: managed JPEG encoder returns payload bytes");
            Check(encoded[0] == 0xff && encoded[1] == 0xd8, "138J-4B: JPEG starts with SOI marker");
            Check(encoded[encoded.Length - 2] == 0xff && encoded[encoded.Length - 1] == 0xd9, "138J-4C: JPEG ends with EOI marker");
            Check(encoded.Contains((byte)0xc0) && encoded.Contains((byte)0xda), "138J-4D: JPEG contains SOF0 and SOS markers");
        }

        /// <summary>
        /// Verifies VGA input can be encoded within time budget.
        /// </summary>
        private static void ManagedJpegEncoderHandlesVgaFramesWithoutWorkerStarvation()
        {
            const int width = 640;
            const int height = 480;
            var rgb = new byte[width * height * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * 3;
                    rgb[offset] = (byte)(x & 0xff);
                    rgb[offset + 1] = (byte)(y & 0xff);
                    rgb[offset + 2] = (byte)((x + y) & 0xff);
                }
            }

            var sw = Stopwatch.StartNew();
            var encoded = ManagedJpegEncoder.EncodeRgb24(rgb, width, height, quality: 70, flipVertical: false);
            sw.Stop();

            Check(encoded.Length > 1024, "138J-4E: managed JPEG encoder returns VGA payload bytes");
            Check(sw.ElapsedMilliseconds < 2000,
                $"138J-4F: managed JPEG encoder avoids worker starvation at VGA ({sw.ElapsedMilliseconds}ms)");
        }

        /// <summary>
        /// Verifies async JPEG path does not call Unity APIs from worker thread.
        /// </summary>
        private static void CameraPublisherWiresAsyncJpegWithoutUnityWorkerApis()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Check(source.Contains("CameraFrameBudgetPolicy.Evaluate", StringComparison.Ordinal),
                "138J-5A: camera scheduler uses CameraFrameBudgetPolicy");
            Check(source.Contains("ManagedJpegEncoder.EncodeRgb24", StringComparison.Ordinal),
                "138J-5B: camera JPEG worker uses the Unity-free encoder");
            Check(source.Contains("lastPublishedCaptureUnixNs", StringComparison.Ordinal),
                "138J-5C: camera tracks monotonic JPEG publish order");
            Check(source.Contains("Convert.ToBase64String", StringComparison.Ordinal)
                  && source.Contains("serializeMs", StringComparison.Ordinal),
                "138J-5D: JSON/base64 path remains visible to CameraDiag");

            var workerStart = source.IndexOf("EncodeJpegWorkerLoop", StringComparison.Ordinal);
            if (workerStart >= 0)
            {
                var workerBlock = source.Substring(workerStart);
                Check(!workerBlock.Contains("Texture2D", StringComparison.Ordinal)
                      && !workerBlock.Contains("ImageConversion", StringComparison.Ordinal)
                      && !workerBlock.Contains("UnityEngine.", StringComparison.Ordinal),
                    "138J-5E: worker loop avoids Unity APIs");
            }
            else
            {
                throw new Exception("[FAIL] 138J-5E: camera worker loop exists");
            }
        }

        /// <summary>
        /// Verifies the async JPEG request explicitly flips Unity readback row orientation.
        /// </summary>
        private static void CameraPublisherFlipsUnityReadbackRowsForAsyncJpeg()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var encodeRequestStart = source.IndexOf("private static JpegEncodeResult EncodeJpegRequest", StringComparison.Ordinal);
            var encoderCallStart = source.IndexOf("ManagedJpegEncoder.EncodeRgb24", encodeRequestStart, StringComparison.Ordinal);
            var encoderCallEnd = source.IndexOf(");", encoderCallStart, StringComparison.Ordinal);
            var encoderCall = encodeRequestStart >= 0 && encoderCallStart >= 0 && encoderCallEnd > encoderCallStart
                ? source.Substring(encoderCallStart, encoderCallEnd - encoderCallStart)
                : "";

            Check(encoderCall.Contains("flipVertical: true", StringComparison.Ordinal),
                "138J-6A: async JPEG path flips Unity readback rows before encoding");
        }

        /// <summary>
        /// Verifies captured dimensions are captured at render-time and preserved through readback.
        /// </summary>
        private static void CameraPublisherCarriesCaptureDimensionsAcrossReadback()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Check(source.Contains("var captureWidth = _captureRT.width", StringComparison.Ordinal)
                  && source.Contains("var captureHeight = _captureRT.height", StringComparison.Ordinal),
                "138J-7A: camera snapshots render texture dimensions before readback");
            Check(source.Contains("OnReadbackComplete(req, generation, renderUnixNs, captureWidth, captureHeight)", StringComparison.Ordinal),
                "138J-7B: readback callback carries captured dimensions");
            Check(source.Contains("QueueJpegFrame(req, renderUnixNs, captureWidth, captureHeight", StringComparison.Ordinal),
                "138J-7C: async JPEG queue uses captured dimensions");
            Check(source.Contains("Math.Max(1, captureWidth)", StringComparison.Ordinal)
                  && source.Contains("Math.Max(1, captureHeight)", StringComparison.Ordinal),
                "138J-7D: JPEG encode request stores captured dimensions");
        }

        /// <summary>
        /// Returns a shared baseline budget fixture for assertions.
        /// </summary>
        private static CameraFrameBudgetInput DefaultBudgetInput(
            int pendingReadbacks = 0,
            int encodeQueueDepth = 0,
            int completedQueueDepth = 0,
            int maxPixelsPerFrame = 0)
        {
            return new CameraFrameBudgetInput
            {
                PendingReadbacks = pendingReadbacks,
                MaxPendingReadbacks = 1,
                EncodeQueueDepth = encodeQueueDepth,
                MaxEncodeQueueDepth = 2,
                CompletedQueueDepth = completedQueueDepth,
                MaxCompletedQueueDepth = 2,
                Width = 640,
                Height = 480,
                MaxPixelsPerFrame = maxPixelsPerFrame
            };
        }

        /// <summary>
        /// Reads a file as plain text for source-level assertions.
        /// </summary>
        private static string Read(string path) => File.ReadAllText(path);

        /// <summary>
        /// Increments pass count when condition is true, otherwise throws.
        /// </summary>
        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
