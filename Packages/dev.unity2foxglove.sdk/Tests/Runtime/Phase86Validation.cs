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
                  && source.Contains("private long _droppedInputFrames")
                  && source.Contains("private long _droppedOutputFrames"),
                "86A-1: OpenH264 diagnostic counters use long backing fields");
            Check(source.Contains("Interlocked.Read(ref _framesSubmitted)")
                  && source.Contains("Interlocked.Increment(ref _framesSubmitted)")
                  && source.Contains("Interlocked.Increment(ref _accessUnitsReceived)")
                  && source.Contains("Interlocked.Increment(ref _droppedInputFrames)")
                  && source.Contains("Interlocked.Increment(ref _droppedOutputFrames)"),
                "86A-2: OpenH264 diagnostic counters use Interlocked");
            Check(source.Contains("length == 0")
                  && source.Contains("AcceptHelperSkippedAccessUnit")
                  && source.Contains("length < 0")
                  && source.Contains("MaxAccessUnitBytes"),
                "86A-3: OpenH264 helper treats zero as a skip sentinel and rejects negative/oversized lengths");
            Check(source.Contains("_encodedFrameTimestamps.TryDequeue(out _);")
                  && source.Contains("OpenH264 output queue full"),
                "86A-4: OpenH264 output queue pressure consumes the dropped access-unit timestamp");
            Check(source.Contains("private readonly object _startStopLock")
                  && source.Contains("lock (_startStopLock)")
                  && source.Contains("StopNoLock(clearOutputQueue"),
                "86A-5: OpenH264 start and stop share one lifecycle lock");
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
            var stopMethod = PhaseValidationSourceHelpers.SourceMethod(source, "private void StopNoLock(");
            if (string.IsNullOrEmpty(stopMethod))
                stopMethod = PhaseValidationSourceHelpers.SourceMethod(source, "private void Stop(");
            if (string.IsNullOrEmpty(stopMethod))
                throw new InvalidOperationException("[FAIL] missing source method: private void Stop(");
            var capturesProcess = source.Contains("var process = _process;") ||
                                  source.Contains("var process = Interlocked.Exchange(ref _process, null);");
            var waitsTasks = (stopMethod.Contains("WaitForTask(_stdinTask") &&
                              stopMethod.Contains("WaitForTask(_stdoutTask") &&
                              stopMethod.Contains("WaitForTask(_stderrTask") &&
                              Ordered(stopMethod, "WaitForTask(_stderrTask", "process.Dispose()")) ||
                             (stopMethod.Contains("var stdinTask = Interlocked.Exchange(ref _stdinTask, null);") &&
                              stopMethod.Contains("var stdoutTask = Interlocked.Exchange(ref _stdoutTask, null);") &&
                              stopMethod.Contains("var stderrTask = Interlocked.Exchange(ref _stderrTask, null);") &&
                              stopMethod.Contains("WaitForTask(stdinTask") &&
                              stopMethod.Contains("WaitForTask(stdoutTask") &&
                              stopMethod.Contains("WaitForTask(stderrTask") &&
                              Ordered(stopMethod, "WaitForTask(stderrTask", "process.Dispose()"));
            Check(capturesProcess
                  && source.Contains("RunStdinWriter(process, token")
                  && source.Contains("RunStdoutReader(process, token)")
                  && source.Contains("RunStderrReader(process, token)")
                  && waitsTasks,
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
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer/McapWriter.cs");
            var recorder = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var chunkReader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapChunkReader.cs");
            Check(writer.Contains("private bool _disposed") && writer.Contains("if (_disposed)"),
                "86D-1: McapWriter Dispose is idempotent");
            Check(recorder.Contains("private bool _closed, _recordingFailed, _disposed")
                  && recorder.Contains("if (_disposed) return;"),
                "86D-2: McapRecorder Dispose is idempotent");
            Check(chunkReader.Contains("len > int.MaxValue") && chunkReader.Contains("recordLength"),
                "86D-3: Mcap chunk reader guards oversized chunk record lengths before int casts");
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
            var update = PhaseValidationSourceHelpers.SourceMethod(
                source,
                "private void Update");
            var unregister = PhaseValidationSourceHelpers.SourceMethod(
                source,
                "public static void UnregisterSource");
            var applyDeferred = PhaseValidationSourceHelpers.SourceMethod(
                source,
                "private void ApplyDeferred");

            Check(source.Contains("_deferredAdds")
                  && source.Contains("_deferredRemoves")
                  && source.Contains("_iterating"),
                "86G-1: FoxRun hub has deferred source-mutation queues");
            Check(update.Contains("ApplyDeferred();")
                  && update.IndexOf("ApplyDeferred();", StringComparison.Ordinal)
                     != update.LastIndexOf("ApplyDeferred();", StringComparison.Ordinal)
                  && update.Contains("_iterating = true")
                  && update.Contains("finally")
                  && update.Contains("_iterating = false"),
                "86G-2: FoxRun hub applies source mutations outside enumeration");
            Check(unregister.Contains("QueueRemove(source)")
                  && applyDeferred.Contains("RemoveSourceNow(source)"),
                "86G-3: FoxRun unregister path is centralized through deferred removal");
        }

        private static void VerifyManagerStopCleanup()
        {
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var clientEvents = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            Check(server.Contains("ClearClientEvents()")
                  && clientEvents.Contains("private void ClearClientEvents()")
                  && clientEvents.Contains("_clientLifecycleEvents.Clear()")
                  && clientEvents.Contains("_clientMessageEvents.Clear()"),
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
