// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 86 validation for runtime hardening bugfixes.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 86 runtime hardening source changes.
    /// </summary>
    public static class Phase86Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 86: Runtime Hardening Bugfixes ===");
            _passed = 0;

            VerifyOpenH264Hardening();
            VerifySidecarLifecycle();
            VerifyMediaFoundationBitrateValidation();
            VerifyMcapHardening();
            VerifyRecordingControllerRaceGuard();
            VerifyCertificateDistributorCleanup();
            VerifyFoxRunTimerMutationSafety();
            VerifyManagerStopCleanup();
            VerifyAssetRegistryPathGuard();

            Console.WriteLine($"Phase 86: {_passed} checks passed.");
        }

        private static void VerifyOpenH264Hardening()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            Check(source.Contains("private long _framesSubmitted")
                  && source.Contains("private long _accessUnitsReceived")
                  && source.Contains("private long _droppedInputFrames"),
                "86A-1: OpenH264 diagnostic counters use long backing fields");
            Check(source.Contains("Interlocked.Read(ref _framesSubmitted)")
                  && source.Contains("Interlocked.Increment(ref _framesSubmitted)")
                  && source.Contains("Interlocked.Increment(ref _accessUnitsReceived)")
                  && source.Contains("Interlocked.Increment(ref _droppedInputFrames)"),
                "86A-2: OpenH264 diagnostic counters use Interlocked");
            Check(source.Contains("length <= 0") && source.Contains("MaxAccessUnitBytes"),
                "86A-3: OpenH264 helper packet length rejects non-positive values");
        }

        private static void VerifySidecarLifecycle()
        {
            VerifySidecarLifecycleFile(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "86B-1: FFmpeg H.264 sidecar captures process and waits tasks before dispose");
            VerifySidecarLifecycleFile(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "86B-2: FFmpeg H.265 sidecar captures process and waits tasks before dispose");
            VerifySidecarLifecycleFile(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "86B-3: OpenH264 sidecar captures process and waits tasks before dispose");
        }

        private static void VerifySidecarLifecycleFile(string relativePath, string checkName)
        {
            var source = ReadRepoText(relativePath);
            Check(source.Contains("var process = _process;")
                  && source.Contains("RunStdinWriter(process, token)")
                  && source.Contains("RunStdoutReader(process, token)")
                  && source.Contains("RunStderrReader(process, token)")
                  && source.Contains("WaitForTask(_stdinTask")
                  && source.Contains("WaitForTask(_stdoutTask")
                  && source.Contains("WaitForTask(_stderrTask")
                  && Ordered(source, "WaitForTask(_stderrTask", "process.Dispose()"),
                checkName);
        }

        private static void VerifyMediaFoundationBitrateValidation()
        {
            var options = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderOptions.cs");
            var sidecar = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(options.Contains("MaxBitrateKbps") && options.Contains("BitrateBitsPerSecond"),
                "86C-1: Media Foundation options define explicit bitrate limit and bits-per-second value");
            Check(options.Contains("BitrateKbps > MaxBitrateKbps"),
                "86C-2: Media Foundation options reject absurd bitrate values");
            Check(sidecar.Contains("options.BitrateBitsPerSecond")
                  && !sidecar.Contains("checked((int)Math.Max(1, options.BitrateKbps) * 1000)"),
                "86C-3: Media Foundation sidecar uses validated bitrate calculation");
        }

        private static void VerifyMcapHardening()
        {
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapWriter.cs");
            var recorder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs");
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapReader.cs");
            Check(writer.Contains("private bool _disposed") && writer.Contains("if (_disposed)"),
                "86D-1: McapWriter Dispose is idempotent");
            Check(recorder.Contains("private bool _closed, _recordingFailed, _disposed")
                  && recorder.Contains("if (_disposed) return;"),
                "86D-2: McapRecorder Dispose is idempotent");
            Check(reader.Contains("len > int.MaxValue") && reader.Contains("recordLength"),
                "86D-3: McapReader guards oversized chunk record lengths before int casts");
            Check(recorder.Contains("FlushChunkBeforeLargeWriteIfNeeded")
                  && Ordered(recorder, "FlushChunkBeforeLargeWriteIfNeeded(recordLength)", "var off = (ulong)_chunkBuf.Position"),
                "86D-4: McapRecorder preflushes current chunk before large next message");
        }

        private static void VerifyRecordingControllerRaceGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            Check(source.Contains("using System.Threading;"),
                "86E-1: RecordingController can use Volatile recorder access");
            Check(source.Contains("Volatile.Read(ref _recorder)")
                  && source.Contains("Volatile.Write(ref _recorder"),
                "86E-2: RecordingController uses volatile recorder reads/writes");
            var onParameterChangedIndex = source.IndexOf("private void OnParameterChanged", StringComparison.Ordinal);
            Check(onParameterChangedIndex >= 0, "86E-3a: RecordingController exposes OnParameterChanged");
            var onParameterChanged = source.Substring(onParameterChangedIndex);
            Check(Ordered(onParameterChanged, "var recorder = Volatile.Read(ref _recorder);", "recorder.WriteMetadata"),
                "86E-3: OnParameterChanged writes through a local recorder capture");
        }

        private static void VerifyCertificateDistributorCleanup()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveCertificateDistributor.cs");
            Check(source.Contains("var cts = _cts")
                  && source.Contains("_cts = null")
                  && source.Contains("cts?.Dispose()"),
                "86F-1: certificate distributor Stop disposes and clears CTS");
        }

        private static void VerifyFoxRunTimerMutationSafety()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            Check(source.Contains("_pendingAdds") && source.Contains("_pendingRemoves") && source.Contains("_iteratingTimers"),
                "86G-1: FoxRun hub has pending timer mutation queues");
            Check(source.Contains("ApplyPendingTimerMutations()") && source.Contains("try") && source.Contains("finally"),
                "86G-2: FoxRun hub applies timer mutations outside enumeration");
            Check(source.Contains("RemoveSource(IFoxgloveLogSource source)"),
                "86G-3: FoxRun unregister path is centralized");
        }

        private static void VerifyManagerStopCleanup()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            Check(source.Contains("ClearClientEvents()")
                  && source.Contains("_clientLifecycleEvents.Clear()")
                  && source.Contains("_clientMessageEvents.Clear()"),
                "86H-1: manager StopServer clears stale queued client events");
        }

        private static void VerifyAssetRegistryPathGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            Check(source.Contains("FileSystemPathComparison")
                  && source.Contains("StartsWith(rootPrefix, comparison)")
                  && source.Contains("string.Equals(resolved, normalizedRoot, comparison)"),
                "86I-1: asset registry path traversal guard uses platform-aware comparison");
        }

        private static bool Ordered(string text, string before, string after)
        {
            var beforeIndex = text.IndexOf(before, StringComparison.Ordinal);
            var afterIndex = text.IndexOf(after, StringComparison.Ordinal);
            return beforeIndex >= 0 && afterIndex >= 0 && beforeIndex < afterIndex;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required repository file is missing.", path);
            return File.ReadAllText(path);
        }
    }
}
