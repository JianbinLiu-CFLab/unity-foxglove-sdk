// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Central registry for CI-safe, local-evidence, and explicit phase validations.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationRegistry
    {
        public static IReadOnlyList<PhaseValidationCase> All { get; } = new[]
        {
            DefaultOnly("Skeleton", SkeletonValidation.Validate),
            Ci("--phase1", "Phase 1", Phase1Validation.Validate),
            Ci("--phase2", "Phase 2", Phase2Validation.Validate),
            Ci("--phase3", "Phase 3", Phase3Validation.Validate),
            Ci("--phase4", "Phase 4", Phase4Validation.Validate),
            Ci("--phase5", "Phase 5", Phase5Validation.Validate),
            Ci("--phase6", "Phase 6", Phase6Validation.Validate),
            Ci("--phase7", "Phase 7", Phase7Validation.Validate),
            Ci("--phase8", "Phase 8", Phase8Validation.Validate),
            Ci("--phase9", "Phase 9", Phase9Validation.Validate),
            Ci("--phase10", "Phase 10", Phase10Validation.Validate),
            Ci("--phase11", "Phase 11", Phase11Validation.Validate),
            Ci("--phase12", "Phase 12", Phase12Validation.Validate),
            Ci("--phase13", "Phase 13", Phase13Validation.Validate),
            Ci("--phase14", "Phase 14", Phase14Validation.Validate),
            Ci("--phase16", "Phase 16", Phase16Validation.Validate),
            Ci("--phase17", "Phase 17", Phase17Validation.Validate),
            Ci("--phase24d", "Phase 24D", Phase24DValidation.Validate),
            Ci("--phase28", "Phase 28", Phase28Validation.Validate),
            Ci("--phase31", "Phase 31", Phase31Validation.Validate),
            Ci("--phase32", "Phase 32", Phase32Validation.Run),
            Ci("--phase33", "Phase 33", Phase33Validation.Validate),
            Ci("--phase34", "Phase 34", Phase34Validation.Validate),
            Ci("--phase36", "Phase 36", Phase36Validation.Validate),
            Ci("--phase37", "Phase 37", Phase37Validation.Validate),
            Ci("--phase40", "Phase 40", Phase40Validation.Validate),
            Ci("--phase41", "Phase 41", Phase41Validation.Validate),
            Ci("--phase44", "Phase 44", Phase44Validation.Validate),
            Ci("--phase48", "Phase 48", Phase48Validation.Validate),
            Ci("--phase49", "Phase 49", Phase49Validation.Validate),
            Ci("--phase50", "Phase 50", Phase50Validation.Validate),
            Ci("--phase51", "Phase 51", Phase51Validation.Validate),
            Ci("--phase52", "Phase 52", Phase52Validation.Validate),
            Ci("--phase53", "Phase 53", Phase53Validation.Validate),
            Ci("--phase54", "Phase 54", Phase54Validation.Validate),
            Ci("--phase55", "Phase 55", Phase55Validation.Validate),
            Ci("--phase56", "Phase 56", Phase56Validation.Validate),
            Ci("--phase57", "Phase 57", Phase57Validation.Validate),
            Ci("--phase65", "Phase 65", Phase65Validation.Validate),
            Ci("--phase67", "Phase 67", Phase67Validation.Validate),
            Ci("--phase68", "Phase 68", Phase68Validation.Validate),
            Ci("--phase69", "Phase 69", Phase69Validation.Validate),
            Ci("--phase70", "Phase 70", Phase70Validation.Validate),
            Ci("--phase71", "Phase 71", Phase71Validation.Validate),
            Ci("--phase72", "Phase 72", Phase72Validation.Validate),
            Ci("--phase73", "Phase 73", Phase73Validation.Validate),
            Ci("--phase74", "Phase 74", Phase74Validation.Validate),
            Ci("--phase75", "Phase 75", Phase75Validation.Validate),
            Ci("--phase76", "Phase 76", Phase76Validation.Validate),
            Ci("--phase77", "Phase 77", Phase77Validation.Validate),
            Ci("--phase80", "Phase 80", Phase80Validation.Validate),
            Ci("--phase81", "Phase 81", Phase81Validation.Validate),
            Ci("--phase82", "Phase 82", Phase82Validation.Validate),
            Manual("--phase82-native-smoke", "Phase 82 native smoke", Phase82Validation.RunNativeSmoke),
            Ci("--phase83", "Phase 83", Phase83Validation.Validate),
            Ci("--phase84", "Phase 84", Phase84Validation.Validate),
            Ci("--phase85", "Phase 85", Phase85Validation.Validate),
            Ci("--phase86", "Phase 86", Phase86Validation.Validate),
            Ci("--phase87", "Phase 87", Phase87Validation.Validate),
            Ci("--phase88", "Phase 88", Phase88Validation.Validate),
            Ci("--phase89", "Phase 89", Phase89Validation.Validate),
            Ci("--phase90", "Phase 90", Phase90Validation.Validate),
            Ci("--phase91", "Phase 91", Phase91Validation.Validate),
            Ci("--phase92", "Phase 92", Phase92Validation.Validate),
            Ci("--phase93", "Phase 93", Phase93Validation.Validate),
            Ci("--phase94", "Phase 94", Phase94Validation.Validate),
            Ci("--phase95", "Phase 95", Phase95Validation.Validate),
            Ci("--phase96", "Phase 96", Phase96Validation.Validate),
            Ci("--phase97", "Phase 97", Phase97Validation.Validate),
            Ci("--phase98", "Phase 98", Phase98Validation.Validate),
            Ci("--phase99", "Phase 99", Phase99Validation.Validate),
            Ci("--phase100", "Phase 100", Phase100Validation.Validate),
            Ci("--phase105", "Phase 105", Phase105Validation.Validate),
            Ci("--phase106", "Phase 106", Phase106Validation.Validate),
            Ci("--phase107", "Phase 107", Phase107Validation.Validate),
            Ci("--phase108", "Phase 108", Phase108Validation.Validate),
            Ci("--phase109", "Phase 109", Phase109Validation.Validate),
            Ci("--phase110", "Phase 110", Phase110Validation.Validate),
            Ci("--phase111f", "Phase 111F", Phase111FValidation.Validate),
            Ci("--phase112", "Phase 112", Phase112Validation.Validate),
            Ci("--phase112b", "Phase 112B", Phase112BValidation.Validate),
            Ci("--phase113", "Phase 113", Phase113Validation.Validate),
            Ci("--phase114", "Phase 114", Phase114Validation.Validate),
            Ci("--phase115", "Phase 115", Phase115Validation.Validate),
            Ci("--phase115b", "Phase 115B", Phase115BValidation.Validate),
            Ci("--phase115c", "Phase 115C", Phase115CValidation.Validate),
            Ci("--phase115d", "Phase 115D", Phase115DValidation.Validate),
            Ci("--phase115e", "Phase 115E", Phase115EValidation.Validate),
            Ci("--phase115f", "Phase 115F", Phase115FValidation.Validate),
            Local("--phase115g", "Phase 115G", Phase115GValidation.Validate),
            Local("--phase115h", "Phase 115H", Phase115HValidation.Validate),
            Ci("--phase116", "Phase 116", Phase116Validation.Validate, includeInDefault: false),
            Local("--phase117", "Phase 117", Phase117Validation.Validate),
            Ci("--phase118", "Phase 118", Phase118Validation.Validate, includeInDefault: false),
            Local("--phase119", "Phase 119", Phase119Validation.Validate),
            Local("--phase120", "Phase 120", Phase120Validation.Validate),
            Local("--phase120-official", "Phase 120 official compatibility", Phase120Validation.ValidateOfficial),
            Local("--phase120b", "Phase 120B", Phase120BValidation.Validate),
            Local("--phase121", "Phase 121", Phase121Validation.Validate),
            Ci("--phase121-conformance", "Phase 121 conformance", Phase121Validation.ValidateConformance, includeInDefault: false),
            Ci("--phase122", "Phase 122", Phase122Validation.Validate, includeInDefault: false),
            Ci("--phase123", "Phase 123", Phase123Validation.Validate, includeInDefault: false),
            Ci("--phase124", "Phase 124", Phase124Validation.Validate, includeInDefault: false),
            Ci("--phase125", "Phase 125", Phase125Validation.Validate, includeInDefault: false),
            Ci("--phase126", "Phase 126", Phase126Validation.Validate),
            Ci("--phase128", "Phase 128", Phase128Validation.Validate, includeInDefault: false),
            Ci("--phase129", "Phase 129", Phase129Validation.Validate, includeInDefault: false),
            Ci("--phase130", "Phase 130", Phase130Validation.Validate, includeInDefault: false),
            Ci("--phase131", "Phase 131", Phase131Validation.Validate, includeInDefault: false),
            Ci("--phase132", "Phase 132", Phase132Validation.Validate, includeInDefault: false),
            Ci("--phase134-1", "Phase 134-1", Phase134_1Validation.Validate),
            Ci("--phase134-2", "Phase 134-2", Phase134_2Validation.Validate),
            Ci("--phase134-3", "Phase 134-3", Phase134_3Validation.Validate),
            Ci("--phase134-4", "Phase 134-4", Phase134_4Validation.Validate),
            Ci("--phase134-5", "Phase 134-5", Phase134_5Validation.Validate),
            Ci("--phase134-6", "Phase 134-6", Phase134_6Validation.Validate),
            Ci("--phase134-7", "Phase 134-7", Phase134_7Validation.Validate),
            Ci("--phase134-8", "Phase 134-8", Phase134_8Validation.Validate),
            Ci("--phase134-9", "Phase 134-9", Phase134_9Validation.Validate),
            Ci("--phase134-10", "Phase 134-10", Phase134_10Validation.Validate),
            Ci("--phase134-11", "Phase 134-11", Phase134_11Validation.Validate),
            Ci("--phase134-12", "Phase 134-12", Phase134_12Validation.Validate),
            Ci("--phase134-13", "Phase 134-13", Phase134_13Validation.Validate),
            Ci("--phase134-14", "Phase 134-14", Phase134_14Validation.Validate),
            Ci("--phase134-15", "Phase 134-15", Phase134_15Validation.Validate),
            Ci("--phase134-16", "Phase 134-16", Phase134_16Validation.Validate),
            Ci("--phase134-17", "Phase 134-17", Phase134_17Validation.Validate),
            Ci("--phase134-18", "Phase 134-18", Phase134_18Validation.Validate),
            Ci("--phase134-19", "Phase 134-19", Phase134_19Validation.Validate),
            Ci("--phase134-20", "Phase 134-20", Phase134_20Validation.Validate),
            Ci("--phase134-21", "Phase 134-21", Phase134_21Validation.Validate),
            Ci("--phase134-22", "Phase 134-22", Phase134_22Validation.Validate),
            Ci("--phase134-23", "Phase 134-23", Phase134_23Validation.Validate),
            Ci("--phase134-24", "Phase 134-24", Phase134_24Validation.Validate),
            Ci("--phase134-25", "Phase 134-25", Phase134_25Validation.Validate),
            Ci("--phase134-26", "Phase 134-26", Phase134_26Validation.Validate),
            Ci("--phase134-27", "Phase 134-27", Phase134_27Validation.Validate),
            Ci("--phase134-28", "Phase 134-28", Phase134_28Validation.Validate),
            Ci("--phase134-29", "Phase 134-29", Phase134_29Validation.Validate),
            Ci("--phase134-30", "Phase 134-30", Phase134_30Validation.Validate),
            Ci("--phase134-31", "Phase 134-31", Phase134_31Validation.Validate),
            Ci("--phase134-32", "Phase 134-32", Phase134_32Validation.Validate),
            Ci("--phase134-33", "Phase 134-33", Phase134_33Validation.Validate),
            Ci("--phase134-34", "Phase 134-34", Phase134_34Validation.Validate),
            Ci("--phase134-35", "Phase 134-35", Phase134_35Validation.Validate),
            Ci("--phase142", "Phase 142", Phase142Validation.Validate),
            Local("--phase143", "Phase 143", Phase143Validation.Validate),
            Local("--phase138", "Phase 138", Phase138Validation.Validate),
            Local("--phase138b", "Phase 138B", Phase138BValidation.Validate, "--phase137b"),
        };

        static PhaseValidationRegistry()
        {
            var duplicate = All
                .SelectMany(item => item.AllFlags())
                .GroupBy(flag => flag, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate != null)
                throw new InvalidOperationException("Duplicate validation flag registered: " + duplicate.Key);
        }

        public static IEnumerable<PhaseValidationCase> DefaultValidations(bool includeLocalEvidence)
        {
            return All.Where(item =>
                item.IncludeInDefault
                && (item.Category == ValidationCategory.CiSafe
                    || (includeLocalEvidence && item.Category == ValidationCategory.LocalEvidence)));
        }

        public static PhaseValidationCase Find(IReadOnlyCollection<string> args)
        {
            return All.FirstOrDefault(item => item.Matches(args));
        }

        private static PhaseValidationCase DefaultOnly(string name, System.Action run)
        {
            return new PhaseValidationCase(null, name, ValidationCategory.CiSafe, run, includeInDefault: true);
        }

        private static PhaseValidationCase Ci(
            string flag,
            string name,
            System.Action run,
            bool includeInDefault = true)
        {
            return new PhaseValidationCase(flag, name, ValidationCategory.CiSafe, run, includeInDefault);
        }

        private static PhaseValidationCase Local(string flag, string name, System.Action run, params string[] aliases)
        {
            return new PhaseValidationCase(flag, name, ValidationCategory.LocalEvidence, run, includeInDefault: true, aliases);
        }

        private static PhaseValidationCase Manual(string flag, string name, System.Action run)
        {
            return new PhaseValidationCase(flag, name, ValidationCategory.ManualSmoke, run, includeInDefault: false);
        }
    }
}
