// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
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
        public void ManagerQosChoicesExposeOnlyOfficialProfilesWithStableLabelsAndSummaries()
        {
            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;

            Assert.Collection(
                choices,
                choice => AssertChoice(
                    choice,
                    FoxRunQosProfile.Default,
                    "Default",
                    "Reliable / Volatile / Keep Last 10"),
                choice => AssertChoice(
                    choice,
                    FoxRunQosProfile.SensorData,
                    "Sensor Data",
                    "Best Effort / Volatile / Keep Last 5"),
                choice => AssertChoice(
                    choice,
                    FoxRunQosProfile.SystemDefault,
                    "System Default",
                    "System Default / System Default / System Default"));
            Assert.DoesNotContain(
                choices,
                choice => (int)choice.Profile == 0);
        }

        [Fact]
        public void ManagerQosPopupLabelsReuseTheStableChoiceLabelsWithoutReallocation()
        {
            var choices = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosChoices;
            var labels = FoxRunRos2SubscriptionInspectorPresentation.ManagerQosLabels;

            Assert.Same(labels, FoxRunRos2SubscriptionInspectorPresentation.ManagerQosLabels);
            Assert.Equal(choices.Count, labels.Length);
            Assert.Equal(new[] { "Default", "Sensor Data", "System Default" }, labels);
            for (var index = 0; index < choices.Count; index++)
                Assert.Equal(choices[index].Label, labels[index]);
        }

        [Fact]
        public void AdvancedSummaryUsesOfficialAxesAndOmitsDepthForKeepAll()
        {
            Assert.Equal(
                "Best Effort / Transient Local / Keep All",
                FoxRunRos2SubscriptionInspectorPresentation.Summary(
                    new FoxRunResolvedQos(
                        FoxRunQosProfile.SystemDefault,
                        FoxRunQosReliability.BestEffort,
                        FoxRunQosDurability.TransientLocal,
                        FoxRunQosHistory.KeepAll,
                        0)));
            Assert.Equal(
                "Reliable / Volatile / Keep Last 37",
                FoxRunRos2SubscriptionInspectorPresentation.Summary(
                    new FoxRunResolvedQos(
                        FoxRunQosProfile.Default,
                        FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile,
                        FoxRunQosHistory.KeepLast,
                        37)));
        }

        [Fact]
        public void DeclaredSummaryPreservesInheritanceAndShowsOnlyExplicitOverrides()
        {
            Assert.Equal(
                "Inherit",
                FoxRunRos2SubscriptionInspectorPresentation.DeclaredSummary(
                    0,
                    0,
                    0,
                    0,
                    0));
            Assert.Equal(
                "Sensor Data",
                FoxRunRos2SubscriptionInspectorPresentation.DeclaredSummary(
                    FoxRunQosProfile.SensorData,
                    0,
                    0,
                    0,
                    0));
            Assert.Equal(
                "Default; Reliability=Best Effort; History=Keep Last; Depth=37",
                FoxRunRos2SubscriptionInspectorPresentation.DeclaredSummary(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.BestEffort,
                    0,
                    FoxRunQosHistory.KeepLast,
                    37));
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

        [Fact]
        public void SubscriptionPayloadConversionPreservesStoredBytesAcrossDecimalKBAndMB()
        {
            var presentation = typeof(FoxRunRos2SubscriptionInspectorPresentation);
            var labels = presentation.GetProperty(
                "SubscriptionMaxPayloadLabels",
                BindingFlags.Static | BindingFlags.NonPublic);
            var toDisplay = presentation.GetMethod(
                "ToSubscriptionPayloadDisplayValue",
                BindingFlags.Static | BindingFlags.NonPublic);
            var toBytes = presentation.GetMethod(
                "ToClampedSubscriptionPayloadBytes",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(labels);
            Assert.NotNull(toDisplay);
            Assert.NotNull(toBytes);
            Assert.Equal(new[] { "KB", "MB" }, (string[])labels.GetValue(null));
            Assert.Equal(
                65.536d,
                (double)toDisplay.Invoke(
                    null,
                    new object[] { 65_536, FoxRunRos2NativeCopyBudgetUnit.KB }));
            Assert.Equal(
                0.065536d,
                (double)toDisplay.Invoke(
                    null,
                    new object[] { 65_536, FoxRunRos2NativeCopyBudgetUnit.MB }));
            Assert.Equal(
                65_536,
                (int)toBytes.Invoke(
                    null,
                    new object[] { 65.536d, FoxRunRos2NativeCopyBudgetUnit.KB }));
            Assert.Equal(
                65_536,
                (int)toBytes.Invoke(
                    null,
                    new object[] { 0.065536d, FoxRunRos2NativeCopyBudgetUnit.MB }));
        }

        [Fact]
        public void SubscriptionPayloadConversionClampsMalformedValuesToSerializedBounds()
        {
            var toBytes = typeof(FoxRunRos2SubscriptionInspectorPresentation).GetMethod(
                "ToClampedSubscriptionPayloadBytes",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(toBytes);
            Assert.Equal(
                256,
                (int)toBytes.Invoke(
                    null,
                    new object[] { double.NaN, FoxRunRos2NativeCopyBudgetUnit.KB }));
            Assert.Equal(
                256,
                (int)toBytes.Invoke(
                    null,
                    new object[] { -1d, FoxRunRos2NativeCopyBudgetUnit.MB }));
            Assert.Equal(
                int.MaxValue,
                (int)toBytes.Invoke(
                    null,
                    new object[] { double.PositiveInfinity, FoxRunRos2NativeCopyBudgetUnit.KB }));
            Assert.Equal(
                int.MaxValue,
                (int)toBytes.Invoke(
                    null,
                    new object[] { double.MaxValue, FoxRunRos2NativeCopyBudgetUnit.MB }));
        }

        private static void AssertChoice(
            FoxRunRos2QosInspectorChoice choice,
            FoxRunQosProfile profile,
            string label,
            string summary)
        {
            Assert.Equal(profile, choice.Profile);
            Assert.Equal(label, choice.Label);
            Assert.Equal(summary, choice.Summary);
        }
    }
}
