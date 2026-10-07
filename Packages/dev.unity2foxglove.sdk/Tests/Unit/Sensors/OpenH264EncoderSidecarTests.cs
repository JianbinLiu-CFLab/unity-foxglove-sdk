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
    [Trait("Phase", "140-33")]
    [Trait("Domain", "Sensors")]
    public sealed class OpenH264EncoderSidecarTests
    {
        [Fact]
        public void EmptyAccessUnitIsRejectedWithoutConsumingTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            Assert.Throws<System.ArgumentException>(() => sidecar.AcceptHelperAccessUnit(System.Array.Empty<byte>()));
            sidecar.AcceptHelperAccessUnit(new byte[] { 1, 2, 3 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(100UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 1, 2, 3 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void SkipSentinelConsumesOnlySkippedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            sidecar.AcceptHelperSkippedAccessUnit();
            sidecar.AcceptHelperAccessUnit(new byte[] { 4, 5, 6 });

            Assert.Equal(1, sidecar.SkippedAccessUnits);
            Assert.Contains("skipped", sidecar.LastDiagnosticLine);
            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(200UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 4, 5, 6 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptHelperAccessUnit(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.DroppedOutputFrames);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptHelperAccessUnit(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FfmpegH264FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new FfmpegH264EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptEncodedAccessUnitForTests(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.AccessUnitsDropped);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptEncodedAccessUnitForTests(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.Equal(0, sidecar.PendingTimestampCountForTests);
            Assert.Equal(5, sidecar.AccessUnitsProduced);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FfmpegH265FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new FfmpegH265EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptEncodedAccessUnitForTests(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.AccessUnitsDropped);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptEncodedAccessUnitForTests(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.Equal(0, sidecar.PendingTimestampCountForTests);
            Assert.Equal(5, sidecar.AccessUnitsProduced);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Theory]
        [InlineData(@"C:\OpenH264\openh264.dll", "\"C:\\OpenH264\\openh264.dll\"")]
        [InlineData(@"C:\OpenH264 Runtime\", "\"C:\\OpenH264 Runtime\\\\\"")]
        [InlineData("C:\\OpenH264\\quoted\"name.dll", "\"C:\\OpenH264\\quoted\\\"name.dll\"")]
        public void OpenH264ArgumentsUseWindowsCommandLineEscaping(string value, string expected)
        {
            Assert.Equal(expected, QuoteArgument(value));
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryClampsGeometryAndRate()
        {
            var h264 = (FfmpegH264EncoderOptions)CreateOptions(
                "CreateH264Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var h265 = (FfmpegH265EncoderOptions)CreateOptions(
                "CreateH265Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var openH264 = (OpenH264EncoderOptions)CreateOptions(
                "CreateOpenH264Options",
                "",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var mediaFoundation = (MediaFoundationH264EncoderOptions)CreateOptions(
                "CreateMediaFoundationH264Options",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);

            AssertPositiveVideoOptions(h264);
            AssertPositiveVideoOptions(h265);
            AssertPositiveVideoOptions(openH264);
            AssertPositiveVideoOptions(mediaFoundation);
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryRoundsHalfFrameRatesAwayFromZero()
        {
            Assert.Equal(31, CameraVideoSidecarConfigFactory.ResolveFrameRate(30.5f));
        }

        [Fact]
        public void OpenH264StartInfoUsesTimestampProtocolV2()
        {
            var options = new OpenH264EncoderOptions
            {
                HelperExecutablePath = "openh264_probe_encoder",
                OpenH264DllPath = "openh264.dll"
            };

            var startInfo = options.CreateStartInfo();
            Assert.DoesNotContain("--protocol", startInfo.Arguments, StringComparison.Ordinal);
            Assert.Equal("2", startInfo.Environment["OPENH264_PROBE_PROTOCOL"]);
        }

        [Fact]
        public void ProtocolHeaderBufferCanBeReusedAcrossFrames()
        {
            var method = typeof(OpenH264EncoderSidecar).GetMethod(
                "PopulateProtocolHeader",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var header = new byte[12];
            using var stream = new MemoryStream();
            method.Invoke(
                null,
                new object[] { header, 0x0102030405060708UL, 0x0A0B0C0D });
            stream.Write(header, 0, header.Length);
            method.Invoke(
                null,
                new object[] { header, 9UL, 3 });
            stream.Write(header, 0, header.Length);

            Assert.Equal(
                new byte[]
                {
                    8, 7, 6, 5, 4, 3, 2, 1, 13, 12, 11, 10,
                    9, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0
                },
                stream.ToArray());

            var source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs"));
            Assert.Equal(1, CountOccurrences(source, "var protocolHeader = new byte[12];"));
            var writerStart = source.IndexOf("private async Task RunStdinWriter", StringComparison.Ordinal);
            var writerEnd = source.IndexOf("private Task RunStdoutReader", writerStart, StringComparison.Ordinal);
            var writer = source.Substring(writerStart, writerEnd - writerStart);
            Assert.Equal(1, CountOccurrences(writer, "new byte[12]"));
            Assert.Contains("PopulateProtocolHeader(protocolHeader", writer, StringComparison.Ordinal);
            Assert.Contains("WriteAsync(protocolHeader", writer, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteProtocolHeaderAsync", source, StringComparison.Ordinal);
            var headerWriterStart = source.IndexOf("private static void PopulateProtocolHeader", StringComparison.Ordinal);
            var headerWriterEnd = source.IndexOf("private static ulong ReadUInt64LittleEndian", headerWriterStart, StringComparison.Ordinal);
            var headerWriter = source.Substring(headerWriterStart, headerWriterEnd - headerWriterStart);
            Assert.DoesNotContain("new byte[12]", headerWriter, StringComparison.Ordinal);
        }

        [Fact]
        public void RawSnapshotOwnsPixelsBeforeSubscriberMutation()
        {
            var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            var frame = CameraRawImageFrameBuilder.BuildRgb8(
                1UL,
                "frame",
                2,
                2,
                source,
                flipVertical: false);

            source[0] = 99;
            var data = (byte[])frame.GetType().GetProperty("Data").GetValue(frame);

            Assert.NotSame(source, data);
            Assert.Equal(1, data[0]);
        }
        [Fact]
        public void OpenH264TimestampTestSeamAvoidsPrivateFieldReflection()
        {
            var sidecar = new OpenH264EncoderSidecar();

            sidecar.EnqueueTimestampForTests(123UL);

            Assert.Equal(1, sidecar.PendingTimestampCountForTests);
        }

        [Fact]
        public async Task LegacyHelperFallbackCompletesWithoutProtocolMarker()
        {
            var sidecar = new OpenH264EncoderSidecar();
            SetField(sidecar, "_protocolNegotiationTimeoutMs", 50);
            var wait = typeof(OpenH264EncoderSidecar).GetMethod(
                "WaitForProtocolNegotiation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(wait);

            var task = (Task)wait.Invoke(sidecar, new object[] { CancellationToken.None });
            await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, (int)GetField(sidecar, "_helperProtocolVersion"));
            Assert.Equal(1, (int)GetField(sidecar, "_protocolNegotiationState"));
            Assert.Contains("legacy framing", sidecar.LastDiagnosticLine, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task SlowHelperMarkerWithinNegotiationWindowSelectsTimestampProtocol()
        {
            var sidecar = new OpenH264EncoderSidecar();
            var wait = typeof(OpenH264EncoderSidecar).GetMethod(
                "WaitForProtocolNegotiation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var accept = typeof(OpenH264EncoderSidecar).GetMethod(
                "TryAcceptProtocolMarker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(wait);
            Assert.NotNull(accept);

            // A cold helper can take longer than the previous 250 ms window to
            // advertise; the marker must still win over the legacy fallback.
            var task = (Task)wait.Invoke(sidecar, new object[] { CancellationToken.None });
            await Task.Delay(300);
            Assert.False(task.IsCompleted);
            Assert.True((bool)accept.Invoke(sidecar, null));
            await task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(2, (int)GetField(sidecar, "_helperProtocolVersion"));
            Assert.Equal(2, (int)GetField(sidecar, "_protocolNegotiationState"));
        }

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
                    var result = pipeline.SubmitVideoFrame(source, (ulong)frame, 2, 2);

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

        [Fact]
        public void MediaFoundationWorkerReleaseClearsManagedStateOnWorkerThread()
        {
            var sidecar = new MediaFoundationH264EncoderSidecar();
            SetField(sidecar, "_options", new MediaFoundationH264EncoderOptions { Width = 2, Height = 2 });
            SetField(sidecar, "_nv12Scratch", new byte[6]);
            var worker = new Thread(() => Invoke(sidecar, "ReleaseEncoderResources"))
            {
                IsBackground = true
            };

            worker.Start();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(GetField(sidecar, "_options"));
            Assert.Null(GetField(sidecar, "_nv12Scratch"));
            Assert.False(sidecar.IsRunning);
            sidecar.Dispose();
        }

        [Fact]
        public void MediaFoundationTimestampTrackingEvictsOnlyTheOldestSample()
        {
            var sidecar = new MediaFoundationH264EncoderSidecar();
            var register = typeof(MediaFoundationH264EncoderSidecar).GetMethod(
                "RegisterSampleTimestamp",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(register);
            const long capacity = 256;
            try
            {
                for (var sampleTime = 0L; sampleTime <= capacity; sampleTime++)
                    register.Invoke(sidecar, new object[] { sampleTime, 1_000UL + (ulong)sampleTime });

                var map = (IDictionary)GetField(sidecar, "_sampleTimestampNsByTime");
                Assert.Equal((int)capacity, map.Count);
                Assert.False(map.Contains(0L));
                Assert.Equal(1_001UL, map[1L]);
                Assert.Equal(1_000UL + (ulong)capacity, map[capacity]);
            }
            finally
            {
                sidecar.Dispose();
            }
        }

        private static object CreateFrameSourceSidecar(int codec, Process process)
        {
            object sidecar;
            if (codec == 2)
            {
                sidecar = new MediaFoundationH264EncoderSidecar();
                SetProperty(sidecar, "IsRunning", true);
                SetField(sidecar, "_options", new MediaFoundationH264EncoderOptions { Width = 2, Height = 2 });
            }
            else
            {
                sidecar = codec == 0 ? (object)new FfmpegH264EncoderSidecar() : new FfmpegH265EncoderSidecar();
                SetField(sidecar, "_options", codec == 0
                    ? (object)new FfmpegH264EncoderOptions { Width = 2, Height = 2 }
                    : new FfmpegH265EncoderOptions { Width = 2, Height = 2 });
                SetField(sidecar, "_process", process);
            }
            return sidecar;
        }

        private static void DisposeFrameSourceSidecar(object sidecar, int codec)
        {
            if (codec != 2)
                SetField(sidecar, "_process", null);
            ((IDisposable)sidecar).Dispose();
        }

        private sealed class CopyObservation
        {
            internal int DestinationLength;
            internal int CopyCount;
        }

        private readonly struct ObservedFrameSource : ICameraVideoFrameBytesSource
        {
            private readonly long _a, _b, _c, _d, _e, _f, _g, _h, _i, _j, _k, _l, _m, _n, _o, _p;
            private readonly bool _fail;
            internal readonly CopyObservation Observation;
            public int Length { get; }

            internal ObservedFrameSource(int length, bool fail = false)
            {
                Length = length;
                _fail = fail;
                Observation = new CopyObservation();
                _a = _b = _c = _d = _e = _f = _g = _h = _i = _j = _k = _l = _m = _n = _o = _p = 0;
            }

            public void CopyTo(byte[] destination)
            {
                if (_fail)
                    throw new InvalidOperationException("Injected frame copy failure.");
                Observation.CopyCount++;
                Observation.DestinationLength = destination.Length;
                Array.Fill(destination, (byte)0xA5);
                for (var i = 0; i < Length; i++)
                    destination[i] = (byte)(i + 1);
            }
        }

        private static string QuoteArgument(string value)
        {
            var method = typeof(OpenH264EncoderOptions).GetMethod(
                "QuoteArgument",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { value });
        }

        private static object CreateOptions(string methodName, params object[] args)
        {
            var factory = typeof(FfmpegH264EncoderOptions).Assembly.GetType(
                "Foxglove.Schemas.Video.CameraVideoSidecarOptionsFactory");
            Assert.NotNull(factory);
            var method = factory.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(null, args);
        }

        private static void AssertPositiveVideoOptions(object options)
        {
            Assert.Equal(1, IntProperty(options, "Width"));
            Assert.Equal(1, IntProperty(options, "Height"));
            Assert.Equal(1, IntProperty(options, "FrameRate"));
            Assert.Equal(1, IntProperty(options, "BitrateKbps"));
            Assert.Equal(1, IntProperty(options, "KeyframeInterval"));
            Assert.Equal(1, IntProperty(options, "MaxInputQueue"));
            Assert.Equal(1, IntProperty(options, "MaxOutputQueue"));
        }

        private static int IntProperty(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(field);
            return (int)field.GetValue(target);
        }

        private static void AssertSingleOutstandingWakeup(object sidecar, object options)
        {
            SetField(sidecar, "_options", options);
            SetField(sidecar, "_maxInputQueue", 2);
            using var currentProcess = Process.GetCurrentProcess();
            SetField(sidecar, "_process", currentProcess);
            var submit = sidecar.GetType().GetMethod(
                "TrySubmitFrame",
                new[] { typeof(byte[]), typeof(ulong) });
            Assert.NotNull(submit);

            try
            {
                Assert.True((bool)submit.Invoke(sidecar, new object[] { new byte[12], 1UL }));
                Assert.True((bool)submit.Invoke(sidecar, new object[] { new byte[12], 2UL }));

                var signal = (SemaphoreSlim)GetField(sidecar, "_inputSignal");
                Assert.Equal(1, signal.CurrentCount);
            }
            finally
            {
                SetField(sidecar, "_process", null);
                sidecar.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(sidecar, null);
            }
        }

        private static void AssertDrainDoesNotConsumeConcurrentWakeup(object sidecar, object options)
        {
            SetField(sidecar, "_options", options);
            SetField(sidecar, "_maxInputQueue", 2);
            using var currentProcess = Process.GetCurrentProcess();
            SetField(sidecar, "_process", currentProcess);
            var submit = sidecar.GetType().GetMethod(
                "TrySubmitFrame",
                new[] { typeof(byte[]), typeof(ulong) });
            var drain = sidecar.GetType().GetMethod(
                "DrainInputQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(submit);
            Assert.NotNull(drain);

            try
            {
                Assert.True((bool)submit.Invoke(sidecar, new object[] { new byte[12], 1UL }));
                var signal = (SemaphoreSlim)GetField(sidecar, "_inputSignal");
                const int extraWakeups = 5_000_000;
                signal.Release(extraWakeups);
                var initialWakeups = signal.CurrentCount;

                var drainTask = Task.Run(() => drain.Invoke(sidecar, null));
                Assert.True(
                    SpinWait.SpinUntil(
                        () => signal.CurrentCount < initialWakeups,
                        TimeSpan.FromSeconds(5)),
                    "Input drain did not begin consuming wakeups.");
                Assert.False(drainTask.IsCompleted, "Input drain completed before the stop-boundary interleaving was established.");

                Assert.True((bool)submit.Invoke(sidecar, new object[] { new byte[12], 2UL }));
                drainTask.GetAwaiter().GetResult();

                Assert.Equal(1, (int)GetField(sidecar, "_inputCount"));
                Assert.Equal(1, signal.CurrentCount);
            }
            finally
            {
                SetField(sidecar, "_process", null);
                sidecar.GetType().GetMethod("Stop", Type.EmptyTypes)?.Invoke(sidecar, null);
            }
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var gitPath = Path.Combine(directory.FullName, ".git");
                if (File.Exists(gitPath) || Directory.Exists(gitPath))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private static void SetProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property);
            property.SetValue(target, value);
        }

        private static void Invoke(object target, string name)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
        }
    }
}
