// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Configured-path OpenH264 helper and DLL validation.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Foxglove.Schemas.Video;

namespace Unity.FoxgloveSDK.Editor
{
    public enum OpenH264ExecutableStatus
    {
        NotChecked,
        Found,
        Missing,
        Invalid
    }

    public readonly struct OpenH264ExecutableCheckResult
    {
        public OpenH264ExecutableCheckResult(
            OpenH264ExecutableStatus status,
            string helperPath,
            string dllPath,
            string diagnosticLine,
            string errorMessage)
        {
            Status = status;
            HelperPath = helperPath ?? "";
            DllPath = dllPath ?? "";
            DiagnosticLine = diagnosticLine ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        public OpenH264ExecutableStatus Status { get; }
        public string HelperPath { get; }
        public string DllPath { get; }
        public string DiagnosticLine { get; }
        public string ErrorMessage { get; }
    }

    public static class OpenH264ExecutableCheck
    {
        public static OpenH264ExecutableCheckResult Check(string helperPath, string dllPath, int timeoutMs = 3000)
        {
            var normalizedHelper = NormalizePath(helperPath);
            var normalizedDll = NormalizePath(dllPath);
            if (string.IsNullOrEmpty(normalizedHelper) || !File.Exists(normalizedHelper))
            {
                return new OpenH264ExecutableCheckResult(
                    OpenH264ExecutableStatus.Missing,
                    normalizedHelper,
                    normalizedDll,
                    "",
                    "OpenH264 helper executable was not found. Use ... to choose openh264_probe_encoder.exe.");
            }

            if (string.IsNullOrEmpty(normalizedDll) || !File.Exists(normalizedDll))
            {
                return new OpenH264ExecutableCheckResult(
                    OpenH264ExecutableStatus.Missing,
                    normalizedHelper,
                    normalizedDll,
                    "",
                    "OpenH264 DLL was not found. Use ... to choose Cisco's openh264 DLL or Install OpenH264... to download it.");
            }

            try
            {
                var options = new OpenH264EncoderOptions
                {
                    HelperExecutablePath = normalizedHelper,
                    OpenH264DllPath = normalizedDll,
                    Width = 16,
                    Height = 16,
                    FrameRate = 1,
                    BitrateKbps = 64,
                    KeyframeInterval = 1
                };

                using (var process = new Process())
                {
                    process.StartInfo = options.CreateStartInfo();
                    if (!process.Start())
                    {
                        return Invalid(normalizedHelper, normalizedDll, "", "OpenH264 helper process did not start.");
                    }

                    var frame = CreateBlackI420Frame(options.Width, options.Height);
                    process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
                    process.StandardInput.BaseStream.Flush();
                    process.StandardInput.Close();

                    if (!process.WaitForExit(Math.Max(500, timeoutMs)))
                    {
                        TryKill(process);
                        return Invalid(normalizedHelper, normalizedDll, "", "OpenH264 validation timed out.");
                    }

                    var stdout = process.StandardOutput.BaseStream;
                    var hasLengthPrefix = stdout.ReadByte() >= 0
                        && stdout.ReadByte() >= 0
                        && stdout.ReadByte() >= 0
                        && stdout.ReadByte() >= 0;
                    var stderr = process.StandardError.ReadToEnd();
                    var diagnostic = LastNonEmptyLine(stderr);
                    var compatibilityError = BuildCompatibilityError(stderr);
                    if (!string.IsNullOrEmpty(compatibilityError))
                    {
                        return Invalid(normalizedHelper, normalizedDll, diagnostic, compatibilityError);
                    }

                    if (process.ExitCode == 0 && hasLengthPrefix)
                    {
                        return new OpenH264ExecutableCheckResult(
                            OpenH264ExecutableStatus.Found,
                            normalizedHelper,
                            normalizedDll,
                            diagnostic,
                            "");
                    }

                    var error = string.IsNullOrWhiteSpace(stderr)
                        ? "OpenH264 helper did not emit a valid H.264 access unit."
                        : stderr.Trim();
                    return Invalid(normalizedHelper, normalizedDll, diagnostic, error);
                }
            }
            catch (Win32Exception ex)
            {
                return new OpenH264ExecutableCheckResult(
                    OpenH264ExecutableStatus.Missing,
                    normalizedHelper,
                    normalizedDll,
                    "",
                    ex.Message);
            }
            catch (Exception ex)
            {
                return Invalid(normalizedHelper, normalizedDll, "", ex.Message);
            }
        }

        private static OpenH264ExecutableCheckResult Invalid(
            string helperPath,
            string dllPath,
            string diagnosticLine,
            string errorMessage)
            => new OpenH264ExecutableCheckResult(
                OpenH264ExecutableStatus.Invalid,
                helperPath,
                dllPath,
                diagnosticLine,
                errorMessage);

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            var trimmed = path.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                trimmed = trimmed.Substring(1, trimmed.Length - 2);

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        private static byte[] CreateBlackI420Frame(int width, int height)
        {
            var yBytes = width * height;
            var frame = new byte[yBytes * 3 / 2];
            for (var i = 0; i < yBytes; i++)
                frame[i] = 16;
            for (var i = yBytes; i < frame.Length; i++)
                frame[i] = 128;
            return frame;
        }

        private static string LastNonEmptyLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string last = "";
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        last = line.Trim();
                }
            }

            return last;
        }

        private static string BuildCompatibilityError(string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return "";

            return stderr.IndexOf("Usage: openh264_probe_encoder", StringComparison.OrdinalIgnoreCase) >= 0
                   && stderr.IndexOf("--openh264-dll", StringComparison.OrdinalIgnoreCase) < 0
                ? "Selected OpenH264 helper is outdated. Rebuild or reselect the Phase 81 helper executable; expected usage includes --openh264-dll <path>."
                : "";
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
