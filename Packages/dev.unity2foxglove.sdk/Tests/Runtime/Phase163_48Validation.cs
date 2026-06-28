// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-48 review closure for transport/runtime-control validation hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_48Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            var phase65 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase65Validation.cs");
            Check(phase65.Contains("InterleavedPlaybackControlBurstsKeepRequestIdsPerClient", StringComparison.Ordinal)
                  && phase65.Contains("desktop-mixed-0", StringComparison.Ordinal)
                  && phase65.Contains("web-mixed-0", StringComparison.Ordinal),
                "163-48A-1: Phase65 covers interleaved multi-client playback-control bursts");

            var phase67 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase67Validation.cs");
            Check(phase67.Contains("public void Disconnect(uint clientId)", StringComparison.Ordinal)
                  && phase67.Contains("disconnected clients do not receive later status broadcasts", StringComparison.Ordinal),
                "163-48A-2: Phase67 fake transport can simulate disconnects");

            var phase68 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase68Validation.cs");
            Check(phase68.Contains("[FAIL] 68E-7", StringComparison.Ordinal)
                  && phase68.Contains("catch (IOException ex)", StringComparison.Ordinal),
                "163-48B-1: Phase68 reports invalid-file handle leaks as labeled failures");

            var phase70 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase70Validation.cs");
            var phase74 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase74Validation.cs");
            Check(phase70.Contains("throw new Exception(\"[FAIL] \" + name)", StringComparison.Ordinal)
                  && phase74.Contains("throw new Exception(\"[FAIL] \" + name)", StringComparison.Ordinal),
                "163-48B-2: Phase70 and Phase74 assertion failures carry standard labels");

            var phase72 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase72Validation.cs");
            var phase73 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase73Validation.cs");
            Check(phase72.Contains("IsByRefLike", StringComparison.Ordinal)
                  && phase73.Contains("73C-7: clearing MCAP recorder demand", StringComparison.Ordinal),
                "163-48C-1: cadence and demand lifecycle validations expose fragile assumptions");

            var phase75 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase75Validation.cs");
            var phase77 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase77Validation.cs");
            Check(phase75.Contains("\"FoxgloveCameraPublisher.cs\"", StringComparison.Ordinal)
                  && phase75.Contains("\"FoxgloveCameraPublisher.Video.cs\"", StringComparison.Ordinal)
                  && !phase75.Contains("Directory.GetFiles(dir, \"FoxgloveCameraPublisher*.cs\")", StringComparison.Ordinal)
                  && phase77.Contains("package.json", StringComparison.Ordinal)
                  && !phase77.Contains("Directory.Exists(Path.Combine(dir, \".git\"))", StringComparison.Ordinal),
                "163-48D-1: source-shape validations use exact files and package-root anchors");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_48Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-48", StringComparison.Ordinal)
                  && registry.Contains("Phase163_48Validation.Validate", StringComparison.Ordinal),
                "163-48E-1: validation registry exposes --phase163-48");

            Console.WriteLine($"Phase 163-48: {_passed} transport/runtime-control review checks passed.");
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
