// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Video encoder sidecar timestamp behavior.

using System;
using System.Reflection;
using System.Collections;
using System.IO;
using Unity.FoxgloveSDK.Components;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{


public sealed partial class OpenH264EncoderSidecarTests
    {
        [Fact]
        public void ProtocolMarkerAfterLegacyFallbackIsReportedAsMismatch()
        {
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_protocolNegotiationState", 1);
            var accept = typeof(OpenH264EncoderSidecar).GetMethod(
                "TryAcceptProtocolMarker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(accept);

            Assert.False((bool)accept.Invoke(sidecar, null));
            Assert.Equal(1, (int)GetField(sidecar, "_helperProtocolVersion"));
            Assert.Equal(1, (int)GetField(sidecar, "_protocolNegotiationState"));
        }

        [Fact]
        public void TimestampedOutputQueueDropConsumesExactlyOneWrittenFrame()
        {
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_maxOutputQueue", 1);
            SetField(sidecar, "_outputCount", 1);
            SetField(sidecar, "_writtenFrameCount", 2L);
            var accept = typeof(OpenH264EncoderSidecar).GetMethod(
                "AcceptHelperAccessUnit",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(byte[]), typeof(ulong) },
                modifiers: null);
            Assert.NotNull(accept);

            accept.Invoke(sidecar, new object[] { new byte[] { 1 }, 42UL });

            Assert.Equal(1L, (long)GetField(sidecar, "_writtenFrameCount"));
            Assert.Equal(1, sidecar.DroppedOutputFrames);
        }

        [Fact]
        public void WrittenFrameAdmissionIncludesTheFrameBeingProcessed()
        {
            using var process = Process.GetCurrentProcess();
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_options", new OpenH264EncoderOptions { Width = 2, Height = 2 });
            SetField(sidecar, "_maxInputQueue", 1);
            SetField(sidecar, "_maxOutputQueue", 0);
            SetField(sidecar, "_process", process);
            var dequeue = typeof(OpenH264EncoderSidecar).GetMethod(
                "TryDequeueInputFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var returnBuffer = typeof(OpenH264EncoderSidecar).GetMethod(
                "ReturnInputFrameBuffer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(dequeue);
            Assert.NotNull(returnBuffer);

            try
            {
                Assert.True(sidecar.TrySubmitFrame(new byte[6], 1UL));
                var args = new object[] { process, CancellationToken.None, null };
                Assert.True((bool)dequeue.Invoke(sidecar, args));
                Assert.Equal(1, sidecar.PendingTimestampCountForTests);
                Assert.False(sidecar.TrySubmitFrame(new byte[6], 2UL));
                returnBuffer.Invoke(null, new[] { args[2] });
            }
            finally
            {
                SetField(sidecar, "_process", null);
                sidecar.Stop();
            }
        }

        [Fact]
        public void FfmpegInputBatchUsesOneOutstandingWakeupPerCodec()
        {
            AssertSingleOutstandingWakeup(
                new FfmpegH264EncoderSidecar(),
                new FfmpegH264EncoderOptions { Width = 2, Height = 2, MaxInputQueue = 2 });
            AssertSingleOutstandingWakeup(
                new FfmpegH265EncoderSidecar(),
                new FfmpegH265EncoderOptions { Width = 2, Height = 2, MaxInputQueue = 2 });
        }

        [Fact]
        public void FfmpegDrainDoesNotConsumeWakeupPublishedAfterQueueReset()
        {
            AssertDrainDoesNotConsumeConcurrentWakeup(
                new FfmpegH264EncoderSidecar(),
                new FfmpegH264EncoderOptions { Width = 2, Height = 2, MaxInputQueue = 2 });
            AssertDrainDoesNotConsumeConcurrentWakeup(
                new FfmpegH265EncoderSidecar(),
                new FfmpegH265EncoderOptions { Width = 2, Height = 2, MaxInputQueue = 2 });
        }

        [Fact]
        public void MediaFoundationSubmissionRejectsFrameWhenStopped()
        {
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            Assert.False(sidecar.TrySubmitFrame(new byte[12], 123UL));
            Assert.Contains("not running", sidecar.LastError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MediaFoundationSubmissionRejectsInvalidFrameSize()
        {
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            SetProperty(sidecar, "IsRunning", true);
            SetField(sidecar, "_options", new MediaFoundationH264EncoderOptions { Width = 2, Height = 2 });
            Assert.False(sidecar.TrySubmitFrame(new byte[11], 123UL));
            Assert.Contains("byte count", sidecar.LastError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WorkerConvergesToStoppedStateOnSubmissionFailure()
        {
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            SetProperty(sidecar, "IsRunning", true);
            SetField(sidecar, "_options", new MediaFoundationH264EncoderOptions { Width = 3, Height = 2 });
            var worker = new Thread(() => Invoke(sidecar, "EncoderWorkerLoop"));
            worker.Start();
            Assert.True(sidecar.TrySubmitFrame(new byte[18], 123UL));
            Assert.True(worker.Join(TimeSpan.FromSeconds(2)));
            Assert.False(sidecar.IsRunning);
            Assert.False(string.IsNullOrWhiteSpace(sidecar.LastError));
        }

        [Fact]
        public void MediaFoundationInputQueueReportsDroppedInputFrames()
        {
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            SetProperty(sidecar, "IsRunning", true);
            SetField(sidecar, "_options", new MediaFoundationH264EncoderOptions { Width = 2, Height = 2 });

            Assert.True(sidecar.TrySubmitFrame(new byte[12], 1UL));
            Assert.True(sidecar.TrySubmitFrame(new byte[12], 2UL));
            Assert.True(sidecar.TrySubmitFrame(new byte[12], 3UL));

            Assert.Equal(1, sidecar.DroppedInputFrames);
            Assert.Equal(2, sidecar.InputQueueDepth);
        }

        [Fact]
        public void StartRejectsWindowsOnlyEncoderOutsideWindows()
        {
            if (OperatingSystem.IsWindows())
                return;
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            Assert.False(sidecar.Start(new MediaFoundationH264EncoderOptions { Width = 2, Height = 2 }));
            Assert.Contains("only available on Windows", sidecar.LastError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MediaFoundationInputQueueReportsBoundedCapacity()
        {
            using var sidecar = new MediaFoundationH264EncoderSidecar();
            Assert.Equal(2, sidecar.MaxInputQueue);
            Assert.Equal(0, sidecar.InputQueueDepth);
        }

        [Fact]
        public void DeferredSidecarRetirementKeepsOwnerUntilCleanup()
        {
            var owner = new TestDeferredSidecar();
            var before = CameraVideoSidecarRetirementRegistry.PendingCountForTests;

            CameraVideoSidecarRetirementRegistry.Retire(owner);
            Assert.Equal(before + 1, CameraVideoSidecarRetirementRegistry.PendingCountForTests);

            CameraVideoSidecarRetirementRegistry.Poll();
            Assert.Equal(before + 1, CameraVideoSidecarRetirementRegistry.PendingCountForTests);
            Assert.Equal(1, owner.PollCount);

            owner.Ready = true;
            CameraVideoSidecarRetirementRegistry.Poll();
            Assert.Equal(before, CameraVideoSidecarRetirementRegistry.PendingCountForTests);
            Assert.Equal(2, owner.PollCount);
        }

        private sealed class TestDeferredSidecar : ICameraVideoSidecarDeferredCleanup
        {
            internal bool Ready;
            internal int PollCount;

            public bool TryFinalizeDeferredCleanup()
            {
                PollCount++;
                return Ready;
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FrameSourceCopiesOnlyLogicalBytesIntoOversizedPoolBuffer(int codec)
        {
            using var process = Process.GetCurrentProcess();
            var sidecar = CreateFrameSourceSidecar(codec, process);
            var source = new ObservedFrameSource(12);
            try
            {
                Assert.True(((ICameraVideoFrameSourceSidecar)sidecar).TrySubmitFrame(source, 123UL));
                Assert.Equal(1, source.Observation.CopyCount);
                Assert.True(source.Observation.DestinationLength > source.Length);
                var frames = (IEnumerable)GetField(sidecar, "_inputFrames");
                object queued = null;
                foreach (var entry in frames)
                    queued = entry;
                Assert.NotNull(queued);
                var bytes = (byte[])queued.GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued);
                var length = (int)queued.GetType().GetProperty("Length", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued);
                var timestamp = (ulong)queued.GetType().GetProperty("TimestampNs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued);
                Assert.Equal(12, length);
                Assert.Equal(123UL, timestamp);
                for (var i = 0; i < length; i++)
                    Assert.Equal((byte)(i + 1), bytes[i]);
                for (var i = length; i < bytes.Length; i++)
                    Assert.Equal((byte)0xA5, bytes[i]);
            }
            finally
            {
                DisposeFrameSourceSidecar(sidecar, codec);
            }
        }

        [Fact]
        public void OpenH264RgbFrameSourceQueuesRgbBytesForWorkerConversion()
        {
            using var process = Process.GetCurrentProcess();
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_options", new OpenH264EncoderOptions { Width = 2, Height = 2 });
            SetField(sidecar, "_maxInputQueue", 2);
            SetField(sidecar, "_maxOutputQueue", 4);
            SetField(sidecar, "_process", process);

            try
            {
                var source = new ObservedFrameSource(12);
                Assert.True(((ICameraVideoRgbFrameSourceSidecar)sidecar).TrySubmitRgbFrame(source, 789UL));

                var frames = (IEnumerable)GetField(sidecar, "_inputFrames");
                object queued = null;
                foreach (var entry in frames)
                    queued = entry;
                Assert.NotNull(queued);
                Assert.True((bool)queued.GetType().GetProperty("IsRgb24", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued));
                Assert.Equal(12, (int)queued.GetType().GetProperty("Length", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued));
                Assert.Equal(789UL, (ulong)queued.GetType().GetProperty("TimestampNs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(queued));
            }
            finally
            {
                SetField(sidecar, "_process", null);
                sidecar.Dispose();
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FailedFrameSourceCopyLeavesInputQueueUnchanged(int codec)
        {
            using var process = Process.GetCurrentProcess();
            var sidecar = CreateFrameSourceSidecar(codec, process);
            try
            {
                var source = new ObservedFrameSource(12, fail: true);
                Assert.Throws<InvalidOperationException>(() => ((ICameraVideoFrameSourceSidecar)sidecar).TrySubmitFrame(source, 123UL));
                Assert.Equal(0, ((ICameraVideoEncoderSidecar)sidecar).InputQueueDepth);
                Assert.True(((ICameraVideoFrameSourceSidecar)sidecar).TrySubmitFrame(new ObservedFrameSource(12), 456UL));
                Assert.Equal(1, ((ICameraVideoEncoderSidecar)sidecar).InputQueueDepth);
            }
            finally
            {
                DisposeFrameSourceSidecar(sidecar, codec);
            }
        }

        /// Every encoder backend receives exactly one handoff copy per frame from the
        /// publish pipeline, and no caller-side scratch copy or RGB-to-I420 conversion runs.
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PublishPipelineHandsEachFrameToEncoderWithExactlyOneCopy(int codec)
        {
            using var process = Process.GetCurrentProcess();
            object sidecar;
            CameraOutputMode mode;
            if (codec == 3)
            {
                sidecar = new OpenH264EncoderSidecar();
                SetField(sidecar, "_options", new OpenH264EncoderOptions { Width = 2, Height = 2 });
                SetField(sidecar, "_maxInputQueue", 2);
                SetField(sidecar, "_maxOutputQueue", 4);
                SetField(sidecar, "_process", process);
                mode = CameraOutputMode.H264OpenH264;
            }
            else
            {
                sidecar = CreateFrameSourceSidecar(codec, process);
                mode = codec == 0 ? CameraOutputMode.H264Ffmpeg
                    : codec == 1 ? CameraOutputMode.H265Ffmpeg
                    : CameraOutputMode.H264MediaFoundationExperimental;
            }

            var pipeline = new CameraVideoPublishPipeline(new CameraPublishDiagnostics());
            var session = GetField(pipeline, "_videoSidecarSession");
            SetField(session, "_sidecar", sidecar);
            SetField(session, "_mode", mode);
            SetField(session, "_width", 2);
            SetField(session, "_height", 2);
            try
            {
                for (var frame = 1; frame <= 2; frame++)
                {
                    var source = new ObservedFrameSource(12);
                    var result = pipeline.SubmitVideoFrame(
                        source,
                        new CameraCaptureIdentity(1, (ulong)frame, 2, 2));

                    Assert.True(result.Submitted, result.Reason);
                    Assert.Equal(1, source.Observation.CopyCount);
                }

                Assert.Null(GetField(pipeline, "_rgbScratch"));
                Assert.Null(GetField(pipeline, "_i420Scratch"));
                foreach (var entry in (IEnumerable)GetField(sidecar, "_inputFrames"))
                {
                    var length = (int)entry.GetType().GetProperty("Length", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                    Assert.Equal(12, length);
                }
            }
            finally
            {
                SetField(session, "_sidecar", null);
                if (codec == 3)
                {
                    SetField(sidecar, "_process", null);
                    ((IDisposable)sidecar).Dispose();
                }
                else
                {
                    DisposeFrameSourceSidecar(sidecar, codec);
                }

                pipeline.Dispose();
            }
        }

    }
}
