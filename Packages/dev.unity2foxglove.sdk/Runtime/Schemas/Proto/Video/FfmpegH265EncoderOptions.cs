// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: FFmpeg H.265/HEVC sidecar command-line options for foxglove.CompressedVideo.

using System;
using System.Diagnostics;
using System.Globalization;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Options used to launch an external FFmpeg encoder for low-latency HEVC Annex B output.
    /// </summary>
    public sealed class FfmpegH265EncoderOptions
    {
        public string FfmpegPath = "ffmpeg";
        public int Width = 640;
        public int Height = 480;
        public int FrameRate = 30;
        public int BitrateKbps = 4000;
        public int KeyframeInterval = 30;
        public string Preset = "ultrafast";
        public int MaxInputQueue = 2;
        public int MaxOutputQueue = 4;

        /// <summary>Returns the expected RGB24 byte count for one raw input frame.</summary>
        public int FrameByteCount => Positive(Width, 640) * Positive(Height, 480) * 3;

        /// <summary>Builds the FFmpeg process start info without invoking a shell.</summary>
        public ProcessStartInfo CreateStartInfo()
        {
            var width = Positive(Width, 640);
            var height = Positive(Height, 480);
            var fps = Positive(FrameRate, 30);
            var bitrate = Positive(BitrateKbps, 4000);
            var keyframeInterval = Positive(KeyframeInterval, fps);
            var ffmpeg = FfmpegExecutableResolver.ResolveExecutablePath(FfmpegPath);
            var preset = string.IsNullOrWhiteSpace(Preset) ? "ultrafast" : Preset.Trim();

            var args = string.Join(" ", new[]
            {
                "-hide_banner",
                "-loglevel warning",
                "-f rawvideo",
                "-pix_fmt rgb24",
                "-s " + width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture),
                "-r " + fps.ToString(CultureInfo.InvariantCulture),
                "-i pipe:0",
                "-vf vflip",
                "-an",
                "-c:v libx265",
                "-pix_fmt yuv420p",
                "-preset " + QuoteArg(preset),
                "-tune zerolatency",
                "-bf 0",
                "-g " + keyframeInterval.ToString(CultureInfo.InvariantCulture),
                "-b:v " + bitrate.ToString(CultureInfo.InvariantCulture) + "k",
                "-x265-params aud=1:repeat-headers=1:bframes=0",
                "-f hevc",
                "pipe:1"
            });

            return new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static int Positive(int value, int fallback)
            => value > 0 ? value : fallback;

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
