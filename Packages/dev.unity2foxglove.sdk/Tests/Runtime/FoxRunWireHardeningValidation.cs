// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for Phase182 FoxRun wire-hardening evidence classification and CI wiring.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunWireHardeningValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Wire Hardening Structural Tests ---");
            _passed = 0;

            VerifyRegistryClassification();
            VerifyExplicitCompileSurface();
            VerifyPanelBehaviorCiWiring();

            Console.WriteLine("FoxRun wire hardening: " + _passed + " checks passed.\n");
        }

        private static void VerifyRegistryClassification()
        {
            var phase182 = PhaseValidationRegistry.All.Where(item => item.Flag == "--phase182").ToArray();
            var phase176 = PhaseValidationRegistry.All.Where(item => item.Flag == "--phase176").ToArray();

            Check(phase182.Length == 1
                  && phase182[0].Name == "FoxRun publish and inbound wire hardening"
                  && phase182[0].Category == ValidationCategory.CiSafe
                  && phase182[0].IncludeInDefault
                  && phase182[0].Evidence == ValidationEvidence.Structural
                  && phase176.Length == 1
                  && phase176[0].Evidence == ValidationEvidence.Structural,
                "182S-1: Phase176 and the single CI-safe Phase182 gate are classified as structural evidence only");
        }

        private static void VerifyExplicitCompileSurface()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(project.Contains("<Compile Include=\"FoxRunWireHardeningValidation.cs\" />", StringComparison.Ordinal),
                "182S-2: the runtime harness explicitly compiles the Phase182 structural guard");
        }

        private static void VerifyPanelBehaviorCiWiring()
        {
            var runCi = ReadRepoText("Scripts/release/run_ci.py");
            var workflow = ReadRepoText(".github/workflows/dotnet-tests.yml");

            Check(runCi.Contains("CiJob(\"foxrun-publish-panel\"", StringComparison.Ordinal)
                  && runCi.Contains("args.only in (None, \"foxrun-publish-panel\")", StringComparison.Ordinal)
                  && runCi.Contains("foxrun_publish_panel_npm(\"ci\")", StringComparison.Ordinal)
                  && runCi.Contains("foxrun_publish_panel_npm(\"run\", \"typecheck\")", StringComparison.Ordinal)
                  && runCi.Contains("foxrun_publish_panel_npm(\"test\")", StringComparison.Ordinal)
                  && workflow.Contains("- name: Run FoxRun publish panel behavior tests", StringComparison.Ordinal)
                  && workflow.Contains("working-directory: Tools/foxglove-extensions/foxrun-publish-panel", StringComparison.Ordinal)
                  && workflow.Contains("npm ci", StringComparison.Ordinal)
                  && workflow.Contains("npm run typecheck", StringComparison.Ordinal)
                  && workflow.Contains("npm test", StringComparison.Ordinal),
                "182S-3: local and remote CI run the panel lockfile install, typecheck, and Vitest behavior commands");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
