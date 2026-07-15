// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Locks validation evidence metadata and classified console output behavior.

using System;
using System.IO;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Domain", "Harness")]
    public sealed class ValidationEvidenceOutputTests
    {
        [Fact]
        public void FormatterUsesStableEvidenceOrder()
        {
            var evidence = ValidationEvidence.ManualEvidence
                | ValidationEvidence.Performance
                | ValidationEvidence.FaultInjection
                | ValidationEvidence.Conformance
                | ValidationEvidence.Structural
                | ValidationEvidence.Behavior;

            Assert.Equal(
                "[BEHAVIOR] [STRUCTURAL] [CONFORMANCE] [FAULT_INJECTION] [PERFORMANCE] [MANUAL_EVIDENCE]",
                ValidationEvidenceFormatter.Format(evidence));
        }

        [Fact]
        public void WriterPrefixesEveryNonEmptyLogicalLineAndPreservesBlankLines()
        {
            using var target = new StringWriter();
            using var writer = new ValidationEvidenceTextWriter(
                target,
                ValidationEvidence.Conformance | ValidationEvidence.FaultInjection);

            writer.Write("first");
            writer.WriteLine();
            writer.WriteLine();
            writer.Write("second\r\nthird");
            writer.Flush();

            var prefix = "[CONFORMANCE] [FAULT_INJECTION] ";
            Assert.Equal(
                prefix + "first" + Environment.NewLine
                + Environment.NewLine
                + prefix + "second\r\n"
                + prefix + "third",
                target.ToString());
        }

        [Fact]
        public void ValidationCaseRejectsMissingEvidenceClassification()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PhaseValidationCase(
                "--missing-evidence",
                "Missing evidence",
                ValidationCategory.CiSafe,
                ValidationEvidence.None,
                () => { },
                includeInDefault: false));
        }

        [Fact]
        public void McapStrictAndFaultMatrixTestsExposeEvidenceTraits()
        {
            Assert.True(HasEvidenceTrait(typeof(McapSpecComplianceTests), "Conformance"));

            var faultMethod = typeof(McapSpecComplianceTests)
                .GetMethod(nameof(McapSpecComplianceTests.MiddleChunkFailurePreservesOnlyDurableEarlierChunks));
            Assert.True(HasEvidenceTrait(faultMethod, "FaultInjection"));
        }

        private static bool HasEvidenceTrait(System.Reflection.MemberInfo member, string value)
        {
            foreach (var attribute in member.CustomAttributes)
            {
                if (attribute.AttributeType != typeof(TraitAttribute)
                    || attribute.ConstructorArguments.Count != 2)
                    continue;

                if ((string)attribute.ConstructorArguments[0].Value == "Evidence"
                    && (string)attribute.ConstructorArguments[1].Value == value)
                    return true;
            }

            return false;
        }
    }
}
