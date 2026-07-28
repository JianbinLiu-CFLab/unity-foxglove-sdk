// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins the Inspector-only Source and Targets normalization rules.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "184-B")]
    [Trait("Domain", "FoxRunProfileInspector")]
    public sealed class FoxRunEndpointEditorModelTests
    {
        [Theory]
        [InlineData(FoxRunEndpoint.Foxglove, FoxRunEndpoint.Foxglove)]
        [InlineData(FoxRunEndpoint.Ros2Native, FoxRunEndpoint.Ros2Native)]
        [InlineData(FoxRunEndpoint.Ros2Bridge, FoxRunEndpoint.Foxglove)]
        [InlineData(
            FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
            FoxRunEndpoint.Foxglove)]
        [InlineData((FoxRunEndpoint)0, FoxRunEndpoint.Foxglove)]
        public void SourceNormalizesToExactlyOneSupportedEndpoint(
            FoxRunEndpoint value,
            FoxRunEndpoint expected)
        {
            Assert.Equal(expected, FoxRunEndpointEditorModel.NormalizeSource(value));
        }

        [Theory]
        [InlineData(FoxRunEndpoint.Foxglove)]
        [InlineData(FoxRunEndpoint.Ros2Native)]
        [InlineData(FoxRunEndpoint.Ros2Bridge)]
        [InlineData(FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native)]
        [InlineData(FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge)]
        [InlineData(FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge)]
        [InlineData(
            FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge)]
        public void TargetsPreserveEveryNonemptyKnownCombination(FoxRunEndpoint targets)
        {
            Assert.Equal(targets, FoxRunEndpointEditorModel.NormalizeTargets(targets));
        }

        [Fact]
        public void EmptyOrUnknownTargetsFailClosedToTheDefaultFoxgloveTarget()
        {
            Assert.Equal(
                FoxRunEndpoint.Foxglove,
                FoxRunEndpointEditorModel.NormalizeTargets((FoxRunEndpoint)0));
            Assert.Equal(
                FoxRunEndpoint.Foxglove,
                FoxRunEndpointEditorModel.NormalizeTargets((FoxRunEndpoint)128));
        }

        [Fact]
        public void IncludesReadsTheNormalizedTargetSet()
        {
            var targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge;

            Assert.True(FoxRunEndpointEditorModel.Includes(targets, FoxRunEndpoint.Foxglove));
            Assert.False(FoxRunEndpointEditorModel.Includes(targets, FoxRunEndpoint.Ros2Native));
            Assert.True(FoxRunEndpointEditorModel.Includes(targets, FoxRunEndpoint.Ros2Bridge));
        }

        [Theory]
        [InlineData(true, false, false, FoxRunEndpoint.Foxglove)]
        [InlineData(false, true, false, FoxRunEndpoint.Ros2Native)]
        [InlineData(false, false, true, FoxRunEndpoint.Ros2Bridge)]
        [InlineData(
            true,
            true,
            false,
            FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native)]
        [InlineData(false, false, false, FoxRunEndpoint.Foxglove)]
        public void FoxRunDefaultsInheritEnabledPublishDestinations(
            bool foxglove,
            bool ros2Native,
            bool ros2Bridge,
            FoxRunEndpoint expected)
        {
            Assert.Equal(
                expected,
                FoxRunPublishTargetPolicy.FromPublishDestinations(
                    foxgloveEnabled: foxglove,
                    ros2NativeEnabled: ros2Native,
                    ros2BridgeEnabled: ros2Bridge));
        }

        [Fact]
        public void FoxRunSubscriptionsAreEnabledByDefault()
        {
            var inbound = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");

            Assert.Contains(
                "[SerializeField] private bool _enableFoxRunInbound = true;",
                inbound);
            Assert.DoesNotContain("Disabled by default.", inbound);
        }

        [Fact]
        public void InspectorUsesOnlyThePrimaryPublishDestinations()
        {
            var labels = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEndpointEditorLabels.cs");
            var publish = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");

            Assert.DoesNotContain("MaskField", labels);
            Assert.Contains("Subheader(\"Publish Destinations\")", publish);
            Assert.Contains("FromPublishDestinations(", publish);
            Assert.DoesNotContain("Override Publish Destinations for FoxRun", publish);
            Assert.DoesNotContain("FoxRun Override Destinations", publish);
            Assert.DoesNotContain("_overrideFoxRunPublishTargets", publish);
            Assert.DoesNotContain("DrawTargets(", publish);
        }
    }
}
