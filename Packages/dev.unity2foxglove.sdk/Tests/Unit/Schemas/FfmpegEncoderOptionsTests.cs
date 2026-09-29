// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: FFmpeg video encoder option validation truth.

using System;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-001")]
    [Trait("Domain", "Schemas")]
    public sealed class FfmpegEncoderOptionsTests
    {
        [Theory]
        [InlineData("FrameRate")]
        [InlineData("BitrateKbps")]
        [InlineData("KeyframeInterval")]
        [InlineData("MaxInputQueue")]
        [InlineData("MaxOutputQueue")]
        public void H264RejectsInvalidPositiveOnlyFields(string fieldName)
        {
            var options = new FfmpegH264EncoderOptions();
            typeof(FfmpegH264EncoderOptions).GetField(fieldName).SetValue(options, 0);

            Assert.False(options.Validate(out var error));
            Assert.Contains("positive", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("FrameRate")]
        [InlineData("BitrateKbps")]
        [InlineData("KeyframeInterval")]
        [InlineData("MaxInputQueue")]
        [InlineData("MaxOutputQueue")]
        public void H265RejectsInvalidPositiveOnlyFields(string fieldName)
        {
            var options = new FfmpegH265EncoderOptions();
            typeof(FfmpegH265EncoderOptions).GetField(fieldName).SetValue(options, 0);

            Assert.False(options.Validate(out var error));
            Assert.Contains("positive", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FfmpegOptionsKeepEmptyPathAsResolverContract()
        {
            Assert.Equal("", new FfmpegH264EncoderOptions().FfmpegPath);
            Assert.Equal("", new FfmpegH265EncoderOptions().FfmpegPath);
        }

        [Fact]
        public void FfmpegRawVideoCommandsPreserveOneOutputPerInputFrame()
        {
            var h264 = new FfmpegH264EncoderOptions { Width = 2, Height = 2 }.CreateStartInfo().Arguments;
            var h265 = new FfmpegH265EncoderOptions { Width = 2, Height = 2 }.CreateStartInfo().Arguments;

            Assert.Contains("-fps_mode passthrough", h264, StringComparison.Ordinal);
            Assert.Contains("-fps_mode passthrough", h265, StringComparison.Ordinal);
            Assert.Contains("-f mpegts", h264, StringComparison.Ordinal);
            Assert.Contains("-f mpegts", h265, StringComparison.Ordinal);
            Assert.Contains("-mpegts_copyts 1", h264, StringComparison.Ordinal);
            Assert.Contains("-mpegts_copyts 1", h265, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ffmpeg version 5.1.0", true)]
        [InlineData("ffmpeg version 6.1.2", true)]
        [InlineData("ffmpeg version 4.4.2", false)]
        [InlineData("ffmpeg version 5.0.3", false)]
        public void FfmpegVersionSelectsCompatiblePassthroughOption(string versionLine, bool modern)
        {
            var h264 = new FfmpegH264EncoderOptions { Width = 2, Height = 2 }
                .CreateStartInfo(versionLine).Arguments;
            var h265 = new FfmpegH265EncoderOptions { Width = 2, Height = 2 }
                .CreateStartInfo(versionLine).Arguments;
            var expected = modern ? "-fps_mode passthrough" : "-vsync passthrough";
            var rejected = modern ? "-vsync passthrough" : "-fps_mode passthrough";

            Assert.Contains(expected, h264, StringComparison.Ordinal);
            Assert.Contains(expected, h265, StringComparison.Ordinal);
            Assert.DoesNotContain(rejected, h264, StringComparison.Ordinal);
            Assert.DoesNotContain(rejected, h265, StringComparison.Ordinal);
        }

        [Fact]
        public void H264CreateStartInfoRejectsInvalidOptions()
        {
            var options = new FfmpegH264EncoderOptions { FrameRate = 0 };

            Assert.Throws<ArgumentException>(() => options.CreateStartInfo());
        }

        [Fact]
        public void H265CreateStartInfoRejectsInvalidOptions()
        {
            var options = new FfmpegH265EncoderOptions { BitrateKbps = -1 };

            Assert.Throws<ArgumentException>(() => options.CreateStartInfo());
        }
    }
}
