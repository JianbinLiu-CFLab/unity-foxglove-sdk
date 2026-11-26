// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Sensors
// Purpose: Bound the Media Foundation H.264 bitrate accepted by option validation.

using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    /// <summary>Bounds the Media Foundation H.264 bitrate accepted by option validation.</summary>
    public sealed class MediaFoundationH264EncoderOptionsTests
    {
        [Fact]
        /// <summary>Accepts the documented maximum and rejects one kbps above it.</summary>
        public void ValidateRejectsBitrateAboveMaximum()
        {
            var atLimit = new MediaFoundationH264EncoderOptions
            {
                BitrateKbps = MediaFoundationH264EncoderOptions.MaxBitrateKbps
            };
            Assert.True(atLimit.Validate(out var atLimitError), atLimitError);

            var aboveLimit = new MediaFoundationH264EncoderOptions
            {
                BitrateKbps = MediaFoundationH264EncoderOptions.MaxBitrateKbps + 1
            };
            Assert.False(aboveLimit.Validate(out var error));
            Assert.Contains("bitrate must be <=", error);
        }
    }
}
