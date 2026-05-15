// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 75 validation for unified camera output mode UX and
// configured FFmpeg executable checking.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 75 camera output mode behavior using Unity-free
    /// reflection and source checks.
    /// </summary>
    public static class Phase75Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 75 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 75: Camera Output Mode UX and Configured FFmpeg Check ===");
            _passed = 0;

            VerifyModeProfiles();
            VerifyCameraPublisherSource();
            VerifyFfmpegExecutableCheck();
            VerifyCameraEditorSource();
            VerifyLegacyPublisherSource();

            Console.WriteLine($"Phase 75: {_passed} checks passed.");
        }

        private static void VerifyModeProfiles()
        {
            var modeType = Type.GetType("Unity.FoxgloveSDK.Components.CameraOutputMode, FoxgloveSdk.Tests");
            Check(modeType != null && modeType.IsEnum, "75A-1: CameraOutputMode enum exists");
            Check(Enum.GetNames(modeType).Contains("Jpeg"), "75A-2: CameraOutputMode exposes Jpeg");
            Check(Enum.GetNames(modeType).Contains("H264Ffmpeg"), "75A-3: CameraOutputMode exposes H264Ffmpeg");
            Check(!Enum.GetNames(modeType).Contains("H265Ffmpeg"), "75A-4: Phase 75 does not expose H265Ffmpeg");
            Check(Convert.ToInt32(Enum.Parse(modeType, "Jpeg")) == 0, "75A-5: Jpeg enum value is stable at 0");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264Ffmpeg")) == 1, "75A-6: H264Ffmpeg enum value is stable at 1");

            var profileType = Type.GetType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile, FoxgloveSdk.Tests");
            Check(profileType != null, "75A-7: CameraVideoOutputProfile exists as codec/profile boundary");

            var forMode = profileType.GetMethod("ForMode", BindingFlags.Public | BindingFlags.Static);
            Check(forMode != null && forMode.ReturnType == profileType, "75A-8: profile exposes ForMode(mode)");

            var jpegProfile = forMode.Invoke(null, new[] { Enum.Parse(modeType, "Jpeg") });
            var h264Profile = forMode.Invoke(null, new[] { Enum.Parse(modeType, "H264Ffmpeg") });

            Check(GetStringProperty(profileType, jpegProfile, "DefaultTopic") == "/unity/camera",
                "75A-9: JPEG default topic is /unity/camera");
            Check(GetStringProperty(profileType, h264Profile, "DefaultTopic") == "/unity/camera",
                "75A-10: H.264 default topic matches JPEG /unity/camera");
            Check(GetStringProperty(profileType, jpegProfile, "SchemaName") == "foxglove.CompressedImage",
                "75A-11: JPEG schema is foxglove.CompressedImage");
            Check(GetStringProperty(profileType, h264Profile, "SchemaName") == "foxglove.CompressedVideo",
                "75A-12: H.264 schema is foxglove.CompressedVideo");
            Check(GetStringProperty(profileType, h264Profile, "VideoFormat") == "h264",
                "75A-13: H.264 video format is h264");
            Check(GetBoolProperty(profileType, jpegProfile, "SupportsJson")
                    && GetBoolProperty(profileType, jpegProfile, "SupportsProtobuf"),
                "75A-14: JPEG supports JSON and protobuf");
            Check(!GetBoolProperty(profileType, h264Profile, "SupportsJson")
                    && GetBoolProperty(profileType, h264Profile, "SupportsProtobuf"),
                "75A-15: H.264 supports protobuf only");
        }

        private static void VerifyCameraPublisherSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(!string.IsNullOrEmpty(source), "75B-1: FoxgloveCameraPublisher source exists");
            Check(source.Contains("_outputMode = CameraOutputMode.Jpeg"), "75B-2: camera defaults to JPEG mode");
            Check(source.Contains("CameraVideoOutputProfile.ForMode"), "75B-3: camera resolves schema through profile");
            Check(source.Contains("SchemaName =>"), "75B-4: camera exposes mode-aware SchemaName");
            Check(source.Contains("SupportsJsonEncoding =>"), "75B-5: camera exposes mode-aware JSON support");
            Check(source.Contains("SupportsProtobufEncoding =>"), "75B-6: camera exposes protobuf support");
            Check(source.Contains("_videoBitrateKbps"), "75B-7: camera uses codec-neutral video bitrate field");
            Check(source.Contains("_videoKeyframeInterval"), "75B-8: camera uses codec-neutral keyframe field");
            Check(source.Contains("_videoMaxOutputQueue"), "75B-9: camera uses codec-neutral output queue field");
            Check(!source.Contains("_h264BitrateKbps"), "75B-10: camera does not use H.264-specific bitrate field");
            Check(!source.Contains("_h264MaxOutputQueue"), "75B-11: camera does not use H.264-specific queue field");

            Check(source.Contains("FfmpegH264EncoderSidecar"), "75B-12: camera integrates H.264 FFmpeg sidecar");
            Check(source.Contains("FfmpegH264EncoderOptions"), "75B-13: camera creates H.264 FFmpeg options");
            Check(source.Contains("CameraCompressedVideoBuilder.Serialize"), "75B-14: camera serializes CompressedVideo in H.264 mode");
            Check(source.Contains("CameraCompressedVideoBuilder.H264Format"), "75B-15: camera publishes format=h264");
            Check(source.Contains("StopVideoSidecar") || source.Contains("StopSidecar"),
                "75B-16: camera can stop the video sidecar");
            Check(source.Contains("_ffmpegPath = \"\"") && !source.Contains("_ffmpegPath = \"ffmpeg\""),
                "75B-17: camera leaves FFmpeg path empty by default");

            var lateUpdate = Slice(source, "private void LateUpdate()", "private void OnReadbackComplete");
            CheckOrdered(lateUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "75B-18: demand preflight happens after cadence");
            CheckOrdered(lateUpdate, "ShouldPreparePublishPayload()", "_captureCam.Render()", "75B-19: demand preflight happens before render");
            CheckOrdered(lateUpdate, "ShouldPreparePublishPayload()", "AsyncGPUReadback.Request", "75B-20: demand preflight happens before GPU readback");

            var h264FailurePath = Slice(source, "private bool EnsureVideoSidecarStarted", "private void DrainEncodedAccessUnits");
            Check(!h264FailurePath.Contains("EncodeToJPG") && !h264FailurePath.Contains("CameraCompressedImageBuilder"),
                "75B-21: H.264 failure path does not fall back to JPEG");
        }

        private static void VerifyFfmpegExecutableCheck()
        {
            var checkType = Type.GetType("Foxglove.Schemas.Video.FfmpegExecutableCheck, FoxgloveSdk.Tests");
            Check(checkType != null, "75C-1: FfmpegExecutableCheck type exists");

            var statusType = Type.GetType("Foxglove.Schemas.Video.FfmpegExecutableStatus, FoxgloveSdk.Tests");
            Check(statusType != null && statusType.IsEnum, "75C-2: FfmpegExecutableStatus enum exists");
            Check(Enum.GetNames(statusType).Contains("NotChecked"), "75C-3: status includes NotChecked");
            Check(Enum.GetNames(statusType).Contains("Found"), "75C-4: status includes Found");
            Check(Enum.GetNames(statusType).Contains("Missing"), "75C-5: status includes Missing");
            Check(Enum.GetNames(statusType).Contains("Invalid"), "75C-6: status includes Invalid");

            var check = checkType.GetMethod("Check", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(int) }, null);
            Check(check != null, "75C-7: checker exposes Check(configuredPath, timeoutMs)");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegExecutableCheck.cs");
            Check(!source.Contains("SetEnvironmentVariable"), "75C-8: checker does not mutate environment variables");
            Check(!source.Contains("GetFiles") && !source.Contains("EnumerateFiles"), "75C-9: checker does not recursively search files");
            Check(!source.Contains("Program Files"), "75C-10: checker does not probe Program Files");
            Check(!source.Contains("C:\\\\Tools"), "75C-11: checker does not hard-code C:\\Tools");
            Check(source.Contains("SystemPathExecutable") && source.Contains("string.IsNullOrWhiteSpace(configuredPath)"),
                "75C-12: empty configured path intentionally checks ffmpeg through system PATH");
            Check(source.Contains("FfmpegExecutableResolver.ResolveExecutablePath"),
                "75C-13: checker resolves FFmpeg before spawning the process");
            Check(source.Contains("EnvironmentVariableTarget.User") && source.Contains("EnvironmentVariableTarget.Machine"),
                "75C-14: empty path can resolve user and machine PATH even when Unity inherited a stale PATH");

            var result = check.Invoke(null, new object[] { @"Z:\definitely_missing_ffmpeg.exe", 250 });
            var status = result.GetType().GetProperty("Status")?.GetValue(result);
            Check(status != null && status.ToString() == "Missing", "75C-15: rooted missing executable returns Missing");

            var sidecarSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            Check(sidecarSource.Contains("BuildStartFailureMessage"),
                "75C-16: sidecar wraps FFmpeg process start failures with actionable guidance");
            Check(sidecarSource.Contains("Unity process PATH") && sidecarSource.Contains("FFmpeg Path"),
                "75C-17: missing ffmpeg guidance explains Unity PATH and explicit FFmpeg Path");

            var optionsSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderOptions.cs");
            Check(optionsSource.Contains("FfmpegExecutableResolver.ResolveExecutablePath"),
                "75C-18: H.264 runtime uses resolved FFmpeg executable path");

            var resolverType = Type.GetType("Foxglove.Schemas.Video.FfmpegExecutableResolver, FoxgloveSdk.Tests");
            var resolve = resolverType?.GetMethod("ResolveExecutablePath", BindingFlags.Public | BindingFlags.Static);
            Check(resolve != null, "75C-19: resolver exposes ResolveExecutablePath(configuredPath)");

            var tempDir = Path.Combine(Path.GetTempPath(), "unity2foxglove-ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var exeName = Path.DirectorySeparatorChar == '\\' ? "ffmpeg.exe" : "ffmpeg";
                var exePath = Path.Combine(tempDir, exeName);
                File.WriteAllText(exePath, "");
                var resolved = resolve.Invoke(null, new object[] { tempDir }) as string;
                Check(resolved == Path.GetFullPath(exePath),
                    "75C-20: directory FFmpeg path resolves executable inside that directory");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        private static void VerifyCameraEditorSource()
        {
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(!string.IsNullOrEmpty(editorSource), "75D-1: dedicated camera publisher editor exists");
            Check(editorSource.Contains("[CustomEditor(typeof(FoxgloveCameraPublisher))]"),
                "75D-2: camera editor targets FoxgloveCameraPublisher");
            Check(editorSource.Contains("Camera Output Mode"), "75D-3: editor draws Camera Output Mode");
            Check(editorSource.Contains("CameraOutputModeLabels"),
                "75D-4: editor uses explicit camera output mode labels");
            Check(editorSource.Contains("\"JPEG\"") && editorSource.Contains("\"H.264 (FFmpeg)"),
                "75D-5: editor labels use canonical JPEG and FFmpeg casing");
            Check(!editorSource.Contains("PropertyField(outputMode"),
                "75D-6: editor does not expose raw enum names for camera output mode");
            Check(editorSource.Contains("GUILayout.Button(\"...\", GUILayout.Width(30))"),
                "75D-7: editor uses compact ellipsis browse button");
            Check(!editorSource.Contains("GUILayout.Button(\"Browse\""),
                "75D-8: editor does not use a full-width Browse button");
            Check(editorSource.Contains("Check FFmpeg"), "75D-9: editor includes Check FFmpeg action");
            Check(editorSource.Contains("Reveal Folder"), "75D-10: editor includes Reveal Folder action");
            Check(editorSource.Contains("Empty path uses system PATH"),
                "75D-11: editor explains empty FFmpeg path PATH behavior");
            Check(editorSource.Contains("_ffmpegCheck.ExecutablePath"),
                "75D-12: Reveal Folder can use the checked resolved executable path");
            Check(editorSource.Contains("GetFfmpegFolderPath") && editorSource.Contains("Directory.Exists(configuredPath)"),
                "75D-13: Reveal Folder treats directory paths as folders, not parent-file paths");
            Check(editorSource.Contains("EditorUtility.OpenWithDefaultApp(dir)") && !editorSource.Contains("EditorUtility.RevealInFinder(dir)"),
                "75D-14: Reveal Folder opens the resolved FFmpeg folder itself");
            Check(editorSource.Contains("checkRequested") && editorSource.Contains("revealRequested"),
                "75D-15: editor defers check/reveal side effects until after GUILayout scope closes");
            Check(editorSource.Contains("ApplyTopicForModeChange"), "75D-16: editor implements safe topic auto-switch");
            Check(editorSource.Contains("Video Bitrate Kbps"), "75D-17: editor uses codec-neutral video bitrate label");
            Check(editorSource.Contains("Supported Encodings"), "75D-18: editor preserves shared encoding summary");
            Check(editorSource.Contains("Effective Publish Rate Hz"), "75D-19: editor preserves effective publish rate summary");

            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            Check(!managerEditor.Contains("Camera Output Mode"), "75D-20: camera-specific UI is not in manager editor");
            Check(!managerEditor.Contains("Check FFmpeg"), "75D-21: FFmpeg camera UI is not in manager editor");
        }

        private static void VerifyLegacyPublisherSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");
            Check(source.Contains("class FoxgloveCompressedVideoCameraPublisher"),
                "75E-1: legacy standalone H.264 component remains");
            Check(source.Contains("[AddComponentMenu(\"\")]"),
                "75E-2: legacy standalone H.264 component is hidden from normal Add Component menu");
            Check(source.Contains("Prefer FoxgloveCameraPublisher"),
                "75E-3: legacy component points users to the unified camera publisher");
        }

        private static string GetStringProperty(Type type, object target, string name)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as string;

        private static bool GetBoolProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is bool b && b;
        }

        private static void CheckOrdered(string text, string first, string second, string name)
        {
            var firstIdx = text.IndexOf(first, StringComparison.Ordinal);
            var secondIdx = text.IndexOf(second, StringComparison.Ordinal);
            Check(firstIdx >= 0 && secondIdx >= 0 && firstIdx < secondIdx, name);
        }

        private static string Slice(string text, string start, string end)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var startIdx = text.IndexOf(start, StringComparison.Ordinal);
            if (startIdx < 0) return "";
            var endIdx = text.IndexOf(end, startIdx + start.Length, StringComparison.Ordinal);
            return endIdx < 0 ? text.Substring(startIdx) : text.Substring(startIdx, endIdx - startIdx);
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
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[OK] " + name);
        }
    }
}
