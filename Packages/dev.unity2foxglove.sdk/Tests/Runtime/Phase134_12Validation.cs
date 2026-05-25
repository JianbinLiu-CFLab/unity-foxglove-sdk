// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-12 regression coverage for camera readback teardown policy.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_12Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-12: Protobuf Builders And Typed Publishers ===");
            _passed = 0;

            CameraPublisherDoesNotUseGlobalReadbackWait(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs",
                "primary camera publisher");
            CameraPublisherDoesNotUseGlobalReadbackWait(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs",
                "legacy compressed video publisher");

            Console.WriteLine($"Phase 134-12: {_passed} checks passed.");
        }

        private static void CameraPublisherDoesNotUseGlobalReadbackWait(string path, string label)
        {
            var source = File.ReadAllText(path);
            Check(!source.Contains("AsyncGPUReadback.WaitAllRequests()"),
                $"134-12A-1: {label} destroy path does not wait for global AsyncGPUReadback requests");
            Check(source.Contains("_cleanupWhenReadbacksDrain = _pendingRequests > 0;"),
                $"134-12A-2: {label} retains local pending-readback cleanup policy");
            Check(source.Contains("if (_pendingRequests == 0") && source.Contains("CleanupResources();"),
                $"134-12A-3: {label} still cleans resources immediately when no local readback is pending");
            Check(source.Contains("generation != _captureGeneration"),
                $"134-12A-4: {label} keeps generation guard for stale callbacks");
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
