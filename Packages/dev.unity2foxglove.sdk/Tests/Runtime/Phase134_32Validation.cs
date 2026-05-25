// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-32 regression coverage for optional R2FU adapter test harness isolation.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_32Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-32: Runtime Test Harness ===");
            _passed = 0;

            VerifyOptionalAdapterCompileGate();
            VerifyAdapterFacadeValidationsUseReflection();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 134-32: {_passed} checks passed.");
        }

        private static void VerifyOptionalAdapterCompileGate()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(project.Contains("<IncludeRos2ForUnityAdapter Condition=\"'$(IncludeRos2ForUnityAdapter)' == ''\">false</IncludeRos2ForUnityAdapter>", StringComparison.Ordinal),
                "134-32A-1: test project defaults optional R2FU adapter compilation to false");
            Check(project.Contains("../../../dev.unity2foxglove.ros2forunity/Runtime/**/*.cs", StringComparison.Ordinal)
                  && project.Contains("Condition=\"'$(IncludeRos2ForUnityAdapter)' == 'true' and Exists('../../../dev.unity2foxglove.ros2forunity/Runtime')\"", StringComparison.Ordinal),
                "134-32A-2: optional R2FU adapter sources compile only behind explicit property and path check");
            Check(!project.Contains("<Compile Include=\"../../../dev.unity2foxglove.ros2forunity/Runtime/**/*.cs\" LinkBase=\"Ros2ForUnityRuntime\" />", StringComparison.Ordinal),
                "134-32A-3: test project no longer unconditionally compiles optional R2FU adapter sources");
        }

        private static void VerifyAdapterFacadeValidationsUseReflection()
        {
            var phase108 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs");
            var phase13421 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_21Validation.cs");

            Check(!phase108.Contains("using Unity2Foxglove.Ros2ForUnity;", StringComparison.Ordinal)
                  && !phase13421.Contains("using Unity2Foxglove.Ros2ForUnity;", StringComparison.Ordinal),
                "134-32B-1: facade behavior validations do not directly import optional R2FU namespace");
            Check(phase108.Contains("FindType(\"Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory\")", StringComparison.Ordinal)
                  && phase13421.Contains("FactoryTypeName = \"Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory\"", StringComparison.Ordinal),
                "134-32B-2: facade behavior validations discover optional runtime types by reflection");
            Check(!phase108.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal)
                  && !phase13421.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal),
                "134-32B-3: facade behavior validations avoid compile-time factory calls");
            Check(phase108.Contains("skipped unless IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && phase13421.Contains("skipped unless IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal),
                "134-32B-4: facade behavior validations document adapter-enabled runtime gate");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("<Compile Include=\"Phase134_32Validation.cs\" />", StringComparison.Ordinal),
                "134-32C-1: Phase134_32Validation is compiled by the runtime test project");
            Check(registry.Contains("Ci(\"--phase134-32\", \"Phase 134-32\", Phase134_32Validation.Validate)", StringComparison.Ordinal),
                "134-32C-2: Phase134_32Validation is wired into the validation registry");
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
