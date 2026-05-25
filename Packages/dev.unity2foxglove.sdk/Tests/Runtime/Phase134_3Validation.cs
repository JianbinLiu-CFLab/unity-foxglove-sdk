// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-3 recording shutdown and replay callback hardening.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_3Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-3: Recording and replay controller hardening ===");
            _passed = 0;

            VerifyRecordingDetachSwallowsRecorderCloseFailure();
            VerifyReplayRuntimeEventsAreExceptionIsolated();
            VerifyReplayDispatchLeavesPlaybackLockBeforeUserCallbacks();
            VerifyControllerDirectReplayEventsAreExceptionIsolated();

            Console.WriteLine($"Phase 134-3: {_passed} checks passed.");
        }

        private static void VerifyRecordingDetachSwallowsRecorderCloseFailure()
        {
            var logger = new CaptureLogger();
            var controller = new RecordingController(logger);
            var stream = new ThrowingStream();
            var recorder = new McapRecorder(stream, logger, new McapWriterOptions { UseChunking = false }, leaveOpen: true);
            stream.ThrowOnWrite = true;
            stream.ThrowOnFlush = true;

            SetPrivateField(controller, "_recorder", recorder);

            controller.DetachFromSession();

            Check(logger.Warnings.Any(message => message.Contains("MCAP recorder", StringComparison.OrdinalIgnoreCase)),
                "134-3A-1: recording detach logs recorder finalization failures");
            Check(ReadPrivateField<McapRecorder>(controller, "_recorder") == null,
                "134-3A-2: recording detach clears recorder state even after close failure");
        }

        private static void VerifyReplayRuntimeEventsAreExceptionIsolated()
        {
            var tmp = CreateTempMcap();
            var logger = new CaptureLogger();
            var transport = new Phase134_3Transport();
            var clock = new Phase134_3Clock();
            var runtime = new FoxgloveRuntime(transport, clock, new DefaultSchemaRegistry(), logger);
            var secondHandlerCount = 0;
            try
            {
                runtime.EnableReplay(tmp);
                runtime.OnReplayMessage += (_, _) => throw new InvalidOperationException("listener boom");
                runtime.OnReplayMessage += (_, _) => secondHandlerCount++;
                runtime.Start("phase134-3-replay", "127.0.0.1", 9893);
                runtime.ReplayPlay();
                clock.AdvanceNs(5_000_000UL);

                runtime.Tick();

                Check(secondHandlerCount == 1,
                    "134-3B-1: replay runtime invokes later listeners after an earlier listener throws");
                Check(logger.Warnings.Any(message => message.Contains("replay", StringComparison.OrdinalIgnoreCase)
                                                    && message.Contains("listener boom", StringComparison.Ordinal)),
                    "134-3B-2: replay runtime logs listener failures without aborting Tick");
            }
            finally
            {
                runtime.Dispose();
                TryDelete(tmp);
            }
        }

        private static void VerifyReplayDispatchLeavesPlaybackLockBeforeUserCallbacks()
        {
            var runtimeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var replaySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");

            Check(runtimeSource.Contains("_replay.ApplySnapshotToScene(sceneSnapshotTimeNs, deferCallbacks: true)", StringComparison.Ordinal)
                  && runtimeSource.Contains("_replay.Tick(session, _playbackClock.NowNs, deferCallbacks: true)", StringComparison.Ordinal)
                  && runtimeSource.Contains("_replay.DrainReplayCallbacks()", StringComparison.Ordinal),
                "134-3C-1: runtime defers replay callbacks until after playback lock scope");
            Check(replaySource.Contains("DrainReplayCallbacks", StringComparison.Ordinal)
                  && replaySource.Contains("ReplayCallbackDispatch", StringComparison.Ordinal)
                  && !replaySource.Contains("OnReplayMessage?.Invoke", StringComparison.Ordinal)
                  && !replaySource.Contains("OnReplayMessageContext?.Invoke", StringComparison.Ordinal)
                  && !replaySource.Contains("OnReplayBatchCompleted?.Invoke", StringComparison.Ordinal),
                "134-3C-2: replay controller queues and safely drains public callbacks");
        }

        private static void VerifyControllerDirectReplayEventsAreExceptionIsolated()
        {
            var logger = new CaptureLogger();
            var controller = new ReplayController(logger);
            var context = new ReplayMessageContext(
                channelId: 1,
                topic: "/phase134/replay",
                messageEncoding: "json",
                schemaName: "",
                schemaEncoding: "",
                logTimeNs: 10,
                replayStartTimeNs: 0,
                payload: Encoding.UTF8.GetBytes("{}"));
            var secondHandlerCount = 0;

            controller.OnReplayMessageContext += _ => throw new InvalidOperationException("context boom");
            controller.OnReplayMessageContext += _ => secondHandlerCount++;
            controller.FireContextForTests(context);

            Check(secondHandlerCount == 1,
                "134-3D-1: replay controller invokes later context listeners after an earlier listener throws");
            Check(logger.Warnings.Any(message => message.Contains("context boom", StringComparison.Ordinal)),
                "134-3D-2: replay controller logs context listener failures");
        }

        private static string CreateTempMcap()
        {
            var tmp = Path.GetTempFileName();
            using (var fs = new FileStream(tmp, FileMode.Truncate, FileAccess.Write))
            {
                var recorder = new McapRecorder(fs);
                recorder.AddChannel(1, "/phase134/replay", "json", "", "", "");
                recorder.WriteMessage(1, 1_000_000UL, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }
            return tmp;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        private static T ReadPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class CaptureLogger : IFoxgloveLogger
        {
            public readonly List<string> Warnings = new();
            public readonly List<string> Errors = new();

            public void LogWarning(string message) => Warnings.Add(message ?? string.Empty);
            public void LogError(string message) => Errors.Add(message ?? string.Empty);
        }

        private sealed class ThrowingStream : Stream
        {
            private readonly MemoryStream _inner = new();

            public bool ThrowOnWrite { get; set; }
            public bool ThrowOnFlush { get; set; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush()
            {
                if (ThrowOnFlush)
                    throw new IOException("phase134 flush failure");
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
                => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin)
                => _inner.Seek(offset, origin);

            public override void SetLength(long value)
                => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase134 write failure");
                _inner.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase134 write failure");
                _inner.Write(buffer);
            }
        }

        private sealed class Phase134_3Clock : IFoxgloveClock
        {
            public ulong NowNs { get; private set; }
            public void AdvanceNs(ulong deltaNs) => NowNs += deltaNs;
        }

        private sealed class Phase134_3Transport : IFoxgloveTransport, IReplayResettableFoxgloveTransport, IFoxgloveTransportStatsProvider
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void ClearDataQueues() { }

            public TransportStatsSnapshot GetStatsSnapshot()
            {
                return new TransportStatsSnapshot
                {
                    Supported = true,
                    IsRunning = IsRunning,
                    ActiveClientCount = 0,
                    Clients = Array.Empty<TransportClientStats>()
                };
            }
        }
    }
}
