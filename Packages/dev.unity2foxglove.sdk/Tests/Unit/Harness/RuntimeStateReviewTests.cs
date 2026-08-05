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
