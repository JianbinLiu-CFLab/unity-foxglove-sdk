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

        [Fact]
        public void StatusTextBuilderFormatsReplayFallbackWithDefaults()
        {
            var message = StatusTextBuilder.CreateReplayFallbackWarning(" ", "");

            Assert.Contains("Replay was requested but did not enable", message);
            Assert.Contains("Replay file: <empty>.", message);
            Assert.Contains("Cause: No replay failure details were reported.", message);
        }

        [Fact]
        public void StatusTextBuilderFormatsReplayFallbackWithPathAndCause()
        {
            var message = StatusTextBuilder.CreateReplayFallbackWarning("C:/recordings/run.mcap", "bad schema");

            Assert.Contains("Replay file: C:/recordings/run.mcap.", message);
            Assert.EndsWith("Cause: bad schema", message);
        }

        [Fact]
        public void StatusTextBuilderFormatsConnectionAndBridgeMessages()
        {
            Assert.Equal(
                "[Foxglove] Server started on ws://127.0.0.1:8765",
                StatusTextBuilder.CreateServerStartedMessage("ws://127.0.0.1:8765"));
            Assert.Equal(
                "[Foxglove] ROS2 Bridge disabled: connection refused",
                StatusTextBuilder.CreateRos2BridgeDisabledWarning("connection refused"));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("/camera/image", true)]
        [InlineData("camera/image", true)]
        public void TopicNameNormalizerPreservesManagerPublishTopicValidity(string topic, bool expected)
            => Assert.Equal(expected, TopicNameNormalizer.IsValidPublishTopic(topic));

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("camera/image", "/camera/image")]
        [InlineData("/camera/image", "/camera/image")]
        [InlineData(" //camera//image/ ", "/camera/image")]
        [InlineData("/", "")]
        public void TopicNameNormalizerNormalizesRosStyleTopics(string topic, string expected)
            => Assert.Equal(expected, TopicNameNormalizer.NormalizeRosStyleTopic(topic));
    }
}
