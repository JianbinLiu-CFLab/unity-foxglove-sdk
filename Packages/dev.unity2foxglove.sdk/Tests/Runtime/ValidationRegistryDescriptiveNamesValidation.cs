// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Ensures validation registry display names describe validated behavior.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for readable validation registry names.</summary>
    internal static class ValidationRegistryDescriptiveNamesValidation
    {
        private static readonly Regex RegistryEntryPattern = new Regex(
            "(?:Ci|Local|Manual)\\(\"(?<flag>--phase[^\"]+)\",\\s*\"(?<name>[^\"]+)\",\\s*(?<class>[A-Za-z0-9_]+)Validation\\.Validate",
            RegexOptions.Compiled);

        private static readonly Regex PlainPhaseNamePattern = new Regex(
            "^Phase [0-9A-Za-z_-]+$",
            RegexOptions.Compiled);

        private static int _passed;

        /// <summary>Runs the descriptive registry-name validation.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Validation Registry Descriptive Names Tests ---");
            _passed = 0;

            VerifyCleanPurposeEntriesUseDescriptiveNames();
            VerifyNewValidationUsesDescriptiveFileAndRegistryName();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Validation registry descriptive names: {_passed} checks passed.");
        }

        private static void VerifyCleanPurposeEntriesUseDescriptiveNames()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var cleanPurposeEntries = 0;
            var plainEntriesWithoutCleanPurpose = 0;
            string firstPlainCleanPurposeEntry = null;

            foreach (Match match in RegistryEntryPattern.Matches(registry))
            {
                var name = match.Groups["name"].Value;
                var className = match.Groups["class"].Value + "Validation";
                var purpose = TryReadCleanPurpose(className);
                if (!string.IsNullOrEmpty(purpose))
                {
                    cleanPurposeEntries++;
                    if (PlainPhaseNamePattern.IsMatch(name) && firstPlainCleanPurposeEntry == null)
                        firstPlainCleanPurposeEntry = match.Groups["flag"].Value;
                }
                else if (PlainPhaseNamePattern.IsMatch(name))
                {
                    plainEntriesWithoutCleanPurpose++;
                }
            }

            Check(cleanPurposeEntries >= 200,
                $"164-58A-1: audited {cleanPurposeEntries} registry entries with clean Purpose metadata");
            Check(firstPlainCleanPurposeEntry == null,
                "164-58A-2: clean Purpose-backed registry entries use descriptive Names"
                + (firstPlainCleanPurposeEntry == null ? string.Empty : " (first plain entry: " + firstPlainCleanPurposeEntry + ")"));
            Check(plainEntriesWithoutCleanPurpose > 0,
                $"164-58A-3: plain legacy names remain only where clean Purpose metadata is unavailable ({plainEntriesWithoutCleanPurpose} entries)");
        }

        private static void VerifyNewValidationUsesDescriptiveFileAndRegistryName()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-58\", \"Validation registry descriptive names\", ValidationRegistryDescriptiveNamesValidation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && !registry.Contains("Phase164_58Validation.Validate", StringComparison.Ordinal),
                "164-58B-1: new validation registry entry uses a descriptive display name and class");
            Check(project.Contains("ValidationRegistryDescriptiveNamesValidation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase164_58Validation.cs", StringComparison.Ordinal),
                "164-58B-2: new validation file uses a descriptive filename instead of a Phase-prefixed filename");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            Check(PhaseValidationRegistry.Find(new[] { "--phase164-58" }) != null,
                "164-58C-1: validation registry resolves --phase164-58");
        }

        private static string TryReadCleanPurpose(string className)
        {
            var relativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + className + ".cs";
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                return null;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < Math.Min(lines.Length, 12); i++)
            {
                var marker = lines[i].IndexOf("Purpose:", StringComparison.Ordinal);
                if (marker < 0)
                    continue;

                var purpose = lines[i].Substring(marker + "Purpose:".Length).Trim().TrimEnd('.');
                return IsCleanAscii(purpose) ? purpose : null;
            }

            return null;
        }

        private static bool IsCleanAscii(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] < 32 || value[i] > 126)
                    return false;
            }

            return true;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
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
