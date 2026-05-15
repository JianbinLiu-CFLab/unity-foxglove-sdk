// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 76 validation for H.265/HEVC foxglove.CompressedVideo mode.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the H.265/HEVC extension to the unified camera video path.
    /// </summary>
    public static class Phase76Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 76 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 76: Camera H.265 HEVC CompressedVideo Mode ===");
            _passed = 0;

            VerifyModeProfiles();
            VerifyPacketizer();
            VerifySidecarCommand();
            VerifyCameraPublisherSource();
            VerifyEditorAndDocs();

            Console.WriteLine($"Phase 76: {_passed} checks passed.");
        }

        private static void VerifyModeProfiles()
        {
            var modeType = Type.GetType("Unity.FoxgloveSDK.Components.CameraOutputMode, FoxgloveSdk.Tests");
            Check(modeType != null && modeType.IsEnum, "76A-1: CameraOutputMode enum exists");
            Check(Enum.GetNames(modeType).Contains("H265Ffmpeg"), "76A-2: CameraOutputMode exposes H265Ffmpeg");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H265Ffmpeg")) == 2,
                "76A-3: H265Ffmpeg enum value is stable at 2");

            var profileType = Type.GetType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile, FoxgloveSdk.Tests");
            Check(profileType != null, "76A-4: CameraVideoOutputProfile exists");
            var forMode = profileType.GetMethod("ForMode", BindingFlags.Public | BindingFlags.Static);
            var h265Profile = forMode.Invoke(null, new[] { Enum.Parse(modeType, "H265Ffmpeg") });

            Check(GetStringProperty(profileType, h265Profile, "DefaultTopic") == "/unity/camera",
                "76A-5: H.265 default topic is /unity/camera");
            Check(GetStringProperty(profileType, h265Profile, "SchemaName") == "foxglove.CompressedVideo",
                "76A-6: H.265 schema is foxglove.CompressedVideo");
            Check(GetStringProperty(profileType, h265Profile, "VideoFormat") == "h265",
                "76A-7: H.265 video format is h265");
            Check(GetStringProperty(profileType, h265Profile, "DisplayName").Contains("HEVC"),
                "76A-8: H.265 display name calls out HEVC");
            Check(!GetBoolProperty(profileType, h265Profile, "SupportsJson")
                    && GetBoolProperty(profileType, h265Profile, "SupportsProtobuf"),
                "76A-9: H.265 supports protobuf only");

            var builderType = Type.GetType("Foxglove.Schemas.CameraCompressedVideoBuilder, FoxgloveSdk.Tests");
            Check(builderType != null, "76A-10: CameraCompressedVideoBuilder exists");
            Check(GetStaticStringField(builderType, "H265Format") == "h265",
                "76A-11: builder exposes h265 format constant");

            var sample = Bytes(0, 0, 0, 1, 70, 1, 80, 0, 0, 0, 1, 38, 1, 1);
            var serialize = builderType.GetMethod(
                "Serialize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ulong), typeof(string), typeof(byte[]), typeof(string) },
                modifiers: null);
            var payload = (byte[])serialize.Invoke(null, new object[] { 1_778_800_001_234_567_890UL, "cam", sample, "h265" });
            var parsed = Foxglove.CompressedVideo.Parser.ParseFrom(payload);
            Check(parsed.Format == "h265", "76A-12: builder serializes format=h265");
            Check(parsed.FrameId == "cam" && parsed.Data.ToByteArray().SequenceEqual(sample),
                "76A-13: builder preserves H.265 frame id and bytes");
        }

        private static void VerifyPacketizer()
        {
            var packetizerType = Type.GetType("Foxglove.Schemas.Video.H265AnnexBAccessUnitPacketizer, FoxgloveSdk.Tests");
            Check(packetizerType != null, "76B-1: H265AnnexBAccessUnitPacketizer type exists");

            Check(InvokeStaticBool(packetizerType, "HasAnnexBStartCode", Bytes(0, 0, 1, 70, 1, 80)),
                "76B-2: packetizer recognizes three-byte Annex B start codes");
            Check(InvokeStaticBool(packetizerType, "HasAnnexBStartCode", Bytes(0, 0, 0, 1, 70, 1, 80)),
                "76B-3: packetizer recognizes four-byte Annex B start codes");

            var keyPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, keyPacketizer, Concat(
                H265Nal(35, 0x50),
                H265Nal(32, 0x01, 0x02),
                H265Nal(33, 0x03, 0x04),
                H265Nal(34, 0x05, 0x06),
                H265Nal(19, 0x88, 0x84)));
            Check(!InvokeTryDequeue(packetizerType, keyPacketizer, out _),
                "76B-4: packetizer waits for next AUD before emitting live keyframe unit");
            InvokeAppend(packetizerType, keyPacketizer, H265Nal(35, 0x50));
            Check(InvokeTryDequeue(packetizerType, keyPacketizer, out var keyUnit),
                "76B-5: next AUD emits previous HEVC keyframe access unit");
            Check(InvokeStaticBool(packetizerType, "ContainsIrapNal", keyUnit),
                "76B-6: emitted keyframe unit contains IRAP");
            Check(InvokeStaticBool(packetizerType, "ContainsVpsNal", keyUnit)
                    && InvokeStaticBool(packetizerType, "ContainsSpsNal", keyUnit)
                    && InvokeStaticBool(packetizerType, "ContainsPpsNal", keyUnit),
                "76B-7: emitted keyframe unit contains VPS/SPS/PPS");
            Check(InvokeStaticBool(packetizerType, "LooksLikeDecodableH265AccessUnit", keyUnit),
                "76B-8: emitted keyframe unit looks decodable");

            var deltaPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, deltaPacketizer, Concat(H265Nal(35, 0x50), H265Nal(1, 0x9a), H265Nal(35, 0x50)));
            Check(InvokeTryDequeue(packetizerType, deltaPacketizer, out var deltaUnit),
                "76B-9: packetizer emits non-IRAP VCL access units");
            Check(InvokeStaticBool(packetizerType, "ContainsVclNal", deltaUnit)
                    && !InvokeStaticBool(packetizerType, "ContainsIrapNal", deltaUnit),
                "76B-10: delta unit contains VCL without IRAP");

            var badPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, badPacketizer, Bytes(1, 2, 3, 4, 5, 6));
            Check(!InvokeFlush(packetizerType, badPacketizer, out _),
                "76B-11: non-Annex-B data emits no access units");

            var partialPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, partialPacketizer, Bytes(0, 0));
            InvokeAppend(packetizerType, partialPacketizer, Concat(Bytes(0, 1, 70, 1, 80), H265Nal(1, 0x80), Bytes(0)));
            InvokeAppend(packetizerType, partialPacketizer, Bytes(0, 0, 1, 70, 1, 80));
            Check(InvokeTryDequeue(packetizerType, partialPacketizer, out var partialUnit),
                "76B-12: packetizer handles chunks split across start-code boundaries");
            Check(InvokeStaticBool(packetizerType, "ContainsVclNal", partialUnit),
                "76B-13: partial chunk output preserves HEVC VCL data");
        }

        private static void VerifySidecarCommand()
        {
            var optionsType = Type.GetType("Foxglove.Schemas.Video.FfmpegH265EncoderOptions, FoxgloveSdk.Tests");
            Check(optionsType != null, "76C-1: FfmpegH265EncoderOptions type exists");

            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 320);
            SetField(optionsType, options, "Height", 240);
            SetField(optionsType, options, "FrameRate", 24);
            SetField(optionsType, options, "BitrateKbps", 1200);
            SetField(optionsType, options, "KeyframeInterval", 24);

            var createStartInfo = optionsType.GetMethod("CreateStartInfo", BindingFlags.Public | BindingFlags.Instance);
            Check(createStartInfo != null && createStartInfo.ReturnType == typeof(ProcessStartInfo),
                "76C-2: options create ProcessStartInfo");

            var psi = (ProcessStartInfo)createStartInfo.Invoke(options, null);
            Check(psi != null && !psi.UseShellExecute, "76C-3: FFmpeg process runs without shell execution");
            Check(psi.RedirectStandardInput && psi.RedirectStandardOutput && psi.RedirectStandardError,
                "76C-4: FFmpeg stdin/stdout/stderr are redirected");

            var command = psi.FileName + " " + psi.Arguments;
            CheckContains(command, "-f rawvideo", "76C-5: command declares rawvideo input");
            CheckContains(command, "-pix_fmt rgb24", "76C-6: command declares rgb24 input");
            CheckContains(command, "-s 320x240", "76C-7: command uses configured size");
            CheckContains(command, "-r 24", "76C-8: command uses configured frame rate");
            CheckContains(command, "-vf vflip", "76C-9: command flips Unity readback rows for Foxglove video display");
            CheckContains(command, "-c:v libx265", "76C-10: command uses libx265");
            CheckContains(command, "-pix_fmt yuv420p", "76C-11: command emits yuv420p HEVC");
            CheckContains(command, "-tune zerolatency", "76C-12: command requests zerolatency tuning");
            CheckContains(command, "-bf 0", "76C-13: command disables B frames");
            CheckContains(command, "-g 24", "76C-14: command uses configured keyframe interval");
            CheckContains(command, "-b:v 1200k", "76C-15: command uses configured bitrate");
            CheckContains(command, "aud=1", "76C-16: command emits HEVC AUD NAL units");
            CheckContains(command, "repeat-headers=1", "76C-17: command repeats VPS/SPS/PPS headers");
            CheckContains(command, "bframes=0", "76C-18: command disables x265 B frames");
            CheckContains(command, "-f hevc", "76C-19: command outputs HEVC elementary stream");
            CheckContains(command, "pipe:1", "76C-20: command writes Annex B stream to stdout");

            var sidecarType = Type.GetType("Foxglove.Schemas.Video.FfmpegH265EncoderSidecar, FoxgloveSdk.Tests");
            Check(sidecarType != null, "76C-21: FfmpegH265EncoderSidecar type exists");
            Check(sidecarType.GetMethod("Start", new[] { optionsType })?.ReturnType == typeof(bool),
                "76C-22: sidecar exposes Start(options)");
            Check(sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) })?.ReturnType == typeof(bool),
                "76C-23: sidecar exposes non-blocking frame submission");
            Check(sidecarType.GetMethod("TryDequeueAccessUnit") != null,
                "76C-24: sidecar exposes completed access-unit dequeue");
        }

        private static void VerifyCameraPublisherSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(!string.IsNullOrEmpty(source), "76D-1: FoxgloveCameraPublisher source exists");
            Check(source.Contains("CameraVideoCodec.H265"), "76D-2: camera routes H.265 video codec");
            Check(source.Contains("FfmpegH265EncoderSidecar"), "76D-3: camera integrates H.265 FFmpeg sidecar");
            Check(source.Contains("FfmpegH265EncoderOptions"), "76D-4: camera creates H.265 FFmpeg options");
            Check(source.Contains("CameraCompressedVideoBuilder.H265Format"), "76D-5: camera publishes format=h265");
            Check(source.Contains("profile.VideoFormat"), "76D-6: camera serializes video using resolved profile format");

            var lateUpdate = Slice(source, "private void LateUpdate()", "private void OnReadbackComplete");
            CheckOrdered(lateUpdate, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "76D-7: demand preflight happens after cadence");
            CheckOrdered(lateUpdate, "ShouldPreparePublishPayload()", "EnsureVideoSidecarStarted(profile)", "76D-8: demand preflight happens before sidecar startup");
            CheckOrdered(lateUpdate, "ShouldPreparePublishPayload()", "_captureCam.Render()", "76D-9: demand preflight happens before render");
            CheckOrdered(lateUpdate, "ShouldPreparePublishPayload()", "AsyncGPUReadback.Request", "76D-10: demand preflight happens before GPU readback");

            var sidecarStart = Slice(source, "private bool EnsureVideoSidecarStarted", "private void DrainEncodedAccessUnits");
            Check(!sidecarStart.Contains("EncodeToJPG") && !sidecarStart.Contains("CameraCompressedImageBuilder"),
                "76D-11: H.265 failure path does not fall back to JPEG");
        }

        private static void VerifyEditorAndDocs()
        {
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(!string.IsNullOrEmpty(editorSource), "76E-1: dedicated camera publisher editor exists");
            Check(editorSource.Contains("\"H.265 / HEVC (FFmpeg)\""),
                "76E-2: editor exposes H.265 / HEVC output label");
            Check(editorSource.Contains("CameraOutputMode.H265Ffmpeg"),
                "76E-3: editor handles H.265 mode explicitly");
            Check(editorSource.Contains("H.265/HEVC playback depends on platform decoder support"),
                "76E-4: editor warns that HEVC playback depends on platform decoder support");
            Check(editorSource.Contains("Check FFmpeg") && editorSource.Contains("Reveal Folder"),
                "76E-5: editor keeps FFmpeg check and reveal actions for video modes");

            var docs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md");
            Check(docs.Contains("H.265") && docs.Contains("HEVC"),
                "76E-6: Inspector reference documents H.265/HEVC mode");
            Check(docs.Contains("format") && docs.Contains("h265"),
                "76E-7: docs mention H.265 CompressedVideo format");
        }

        private static void CheckContains(string text, string pattern, string name)
            => Check(text.Contains(pattern), name);

        private static string GetStaticStringField(Type type, string name)
            => type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

        private static string GetStringProperty(Type type, object target, string name)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as string;

        private static bool GetBoolProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is bool b && b;
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            field.SetValue(target, value);
        }

        private static void InvokeAppend(Type type, object packetizer, byte[] data)
            => type.GetMethod("Append", new[] { typeof(byte[]) }).Invoke(packetizer, new object[] { data });

        private static bool InvokeTryDequeue(Type type, object packetizer, out byte[] accessUnit)
        {
            var args = new object[] { null };
            var result = (bool)type.GetMethod("TryDequeueAccessUnit").Invoke(packetizer, args);
            accessUnit = (byte[])args[0];
            return result;
        }

        private static bool InvokeFlush(Type type, object packetizer, out byte[] accessUnit)
        {
            var args = new object[] { null };
            var result = (bool)type.GetMethod("Flush").Invoke(packetizer, args);
            accessUnit = (byte[])args[0];
            return result;
        }

        private static bool InvokeStaticBool(Type type, string methodName, byte[] data)
            => (bool)type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { data });

        private static byte[] H265Nal(byte type, params byte[] payload)
        {
            var nal = new byte[4 + 2 + payload.Length];
            nal[0] = 0;
            nal[1] = 0;
            nal[2] = 0;
            nal[3] = 1;
            nal[4] = unchecked((byte)(type << 1));
            nal[5] = 1;
            Buffer.BlockCopy(payload, 0, nal, 6, payload.Length);
            return nal;
        }

        private static byte[] Bytes(params int[] values)
        {
            var bytes = new byte[values.Length];
            for (var i = 0; i < values.Length; i++)
                bytes[i] = unchecked((byte)values[i]);
            return bytes;
        }

        private static byte[] Concat(params byte[][] chunks)
        {
            var length = chunks.Sum(c => c?.Length ?? 0);
            var result = new byte[length];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return result;
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
