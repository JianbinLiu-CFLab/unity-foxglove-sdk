// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Locks the ROS-free migration policy for directional transport coordinates.

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Manager
{
    [Trait("Phase", "180")]
    [Trait("Domain", "CoordinateTransport")]
    public sealed class CoordinateTransportPolicyTests
    {
        [Fact]
        public void NewPolicyDefaultsBothExternalTransportDirectionsToRightHand()
        {
            Assert.Equal(CoordinateMode.RightHand, CoordinateTransportPolicy.DefaultOutputCoordinateMode);
            Assert.Equal(CoordinateMode.RightHand, CoordinateTransportPolicy.DefaultInputCoordinateMode);
        }

        [Theory]
        [InlineData(CoordinateMode.LeftHand)]
        [InlineData(CoordinateMode.RightHand)]
        public void LegacyCoordinateModeMigratesToBothDirections(CoordinateMode legacy)
        {
            var version = 0;
            var output = CoordinateMode.RightHand;
            var input = CoordinateMode.RightHand;

            CoordinateTransportPolicy.Migrate(ref version, legacy, ref output, ref input);

            Assert.Equal(CoordinateTransportPolicy.CurrentSerializationVersion, version);
            Assert.Equal(legacy, output);
            Assert.Equal(legacy, input);
        }

        [Fact]
        public void MalformedLegacyModeKeepsLegacyLeftHandBehaviorDuringMigration()
        {
            var version = 0;
            var output = CoordinateMode.RightHand;
            var input = CoordinateMode.RightHand;

            CoordinateTransportPolicy.Migrate(
                ref version,
                (CoordinateMode)99,
                ref output,
                ref input);

            Assert.Equal(CoordinateMode.LeftHand, output);
            Assert.Equal(CoordinateMode.LeftHand, input);
        }
    }
}
