// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Video encoder sidecar timestamp behavior.

using System;
using System.Reflection;
using System.Collections;
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
        public void OpenH264TimestampTestSeamAvoidsPrivateFieldReflection()
        {
            var sidecar = new OpenH264EncoderSidecar();

            sidecar.EnqueueTimestampForTests(123UL);

            Assert.Equal(1, sidecar.PendingTimestampCountForTests);
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
