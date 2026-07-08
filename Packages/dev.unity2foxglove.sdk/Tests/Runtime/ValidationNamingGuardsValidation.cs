// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Ensures validation naming guardrails prevent new phase-only names and filenames.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for validation naming guardrails.</summary>
    internal static class ValidationNamingGuardsValidation
    {
        private static int _passed;

        /// <summary>Runs validation naming guardrail checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Validation Naming Guardrails Tests ---");
            _passed = 0;

            VerifyRegistryRejectsNewPhaseOnlyNames();
            VerifyPackageValidatorRejectsNewPhasePrefixedFiles();
            VerifyRegressionTestsCoverPackageValidatorNaming();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Validation naming guardrails: {_passed} checks passed.");
        }

        private static void VerifyRegistryRejectsNewPhaseOnlyNames()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(registry.Contains("private static readonly Regex PhaseOnlyNamePattern", StringComparison.Ordinal)
                  && registry.Contains("@\"^Phase \\d+[A-Za-z]*(?:-\\d+)?$\"", StringComparison.Ordinal),
                "164-59A-1: registry defines phase-only validation name pattern");
            Check(registry.Contains("private static readonly HashSet<string> LegacyPhaseOnlyNames", StringComparison.Ordinal)
                  && registry.Contains("\"Phase 164-54\"", StringComparison.Ordinal)
                  && !registry.Contains("\"Phase 164-59\"", StringComparison.Ordinal),
                "164-59A-2: registry allowlists only known legacy phase-only names");
            Check(registry.Contains("Validation name must be descriptive, not just a phase number", StringComparison.Ordinal)
                  && registry.Contains("&& !LegacyPhaseOnlyNames.Contains(item.Name)", StringComparison.Ordinal),
                "164-59A-3: registry static constructor rejects new phase-only names");
        }

        private static void VerifyPackageValidatorRejectsNewPhasePrefixedFiles()
        {
            var validator = ReadRepoText("Scripts/package/validate_unity_package.py");
            var namingCheck = PythonFunction(validator, "def check_validation_naming(");
            var main = PythonFunction(validator, "def main() -> int:");

            Check(validator.Contains("VALIDATION_PHASE_FILENAME_RE", StringComparison.Ordinal)
                  && validator.Contains("VALIDATION_PHASE_FILENAME_INDEX_RE", StringComparison.Ordinal)
                  && validator.Contains("LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE = 164", StringComparison.Ordinal)
                  && validator.Contains("LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX = 58", StringComparison.Ordinal),
                "164-59B-1: package validator declares validation filename cutoff constants");
            Check(namingCheck.Contains("runtime validation source filenames are descriptive", StringComparison.Ordinal)
                  && namingCheck.Contains("index >= LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX", StringComparison.Ordinal),
                "164-59B-2: package validator rejects new Phase-number-prefixed validation filenames");
            Check(main.Contains("package_files = [path for path in package_entries if path.is_file()]", StringComparison.Ordinal)
                  && main.Contains("check_validation_naming(results, package_files)", StringComparison.Ordinal),
                "164-59B-3: package validator runs validation filename guard from the main release check");
        }

        private static void VerifyRegressionTestsCoverPackageValidatorNaming()
        {
            var tests = ReadRepoText("Scripts/package/regression_checks/test_validate_unity_package.py");

            Check(tests.Contains("test_validation_naming_allows_legacy_phase_files", StringComparison.Ordinal)
                  && tests.Contains("Phase164_57Validation.cs", StringComparison.Ordinal),
                "164-59C-1: package validator regression test preserves legacy filename allowance");
            Check(tests.Contains("test_validation_naming_rejects_new_phase_files", StringComparison.Ordinal)
                  && tests.Contains("Phase164_59Validation.cs", StringComparison.Ordinal),
                "164-59C-2: package validator regression test rejects new Phase-prefixed filenames");
            Check(tests.Contains("Phase164_59FooValidation.cs", StringComparison.Ordinal),
                "164-59C-3: package validator regression test rejects suffixed new Phase-prefixed filenames");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-59\", \"Validation naming guardrails\", ValidationNamingGuardsValidation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && PhaseValidationRegistry.Find(new[] { "--phase164-59" }) != null,
                "164-59D-1: validation registry exposes descriptive Phase164-59 guard");
            Check(project.Contains("ValidationNamingGuardsValidation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase164_59Validation.cs", StringComparison.Ordinal),
                "164-59D-2: runtime validation project compiles descriptive validation guard file");
        }

        private static string PythonFunction(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("[FAIL] missing Python function: " + signature);

            var next = source.IndexOf("\n\ndef ", start + signature.Length, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = TestRepoRootLocator.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
