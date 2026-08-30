// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Cover Phase187 runtime-state review regressions.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
            var method = SourceMethod(source, "private void RunStopCleanup");
            const string detach = "_recording.DetachFromSession";
            const string dispose = "session?.Dispose()";

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
        public void RuntimeDisposeAfterRestartCleansTheCurrentSession()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            try
            {
                runtime.Start("phase187-d01-002-first-cycle");
                runtime.Stop();

                runtime.Start("phase187-d01-002-second-cycle");
                Assert.Equal(4, transport.HandlerCount);
                Assert.False(ReadPrivateField<bool>(runtime, "_stopCleanupComplete"));

                runtime.Dispose();

                Assert.Null(runtime.Session);
                Assert.Equal(0, transport.HandlerCount);
                Assert.False(transport.IsRunning);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeDisposeRetriesSessionCleanupAfterHandlerRemovalFailure()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextHandlerRemoval = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-d01-session-retry");

            try
            {
                Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
                Assert.Equal(0, transport.HandlerCount);
                Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
                Assert.Equal(1, transport.DisposeCalls);
                Assert.Equal(1, transport.StopCalls);
                Assert.Equal(2, transport.ConnectedRemovalCalls);
                Assert.Equal(1, transport.DisconnectedRemovalCalls);
                Assert.Equal(1, transport.TextRemovalCalls);
                Assert.Equal(1, transport.BinaryRemovalCalls);

                runtime.Dispose();

                Assert.Equal(0, transport.HandlerCount);
                Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
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
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(2, transport.StopCalls);
            Assert.Equal(1, transport.ConnectedRemovalCalls);
            Assert.Equal(1, transport.DisconnectedRemovalCalls);
            Assert.Equal(1, transport.TextRemovalCalls);
            Assert.Equal(1, transport.BinaryRemovalCalls);
            runtime.Dispose();
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));

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
                Assert.False(ReadPrivateField<bool>(runtime, "_stopCleanupComplete"));
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
        public void RuntimeStopCleanupStateRetriesOnlyFailedStepAndTracksRequiredOwnership()
        {
            var state = new RuntimeStopCleanupState();
            var completedAttempts = 0;
            ExceptionDispatchInfo firstFailure = null;

            Assert.True(state.IsComplete);
            state.TryCleanup(
                RuntimeStopCleanupStep.Session,
                () => completedAttempts++,
                ref firstFailure);
            Assert.Equal(0, completedAttempts);

            state.Reset();
            var replayAttempts = 0;

            state.TryCleanup(
                RuntimeStopCleanupStep.ReplayOrchestrator,
                () => completedAttempts++,
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.ReplayOrchestrator,
                () => completedAttempts++,
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.ReplaySuppressionWarnings,
                () => { },
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.ReplaySnapshot,
                () => { },
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.ReplaySceneSnapshot,
                () => { },
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.Recording,
                () => { },
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.Session,
                () => { },
                ref firstFailure);
            state.TryCleanup(
                RuntimeStopCleanupStep.ReplayPanelHistory,
                () =>
                {
                    replayAttempts++;
                    throw new InvalidOperationException("optional cleanup");
                },
                ref firstFailure);

            Assert.True(state.IsReadyForStart);
            Assert.False(state.IsComplete);
            Assert.Equal(1, completedAttempts);
            Assert.Equal("optional cleanup", firstFailure.SourceException.Message);

            state.TryCleanup(
                RuntimeStopCleanupStep.ReplayPanelHistory,
                () => replayAttempts++,
                ref firstFailure);

            Assert.Equal(2, replayAttempts);
            Assert.True(state.IsComplete);
        }

        [Fact]
        public void RuntimeStartFailureDisposesPartiallyStartedSessionBeforeRethrowing()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextStart = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            try
            {
                Assert.Throws<InvalidOperationException>(() => runtime.Start("phase187-start-failure"));
                Assert.Equal(0, transport.HandlerCount);
                Assert.False(transport.IsRunning);

                runtime.Dispose();
                Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeStartRollsBackFactoryConstructorSubscriptions()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextBinaryAdd = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            try
            {
                var failure = Assert.Throws<InvalidOperationException>(
                    () => runtime.Start("phase187-b1-factory-constructor"));

                Assert.Equal("binary add failure", failure.Message);
                Assert.Null(runtime.Session);
                Assert.Equal(0, transport.HandlerCount);
                Assert.False(transport.IsRunning);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeCannotRestartAfterSuccessfulDispose()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-disposed");

            runtime.Dispose();

            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));
            Assert.Equal(0, transport.HandlerCount);
            Assert.False(transport.IsRunning);
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void RuntimeCannotRestartAfterFailedDispose()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextHandlerRemoval = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-failed-dispose");

            Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();
            Assert.Equal(0, transport.HandlerCount);
            Assert.False(transport.IsRunning);
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void RuntimeDisposeRetriesTransportFailureWithoutAllowingAnotherEpoch()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextDispose = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-transport-dispose-failure");

            var failure = Assert.Throws<InvalidOperationException>(() => runtime.Dispose());

            Assert.Equal("transport dispose failure", failure.Message);
            Assert.Equal(0, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, transport.StopCalls);
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();

            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(2, transport.DisposeCalls);
            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void RuntimeDisposeAbandonsPermanentCallbackFailureOnlyAfterTransportClosure()
        {
            var transport = new ThrowingStopTransport { ThrowOnEveryHandlerRemoval = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-permanent-handler-failure");

            Assert.Throws<InvalidOperationException>(() => runtime.Dispose());

            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(1, transport.HandlerCount);
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void SessionConstructorRollsBackPartialSubscriptionsWhenEventAddThrows()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextBinaryAdd = true };

            var failure = Assert.Throws<InvalidOperationException>(
                () => new FoxgloveSession(
                    "phase187-b1-constructor",
                    transport,
                    logger: new ConsoleLogger()));

            Assert.Equal("binary add failure", failure.Message);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void SessionDisposeRetriesOnlyFailedCleanupSubstep()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextHandlerRemoval = true };
            var session = new FoxgloveSession(
                "phase187-b1-session-idempotency",
                transport,
                logger: new ConsoleLogger());

            Assert.Throws<InvalidOperationException>(() => session.Dispose());
            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(1, transport.ConnectedRemovalCalls);
            Assert.Equal(1, transport.DisconnectedRemovalCalls);
            Assert.Equal(1, transport.TextRemovalCalls);
            Assert.Equal(1, transport.BinaryRemovalCalls);

            session.Dispose();

            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(2, transport.ConnectedRemovalCalls);
            Assert.Equal(1, transport.DisconnectedRemovalCalls);
            Assert.Equal(1, transport.TextRemovalCalls);
            Assert.Equal(1, transport.BinaryRemovalCalls);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void RuntimeDisposeRejectsReentrantCleanupWithoutRepeatingTransportDispose()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            transport.DisposeCallback = runtime.Dispose;
            runtime.Start("phase187-b1-reentrant-dispose");

            runtime.Dispose();

            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
        }

        [Fact]
        public void RuntimeStopTracksCompletionExplicitlyInsteadOfInferringFromSessionNull()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var method = SourceMethod(source, "public void Stop()");

            Assert.Contains("RunStopCleanup(ref firstFailure);", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_stopCleanupComplete = firstFailure == null;", source, StringComparison.Ordinal);
            Assert.Contains("_stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_stopCleanupComplete = _session == null;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerDestroyUsesTestableRuntimeDisposeRetryAndReleasesReference()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var method = SourceMethod(source, "private void OnDestroy()");

            Assert.Contains(
                "FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(",
                method,
                StringComparison.Ordinal);
            Assert.Contains("() => _runtime?.Dispose()", method, StringComparison.Ordinal);
            Assert.Contains("() => _runtime = null", method, StringComparison.Ordinal);
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
            internal bool ThrowOnNextStart { get; set; }
            internal bool ThrowOnNextHandlerRemoval { get; set; }
            internal bool ThrowOnEveryHandlerRemoval { get; set; }
            internal bool ThrowOnNextBinaryAdd { get; set; }
            internal bool ThrowOnNextDispose { get; set; }
            internal Action DisposeCallback { get; set; }
            internal int DisposeCalls { get; private set; }
            internal int StopCalls { get; private set; }
            internal int ConnectedRemovalCalls { get; private set; }
            internal int DisconnectedRemovalCalls { get; private set; }
            internal int TextRemovalCalls { get; private set; }
            internal int BinaryRemovalCalls { get; private set; }
            internal int HandlerCount =>
                InvocationCount(_connected)
                + InvocationCount(_disconnected)
                + InvocationCount(_textReceived)
                + InvocationCount(_binaryReceived);

            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected
            {
                add => _connected += value;
                remove
                {
                    ConnectedRemovalCalls++;
                    if (ThrowOnEveryHandlerRemoval || ThrowOnNextHandlerRemoval)
                    {
                        ThrowOnNextHandlerRemoval = false;
                        throw new InvalidOperationException("handler removal failure");
                    }
                    _connected -= value;
                }
            }

            public event Action<uint> OnClientDisconnected
            {
                add => _disconnected += value;
                remove
                {
                    DisconnectedRemovalCalls++;
                    _disconnected -= value;
                }
            }

            public event Action<uint, string> OnTextReceived
            {
                add => _textReceived += value;
                remove
                {
                    TextRemovalCalls++;
                    _textReceived -= value;
                }
            }

            public event Action<uint, byte[]> OnBinaryReceived
            {
                add
                {
                    _binaryReceived += value;
                    if (ThrowOnNextBinaryAdd)
                    {
                        ThrowOnNextBinaryAdd = false;
                        throw new InvalidOperationException("binary add failure");
                    }
                }
                remove
                {
                    BinaryRemovalCalls++;
                    _binaryReceived -= value;
                }
            }

            public void Start(string host, int port)
            {
                IsRunning = true;
                if (ThrowOnNextStart)
                {
                    ThrowOnNextStart = false;
                    throw new InvalidOperationException("start failure");
                }

            }

            public void Stop()
            {
                StopCalls++;
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
                DisposeCallback?.Invoke();
                if (ThrowOnNextDispose)
                {
                    ThrowOnNextDispose = false;
                    throw new InvalidOperationException("transport dispose failure");
                }
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
