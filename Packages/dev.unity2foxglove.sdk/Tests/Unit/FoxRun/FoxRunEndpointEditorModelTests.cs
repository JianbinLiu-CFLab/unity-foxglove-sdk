// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins the Inspector-only Source and Targets normalization rules.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
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
    }
}
