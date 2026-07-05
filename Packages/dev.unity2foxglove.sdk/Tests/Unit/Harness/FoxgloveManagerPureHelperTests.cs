// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "170D")]
    [Trait("Domain", "Manager")]
    public sealed class FoxgloveManagerPureHelperTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(8765)]
        [InlineData(65535)]
        public void ManagerConfigValidatorAcceptsValidTcpPorts(int port)
        {
            Assert.True(ManagerConfigValidator.IsValidTcpPort(port));
            Assert.Equal(port, ManagerConfigValidator.ClampTcpPort(port));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, 1)]
        [InlineData(65536, 65535)]
        public void ManagerConfigValidatorRejectsAndClampsInvalidTcpPorts(int port, int clamped)
        {
            Assert.False(ManagerConfigValidator.IsValidTcpPort(port));
            Assert.Equal(clamped, ManagerConfigValidator.ClampTcpPort(port));
        }

        [Theory]
        [InlineData(-4, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(1024, 1024)]
        public void ManagerConfigValidatorClampsPositiveIntegerSettings(int value, int expected)
            => Assert.Equal(expected, ManagerConfigValidator.ClampAtLeastOne(value));
    }
}
