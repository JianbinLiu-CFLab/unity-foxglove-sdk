// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-49 review closure for FoxRun/schema validation hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_49Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            var phase81 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase81Validation.cs");
            Check(phase81.Contains("ErrorContains(args, 5, \"even\")", StringComparison.Ordinal)
                  && phase81.Contains("ComputeU(127, 127, 127)", StringComparison.Ordinal)
                  && phase81.Contains("ComputeV(127, 127, 127)", StringComparison.Ordinal),
                "163-49A-1: Phase81 validates I420 chroma and null-safe converter errors");

            var phase82 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase82Validation.cs");
            Check(phase82.Contains("Native H.264 produced zero access units", StringComparison.Ordinal)
                  && phase82.Contains("OrderBy(field => field.Name", StringComparison.Ordinal)
                  && phase82.Contains("82C-31-\" + count.ToString(\"D2\")", StringComparison.Ordinal),
                "163-49A-2: Phase82 reports zero-output native smoke and stable GUID checks");

            var phase83 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase83Validation.cs");
            var phase84 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase84Validation.cs");
            Check(phase83.Contains("missing source marker", StringComparison.Ordinal)
                  && phase84.Contains("missing source marker", StringComparison.Ordinal)
                  && !phase83.Contains("return string.Empty;", StringComparison.Ordinal)
                  && !phase84.Contains("return string.Empty;", StringComparison.Ordinal),
                "163-49B-1: Phase83 and Phase84 fail explicitly when source markers disappear");

            var phase86 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase86Validation.cs");
            Check(phase86.Contains("PhaseValidationSourceHelpers.SourceMethod(source, \"private void Stop(\")", StringComparison.Ordinal)
                  && phase86.Contains("Ordered(stopMethod, \"WaitForTask(_stderrTask\", \"process.Dispose()\")", StringComparison.Ordinal),
                "163-49B-2: Phase86 sidecar lifecycle ordering is scoped to Stop");

            var phase91 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase91Validation.cs");
            Check(phase91.Contains("PointCloudQoS.ComputePackedStride(frame)", StringComparison.Ordinal)
                  && phase91.Contains("expected stride ", StringComparison.Ordinal)
                  && phase91.Contains("got \" + actualStride", StringComparison.Ordinal),
                "163-49C-1: Phase91 derives PointCloud stride from shared packing policy");

            var phase94 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase94Validation.cs");
            Check(phase94.Contains("94B-6a: oversized-payload validation stays within a safe allocation threshold", StringComparison.Ordinal)
                  && phase94.Contains("94B-6b: frame writer rejects oversized payloads", StringComparison.Ordinal),
                "163-49C-2: Phase94 guards oversized test allocations before allocating");

            var phase98 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase98Validation.cs");
            Check(phase98.Contains("foxglove_msgs/msg/IMU", StringComparison.Ordinal)
                  && phase98.Contains("sensor_msgs/msg/PointCloud2", StringComparison.Ordinal)
                  && phase98.Contains("foxglove_msgs/msg/CompressedPointCloud", StringComparison.Ordinal)
                  && phase98.Contains("foxglove_msgs/msg/CameraCalibration", StringComparison.Ordinal),
                "163-49D-1: Phase98 covers acronym and numeric schema topic conversion");

            var phase99 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase99Validation.cs");
            Check(phase99.Contains("var stdoutTask = process.StandardOutput.ReadToEndAsync();", StringComparison.Ordinal)
                  && phase99.Contains("var stderrTask = process.StandardError.ReadToEndAsync();", StringComparison.Ordinal)
                  && phase99.IndexOf("ReadToEndAsync();", StringComparison.Ordinal) < phase99.IndexOf("process.WaitForExit(3000)", StringComparison.Ordinal),
                "163-49E-1: Phase99 drains child-process output before waiting for exit");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_49Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-49", StringComparison.Ordinal)
                  && registry.Contains("Phase163_49Validation.Validate", StringComparison.Ordinal),
                "163-49F-1: validation registry exposes --phase163-49");

            Console.WriteLine($"Phase 163-49: {_passed} FoxRun/schema validation checks passed.");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidDataException("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
