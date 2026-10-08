// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Central registry for CI-safe, local-evidence, and explicit phase validations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Build-time registry of all phase-level validation entry points used by the
    /// local validation harness and CI pipeline.
    /// </summary>
    internal static partial class PhaseValidationRegistry
    {
        private static readonly IReadOnlyDictionary<string, PhaseValidationCase> FlagIndex;

        private static readonly Regex PhaseOnlyNamePattern = new Regex(
            @"^Phase \d+[A-Za-z]*(?:-\d+)?$",
            RegexOptions.Compiled);

        private static readonly HashSet<string> LegacyPhaseOnlyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Phase 28",
            "Phase 31",
            "Phase 36",
            "Phase 137G",
            "Phase 164-4",
            "Phase 164-5",
            "Phase 164-6",
            "Phase 164-7",
            "Phase 164-8",
            "Phase 164-9",
            "Phase 164-10",
            "Phase 164-11",
            "Phase 164-12",
            "Phase 164-13",
            "Phase 164-14",
            "Phase 164-15",
            "Phase 164-16",
            "Phase 164-17",
            "Phase 164-18",
            "Phase 164-19",
            "Phase 164-20",
            "Phase 164-21",
            "Phase 164-22",
            "Phase 164-23",
            "Phase 164-24",
            "Phase 164-25",
            "Phase 164-26",
            "Phase 164-27",
            "Phase 164-28",
            "Phase 164-29",
            "Phase 164-30",
            "Phase 164-31",
            "Phase 164-32",
            "Phase 164-33",
            "Phase 164-34",
            "Phase 164-35",
            "Phase 164-36",
            "Phase 164-37",
            "Phase 164-38",
            "Phase 164-39",
            "Phase 164-40",
            "Phase 164-41",
            "Phase 164-42",
            "Phase 164-43",
            "Phase 164-44",
            "Phase 164-45",
            "Phase 164-46",
            "Phase 164-47",
            "Phase 164-48",
            "Phase 164-49",
            "Phase 164-50",
            "Phase 164-51",
            "Phase 164-52",
            "Phase 164-53",
            "Phase 164-54",
        };

        public static IEnumerable<PhaseValidationCase> DefaultValidations(bool includeLocalEvidence)
        {
            return All.Where(item =>
                item.IncludeInDefault
                && (item.Category == ValidationCategory.CiSafe
                    || (includeLocalEvidence && item.Category == ValidationCategory.LocalEvidence)));
        }

        /// <summary>
        /// Returns every validation classified as CI-safe, including entries
        /// intentionally omitted from the fast default sweep. This explicit
        /// view keeps the opt-in suites auditable without conflating them with
        /// manual or environment-dependent evidence lanes.
        /// </summary>
        public static IEnumerable<PhaseValidationCase> CiSafeValidations()
        {
            return All.Where(item => item.Category == ValidationCategory.CiSafe);
        }

        /// <summary>
        /// Finds the first validation case matching CLI args.
        /// </summary>
        public static PhaseValidationCase Find(IReadOnlyCollection<string> args)
        {
            foreach (var arg in args)
            {
                if (FlagIndex.TryGetValue(arg, out var validation))
                    return validation;
            }

            return null;
        }

        /// <summary>
        /// Finds all validation cases matching CLI args.
        /// </summary>
        public static IEnumerable<PhaseValidationCase> FindAll(IReadOnlyCollection<string> args)
        {
            var emitted = new HashSet<PhaseValidationCase>();
            foreach (var arg in args)
            {
                if (FlagIndex.TryGetValue(arg, out var validation) && emitted.Add(validation))
                    yield return validation;
            }
        }

        /// <summary>
        /// Builds a validation entry with default execution behavior.
        /// </summary>
        private static PhaseValidationCase DefaultOnly(
            string name,
            System.Action run,
            ValidationEvidence evidence)
        {
            return new PhaseValidationCase(
                null,
                name,
                ValidationCategory.CiSafe,
                evidence,
                run,
                includeInDefault: true);
        }

        /// <summary>
        /// Builds a CI-safe validation entry with a command flag.
        /// </summary>
        private static PhaseValidationCase Ci(
            string flag,
            string name,
            System.Action run,
            ValidationEvidence evidence,
            bool includeInDefault = true)
        {
            return new PhaseValidationCase(flag, name, ValidationCategory.CiSafe, evidence, run, includeInDefault);
        }

        /// <summary>
        /// Builds a local-evidence validation entry with optional aliases.
        /// </summary>
        private static PhaseValidationCase Local(
            string flag,
            string name,
            System.Action run,
            ValidationEvidence evidence,
            params string[] aliases)
        {
            return new PhaseValidationCase(
                flag,
                name,
                ValidationCategory.LocalEvidence,
                evidence,
                run,
                includeInDefault: true,
                aliases);
        }

        /// <summary>
        /// Builds a manual smoke validation entry.
        /// </summary>
        private static PhaseValidationCase Manual(
            string flag,
            string name,
            System.Action run,
            ValidationEvidence evidence)
        {
            return new PhaseValidationCase(
                flag,
                name,
                ValidationCategory.ManualSmoke,
                evidence,
                run,
                includeInDefault: false);
        }
    }
}
