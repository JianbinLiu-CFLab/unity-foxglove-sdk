// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Cover Phase187 runtime-state review regressions.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Runtime state")]
    public sealed class RuntimeStateReviewTests
    {
        [Fact]
        public void ActiveRecorderReceivesCoordinateModeChanges()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-phase187-coordinate-" + Guid.NewGuid().ToString("N") + ".mcap");
            var logger = new ConsoleLogger();
            using var controller = new RecordingController(logger, new SystemClock());
            using var session = new FoxgloveSession(
                "phase187-coordinate",
                new BlockingTransport(),
                logger: logger);

            try
            {
                controller.Enable(path, McapRecorder.DefaultChunkSizeBytes, "", "output-before", "input-before");
                controller.AttachToSession(new FoxgloveParameterStore(logger), session);

                var recorder = ReadPrivateField<McapRecorder>(controller, "_recorder");
                Assert.NotNull(recorder);

                controller.SetCoordinateModes("output-after", "input-after");

                Assert.Equal("output-after", recorder.OutputCoordinateMode);
                Assert.Equal("input-after", recorder.InputCoordinateMode);
            }
            finally
            {
                controller.DetachFromSession();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ClientSeekClearsExternalCursorOwnership()
        {
            var coordinator = new TickCoordinator(new ReplaySnapshotStateMachine());
            WritePrivateField(coordinator, "_hasExternalCursorTime", true);
            WritePrivateField(coordinator, "_lastExternalCursorTimeNs", 900UL);
            var logger = new ConsoleLogger();
            var clock = new PlaybackClock();
            clock.EnableRange(0, 1_000);
            using var replay = new ReplayController(logger, null, clock);

            coordinator.ApplyPlaybackControl(
                1,
                1f,
                true,
                500,
                "phase187-seek",
                replay,
                clock,
                new SystemClock(),
                logger);

            Assert.False(ReadPrivateField<bool>(coordinator, "_hasExternalCursorTime"));
            Assert.Equal(0UL, ReadPrivateField<ulong>(coordinator, "_lastExternalCursorTimeNs"));
        }

        [Fact]
        public async Task GraphMutationDuringBroadcastRemainsDirtyForNextMetadataSnapshot()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            using var transport = new BlockingTransport();
            var handler = new SessionGraphHandler(transport, new ConsoleLogger(), () => recorder);

            handler.Subscribe(187);
            handler.SetUnityPublishedTopic("/phase187/before");
            transport.Arm();

            var broadcast = Task.Run(handler.BroadcastUpdate);
            Assert.True(
                transport.SendEntered.Wait(TimeSpan.FromSeconds(5)),
                "Broadcast did not reach the blocking transport seam.");

            handler.AddAdvertisedService("/phase187/during");
            transport.Release.Set();
            await broadcast;

            Assert.Equal(1, ReadPrivateField<int>(handler, "_dirty"));
        }

        [Fact]
        public void InvalidTopicWarningKeyIsSharedAcrossPublishEntryPoints()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var method = SourceMethod(source, "private bool ValidateConfiguredTopic");

            Assert.Contains("var key = \"invalid-topic\";", method, StringComparison.Ordinal);
            Assert.DoesNotContain("\"invalid-topic:\" + operation", method, StringComparison.Ordinal);
            Assert.DoesNotContain("GetTopicWarningKey", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeStopDetachesRecordingExactlyOnceBeforeSessionDispose()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var method = SourceMethod(source, "public void Stop()");
            const string detach = "_recording.DetachFromSession();";
            const string dispose = "session?.Dispose();";

            Assert.Equal(1, CountOccurrences(method, detach));
            Assert.True(
                method.IndexOf(detach, StringComparison.Ordinal)
                < method.IndexOf(dispose, StringComparison.Ordinal));
        }

        [Fact]
        public void RuntimeStopDetachesSessionHandlersWhenTransportStopThrowsAndRestartDoesNotDuplicate()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            try
            {
                runtime.Start("phase187-d01-002-stop");
                Assert.Equal(4, transport.HandlerCount);

                transport.ThrowOnNextStop = true;
                var failure = Assert.Throws<InvalidOperationException>(() => runtime.Stop());
                Assert.Equal("stop failure", failure.Message);
                Assert.Equal(0, transport.HandlerCount);

                runtime.Start("phase187-d01-002-restart");
                Assert.Equal(4, transport.HandlerCount);
                runtime.Stop();
                Assert.Equal(0, transport.HandlerCount);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeDisposePreservesCleanupAndReleasesTransportAfterStopFailure()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextStop = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-d01-002-dispose");

            Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
            runtime.Dispose();

            Assert.Equal(0, transport.HandlerCount);
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void RuntimeDisposeRunsSessionCleanupWhenRecordingDetachThrows()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-d01-review-h1");

            var recording = ReadPrivateField<RecordingController>(runtime, "_recording");
            var lifecycleGate = recording.GetType().GetField(
                "_lifecycleGate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lifecycleGate);

            try
            {
                // Nulling the private gate is a deterministic fault injection for
                // the recording detach step; no production seam is widened for tests.
                lifecycleGate.SetValue(recording, null);
                Assert.ThrowsAny<Exception>(() => runtime.Dispose());

                Assert.Equal(0, transport.HandlerCount);
                Assert.True(ReadPrivateField<bool>(runtime, "_stopCleanupComplete"));
                Assert.False(ReadPrivateField<bool>(runtime, "_recordingDisposed"));
                Assert.Equal(1, transport.DisposeCalls);

                lifecycleGate.SetValue(recording, new object());
                runtime.Dispose();
                Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            }
            finally
            {
                lifecycleGate.SetValue(recording, new object());
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeStopTracksCompletionExplicitlyInsteadOfInferringFromSessionNull()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var method = SourceMethod(source, "public void Stop()");

            Assert.Contains("_stopCleanupComplete = true;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_stopCleanupComplete = _session == null;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerDestroyRetriesRuntimeDisposeBeforeReleasingReference()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var method = SourceMethod(source, "private void OnDestroy()");
            var firstDisposeIndex = method.IndexOf("_runtime?.Dispose();", StringComparison.Ordinal);
            var secondDisposeIndex = firstDisposeIndex < 0
                ? -1
                : method.IndexOf(
                    "_runtime?.Dispose();",
                    firstDisposeIndex + 1,
                    StringComparison.Ordinal);
            var clearIndex = secondDisposeIndex < 0
                ? -1
                : method.IndexOf("_runtime = null;", secondDisposeIndex, StringComparison.Ordinal);

            Assert.True(firstDisposeIndex >= 0);
            Assert.True(secondDisposeIndex > firstDisposeIndex);
            Assert.True(clearIndex > secondDisposeIndex);
            Assert.Contains("catch", method.Substring(firstDisposeIndex), StringComparison.Ordinal);
        }

        private static T ReadPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(target);
        }

        private static void WritePrivateField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
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

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + signature);
            var brace = source.IndexOf('{', start);
            Assert.True(brace >= 0, "Missing method body: " + signature);
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(start, i - start + 1);
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private sealed class ThrowingStopTransport : IFoxgloveTransport
        {
            private Action<uint> _connected;
            private Action<uint> _disconnected;
            private Action<uint, string> _textReceived;
            private Action<uint, byte[]> _binaryReceived;

            internal bool ThrowOnNextStop { get; set; }
            internal int DisposeCalls { get; private set; }
            internal int HandlerCount =>
                InvocationCount(_connected)
                + InvocationCount(_disconnected)
                + InvocationCount(_textReceived)
                + InvocationCount(_binaryReceived);

            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected
            {
                add => _connected += value;
                remove => _connected -= value;
            }

            public event Action<uint> OnClientDisconnected
            {
                add => _disconnected += value;
                remove => _disconnected -= value;
            }

            public event Action<uint, string> OnTextReceived
            {
                add => _textReceived += value;
                remove => _textReceived -= value;
            }

            public event Action<uint, byte[]> OnBinaryReceived
            {
                add => _binaryReceived += value;
                remove => _binaryReceived -= value;
            }

            public void Start(string host, int port) => IsRunning = true;

            public void Stop()
            {
                IsRunning = false;
                if (!ThrowOnNextStop)
                    return;

                ThrowOnNextStop = false;
                throw new InvalidOperationException("stop failure");
            }

            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }

            public void Dispose()
            {
                DisposeCalls++;
                IsRunning = false;
            }

            private static int InvocationCount(Delegate callback)
                => callback?.GetInvocationList().Length ?? 0;
        }

        /// <summary>Transport seam that can pause one graph broadcast after snapshot creation.</summary>
        private sealed class BlockingTransport : IFoxgloveTransport
        {
            private int _armed;

            internal readonly ManualResetEventSlim SendEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();

            public bool IsRunning => true;
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }

            internal void Arm() => Volatile.Write(ref _armed, 1);
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }

            public void SendText(uint clientId, string json)
            {
                if (Interlocked.Exchange(ref _armed, 0) != 1)
                    return;

                SendEntered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting to release the graph broadcast seam.");
            }

            public void SendBinary(uint clientId, byte[] data) { }

            public void Dispose()
            {
                Release.Set();
                SendEntered.Dispose();
                Release.Dispose();
            }
        }
    }
}
