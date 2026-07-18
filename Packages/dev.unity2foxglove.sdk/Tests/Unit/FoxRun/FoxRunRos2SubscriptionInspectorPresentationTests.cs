// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [Trait("Phase", "180-A")]
    [Trait("Domain", "FoxRunSubscriptionInspector")]
    public sealed class FoxRunRos2SubscriptionInspectorPresentationTests
    {
        [Fact]
        public void ManagerQosChoicesExposeOnlyConcretePresetsWithStableLabelsAndSummaries()
        {
            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;

            Assert.Collection(
                choices,
                choice => AssertChoice(
                    choice,
                    FoxRunRos2QosPreset.Default,
                    "ROS 2 Default (R2FU)",
                    "R2FU default / Keep Last 10"),
                choice => AssertChoice(
                    choice,
                    FoxRunRos2QosPreset.Reliable,
                    "Reliable",
                    "Reliable / Volatile / Keep Last 10"),
                choice => AssertChoice(
                    choice,
                    FoxRunRos2QosPreset.SensorData,
                    "Sensor Data",
                    "Best Effort / Volatile / Keep Last 5"),
                choice => AssertChoice(
                    choice,
                    FoxRunRos2QosPreset.TransientLocal,
                    "Transient Local",
                    "Reliable / Transient Local / Keep Last 1"));
            Assert.DoesNotContain(
                choices,
                choice => choice.Preset == FoxRunRos2QosPreset.Inherit);
        }

        [Fact]
        public void ManagerQosPopupLabelsReuseTheStableChoiceLabelsWithoutReallocation()
        {
            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;
            var labels = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosLabels;

            Assert.Same(labels, FoxRunRos2SubscriptionInspectorPresentation.ManagerQosLabels);
            Assert.Equal(choices.Count, labels.Length);
            Assert.Equal(new[] { "ROS 2 Default (R2FU)", "Reliable", "Sensor Data", "Transient Local" }, labels);
            for (var index = 0; index < choices.Count; index++)
                Assert.Equal(choices[index].Label, labels[index]);
        }

        [Fact]
        public void CopyBudgetConversionUsesDecimalMegabytesWithoutChangingTheStoredBytes()
        {
            Assert.Equal(new[] { "KB", "MB" }, FoxRunRos2SubscriptionInspectorPresentation.NativeCopyBudgetLabels);
            Assert.Equal(
                4.194304d,
                FoxRunRos2SubscriptionInspectorPresentation.ToDisplayValue(
                    FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    4.194304d,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
        }

        [Fact]
        public void CopyBudgetUnitSwitchesPreserveDecimalKBAndMB()
        {
            const int bytes = 1_536_000;

            Assert.Equal(
                1536d,
                FoxRunRos2SubscriptionInspectorPresentation.ToDisplayValue(
                    bytes,
                    FoxRunRos2NativeCopyBudgetUnit.KB));
            Assert.Equal(
                1.536d,
                FoxRunRos2SubscriptionInspectorPresentation.ToDisplayValue(
                    bytes,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
            Assert.Equal(
                bytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    1536d,
                    FoxRunRos2NativeCopyBudgetUnit.KB));
            Assert.Equal(
                bytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    1.536d,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
        }

        [Fact]
        public void CopyBudgetConversionClampsNonFiniteAndOversizedValuesBeforeMultiplication()
        {
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MinBytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    double.NaN,
                    FoxRunRos2NativeCopyBudgetUnit.KB));
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MinBytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    -1d,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MaxBytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    double.PositiveInfinity,
                    FoxRunRos2NativeCopyBudgetUnit.KB));
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MaxBytes,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    double.MaxValue,
                    FoxRunRos2NativeCopyBudgetUnit.MB));
        }

        [Fact]
        public void CopyBudgetConversionRoundsMidpointsAwayFromZeroBeforeClamping()
        {
            Assert.Equal(
                1537,
                FoxRunRos2SubscriptionInspectorPresentation.ToClampedBytes(
                    1536.5d / 1000d,
                    FoxRunRos2NativeCopyBudgetUnit.KB));
        }

        private static void AssertChoice(
            FoxRunRos2QosInspectorChoice choice,
            FoxRunRos2QosPreset preset,
            string label,
            string summary)
        {
            Assert.Equal(preset, choice.Preset);
            Assert.Equal(label, choice.Label);
            Assert.Equal(summary, choice.Summary);
        }
    }
}
