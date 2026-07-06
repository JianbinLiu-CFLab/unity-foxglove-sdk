// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-16 validation for video sidecar and codec review fixes.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_16Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-16: Video Encoding Sidecars and Codecs ===");
            _passed = 0;

            OpenH264RuntimeTreatsZeroLengthAsSkipSentinel();
            OpenH264ExecutableCheckValidatesRealAccessUnits();
            OpenH264InstallerUsesTemporaryDownloadsBeforeFinalPaths();
            OpenH264HelperSourcesStayByteIdentical();
            MediaFoundationTimestampTrackingRemovesResolvedEntries();
            FfmpegTimestampUnderflowIsObservable();
            ExperimentalOpenH264ProbeCarriesDllPathAndSynchronousCleanup();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-16: {_passed} checks passed.");
        }

        private static void OpenH264RuntimeTreatsZeroLengthAsSkipSentinel()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            var stdoutReader = ExtractMethod(source, "private async Task RunStdoutReader");
            var skip = ExtractMethod(source, "internal void AcceptHelperSkippedAccessUnit");
            var enqueue = ExtractMethod(source, "private void EnqueueAccessUnit");

            Check(stdoutReader.Contains("if (length == 0)", StringComparison.Ordinal)
                  && stdoutReader.Contains("AcceptHelperSkippedAccessUnit();", StringComparison.Ordinal)
                  && stdoutReader.Contains("continue;", StringComparison.Ordinal),
                "163-16A-1: OpenH264 stdout reader treats zero length as a skip sentinel");
            Check(stdoutReader.Contains("length < 0 || length > MaxAccessUnitBytes", StringComparison.Ordinal)
                  && !stdoutReader.Contains("length <= 0 || length > MaxAccessUnitBytes", StringComparison.Ordinal),
                "163-16A-2: OpenH264 stdout reader rejects negative and oversized lengths without rejecting skip sentinels");
            Check(skip.Contains("_encodedFrameTimestamps.TryDequeue(out _);", StringComparison.Ordinal)
                  && skip.Contains("Interlocked.Increment(ref _skippedAccessUnits)", StringComparison.Ordinal)
                  && skip.Contains("OpenH264 helper skipped an access unit", StringComparison.Ordinal),
                "163-16A-3: OpenH264 skip sentinel drains one pending timestamp and records diagnostics");
            Check(enqueue.Contains("if (_outputCount >= _maxOutputQueue", StringComparison.Ordinal)
                  && enqueue.Contains("_encodedFrameTimestamps.TryDequeue(out _)", StringComparison.Ordinal)
                  && enqueue.Contains("Interlocked.Increment(ref _droppedOutputFrames)", StringComparison.Ordinal)
                  && enqueue.Contains("_outputCount++", StringComparison.Ordinal)
                  && !enqueue.Contains("Volatile.Read(ref _outputCount)", StringComparison.Ordinal),
                "163-16A-4: OpenH264 output pressure drops at admission and consumes the paired timestamp");
        }

        private static void OpenH264ExecutableCheckValidatesRealAccessUnits()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            var check = ExtractMethod(source, "public static OpenH264ExecutableCheckResult Check");
            var validator = ExtractMethod(source, "private static bool TryValidateLengthPrefixedAccessUnit");
            var compatibility = ExtractMethod(source, "private static string BuildCompatibilityError");

            Check(check.Contains("TryValidateLengthPrefixedAccessUnit(stdout, out var stdoutError)", StringComparison.Ordinal)
                  && check.Contains("process.ExitCode == 0 && hasAccessUnit", StringComparison.Ordinal),
                "163-16B-1: OpenH264 executable check requires a validated access unit before reporting Found");
            Check(validator.Contains("stdout.Length < 4", StringComparison.Ordinal)
                  && validator.Contains("length <= 0", StringComparison.Ordinal)
                  && validator.Contains("stdout.Length < 4 + length", StringComparison.Ordinal)
                  && validator.Contains("LooksLikeDecodableH264AccessUnit(payload)", StringComparison.Ordinal),
                "163-16B-2: OpenH264 executable check rejects sentinels, truncation, and non-decodable payloads");
            Check(compatibility.Contains("stderr:", StringComparison.Ordinal)
                  && compatibility.Contains("OpenH264 helper reported stderr during validation", StringComparison.Ordinal),
                "163-16B-3: OpenH264 executable check surfaces helper stderr instead of hiding non-outdated failures");
        }

        private static void OpenH264InstallerUsesTemporaryDownloadsBeforeFinalPaths()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");
            var install = ExtractMethod(source, "internal static OpenH264InstallResult Install");
            var download = ExtractMethod(source, "private static void DownloadFile");

            Check(install.Contains("compressedDownloadPath = compressedPath + \".download\"", StringComparison.Ordinal)
                  && CheckOrdered(install, "DownloadFile(OpenH264OfficialBinaryManifest.DownloadUrl, compressedDownloadPath);", "TryVerifySha256(")
                  && CheckOrdered(install, "TryVerifySha256(", "File.Move(compressedDownloadPath, compressedPath);"),
                "163-16C-1: OpenH264 installer verifies downloaded archive before moving it to the final path");
            Check(download.Contains("tempDestination = destination + \".partial\"", StringComparison.Ordinal)
                  && download.Contains("File.Create(tempDestination)", StringComparison.Ordinal)
                  && download.Contains("CopyToAsync(destinationStream, 81920, cts.Token)", StringComparison.Ordinal)
                  && download.Contains("File.Move(tempDestination, destination)", StringComparison.Ordinal)
                  && download.Contains("TryDelete(tempDestination)", StringComparison.Ordinal),
                "163-16C-2: OpenH264 downloader bounds body copy and avoids leaving partial bytes at the requested destination");
            Check(source.Contains("CombineDecompressErrors(bzip2Error, pythonError)", StringComparison.Ordinal)
                  && source.Contains("bzip2 failed: ", StringComparison.Ordinal)
                  && source.Contains("Python bz2 failed: ", StringComparison.Ordinal),
                "163-16C-3: OpenH264 decompressor preserves bzip2 and Python fallback diagnostics");
        }

        private static void OpenH264HelperSourcesStayByteIdentical()
        {
            var package = ReadRepoBytes("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp");
            var script = ReadRepoBytes("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");
            Check(package.SequenceEqual(script),
                "163-16D-1: package and script OpenH264 helper sources are byte-identical");
        }

        private static void MediaFoundationTimestampTrackingRemovesResolvedEntries()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var register = ExtractMethod(source, "private void RegisterSampleTimestamp");
            var resolve = ExtractMethod(source, "private ulong ResolveOutputTimestamp");
            var evict = ExtractMethod(source, "private void EvictOldestSampleTimestamp");
            var clear = ExtractMethod(source, "private void ClearSampleTimestampMap");

            Check(source.Contains("Dictionary<long, LinkedListNode<long>> _sampleTimestampNodesByTime", StringComparison.Ordinal)
                  && source.Contains("LinkedList<long> _sampleTimestampOrder", StringComparison.Ordinal),
                "163-16E-1: MediaFoundation timestamp order supports direct node removal");
            Check(register.Contains("_sampleTimestampNodesByTime.TryGetValue(sampleTime", StringComparison.Ordinal)
                  && register.Contains("_sampleTimestampOrder.Remove(existingNode)", StringComparison.Ordinal)
                  && register.Contains("_sampleTimestampOrder.AddLast(sampleTime)", StringComparison.Ordinal),
                "163-16E-2: MediaFoundation timestamp registration replaces duplicate sample-time nodes");
            Check(resolve.Contains("_sampleTimestampNsByTime.Remove(sampleTime)", StringComparison.Ordinal)
                  && resolve.Contains("_sampleTimestampOrder.Remove(node)", StringComparison.Ordinal)
                  && resolve.Contains("_sampleTimestampNodesByTime.Remove(sampleTime)", StringComparison.Ordinal),
                "163-16E-3: MediaFoundation timestamp resolution removes both map and order entries");
            Check(evict.Contains("_sampleTimestampOrder.RemoveFirst()", StringComparison.Ordinal)
                  && evict.Contains("_sampleTimestampNodesByTime.Remove(oldestSampleTime)", StringComparison.Ordinal)
                  && evict.Contains("Interlocked.Increment(ref _evictedTimestampCount)", StringComparison.Ordinal)
                  && clear.Contains("_sampleTimestampNodesByTime.Clear()", StringComparison.Ordinal),
                "163-16E-4: MediaFoundation timestamp eviction and clear keep all timestamp indexes synchronized and visible");
            Check(source.Contains("s_mftOutputDataBufferSize", StringComparison.Ordinal)
                  && source.Contains("GetCachedOutputStreamInfo()", StringComparison.Ordinal)
                  && source.Contains("RefreshOutputStreamInfo();", StringComparison.Ordinal),
                "163-16E-5: MediaFoundation output loop caches stream info and output buffer size");
            Check(source.Contains("Volatile.Read(ref _isRunning)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _lastDiagnosticLine)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _lastError)", StringComparison.Ordinal),
                "163-16E-6: MediaFoundation public state uses memory-barrier accessors");
        }

        private static void FfmpegTimestampUnderflowIsObservable()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var enqueue = ExtractMethod(source, "private void EnqueueAccessUnit");

            Check(source.Contains("private long _timestampQueueUnderflows", StringComparison.Ordinal)
                  && source.Contains("public long TimestampQueueUnderflows", StringComparison.Ordinal),
                "163-16F-1: FFmpeg timestamp queue underflows are exposed as diagnostics");
            Check(enqueue.Contains("Interlocked.Increment(ref _timestampQueueUnderflows)", StringComparison.Ordinal)
                  && enqueue.Contains("FFmpeg H.264 access unit had no queued timestamp", StringComparison.Ordinal),
                "163-16F-2: FFmpeg records a visible diagnostic when output lacks an input timestamp");
        }

        private static void ExperimentalOpenH264ProbeCarriesDllPathAndSynchronousCleanup()
        {
            var sidecar = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            var publisher = ReadRepoText("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var startInfo = ExtractMethod(sidecar, "private static ProcessStartInfo CreateStartInfo");
            var stop = ExtractMethod(sidecar, "public void Stop");
            var cleanup = ExtractMethod(sidecar, "private static void CleanupWorkers");
            var validate = ExtractMethod(sidecar, "public bool Validate");

            Check(sidecar.Contains("public string OpenH264DllPath { get; set; }", StringComparison.Ordinal)
                  && startInfo.Contains("--openh264-dll ", StringComparison.Ordinal)
                  && startInfo.Contains("QuoteArgument(options.OpenH264DllPath)", StringComparison.Ordinal),
                "163-16G-1: experimental OpenH264 probe passes the DLL path required by the Windows helper");
            Check(validate.Contains("RequiresExplicitOpenH264Dll", StringComparison.Ordinal)
                  && validate.Contains("OpenH264 DLL path is empty", StringComparison.Ordinal)
                  && validate.Contains("OpenH264 DLL does not exist", StringComparison.Ordinal),
                "163-16G-2: experimental OpenH264 probe validates the DLL path on Windows");
            Check(publisher.Contains("[SerializeField] private string _openH264DllPath", StringComparison.Ordinal)
                  && publisher.Contains("OpenH264DllPath = _openH264DllPath", StringComparison.Ordinal),
                "163-16G-3: experimental OpenH264 publisher exposes and forwards the DLL path");
            Check(stop.Contains("CleanupWorkers(process, stop, stdinTask, stdoutTask, stderrTask);", StringComparison.Ordinal)
                  && cleanup.Contains("WaitForWorkerTasks(tasks)", StringComparison.Ordinal)
                  && cleanup.Contains("process.WaitForExit(200)", StringComparison.Ordinal)
                  && !sidecar.Contains("ScheduleWorkerCleanup", StringComparison.Ordinal),
                "163-16G-4: experimental OpenH264 stop waits for captured workers before allowing restart");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_16Validation.cs", StringComparison.Ordinal),
                "163-16H-1: runtime test project compiles Phase163_16Validation");
            Check(registry.Contains("--phase163-16", StringComparison.Ordinal)
                  && registry.Contains("Phase163_16Validation.Validate", StringComparison.Ordinal),
                "163-16H-2: validation registry exposes --phase163-16");
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 163-16 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 163-16 validation helper found opening brace");

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unable to extract source block.");
        }

        private static bool CheckOrdered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static byte[] ReadRepoBytes(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllBytes(path);
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
