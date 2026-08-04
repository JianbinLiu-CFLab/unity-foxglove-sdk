// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: RecordingController concurrent lifecycle ownership regressions.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Mcap
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Mcap")]
    public sealed class RecordingControllerLifecycleTests
    {
        [Fact]
        public async Task DisableRetiresAnAttachThatWasAlreadyInFlight()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-phase187-" + Guid.NewGuid().ToString("N") + ".mcap");
            var clock = new BlockingClock();
            var logger = new ConsoleLogger();
            using var controller = new RecordingController(logger, clock);
            using var session = new FoxgloveSession("phase187", new NoopTransport(), clock, logger: logger);
            var parameters = new FoxgloveParameterStore(logger);
            Task attachTask = null;
            Task disableTask = null;

            try
            {
                controller.Enable(path, coordinateMode: "phase187");
                attachTask = Task.Run(() => controller.AttachToSession(parameters, session));
                Assert.True(clock.AttachBlocked.Wait(TimeSpan.FromSeconds(5)), "Attach did not reach the blocked clock seam.");

                var disableInvoked = new ManualResetEventSlim();
                disableTask = Task.Run(() =>
                {
                    disableInvoked.Set();
                    controller.Disable();
                });
                Assert.True(disableInvoked.Wait(TimeSpan.FromSeconds(5)), "Disable task did not start.");

                // On the broken implementation Disable returns here and observes no
                // recorder; the older attach publishes one after the release below.
                await Task.WhenAny(disableTask, Task.Delay(TimeSpan.FromSeconds(1)));
                clock.ReleaseAttach.Set();

                var lifecycleTasks = Task.WhenAll(attachTask, disableTask);
                var completed = await Task.WhenAny(lifecycleTasks, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(lifecycleTasks, completed);
                await lifecycleTasks;
                Assert.False(controller.IsEnabled);

                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally
            {
                clock.ReleaseAttach.Set();
                await WaitBestEffortAsync(attachTask);
                await WaitBestEffortAsync(disableTask);
                controller.Dispose();
                session.Dispose();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static async Task WaitBestEffortAsync(Task task)
        {
            if (task == null)
                return;

            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
                if (ReferenceEquals(completed, task))
                    await task;
            }
            catch
            {
                // The test assertion reports the primary failure; cleanup must continue.
            }
        }

        private sealed class BlockingClock : IFoxgloveClock
        {
            internal readonly ManualResetEventSlim AttachBlocked = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim ReleaseAttach = new ManualResetEventSlim();

            public ulong NowNs
            {
                get
                {
                    AttachBlocked.Set();
                    if (!ReleaseAttach.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Timed out waiting to release the recording attach probe.");
                    return 187UL;
                }
            }
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning => false;
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() { }
        }
    }
}
