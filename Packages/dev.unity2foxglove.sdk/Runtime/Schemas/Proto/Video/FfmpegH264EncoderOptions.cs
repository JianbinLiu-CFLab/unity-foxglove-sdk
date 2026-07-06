// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: FFmpeg H.264 sidecar command-line options for foxglove.CompressedVideo.

using System;
using System.Diagnostics;
using System.Globalization;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Options used to launch an external FFmpeg encoder for low-latency H.264 Annex B output.
    /// </summary>
    public sealed class FfmpegH264EncoderOptions
    {
        private static readonly string[] ValidPresets =
        {
            "ultrafast",
            "superfast",
            "veryfast",
            "faster",
            "fast",
            "medium",
            "slow",
            "slower",
            "veryslow",
            "placebo"
        };

        public string FfmpegPath = "";
        public int Width = 640;
        public int Height = 480;
        public int FrameRate = 30;
        public int BitrateKbps = 4000;
        public int KeyframeInterval = 30;
        public string Preset = "ultrafast";
        public int MaxInputQueue = 2;
        public int MaxOutputQueue = 4;
        public int MaxStderrLineBytes = 8192;
        public int MaxStderrRetainedBytes = 8192;

        /// <summary>Returns the expected RGB24 byte count for one raw input frame.</summary>
        public int FrameByteCount
            => CameraVideoFrameGeometry.GetRgb24FrameByteCountOrZero(Width, Height);

        public bool Validate(out string error)
        {
            if (!ValidatePreset(Preset, out error))
                return false;

            if (!CameraVideoFrameGeometry.ValidateRgb24Dimensions(Width, Height, "FFmpeg H.264 RGB24", out error))
                return false;

            if (FrameRate <= 0)
            {
                error = "FFmpeg H.264 frame rate must be positive.";
                return false;
            }

            if (BitrateKbps <= 0)
            {
                error = "FFmpeg H.264 bitrate must be positive.";
                return false;
            }

            if (KeyframeInterval <= 0)
            {
                error = "FFmpeg H.264 keyframe interval must be positive.";
                return false;
            }

            if (MaxInputQueue <= 0 || MaxOutputQueue <= 0)
            {
                error = "FFmpeg H.264 queue sizes must be positive.";
                return false;
            }

            error = "";
            return true;
        }

        /// <summary>Builds the FFmpeg process start info without invoking a shell.</summary>
        public ProcessStartInfo CreateStartInfo()
        {
            if (!Validate(out var error))
                throw new ArgumentException(error, nameof(FfmpegH264EncoderOptions));

            var width = Width;
            var height = Height;
            var fps = FrameRate;
            var bitrate = BitrateKbps;
            var keyframeInterval = KeyframeInterval;
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
                "-c:v libx264",
                "-preset " + QuoteArg(preset),
                "-tune zerolatency",
                "-bf 0",
                "-g " + keyframeInterval.ToString(CultureInfo.InvariantCulture),
                "-b:v " + bitrate.ToString(CultureInfo.InvariantCulture) + "k",
                "-x264-params aud=1:repeat-headers=1:bframes=0",
                "-f h264",
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

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0
                ? value
                : "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static bool ValidatePreset(string value, out string error)
        {
            var preset = string.IsNullOrWhiteSpace(value) ? "ultrafast" : value.Trim();
            foreach (var c in preset)
            {
                if (c < ' ' || c == '\u007f')
                {
                    error = "FFmpeg H.264 preset contains control characters.";
                    return false;
                }
            }

            if (!IsKnownPreset(preset))
            {
                error = "FFmpeg H.264 preset must be one of the known x264 preset names.";
                return false;
            }

            error = "";
            return true;
        }

        private static bool IsKnownPreset(string preset)
        {
            foreach (var valid in ValidPresets)
                if (string.Equals(valid, preset, StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }
    }
}
