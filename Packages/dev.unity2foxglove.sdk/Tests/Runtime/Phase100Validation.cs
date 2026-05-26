// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 100 runtime hardening closure validation.

using System;
using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase100Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 100: Runtime Hardening Closure ===");
            _passed = 0;

            VerifyRos2BridgeRuntimeHardening();
            VerifyPointCloudDemandCaching();
            VerifyCameraReadbackLifecycle();
            VerifyVideoTailDrainOnStop();
            VerifyFoxRunIntervalsAreNamed();
            VerifyRos2BridgeFrameImmutabilityDecision();
            VerifyPlaybackClockJumpCap();
            VerifyTransformDeadCodeRemoved();
            VerifyPhase100ValidationIsWired();

            Console.WriteLine($"Phase 100: {_passed} checks passed.");
        }

        private static void VerifyRos2BridgeRuntimeHardening()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");
            Check(source.Contains("sinkToClose = _sink")
                  && source.Contains("_sink = null")
                  && source.Contains("CloseSink(sinkToClose)")
                  && source.Contains("Math.Max(1000, _sendTimeoutMs + 250)")
                  && source.Contains("worker.Join(joinTimeoutMs)"),
                "100A-1: Stop closes the sink before bounded worker join to unblock blocking sends");
            Check(source.Contains("_stopRequested || !_enabled") && source.Contains("CloseSink(sink)") && source.Contains("return false"),
                "100A-2: EnsureConnected closes late-connected sink when stop/disable wins");
            Check(source.Contains("catch (ObjectDisposedException) when (ShouldStop())"),
                "100A-3: worker loop treats shutdown disposal as clean exit");
            Check(source.Contains("catch (Exception ex)") && source.Contains("MarkFailure(ex.Message, disconnect: true)")
                  && source.Contains("countFrameFailure: false"),
                "100A-4: worker loop has a top-level failure guard");
            Check(source.Contains("auto-connect is disabled") && source.Contains("return false"),
                "100A-5: autoConnect=false sends fail clearly instead of queuing into an idle runtime");
        }

        private static void VerifyPointCloudDemandCaching()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Check(source.Contains("_hasPreparedPublishDemand") && source.Contains("SetPreparedPublishDemand") && source.Contains("ClearPreparedPublishDemand"),
                "100B-1: point-cloud publisher caches demand for one prepared frame");
            Check(source.Contains("TryGetPreparedPublishDemand(out var publishWebSocket, out var publishBridge)"),
                "100B-2: raw/Draco helpers reuse cached demand when called from Update/PublishFrame");
            Check(source.Contains("protected virtual void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)"),
                "100B-3: protected PublishPreparedFrame signature remains compatible");
        }

        private static void VerifyCameraReadbackLifecycle()
        {
            VerifyCameraLifecycleSource(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs",
                "main camera publisher");
            VerifyCameraLifecycleSource(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs",
                "legacy compressed-video publisher");
        }

        private static void VerifyCameraLifecycleSource(string relativePath, string label)
        {
            var source = ReadRepoText(relativePath);
            Check(source.Contains("_captureGeneration") && source.Contains("_cleanupWhenReadbacksDrain"),
                "100C-1: " + label + " tracks capture generation and deferred cleanup");
            Check(source.Contains("OnReadbackComplete(req, generation)") || source.Contains("OnReadbackComplete(request, generation)"),
                "100C-2: " + label + " passes generation into AsyncGPUReadback callback");
            Check(source.Contains("CompletePendingReadback()"),
                "100C-3: " + label + " centralizes pending readback decrement and drain cleanup");
        }

        private static void VerifyVideoTailDrainOnStop()
        {
            var camera = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var legacy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");

            Check(MethodBodyContainsBefore(camera, "StopVideoSidecar", "DrainEncodedAccessUnits();", ".Dispose()"),
                "100D-1: camera video sidecar drains queued access units before dispose");
            Check(CountInMethod(camera, "StopVideoSidecar", "DrainEncodedAccessUnits();") >= 2,
                "100D-2: camera video sidecar drains again after dispose for tail packets");
            Check(MethodBodyContainsBefore(legacy, "StopSidecar", "DrainEncodedAccessUnits();", ".Dispose()"),
                "100D-3: legacy compressed-video sidecar drains queued access units before dispose");
            Check(CountInMethod(legacy, "StopSidecar", "DrainEncodedAccessUnits();") >= 2,
                "100D-4: legacy compressed-video sidecar drains again after dispose for tail packets");

            VerifyEncoderSidecarStopHygiene("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs", "FFmpeg H264");
            VerifyEncoderSidecarStopHygiene("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs", "FFmpeg H265");
            VerifyEncoderSidecarStopHygiene("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs", "OpenH264");
            VerifyEncoderSidecarStopHygiene("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs", "MediaFoundation H264");
        }

        private static void VerifyEncoderSidecarStopHygiene(string relativePath, string label)
        {
            var source = ReadRepoText(relativePath);
            Check(source.Contains("Stop(clearOutputQueue: true)") && source.Contains("Stop(clearOutputQueue: false)"),
                "100D-5: " + label + " separates restart cleanup from publisher tail-dispose");
            Check(source.Contains("DrainOutputQueue()")
                  && (source.Contains("_outputCount = 0")
                      || source.Contains("Volatile.Write(ref _outputCount, 0)")
                      || source.Contains("Interlocked.Exchange(ref _outputCount, 0)")),
                "100D-6: " + label + " clears queued access units and output count on hard stop");
        }

        private static void VerifyFoxRunIntervalsAreNamed()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            Check(source.Contains("ManagerSearchIntervalSeconds") && !SourceMethodContains(source, "Update", "_mgrSearchCooldown = 3f"),
                "100E-1: FoxRun manager search interval is named, not a magic update-loop literal");
            Check(source.Contains("ScanIntervalSeconds"),
                "100E-2: FoxRun fallback source scan interval remains named");
        }

        private static void VerifyRos2BridgeFrameImmutabilityDecision()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            Check(source.Contains("private readonly byte[] _payload")
                  && source.Contains("_payload = (byte[])payload.Clone()")
                  && source.Contains("public byte[] Payload => (byte[])_payload.Clone()"),
                "100F-1: bridge frame keeps a private immutable payload snapshot and returns defensive copies");
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            Check(writer.Contains("frame.PayloadLength") && writer.Contains("frame.WritePayloadTo(stream)")
                  && !writer.Contains("stream.Write(frame.Payload"),
                "100F-2: bridge writer serializes the owned payload snapshot without using the public clone");
        }

        private static void VerifyPlaybackClockJumpCap()
        {
            var tick = typeof(PlaybackClock).GetMethod(
                "Tick",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(DateTime) },
                null);
            Check(tick != null, "100G-1: PlaybackClock exposes deterministic Tick(DateTime)");

            var clock = new PlaybackClock();
            clock.EnableRange(0, 10_000_000_000UL);
            var t0 = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
            tick.Invoke(clock, new object[] { t0 });
            clock.Play();
            tick.Invoke(clock, new object[] { t0.AddMinutes(10) });

            Check(clock.NowNs > 0 && clock.NowNs < 10_000_000_000UL,
                "100G-2: large wall-clock jump is capped instead of jumping to replay end");
        }

        private static void VerifyTransformDeadCodeRemoved()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            Check(!source.Contains("PublishRos2Transform"),
                "100H-1: unused transform ROS2 helper is removed");
        }

        private static void VerifyPhase100ValidationIsWired()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(program.Contains("--phase100") && program.Contains("Phase100Validation.Validate()"),
                "100I-1: Phase100 validation is available as a standalone test target");
            Check(project.Contains("Phase100Validation.cs"),
                "100I-2: Phase100 validation is included in the runtime test project");
        }

        private static bool SourceMethodContains(string source, string methodName, string needle)
        {
            return ExtractMethodBody(source, methodName).Contains(needle);
        }

        private static bool MethodBodyContainsBefore(string source, string methodName, string first, string second)
        {
            var body = ExtractMethodBody(source, methodName);
            var firstIndex = body.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = body.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex;
        }

        private static int CountInMethod(string source, string methodName, string needle)
        {
            var body = ExtractMethodBody(source, methodName);
            var count = 0;
            var index = 0;
            while ((index = body.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var signatureIndex = FindMethodSignature(source, methodName);
            if (signatureIndex < 0) return "";
            var start = source.IndexOf('{', signatureIndex);
            if (start < 0) return "";
            var depth = 0;
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return "";
        }

        private static int FindMethodSignature(string source, string methodName)
        {
            var needles = new[]
            {
                "private void " + methodName,
                "protected void " + methodName,
                "public void " + methodName,
                "private bool " + methodName,
                "protected bool " + methodName,
                "public bool " + methodName,
                "private static void " + methodName,
                "protected static void " + methodName,
                "public static void " + methodName
            };

            var best = -1;
            foreach (var needle in needles)
            {
                var index = source.IndexOf(needle, StringComparison.Ordinal);
                if (index >= 0 && (best < 0 || index < best))
                    best = index;
            }

            return best;
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
