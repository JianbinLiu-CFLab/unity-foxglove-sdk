// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify manifest-driven runtime RMW and communication capabilities.

using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-D")]
    [Trait("Domain", "R2fuRuntimeCapabilities")]
    public sealed class R2fuRuntimeCapabilityNormalizationTests
    {
        [Fact]
        public void ExplicitCommunicationModesTakePrecedenceAndKeepManifestLabels()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'rmwImplementation': 'rmw_fastrtps_cpp',
  'defaultRmwImplementation': 'rmw_zenoh_cpp',
  'supportedRmwImplementations': ['rmw_fastrtps_cpp', 'rmw_zenoh_cpp'],
  'communicationModes': [
    {
      'id': 'dds-custom',
      'displayName': 'DDS custom transport',
      'rmwImplementation': 'rmw_fastrtps_cpp',
      'default': false
    },
    {
      'id': 'edge-router',
      'displayName': 'Edge router transport',
      'rmwImplementation': 'rmw_zenoh_cpp',
      'default': true
    }
  ]
}".Replace('\'', '\"'));

            Assert.Equal(2, capabilities.CommunicationModes.Count);
            Assert.Equal("edge-router", capabilities.DefaultCommunicationMode.Id);
            Assert.Equal("Edge router transport", capabilities.DefaultCommunicationMode.DisplayName);
            Assert.Equal("rmw_zenoh_cpp", capabilities.DefaultRmwImplementation);
            Assert.True(capabilities.SupportsZenoh);
        }

        [Fact]
        public void SupportedImplementationsSynthesizeModesWhenExplicitModesAreAbsent()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'defaultRmwImplementation': 'rmw_fastrtps_cpp',
  'supportedRmwImplementations': ['rmw_fastrtps_cpp', 'rmw_zenoh_cpp']
}".Replace('\'', '\"'));

            Assert.Equal(2, capabilities.CommunicationModes.Count);
            Assert.Equal("fastdds", capabilities.DefaultCommunicationMode.Id);
            Assert.Equal("FastDDS (default)", capabilities.DefaultCommunicationMode.DisplayName);
            Assert.Contains(capabilities.CommunicationModes, mode => mode.Id == "zenoh" && mode.RmwImplementation == "rmw_zenoh_cpp");
            Assert.True(capabilities.SupportsZenoh);
        }

        [Fact]
        public void LegacySingleRmwManifestProducesOneDeterministicDefaultMode()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'rmwImplementation': 'rmw_fastrtps_cpp'
}".Replace('\'', '\"'));

            var mode = Assert.Single(capabilities.CommunicationModes);
            Assert.Equal("fastdds", mode.Id);
            Assert.Equal("rmw_fastrtps_cpp", mode.RmwImplementation);
            Assert.Equal("rmw_fastrtps_cpp", capabilities.DefaultRmwImplementation);
            Assert.False(capabilities.SupportsZenoh);
        }

        [Fact]
        public void ManifestIdentityRemainsAvailableForRuntimeStatusInsteadOfPackageNameInference()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'runtimeId': 'r2fu-future-win64',
  'rosDistro': 'future',
  'platform': 'win64',
  'rmwImplementation': 'rmw_fastrtps_cpp'
}".Replace('\'', '\"'));

            Assert.Equal("r2fu-future-win64", capabilities.RuntimeId);
            Assert.Equal("future", capabilities.RosDistro);
            Assert.Equal("win64", capabilities.Platform);
        }

        [Fact]
        public void InvalidExplicitModesFailClosedInsteadOfFallingBackToADeclaredLegacyRmw()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'rmwImplementation': 'rmw_fastrtps_cpp',
  'communicationModes': [
    { 'id': '', 'rmwImplementation': '' }
  ]
}".Replace('\'', '\"'));

            Assert.False(capabilities.IsValid);
            Assert.Empty(capabilities.CommunicationModes);
            Assert.False(capabilities.SupportsZenoh);
        }

        [Theory]
        [InlineData(@"{
  'defaultRmwImplementation': 'rmw_fastrtps_cpp',
  'supportedRmwImplementations': ['rmw_fastrtps_cpp', 'rmw_fastrtps_cpp']
}")]
        [InlineData(@"{
  'defaultRmwImplementation': 'rmw_fastrtps_cpp',
  'communicationModes': [
    { 'id': 'primary', 'rmwImplementation': 'rmw_fastrtps_cpp', 'default': true },
    { 'id': 'primary', 'rmwImplementation': 'rmw_zenoh_cpp', 'default': false }
  ]
}")]
        [InlineData(@"{
  'defaultRmwImplementation': 'rmw_fastrtps_cpp',
  'communicationModes': [
    { 'id': 'primary', 'rmwImplementation': 'rmw_fastrtps_cpp', 'default': true },
    { 'id': 'secondary', 'rmwImplementation': 'rmw_fastrtps_cpp', 'default': false }
  ]
}")]
        [InlineData(@"{
  'defaultRmwImplementation': 'rmw_fastrtps_cpp',
  'communicationModes': [
    { 'id': 'fastdds', 'rmwImplementation': 'rmw_fastrtps_cpp', 'default': true },
    { 'id': 'backup', 'rmwImplementation': 'rmw_zenoh_cpp', 'default': true }
  ]
}")]
        [InlineData(@"{
  'supportedRmwImplementations': ['rmw_fastrtps_cpp', 'rmw_zenoh_cpp']
}")]
        [InlineData(@"{
  'rmwImplementation': 'rmw_fastrtps_cpp',
  'defaultRmwImplementation': 'rmw_zenoh_cpp',
  'supportedRmwImplementations': ['rmw_fastrtps_cpp', 'rmw_zenoh_cpp']
}")]
        public void DuplicateOrAmbiguousCapabilityDeclarationsFailClosed(string manifest)
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(manifest.Replace('\'', '\"'));

            Assert.False(capabilities.IsValid);
            Assert.Empty(capabilities.CommunicationModes);
            Assert.NotEmpty(capabilities.Diagnostic);
        }

        [Fact]
        public void UnknownLegacyRmwRemainsSelectableWithoutGuessingItsTransport()
        {
            var capabilities = Ros2ForUnityRuntimeCapabilityParser.Parse(@"{
  'rmwImplementation': 'rmw_future_transport_cpp'
}".Replace('\'', '\"'));

            var mode = Assert.Single(capabilities.CommunicationModes);
            Assert.True(capabilities.IsValid);
            Assert.Equal("rmw_future_transport_cpp", mode.Id);
            Assert.Equal("rmw_future_transport_cpp", mode.DisplayName);
            Assert.False(capabilities.SupportsZenoh);
        }
    }
}
