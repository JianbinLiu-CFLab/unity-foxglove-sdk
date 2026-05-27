// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 80 validation for source-only OpenH264 encoder spike.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 80 OpenH264 source-only spike through source
    /// and package boundary checks. The runtime test assembly intentionally
    /// does not reference UnityEngine or OpenH264.
    /// </summary>
    public static class Phase80Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 80 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 80: OpenH264 Source-Only Encoder Spike ===");
            _passed = 0;

            VerifyPackageBoundaries();
            VerifyProbeSource();
            VerifyNativeHelperSource();
            VerifySpikeDocumentation();

            Console.WriteLine($"Phase 80: {_passed} checks passed.");
        }

        private static void VerifyPackageBoundaries()
        {
            var gitignore = ReadRepoText(".gitignore");
            Check(gitignore.Contains("third-party/"),
                "80A-1: third-party workspace is git-ignored");

            var sdkPackage = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!ContainsAny(sdkPackage, "openh264", "OpenH264", "Unity.WebRTC", "com.unity.webrtc"),
                "80A-2: SDK package.json has no OpenH264 or WebRTC dependency");

            var cameraOutputMode = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs");
            var phase81PromotionExists = File.Exists(Path.Combine(
                FindRepoRoot() ?? "",
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Runtime",
                "Phase81Validation.cs"));
            Check(phase81PromotionExists || !ContainsAny(cameraOutputMode, "OpenH264", "H264OpenH264"),
                "80A-3: production CameraOutputMode excludes OpenH264 until Phase 81 promotion");

            Check(!HasCommittedOpenH264BinaryArtifacts(),
                "80A-4: no OpenH264 binary artifacts are committed under package/assets paths");
        }

        private static void VerifyProbeSource()
        {
            var source = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var converter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/Rgb24ToI420Converter.cs");
            Check(!string.IsNullOrEmpty(source),
                "80B-1: demo OpenH264 probe publisher exists");
            Check(source.Contains("AddComponentMenu(\"Foxglove/Experimental/OpenH264 Source Probe Publisher\")"),
                "80B-2: probe is discoverable through Add Component menu");
            Check(source.Contains("/unity/camera/openh264_probe"),
                "80B-3: probe uses isolated OpenH264 topic");
            Check(source.Contains("foxglove.CompressedVideo") || source.Contains("CompressedVideo"),
                "80B-4: probe publishes CompressedVideo schema");
            Check(source.Contains("CameraCompressedVideoBuilder.Serialize")
                  && source.Contains("CameraCompressedVideoBuilder.H264Format"),
                "80B-5: probe serializes h264 CompressedVideo payloads");
            Check(source.Contains("ShouldPreparePublishPayload")
                  && source.Contains("ShouldPublishNow")
                  && source.Contains("AsyncGPUReadback.Request"),
                "80B-6: probe gates capture on demand/cadence before readback");
            Check(source.Contains("TryConvertRgb24ToI420"),
                "80B-7: probe has dedicated RGB24-to-I420 conversion method");
            Check(source.Contains("Rgb24ToI420Converter.TryConvertRgb24ToI420")
                  && converter.Contains("width % 2")
                  && converter.Contains("height % 2"),
                "80B-8: conversion rejects odd dimensions");
            Check(source.Contains("Rgb24ToI420Converter.TryConvertRgb24ToI420")
                  && converter.Contains("rgb24.Length")
                  && converter.Contains("i420.Length"),
                "80B-9: conversion validates input and output buffer sizes");
            Check(converter.Contains("yOffset")
                  && converter.Contains("uOffset")
                  && converter.Contains("vOffset")
                  && converter.Contains("2x2"),
                "80B-10: conversion writes explicit Y/U/V planes with 2x2 chroma averaging");
            Check(source.Contains("flipVertical: true")
                  && converter.Contains("flipVertical ? height - 1 - y : y"),
                "80B-11: conversion vertically flips Unity readback rows before I420 encoding");
            Check(!ContainsAny(source, "FFmpeg", "Unity.WebRTC", "RTCRtpScriptTransform"),
                "80B-12: OpenH264 probe does not depend on FFmpeg or Unity WebRTC");
        }

        private static void VerifyNativeHelperSource()
        {
            var sidecar = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            Check(!string.IsNullOrEmpty(sidecar),
                "80C-1: managed OpenH264 probe sidecar exists");
            Check(sidecar.Contains("UseShellExecute = false")
                  && sidecar.Contains("RedirectStandardInput = true")
                  && sidecar.Contains("RedirectStandardOutput = true")
                  && sidecar.Contains("RedirectStandardError = true"),
                "80C-2: sidecar starts helper without shell and redirects binary pipes");
            Check(sidecar.Contains("ReadLittleEndianLength")
                  && sidecar.Contains("TryDequeueAccessUnit")
                  && sidecar.Contains("LastStderrLine"),
                "80C-3: sidecar reads length-prefixed access units and stderr diagnostics");

            var helper = ReadRepoText("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");
            Check(!string.IsNullOrEmpty(helper),
                "80C-4: native OpenH264 helper source exists");
            Check(helper.Contains("WelsCreateSVCEncoder")
                  && helper.Contains("InitializeExt")
                  && helper.Contains("EncodeFrame")
                  && helper.Contains("WelsDestroySVCEncoder"),
                "80C-5: helper uses OpenH264 encoder API");
            Check(helper.Contains("std::cout.write")
                  && helper.Contains("WriteLittleEndianLength")
                  && helper.Contains("std::cerr"),
                "80C-6: helper writes length-prefixed stdout and diagnostics to stderr");
            Check(helper.Contains("CAMERA_VIDEO_REAL_TIME")
                  && helper.Contains("RC_BITRATE_MODE")
                  && helper.Contains("SM_SINGLE_SLICE")
                  && helper.Contains("videoFormatI420"),
                "80C-7: helper uses real-time single-slice I420 encoder configuration");

            var readme = ReadRepoText("Scripts/native/openh264_probe/README.md");
            Check(readme.Contains("third-party/openh264")
                  && readme.Contains("source")
                  && readme.Contains("Do not commit")
                  && readme.Contains("Cisco"),
                "80C-8: helper README documents source-only third-party boundary");
        }

        private static void VerifySpikeDocumentation()
        {
            var readme = ReadRepoText("Scripts/native/openh264_probe/README.md");
            Check(readme.Contains("GREEN") && readme.Contains("YELLOW") && readme.Contains("RED") && readme.Contains("BLOCKED"),
                "80D-1: helper README documents four result exits");
            Check(readme.Contains("third-party/openh264")
                  && readme.Contains("source-only")
                  && readme.Contains("Cisco's prebuilt OpenH264 binaries"),
                "80D-2: helper README documents source-only OpenH264 stance");
            Check(readme.Contains("RGB24") && readme.Contains("I420") && readme.Contains("Manual Smoke"),
                "80D-3: helper README covers conversion and manual smoke");
        }

        private static bool HasCommittedOpenH264BinaryArtifacts()
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var roots = new[]
            {
                Path.Combine(root, "Packages"),
                Path.Combine(root, "Unity2Foxglove", "Assets")
            };
            var extensions = new[] { ".dll", ".exe", ".lib", ".so", ".dylib" };
            foreach (var searchRoot in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (name.IndexOf("openh264", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return needles.Any(needle =>
                text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : "";
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }
    }
}
