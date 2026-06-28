// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-46 mid protocol and session validation review closure.

using System;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_46Validation
    {
        public static void Validate()
        {
            VerifySourceInspectionHardFails();
            VerifyProtocolEdgeHarness();
            VerifyBackpressureAndFoxRunChecks();
            VerifyOriginDocumentation();
            VerifyWiring();

            Console.WriteLine("Phase 163-46: mid protocol/session validation checks passed.");
        }

        private static void VerifySourceInspectionHardFails()
        {
            var phase32 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase32Validation.cs");
            Check(phase32.Contains("PhaseValidationSourceHelpers.ReadRequiredRepoText", StringComparison.Ordinal)
                  && !phase32.Contains("skipping source inspection", StringComparison.Ordinal),
                "163-46A-1: Phase32 source inspection uses required repo text instead of warning skip");

            var phase40 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase40Validation.cs");
            Check(phase40.Contains("PhaseValidationSourceHelpers.ReadRequiredRepoText", StringComparison.Ordinal)
                  && !phase40.Contains("skipping source inspection", StringComparison.Ordinal),
                "163-46A-2: Phase40 source inspection uses required repo text instead of warning skip");

            var helper = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationSourceHelpers.cs");
            Check(helper.Contains("FindRequiredRepoRoot", StringComparison.Ordinal)
                  && helper.Contains("ReadRequiredRepoText", StringComparison.Ordinal)
                  && helper.Contains("Missing repository file", StringComparison.Ordinal),
                "163-46A-3: shared source helper exposes hard-fail repo file reads");
        }

        private static void VerifyProtocolEdgeHarness()
        {
            var protocol = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/ProtocolEdgeHardeningValidation.cs");
            Check(protocol.Contains("ReceiveLoop private method exists", StringComparison.Ordinal)
                  && protocol.Contains("PhaseValidationSourceHelpers.RepoPath", StringComparison.Ordinal),
                "163-46B-1: protocol edge harness validates private ReceiveLoop and repo paths");
            Check(!protocol.Contains("public void Log(string message)", StringComparison.Ordinal),
                "163-46B-2: protocol edge capture logger matches IFoxgloveLogger surface");
        }

        private static void VerifyBackpressureAndFoxRunChecks()
        {
            var phase40 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase40Validation.cs");
            Check(phase40.Contains("TestZeroCooldownAllowsCaptureWithPressureObserved", StringComparison.Ordinal)
                  && !phase40.Contains("TestZeroCooldownSkipsOnceOnly", StringComparison.Ordinal),
                "163-46C-1: zero-cooldown backpressure test name matches behavior");

            var phase41 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase41Validation.cs");
            Check(phase41.Contains("FoxRunChangeHelper.FloatChanged(float.NaN", StringComparison.Ordinal)
                  && phase41.Contains("FoxRunChangeHelper.DoubleChanged(double.NaN", StringComparison.Ordinal),
                "163-46C-2: FoxRun NaN tests exercise float and double change helpers");
        }

        private static void VerifyOriginDocumentation()
        {
            var originGuard = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Abstractions/IOriginGuardedFoxgloveTransport.cs");
            var backend = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            Check(originGuard.Contains("file://", StringComparison.Ordinal)
                  && originGuard.Contains("opaque <c>null</c>", StringComparison.Ordinal)
                  && backend.Contains("local file origins are accepted", StringComparison.Ordinal),
                "163-46D-1: origin allowlist docs mention local file-origin bypass");
        }

        private static void VerifyWiring()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_46Validation.cs", StringComparison.Ordinal),
                "163-46E-1: runtime test project compiles Phase163_46Validation");
            Check(registry.Contains("--phase163-46", StringComparison.Ordinal)
                  && registry.Contains("Phase163_46Validation.Validate", StringComparison.Ordinal),
                "163-46E-2: validation registry exposes --phase163-46");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
        }
    }
}
