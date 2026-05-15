// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 74 validation for H.264 foxglove.CompressedVideo publishing.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the first H.264 <c>foxglove.CompressedVideo</c> data path:
    /// protobuf builder, Annex B packetizer, FFmpeg command surface, and
    /// Unity publisher source integration.
    /// </summary>
    public static class Phase74Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 74 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 74: Camera H.264 CompressedVideo Sidecar ===");
            _passed = 0;

            VerifyCatalogAndBuilder();
            VerifyPacketizer();
            VerifySidecarCommand();
            VerifyPublisherSource();

            Console.WriteLine($"Phase 74: {_passed} checks passed.");
        }

        private static void VerifyCatalogAndBuilder()
        {
            Check(typeof(Foxglove.CompressedVideo) != null,
                "74A-1: generated Foxglove.CompressedVideo type exists");
            Check(FoxgloveProtoSchemaCatalog.TryGet("foxglove.CompressedVideo", out var entry),
                "74A-2: protobuf schema catalog contains foxglove.CompressedVideo");
            Check(entry != null && entry.ClrType == typeof(Foxglove.CompressedVideo),
                "74A-3: CompressedVideo catalog entry points at generated CLR type");

            var builderType = Type.GetType("Foxglove.Schemas.CameraCompressedVideoBuilder, FoxgloveSdk.Tests");
            Check(builderType != null, "74A-4: CameraCompressedVideoBuilder type exists");
            Check(GetStaticStringField(builderType, "H264Format") == "h264",
                "74A-5: builder exposes h264 format constant");

            var sample = new byte[] { 0, 0, 0, 1, 9, 0xf0, 0, 0, 0, 1, 0x65, 0x88 };
            var serialize = builderType.GetMethod(
                "Serialize",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ulong), typeof(string), typeof(byte[]), typeof(string) },
                modifiers: null);
            Check(serialize != null && serialize.ReturnType == typeof(byte[]),
                "74A-6: builder exposes Serialize(unixNs, frameId, bytes, format)");

            var payload = (byte[])serialize.Invoke(null, new object[] { 1_778_800_001_234_567_890UL, "cam", sample, "h264" });
            var parsed = Foxglove.CompressedVideo.Parser.ParseFrom(payload);
            Check(parsed.Timestamp.Seconds == 1_778_800_001L && parsed.Timestamp.Nanos == 234_567_890,
                "74A-7: builder timestamp roundtrips");
            Check(parsed.FrameId == "cam", "74A-8: builder frame id roundtrips");
            Check(parsed.Format == "h264", "74A-9: builder format is h264");
            Check(parsed.Data.ToByteArray().SequenceEqual(sample), "74A-10: builder preserves H.264 access-unit bytes");
        }

        private static void VerifyPacketizer()
        {
            var packetizerType = Type.GetType("Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer, FoxgloveSdk.Tests");
            Check(packetizerType != null, "74B-1: H264AnnexBAccessUnitPacketizer type exists");

            Check(InvokeStaticBool(packetizerType, "HasAnnexBStartCode", Bytes(0, 0, 1, 9, 0xf0)),
                "74B-2: packetizer recognizes three-byte Annex B start codes");
            Check(InvokeStaticBool(packetizerType, "HasAnnexBStartCode", Bytes(0, 0, 0, 1, 9, 0xf0)),
                "74B-3: packetizer recognizes four-byte Annex B start codes");

            var keyPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, keyPacketizer, Concat(
                Nal(9, 0xf0),
                Nal(7, 0x42, 0x00, 0x1e),
                Nal(8, 0xce, 0x06, 0xe2),
                Nal(5, 0x88, 0x84)));
            Check(!InvokeTryDequeue(packetizerType, keyPacketizer, out _),
                "74B-4: packetizer waits for next AUD before emitting a live keyframe unit");
            InvokeAppend(packetizerType, keyPacketizer, Nal(9, 0xf0));
            Check(InvokeTryDequeue(packetizerType, keyPacketizer, out var keyUnit),
                "74B-5: next AUD emits previous keyframe access unit");
            Check(InvokeStaticBool(packetizerType, "ContainsIdrNal", keyUnit),
                "74B-6: emitted keyframe unit contains IDR");
            Check(InvokeStaticBool(packetizerType, "ContainsSpsNal", keyUnit)
                    && InvokeStaticBool(packetizerType, "ContainsPpsNal", keyUnit),
                "74B-7: emitted keyframe unit contains SPS and PPS");
            Check(InvokeStaticBool(packetizerType, "LooksLikeDecodableH264AccessUnit", keyUnit),
                "74B-8: emitted keyframe unit looks decodable");

            var deltaPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, deltaPacketizer, Concat(Nal(9, 0xf0), Nal(1, 0x9a), Nal(9, 0xf0)));
            Check(InvokeTryDequeue(packetizerType, deltaPacketizer, out var deltaUnit),
                "74B-9: packetizer emits non-IDR VCL access units");
            Check(InvokeStaticBool(packetizerType, "ContainsVclNal", deltaUnit)
                    && !InvokeStaticBool(packetizerType, "ContainsIdrNal", deltaUnit),
                "74B-10: delta unit contains VCL without IDR");

            var badPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, badPacketizer, Bytes(1, 2, 3, 4, 5, 6));
            Check(!InvokeFlush(packetizerType, badPacketizer, out _),
                "74B-11: non-Annex-B data emits no access units");

            var partialPacketizer = Activator.CreateInstance(packetizerType);
            InvokeAppend(packetizerType, partialPacketizer, Bytes(0, 0));
            InvokeAppend(packetizerType, partialPacketizer, Concat(Bytes(0, 1, 9, 0xf0), Nal(1, 0x80), Bytes(0)));
            InvokeAppend(packetizerType, partialPacketizer, Bytes(0, 0, 1, 9, 0xf0));
            Check(InvokeTryDequeue(packetizerType, partialPacketizer, out var partialUnit),
                "74B-12: packetizer handles chunks split across start-code boundaries");
            Check(InvokeStaticBool(packetizerType, "ContainsVclNal", partialUnit),
                "74B-13: partial chunk output preserves VCL data");
        }

        private static void VerifySidecarCommand()
        {
            var optionsType = Type.GetType("Foxglove.Schemas.Video.FfmpegH264EncoderOptions, FoxgloveSdk.Tests");
            Check(optionsType != null, "74C-1: FfmpegH264EncoderOptions type exists");

            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 320);
            SetField(optionsType, options, "Height", 240);
            SetField(optionsType, options, "FrameRate", 24);
            SetField(optionsType, options, "BitrateKbps", 1200);
            SetField(optionsType, options, "KeyframeInterval", 24);

            var createStartInfo = optionsType.GetMethod("CreateStartInfo", BindingFlags.Public | BindingFlags.Instance);
            Check(createStartInfo != null && createStartInfo.ReturnType == typeof(ProcessStartInfo),
                "74C-2: options create ProcessStartInfo");

            var psi = (ProcessStartInfo)createStartInfo.Invoke(options, null);
            Check(psi != null && !psi.UseShellExecute, "74C-3: FFmpeg process runs without shell execution");
            Check(psi.RedirectStandardInput && psi.RedirectStandardOutput && psi.RedirectStandardError,
                "74C-4: FFmpeg stdin/stdout/stderr are redirected");

            var command = psi.FileName + " " + psi.Arguments;
            CheckContains(command, "-f rawvideo", "74C-5: command declares rawvideo input");
            CheckContains(command, "-pix_fmt rgb24", "74C-6: command declares rgb24 input");
            CheckContains(command, "-s 320x240", "74C-7: command uses configured size");
            CheckContains(command, "-r 24", "74C-8: command uses configured frame rate");
            CheckContains(command, "-c:v libx264", "74C-9: command uses libx264");
            CheckContains(command, "-tune zerolatency", "74C-10: command requests zerolatency tuning");
            CheckContains(command, "-vf vflip", "74C-11: command flips Unity readback rows for Foxglove video display");
            CheckContains(command, "-bf 0", "74C-12: command disables B frames");
            CheckContains(command, "-g 24", "74C-13: command uses configured keyframe interval");
            CheckContains(command, "-b:v 1200k", "74C-14: command uses configured bitrate");
            CheckContains(command, "aud=1", "74C-15: command emits AUD NAL units");
            CheckContains(command, "repeat-headers=1", "74C-16: command repeats SPS/PPS headers");
            CheckContains(command, "bframes=0", "74C-17: command disables x264 B frames");
            CheckContains(command, "-f h264", "74C-18: command outputs H.264 elementary stream");
            CheckContains(command, "pipe:1", "74C-19: command writes Annex B stream to stdout");

            var sidecarType = Type.GetType("Foxglove.Schemas.Video.FfmpegH264EncoderSidecar, FoxgloveSdk.Tests");
            Check(sidecarType != null, "74C-20: FfmpegH264EncoderSidecar type exists");
            Check(sidecarType.GetMethod("Start", new[] { optionsType })?.ReturnType == typeof(bool),
                "74C-21: sidecar exposes Start(options)");
            Check(sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) })?.ReturnType == typeof(bool),
                "74C-22: sidecar exposes non-blocking frame submission");
            Check(sidecarType.GetMethod("TryDequeueAccessUnit") != null,
                "74C-23: sidecar exposes completed access-unit dequeue");
        }

        private static void VerifyPublisherSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");
            Check(!string.IsNullOrEmpty(source), "74D-1: FoxgloveCompressedVideoCameraPublisher source exists");
            Check(source.Contains("class FoxgloveCompressedVideoCameraPublisher"),
                "74D-2: compressed video camera publisher class exists");
            Check(source.Contains("SchemaName => \"foxglove.CompressedVideo\""),
                "74D-3: publisher schema is foxglove.CompressedVideo");
            Check(source.Contains("_topic = \"/unity/camera/video\""),
                "74D-4: publisher defaults topic to /unity/camera/video");
            Check(source.Contains("SupportsJsonEncoding => false"),
                "74D-5: publisher is protobuf-only");
            Check(source.Contains("SupportsProtobufEncoding => true"),
                "74D-6: publisher supports protobuf");
            Check(source.Contains("_maxPendingReadbacks = 1"),
                "74D-7: publisher defaults to one pending readback");

            var update = Slice(source, "private void LateUpdate()", "private void OnReadbackComplete");
            CheckOrdered(update, "ShouldPublishNow()", "ShouldPreparePublishPayload()", "74D-8: publisher applies Phase 73 demand preflight after cadence");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "_captureCam.Render()", "74D-9: publisher demand preflights before camera render");
            CheckOrdered(update, "ShouldPreparePublishPayload()", "AsyncGPUReadback.Request", "74D-10: publisher demand preflights before GPU readback");
            CheckOrdered(update, "DrainEncodedAccessUnits()", "ShouldPublishNow()", "74D-11: publisher drains encoded access units before capture scheduling");

            Check(source.Contains("CameraCompressedVideoBuilder.Serialize"),
                "74D-12: publisher serializes CompressedVideo payloads through builder");
            Check(source.Contains("PublishProto(payload, unixNs)") || source.Contains("PublishProto(payload, timestampNs)"),
                "74D-13: publisher uses inherited PublishProto helper");
            Check(!source.Contains("_manager.PublishProto"),
                "74D-14: publisher does not bypass publisher base publish helper");
        }

        private static void CheckContains(string text, string pattern, string name)
            => Check(text.Contains(pattern), name);

        private static string GetStaticStringField(Type type, string name)
            => type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

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

        private static byte[] Nal(byte type, params byte[] payload)
        {
            var nal = new byte[4 + 1 + payload.Length];
            nal[0] = 0;
            nal[1] = 0;
            nal[2] = 0;
            nal[3] = 1;
            nal[4] = type;
            Buffer.BlockCopy(payload, 0, nal, 5, payload.Length);
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

        private static void CheckOrdered(string text, string before, string after, string name)
        {
            Check(IndexOf(text, before) >= 0 && IndexOf(text, after) >= 0 && IndexOf(text, before) < IndexOf(text, after), name);
        }

        private static int IndexOf(string text, string pattern)
            => text.IndexOf(pattern, StringComparison.Ordinal);

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? text.Substring(startIndex)
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
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
    }
}
