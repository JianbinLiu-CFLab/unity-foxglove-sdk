// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 81 validation for OpenH264 official-binary camera integration.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 81 through source checks and Unity-free conversion tests.
    /// The test assembly intentionally avoids requiring an OpenH264 DLL or helper
    /// executable at test time.
    /// </summary>
    public static class Phase81Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 81: OpenH264 Official Binary Camera Integration ===");
            _passed = 0;

            VerifyCameraModeProfile();
            VerifyRuntimeRoutingSources();
            VerifyInspectorUxSources();
            VerifyInstallerSources();
            VerifyNativeHelperSource();
            VerifyNoBundledOpenH264Binaries();
            VerifyRgb24ToI420Converter();
            VerifyOpenH264OptionsValidation();

            Console.WriteLine($"Phase 81: {_passed} checks passed.");
        }

        private static void VerifyCameraModeProfile()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs");
            Check(source.Contains("H264OpenH264 = 3"),
                "81A-1: CameraOutputMode reserves value 3 for H.264 OpenH264");
            Check(source.Contains("H.264 (OpenH264)")
                  && source.Contains("CameraOutputModeDefaults.H264Topic")
                  && source.Contains("CameraOutputModeDefaults.H264Schema")
                  && source.Contains("CameraCompressedVideoBuilder.H264Format"),
                "81A-2: OpenH264 profile uses /unity/camera, CompressedVideo, and format=h264");
            Check(source.Contains("supportsJson: false") && source.Contains("supportsProtobuf: true"),
                "81A-3: OpenH264 mode is protobuf-only");
        }

        private static void VerifyRuntimeRoutingSources()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(publisher.Contains("_openH264HelperPath") && publisher.Contains("_openH264DllPath"),
                "81B-1: camera publisher serializes OpenH264 helper and DLL paths");
            Check(publisher.Contains("_openH264MaxInputQueue"),
                "81B-2: camera publisher exposes an OpenH264 input queue limit");
            Check(publisher.Contains("OpenH264EncoderSidecar") && publisher.Contains("OpenH264EncoderOptions"),
                "81B-3: camera publisher routes OpenH264 mode to OpenH264 sidecar");
            Check(publisher.Contains("Rgb24ToI420Converter.TryConvertRgb24ToI420"),
                "81B-4: camera publisher converts RGB24 readback data to I420 for OpenH264");
            Check(publisher.Contains("CameraCompressedVideoBuilder.H264Format"),
                "81B-5: OpenH264 mode publishes format=h264");

            var iface = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/ICameraVideoEncoderSidecar.cs");
            Check(iface.Contains("interface ICameraVideoEncoderSidecar")
                  && iface.Contains("LastDiagnosticLine")
                  && iface.Contains("TrySubmitFrame")
                  && iface.Contains("TryDequeueAccessUnit"),
                "81B-6: codec-neutral sidecar interface exists");

            var ffmpegIface = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/IFfmpegVideoEncoderSidecar.cs");
            Check(ffmpegIface.Contains("IFfmpegVideoEncoderSidecar : ICameraVideoEncoderSidecar"),
                "81B-7: FFmpeg sidecar interface extends codec-neutral interface");

            var h264Sidecar = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265Sidecar = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            Check(h264Sidecar.Contains("LastDiagnosticLine") && h265Sidecar.Contains("LastDiagnosticLine"),
                "81B-8: FFmpeg sidecars expose codec-neutral diagnostics");
        }

        private static void VerifyInspectorUxSources()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editor.Contains("\"H.264 (OpenH264)\""),
                "81C-1: camera output dropdown includes H.264 OpenH264");
            Check(editor.Contains("OpenH264 Helper") && editor.Contains("OpenH264 DLL"),
                "81C-2: OpenH264 section separates helper and DLL configuration");
            Check(editor.Contains("GUILayout.Button(\"...\", GUILayout.Width(30))"),
                "81C-3: OpenH264 path fields use compact three-dot browse buttons");
            Check(!editor.Contains("GUILayout.Button(\"Browse\")"),
                "81C-4: OpenH264 section avoids full-width Browse buttons");
            Check(editor.Contains("Check OpenH264") && editor.Contains("Install OpenH264") && editor.Contains("Reveal Folder"),
                "81C-5: OpenH264 section exposes check, install, and reveal actions");
            Check(editor.Contains("OpenH264 Video Codec provided by Cisco Systems, Inc."),
                "81C-6: Inspector shows Cisco OpenH264 attribution");
            var installIndex = editor.IndexOf("Install OpenH264 Runtime...", StringComparison.Ordinal);
            var checkIndex = editor.IndexOf("Check OpenH264", StringComparison.Ordinal);
            Check(installIndex >= 0 && checkIndex >= 0 && installIndex < checkIndex,
                "81C-7: OpenH264 install action is labeled as runtime install and appears before check");
        }

        private static void VerifyInstallerSources()
        {
            var manifest = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryManifest.cs");
            Check(manifest.Contains("v2.6.0") && manifest.Contains("openh264-2.6.0-win64.dll.bz2"),
                "81D-1: installer pins Cisco OpenH264 v2.6.0 Windows x64 asset");
            Check(!manifest.Contains("latest"),
                "81D-2: installer manifest does not use latest");
            Check(manifest.Contains("BINARY_LICENSE") && manifest.Contains("OpenH264 Video Codec provided by Cisco Systems, Inc."),
                "81D-3: installer manifest carries Cisco license and attribution metadata");
            Check(manifest.Contains("HelperFileName") && manifest.Contains("HeaderIncludeRelativePath") && manifest.Contains("HelperSourceRelativePath"),
                "81D-4: installer manifest defines helper output, vendored header, and helper source paths");

            var installer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            Check(installer.Contains("DownloadFile") || installer.Contains("DownloadFileTaskAsync") || installer.Contains("HttpClient"),
                "81D-5: installer has explicit download implementation");
            Check(!installer.Contains("SetEnvironmentVariable"),
                "81D-6: installer does not mutate PATH");
            Check(!installer.Contains("Program Files"),
                "81D-7: installer does not target Program Files");
            Check(installer.Contains(".bz2") && (installer.Contains("BZip2") || installer.Contains("SharpZipLib")),
                "81D-8: installer handles the official bz2-compressed DLL asset");
            Check(installer.Contains("THIRD_PARTY_NOTICES") || installer.Contains("SharpZipLib") || installer.Contains("MIT"),
                "81D-9: installer documents BZip2 decompressor licensing when third-party decompression is used");
            Check(installer.Contains("BuildHelperExecutable") && installer.Contains("cl /nologo") && installer.Contains("vcvars64.bat"),
                "81D-10: installer builds the OpenH264 helper executable locally");
            Check(installer.Contains("GetFinalHelperPath") && installer.Contains("GetFinalDllPath"),
                "81D-11: installer writes helper and DLL into the same versioned runtime directory");
            Check(installer.Contains("Manual fallback") && !installer.Contains("releases/latest"),
                "81D-12: installer preserves pinned-download behavior and documents manual fallback");

            var checker = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            Check(checker.Contains("Selected OpenH264 helper is outdated")
                  && checker.Contains("--openh264-dll <path>"),
                "81D-13: checker reports stale Phase80 helpers with a clear compatibility error");

            var location = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264InstallLocation.cs");
            Check(location.Contains("LocalApplicationData")
                  && location.Contains("Unity2Foxglove")
                  && location.Contains("OpenH264"),
                "81D-14: installer default root is a per-user Unity2Foxglove OpenH264 cache");
            Check(location.Contains("EditorPrefs"),
                "81D-15: installer remembers validated custom roots through EditorPrefs");
            Check(ContainsAll(location, "Assets", "Packages", "ProjectSettings", ".git")
                  && (location.Contains("ProgramFiles") || location.Contains("Program Files")),
                "81D-16: installer rejects project, package, source-control, and system roots");
            Check(location.Contains("GetFinalHelperPath") && location.Contains("GetVersionedDirectory"),
                "81D-17: install location exposes helper and DLL targets in the same versioned directory");

            var notices = ReadRepoText("THIRD_PARTY_NOTICES.md");
            Check(notices.Contains("OpenH264 headers") && notices.Contains("BSD-2-Clause"),
                "81D-18: third-party notices document vendored OpenH264 headers");
        }

        private static void VerifyNativeHelperSource()
        {
            var helper = ReadRepoText("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");
            Check(helper.Contains("--openh264-dll"),
                "81E-1: native helper accepts explicit OpenH264 DLL path");
            Check(helper.Contains("LoadLibrary") && helper.Contains("GetProcAddress"),
                "81E-2: native helper dynamically loads OpenH264 on Windows");
            Check(helper.Contains("WelsCreateSVCEncoder") && helper.Contains("WelsDestroySVCEncoder"),
                "81E-3: native helper resolves OpenH264 encoder entry points");
            Check(helper.Contains("WriteLittleEndianLength") && helper.Contains("std::cout.write"),
                "81E-4: native helper keeps length-prefixed binary stdout");

            var packageHelper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp");
            Check(packageHelper.Contains("--openh264-dll") && packageHelper.Contains("LoadLibrary"),
                "81E-5: package carries the helper source used by the runtime installer");

            var packageHeader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/v2.6.0/include/wels/codec_api.h");
            Check(packageHeader.Contains("ISVCEncoder") && packageHeader.Contains("WelsCreateSVCEncoder"),
                "81E-6: package carries the pinned OpenH264 encoder headers needed for local helper builds");
        }

        private static void VerifyNoBundledOpenH264Binaries()
        {
            Check(!HasCommittedOpenH264BinaryArtifacts(),
                "81F-1: no OpenH264 binary artifacts are committed under package/assets paths");

            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!ContainsAny(packageJson, "openh264", "OpenH264"),
                "81F-2: SDK package.json has no OpenH264 dependency");
        }

        private static void VerifyRgb24ToI420Converter()
        {
            var converter = Type.GetType("Foxglove.Schemas.Video.Rgb24ToI420Converter, FoxgloveSdk.Tests");
            Check(converter != null, "81G-1: RGB24-to-I420 converter type exists");

            var method = converter.GetMethod(
                "TryConvertRgb24ToI420",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(byte[]), typeof(int), typeof(int), typeof(byte[]), typeof(bool), typeof(string).MakeByRefType() },
                null);
            Check(method != null, "81G-2: converter exposes tested TryConvertRgb24ToI420 API");

            var rgb = new byte[]
            {
                255, 0, 0,   0, 255, 0,
                0, 0, 255,   255, 255, 255
            };
            var i420 = new byte[6];
            var args = new object[] { rgb, 2, 2, i420, false, "" };
            Check((bool)method.Invoke(null, args), "81G-3: converter accepts a valid 2x2 RGB24 frame");
            Check(i420.Length == 6, "81G-4: 2x2 I420 output has 6 bytes");
            Check(i420[0] == ComputeY(255, 0, 0)
                  && i420[1] == ComputeY(0, 255, 0)
                  && i420[2] == ComputeY(0, 0, 255)
                  && i420[3] == ComputeY(255, 255, 255),
                "81G-5: converter preserves row order when flipVertical=false");

            var flipped = new byte[6];
            args = new object[] { rgb, 2, 2, flipped, true, "" };
            Check((bool)method.Invoke(null, args), "81G-6: converter accepts flipVertical=true");
            Check(flipped[0] == ComputeY(0, 0, 255)
                  && flipped[1] == ComputeY(255, 255, 255)
                  && flipped[2] == ComputeY(255, 0, 0)
                  && flipped[3] == ComputeY(0, 255, 0),
                "81G-7: converter vertically flips source rows when requested");

            args = new object[] { new byte[3 * 3 * 2], 3, 2, new byte[9], false, "" };
            Check(!(bool)method.Invoke(null, args) && ((string)args[5]).Contains("even"),
                "81G-8: converter rejects odd width with clear error");

            args = new object[] { new byte[2 * 3 * 3], 2, 3, new byte[9], false, "" };
            Check(!(bool)method.Invoke(null, args) && ((string)args[5]).Contains("even"),
                "81G-9: converter rejects odd height with clear error");

            args = new object[] { new byte[1], 2, 2, new byte[6], false, "" };
            Check(!(bool)method.Invoke(null, args) && ((string)args[5]).Contains("RGB24"),
                "81G-10: converter rejects short RGB24 buffers");

            args = new object[] { rgb, 2, 2, new byte[5], false, "" };
            Check(!(bool)method.Invoke(null, args) && ((string)args[5]).Contains("I420"),
                "81G-11: converter rejects short I420 buffers");
        }

        private static void VerifyOpenH264OptionsValidation()
        {
            var optionsType = Type.GetType("Foxglove.Schemas.Video.OpenH264EncoderOptions, FoxgloveSdk.Tests");
            Check(optionsType != null, "81H-1: OpenH264 encoder options type exists");

            var tempDir = Path.Combine(Path.GetTempPath(), "unity2foxglove_phase81_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var helper = Path.Combine(tempDir, "openh264_probe_encoder.exe");
                var compressed = Path.Combine(tempDir, "openh264-2.6.0-win64.dll.bz2");
                File.WriteAllBytes(helper, new byte[] { 0 });
                File.WriteAllBytes(compressed, new byte[] { 0 });

                var options = Activator.CreateInstance(optionsType);
                optionsType.GetField("HelperExecutablePath").SetValue(options, helper);
                optionsType.GetField("OpenH264DllPath").SetValue(options, compressed);
                optionsType.GetField("Width").SetValue(options, 16);
                optionsType.GetField("Height").SetValue(options, 16);
                optionsType.GetField("FrameRate").SetValue(options, 1);
                optionsType.GetField("BitrateKbps").SetValue(options, 64);
                optionsType.GetField("KeyframeInterval").SetValue(options, 1);

                var args = new object[] { "" };
                var validate = optionsType.GetMethod("Validate", new[] { typeof(string).MakeByRefType() });
                Check(validate != null, "81H-2: OpenH264 options expose Validate(out error)");
                Check(!(bool)validate.Invoke(options, args) && ((string)args[0]).Contains(".bz2"),
                    "81H-3: OpenH264 options reject compressed bz2 downloads as DLL paths");
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                }
            }
        }

        private static byte ComputeY(int r, int g, int b)
            => ClampToByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static byte ClampToByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
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

        private static bool ContainsAll(string text, params string[] needles)
            => needles.All(needle => text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);

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
