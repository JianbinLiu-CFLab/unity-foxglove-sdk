// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 100 runtime hardening closure validation.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase100Validation.
    /// </summary>
    public static class Phase100Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
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
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");
            var shell = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeRuntimeShell.cs");
            Check(source.Contains("DisconnectSink(_ownedSink)")
                  && source.Contains("_signal.Set()")
                  && source.Contains("worker.Join(joinTimeoutMs)")
                  && source.Contains("TryRetireAfterTimeout()")
                  && source.Contains("TryConvertToRetired("),
                "100A-1: Stop wakes I/O before bounded join and transfers the pre-reserved lease on timeout");
            Check(source.Contains("_stopRequested || !_enabled")
                  && source.Contains("_ownedSink.Connect(")
                  && source.Contains("DisposeResources()")
                  && source.Contains("return false"),
                "100A-2: late Connect observes stopped admission while final sink disposal stays with the worker lease");
            Check(source.Contains("catch (ObjectDisposedException) when (ShouldStop("),
                "100A-3: worker loop treats shutdown disposal as clean exit");
            Check(source.Contains("catch (Exception ex)") && source.Contains("MarkFailure(ex.Message, disconnect: true)")
                  && source.Contains("countFrameFailure: false"),
                "100A-4: worker loop has a top-level failure guard");
            Check(shell.Contains("if (!enabled || !autoConnect)")
                  && shell.Contains("ROS2 Bridge runtime is not ready.")
                  && shell.Contains("return false"),
                "100A-5: autoConnect=false creates no worker and sends fail clearly");
        }

        private static void VerifyPointCloudDemandCaching()
        {
            var source = ReadPointCloudPublisherSources();
            var state = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishState.cs");
            Check(source.Contains("PointCloudPublishState _publishState")
                  && source.Contains("SetPreparedPublishDemand")
                  && source.Contains("ClearPreparedPublishDemand")
                  && state.Contains("_hasPreparedPublishDemand")
                  && state.Contains("SetPreparedDemand")
                  && state.Contains("ClearPreparedDemand"),
                "100B-1: point-cloud publisher caches demand for one prepared frame");
            Check(source.Contains("TryGetPreparedPublishDemand(out var publishWebSocket, out var publishProvider)"),
                "100B-2: raw/Draco helpers reuse cached Provider demand when called from Update/PublishFrame");
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
            var source = ReadProductPublisherText(relativePath);
            Check(source.Contains("_captureGeneration") && source.Contains("_cleanupWhenReadbacksDrain"),
                "100C-1: " + label + " tracks capture generation and deferred cleanup");
            Check(source.Contains("OnReadbackComplete(req, generation")
                  || source.Contains("OnReadbackComplete(request, generation"),
                "100C-2: " + label + " passes generation into AsyncGPUReadback callback");
            Check(source.Contains("CompletePendingReadback()"),
                "100C-3: " + label + " centralizes pending readback decrement and drain cleanup");
        }

        private static void VerifyVideoTailDrainOnStop()
        {
            var camera = ReadProductPublisherText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var cameraVideo = ReadProductPublisherText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs");
            var cameraSession = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");
            var legacy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");

            Check(camera.Contains("StopVideoSidecar();", StringComparison.Ordinal)
                  && cameraVideo.Contains("_videoPublishPipeline?.StopVideoSidecar(DrainEncodedAccessUnits)", StringComparison.Ordinal)
                  && MethodBodyContainsBefore(cameraSession, "Stop", "drain?.Invoke();", ".Dispose()"),
                "100D-1: camera video sidecar drains queued access units before dispose");
            Check(CountInMethod(cameraSession, "Stop", "drain?.Invoke();") >= 2,
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
            var source = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");
            Check(source.Contains("private readonly byte[] _payload")
                  && source.Contains("private readonly int _payloadOffset")
                  && source.Contains("private readonly int _payloadLength")
                  && source.Contains("clonePayload ? (byte[])payload.Clone() : payload")
                  && source.Contains("internal static Ros2BridgeFrame CreateOwned")
                  && source.Contains("internal static Ros2BridgeFrame CreateWireOwnedView")
                  && source.Contains("public ReadOnlyMemory<byte> PayloadMemory")
                  && source.Contains("public byte[] Payload => PayloadMemory.ToArray()"),
                "100F-1: bridge frame keeps public defensive payload copies while allowing internal owned payload transfer");
            var writer = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            Check(writer.Contains("frame.PayloadLength") && writer.Contains("frame.WritePayloadTo(destination)")
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
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("--phase100", StringComparison.Ordinal)
                  && registry.Contains("Phase100Validation.Validate", StringComparison.Ordinal),
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
            => PhaseValidationSourceHelpers.SourceMethod(source, methodName);

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string ReadProductPublisherText(string relativePath)
        {
            if (relativePath.EndsWith("FoxgloveCameraPublisher.cs", StringComparison.Ordinal))
                return ReadCameraPublisherSources();

            return ReadRepoText(relativePath);
        }

        private static string ReadCameraPublisherSources()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Camera publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        private static string ReadPointCloudPublisherSources()
        {
            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Point-cloud publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxglovePointCloudPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase100 validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
