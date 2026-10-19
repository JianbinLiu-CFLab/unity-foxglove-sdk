// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Video encoder sidecar timestamp behavior.

using System;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Linq;
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

        private static string Read(string relativePath)
        {
            var root = FindRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.ReadAllText(path);
            if (!relativePath.EndsWith("OpenH264EncoderSidecar.cs", StringComparison.Ordinal))
                return source;

            var sections = Path.Combine(
                Path.GetDirectoryName(path),
                "Decomposed",
                "OpenH264EncoderSidecar");
            if (!Directory.Exists(sections))
                return source;

            return source + Environment.NewLine + string.Join(
                Environment.NewLine,
                Directory.GetFiles(sections, "*.cs")
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .Select(File.ReadAllText));
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
