// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-25 regression coverage for experimental OpenH264 probe hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_25Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-25: Unity Demo Experimental Editor Scripts ===");
            _passed = 0;

            VerifyProbePublisher();
            VerifyProbeSidecarOptions();

            Console.WriteLine($"Phase 134-25: {_passed} checks passed.");
        }

        private static void VerifyProbePublisher()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            Check(!source.Contains("AsyncGPUReadback.WaitAllRequests()", StringComparison.Ordinal),
                "134-25A-1: experimental OpenH264 probe does not wait for global GPU readback requests");
            Check(source.Contains("_captureGeneration++", StringComparison.Ordinal)
                  && source.Contains("generation != _captureGeneration", StringComparison.Ordinal),
                "134-25A-2: experimental OpenH264 probe guards stale readback callbacks by generation");
            Check(source.Contains("_cleanupWhenReadbacksDrain = _pendingRequests > 0;", StringComparison.Ordinal)
                  && source.Contains("CompletePendingReadback()", StringComparison.Ordinal),
                "134-25A-3: experimental OpenH264 probe delays cleanup only for its own pending readbacks");
            Check(source.Contains("Range(2, OpenH264ProbeSidecarOptions.MaxDimension)", StringComparison.Ordinal),
                "134-25A-4: experimental OpenH264 probe exposes bounded width and height in the Inspector");
            Check(source.Contains("TryGetProbeFrameLayout", StringComparison.Ordinal)
                  && source.Contains("new byte[i420Bytes]", StringComparison.Ordinal)
                  && !source.Contains("new byte[width * height * 3 / 2]", StringComparison.Ordinal),
                "134-25A-5: experimental OpenH264 probe computes checked frame size before allocation");
            Check(source.Contains("EnsureCaptureResources(width, height)", StringComparison.Ordinal)
                  && !source.Contains("EnsureCaptureResources();", StringComparison.Ordinal),
                "134-25A-6: experimental OpenH264 probe validates dimensions before creating capture resources");
            Check(source.Contains("Rgb24ToI420Converter.TryConvertRgb24ToI420", StringComparison.Ordinal)
                  && !source.Contains("ComputeY(", StringComparison.Ordinal)
                  && !source.Contains("GetVerticallyFlippedRgbIndex", StringComparison.Ordinal),
                "134-25A-7: experimental OpenH264 probe delegates RGB24-to-I420 conversion to the shared SDK converter");
        }

        private static void VerifyProbeSidecarOptions()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            Check(source.Contains("public const int MaxDimension", StringComparison.Ordinal)
                  && source.Contains("public const int MaxFrameBytes", StringComparison.Ordinal),
                "134-25B-1: experimental OpenH264 sidecar declares explicit dimension and frame-byte budgets");
            Check(source.Contains("TryComputeFrameByteCount", StringComparison.Ordinal)
                  && source.Contains("(long)width * height", StringComparison.Ordinal)
                  && source.Contains("bytes > MaxFrameBytes", StringComparison.Ordinal),
                "134-25B-2: experimental OpenH264 sidecar uses checked long frame-size math");
            Check(source.Contains("if (!TryComputeFrameByteCount(Width, Height, out _, out error))", StringComparison.Ordinal),
                "134-25B-3: experimental OpenH264 sidecar validates frame budget before process start");
            Check(!source.Contains("Width * Height * 3 / 2", StringComparison.Ordinal),
                "134-25B-4: experimental OpenH264 sidecar no longer exposes unchecked int frame-size math");
            Check(source.Contains("length <= 0 || length > MaxAccessUnitBytes", StringComparison.Ordinal),
                "134-25B-5: experimental OpenH264 sidecar rejects negative, zero, and oversized access-unit lengths");
            Check(source.Contains("WaitForWorkerTasks", StringComparison.Ordinal)
                  && source.Contains("stop?.Dispose();", StringComparison.Ordinal)
                  && source.IndexOf("WaitForWorkerTasks", StringComparison.Ordinal) < source.IndexOf("stop?.Dispose();", StringComparison.Ordinal),
                "134-25B-6: experimental OpenH264 sidecar waits for worker tasks before disposing cancellation state");
            Check(source.Contains("FramesSubmitted => Volatile.Read", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Increment(ref _framesSubmitted)", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Increment(ref _droppedInputFrames)", StringComparison.Ordinal)
                  && source.Contains("Interlocked.Increment(ref _accessUnitsReceived)", StringComparison.Ordinal),
                "134-25B-7: experimental OpenH264 sidecar counters use atomic updates and volatile reads");
            Check(source.Contains("public string HelperExecutablePath { get; set; }", StringComparison.Ordinal)
                  && source.Contains("public int MaxOutputQueue { get; set; }", StringComparison.Ordinal),
                "134-25B-8: experimental OpenH264 sidecar options expose property setters instead of mutable public fields");

            var build = ReadRepoText("Unity2Foxglove/Assets/Editor/FoxgloveBuild.cs");
            Check(build.Contains("!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)", StringComparison.Ordinal),
                "134-25C-1: Unity IL2CPP build helper handles bare output filenames without creating a null directory");
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
