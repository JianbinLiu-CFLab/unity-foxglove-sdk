// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Cover Phase187 runtime-state review regressions.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Runtime state")]
    public sealed partial class RuntimeStateReviewTests
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

    }
}
