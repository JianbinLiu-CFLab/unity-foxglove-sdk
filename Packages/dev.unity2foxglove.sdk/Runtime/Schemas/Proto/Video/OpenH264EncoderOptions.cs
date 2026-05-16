// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: OpenH264 helper process options for camera H.264 output.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Options used to launch an external OpenH264 helper process.
    /// </summary>
    public sealed class OpenH264EncoderOptions
    {
        public string HelperExecutablePath = "";
        public string OpenH264DllPath = "";
        public int Width = 640;
        public int Height = 480;
        public int FrameRate = 30;
        public int BitrateKbps = 4000;
        public int KeyframeInterval = 30;
        public int MaxInputQueue = 2;
        public int MaxOutputQueue = 4;

        public int FrameByteCount => Width > 0 && Height > 0 ? Width * Height * 3 / 2 : 0;

        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(HelperExecutablePath))
            {
                error = "OpenH264 helper executable path is empty.";
                return false;
            }

            if (!File.Exists(HelperExecutablePath))
            {
                error = "OpenH264 helper executable does not exist: " + HelperExecutablePath;
                return false;
            }

            if (string.IsNullOrWhiteSpace(OpenH264DllPath))
            {
                error = "OpenH264 DLL path is empty.";
                return false;
            }

            if (!File.Exists(OpenH264DllPath))
            {
                error = "OpenH264 DLL does not exist: " + OpenH264DllPath;
                return false;
            }

            if (string.Equals(Path.GetExtension(OpenH264DllPath), ".bz2", StringComparison.OrdinalIgnoreCase))
            {
                error = "OpenH264 DLL path points to the compressed .bz2 download. Use Install OpenH264... or decompress it first, then select the .dll file.";
                return false;
            }

            if (Width <= 0 || Height <= 0 || (Width % 2) != 0 || (Height % 2) != 0)
            {
                error = "OpenH264 requires positive even width and height.";
                return false;
            }

            if (FrameRate <= 0 || BitrateKbps <= 0 || KeyframeInterval <= 0)
            {
                error = "OpenH264 requires positive frame rate, bitrate, and keyframe interval.";
                return false;
            }

            error = "";
            return true;
        }

        public ProcessStartInfo CreateStartInfo()
        {
            var args = string.Join(" ", new[]
            {
                "--openh264-dll " + QuoteArgument(OpenH264DllPath),
                "--width " + Width.ToString(CultureInfo.InvariantCulture),
                "--height " + Height.ToString(CultureInfo.InvariantCulture),
                "--fps " + FrameRate.ToString(CultureInfo.InvariantCulture),
                "--bitrate-kbps " + BitrateKbps.ToString(CultureInfo.InvariantCulture),
                "--keyint " + KeyframeInterval.ToString(CultureInfo.InvariantCulture)
            });

            return new ProcessStartInfo
            {
                FileName = HelperExecutablePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
